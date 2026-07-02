using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using Microsoft.Extensions.Hosting;

namespace MBS_SAP.Services
{
    public class PostgresReplicationScheduler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PostgresReplicationScheduler> _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public PostgresReplicationScheduler(
            IServiceScopeFactory scopeFactory,
            IHostApplicationLifetime appLifetime,
            ILogger<PostgresReplicationScheduler> logger)
        {
            _scopeFactory = scopeFactory;
            _appLifetime = appLifetime;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait until host startup completes. If startup fails (e.g., port conflict),
            // stoppingToken will cancel and we exit cleanly without touching disposed services.
            try
            {
                await WaitUntilApplicationStartedAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await RunInitialSyncAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var nextMidnight = now.Date.AddDays(1);
                var delay = nextMidnight - now;

                if (delay < TimeSpan.FromSeconds(1))
                {
                    delay = TimeSpan.FromSeconds(1);
                }

                _logger.LogInformation("Postgres replication scheduler waiting until {NextRun}.", nextMidnight);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await RunDailySyncAsync(stoppingToken);
            }
        }

        private async Task WaitUntilApplicationStartedAsync(CancellationToken cancellationToken)
        {
            if (_appLifetime.ApplicationStarted.IsCancellationRequested)
            {
                return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = _appLifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
            await tcs.Task.WaitAsync(cancellationToken);
        }

        private async Task RunInitialSyncAsync(CancellationToken cancellationToken)
        {
            var startOfYear = new DateTime(DateTime.Now.Year, 1, 1);
            var lookbackDays = Math.Max(1, (DateTime.Today - startOfYear.Date).Days + 1);

            _logger.LogInformation("Starting initial postgres replication from start of year with lookback {LookbackDays} days.", lookbackDays);
            await RunReplicationAsync(lookbackDays, "initial", cancellationToken);
        }

        private async Task RunDailySyncAsync(CancellationToken cancellationToken)
        {
            const int lookbackDays = 90;
            _logger.LogInformation("Starting scheduled midnight postgres replication with lookback {LookbackDays} days.", lookbackDays);
            await RunReplicationAsync(lookbackDays, "daily", cancellationToken);
        }

        private async Task RunReplicationAsync(int lookbackDays, string mode, CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var replicationService = scope.ServiceProvider.GetRequiredService<PostgresReplicationService>();
                var result = await replicationService.ReplicateAsync(lookbackDays, cancellationToken);

                var dedupResult = await RunHazardDedupCleanupAsync(scope.ServiceProvider, cancellationToken);

                _logger.LogInformation(
                    "Postgres replication {Mode} completed. Hazard +{HazardInserted} ~{HazardUpdated} (dup {HazardSkipped}, company {HazardSkippedCompany}), " +
                    "Inspection +{InspectionInserted} ~{InspectionUpdated} (dup {InspectionSkipped}, company {InspectionSkippedCompany}), " +
                    "hazard dedup cleaned {DedupCleanedRows} rows, lookback {LookbackDays} days.",
                    mode,
                    result.HazardInserted,
                    result.HazardUpdated,
                    result.HazardSkipped,
                    result.HazardSkippedCompany,
                    result.InspectionInserted,
                    result.InspectionUpdated,
                    result.InspectionSkipped,
                    result.InspectionSkippedCompany,
                    dedupResult,
                    result.LookbackDays);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Postgres replication {Mode} canceled.", mode);
            }
            catch (ObjectDisposedException)
            {
                _logger.LogInformation("Postgres replication {Mode} skipped because host is shutting down.", mode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres replication {Mode} failed.", mode);
            }
        }

        private async Task<int> RunHazardDedupCleanupAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();

            var hazardRows = await context.HazardReports
                .Where(h => !h.IsDeleted)
                .Select(h => new
                {
                    h.Id,
                    h.Nik,
                    h.Tanggal,
                    h.Waktu,
                    h.Area,
                    h.Lokasi,
                    h.Temuan,
                    h.PerusahaanId,
                    h.CreatedAt
                })
                .OrderByDescending(h => h.CreatedAt)
                .ThenByDescending(h => h.Id)
                .ToListAsync(cancellationToken);

            static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

            var keySet = new HashSet<string>();
            var duplicateIds = new List<int>();

            foreach (var row in hazardRows)
            {
                var companyKey = row.PerusahaanId?.ToString() ?? "0";
                var key = $"{Normalize(row.Nik)}|{row.Tanggal:yyyyMMdd}|{row.Waktu.Hours:D2}:{row.Waktu.Minutes:D2}:{row.Waktu.Seconds:D2}|{Normalize(row.Area)}|{Normalize(row.Lokasi)}|{Normalize(row.Temuan)}|{companyKey}";

                if (!keySet.Add(key))
                {
                    duplicateIds.Add(row.Id);
                }
            }

            if (duplicateIds.Count == 0)
            {
                _logger.LogInformation("Hazard dedup audit: no active duplicates found.");
                return 0;
            }

            var duplicateRows = await context.HazardReports
                .Where(h => duplicateIds.Contains(h.Id))
                .ToListAsync(cancellationToken);

            foreach (var row in duplicateRows)
            {
                row.IsDeleted = true;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Hazard dedup cleanup soft-deleted {DuplicateCount} duplicate rows.", duplicateRows.Count);
            return duplicateRows.Count;
        }
    }
}