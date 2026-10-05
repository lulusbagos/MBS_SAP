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
                var delay = GetDelayToNextRunTime();
                _logger.LogInformation("Postgres replication scheduler waiting {DelayHours:F1} hours (next run around {NextRun}).", delay.TotalHours, DateTime.Now.Add(delay));

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

                await RunPeriodicSyncAsync(stoppingToken);
            }
        }

        private TimeSpan GetDelayToNextRunTime()
        {
            var now = DateTime.Now;
            
            var todayNoon = DateTime.Today.AddHours(12); // 12:00 PM today
            var tomorrowMidnight = DateTime.Today.AddDays(1); // 12:00 AM tomorrow
            var tomorrowNoon = DateTime.Today.AddDays(1).AddHours(12); // 12:00 PM tomorrow

            DateTime nextRun;
            if (now < todayNoon)
            {
                nextRun = todayNoon;
            }
            else if (now < tomorrowMidnight)
            {
                nextRun = tomorrowMidnight;
            }
            else
            {
                nextRun = tomorrowNoon;
            }

            var delay = nextRun - now;
            
            // If the delay is extremely small (e.g. less than 5 seconds), it means we just finished running 
            // right at the target time. To avoid executing repeatedly in the same second, we push to the next run time.
            if (delay.TotalSeconds < 5)
            {
                if (nextRun == todayNoon)
                {
                    nextRun = tomorrowMidnight;
                }
                else if (nextRun == tomorrowMidnight)
                {
                    nextRun = tomorrowNoon;
                }
                else
                {
                    nextRun = DateTime.Today.AddDays(2);
                }
                delay = nextRun - now;
            }

            return delay;
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
            const int lookbackDays = 2; // Pull last 2 days on startup to prevent db suspension
            _logger.LogInformation("Starting initial postgres replication with lookback {LookbackDays} days.", lookbackDays);
            await RunReplicationAsync(lookbackDays, "initial", cancellationToken);
        }

        private async Task RunPeriodicSyncAsync(CancellationToken cancellationToken)
        {
            const int lookbackDays = 2; // Pull last 2 days during periodic runs
            _logger.LogInformation("Starting scheduled postgres replication at target times with lookback {LookbackDays} days.", lookbackDays);
            await RunReplicationAsync(lookbackDays, "periodic", cancellationToken);
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

                var dedupResult = await RunAllDedupCleanupAsync(scope.ServiceProvider, cancellationToken);

                _logger.LogInformation(
                    "Postgres replication {Mode} completed. " +
                    "Hazard +{HazardInserted} ~{HazardUpdated} (dup {HazardSkipped}), " +
                    "Inspection +{InspectionInserted} ~{InspectionUpdated} (dup {InspectionSkipped}), " +
                    "Coaching +{CoachingInserted} ~{CoachingUpdated} (dup {CoachingSkipped}), " +
                    "Observation +{ObservationInserted} ~{ObservationUpdated} (dup {ObservationSkipped}), " +
                    "P2H +{P2hInserted} ~{P2hUpdated} (dup {P2hSkipped}), " +
                    "P5M +{P5mInserted} ~{P5mUpdated} (dup {P5mSkipped}), " +
                    "SafetyTalk +{SafetyTalkInserted} ~{SafetyTalkUpdated} (dup {SafetyTalkSkipped}), " +
                    "all-modules dedup cleaned {DedupCleanedRows} rows, lookback {LookbackDays} days.",
                    mode,
                    result.HazardInserted,
                    result.HazardUpdated,
                    result.HazardSkipped,
                    result.InspectionInserted,
                    result.InspectionUpdated,
                    result.InspectionSkipped,
                    result.CoachingInserted,
                    result.CoachingUpdated,
                    result.CoachingSkipped,
                    result.ObservationInserted,
                    result.ObservationUpdated,
                    result.ObservationSkipped,
                    result.P2hInserted,
                    result.P2hUpdated,
                    result.P2hSkipped,
                    result.P5mInserted,
                    result.P5mUpdated,
                    result.P5mSkipped,
                    result.SafetyTalkInserted,
                    result.SafetyTalkUpdated,
                    result.SafetyTalkSkipped,
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

        private async Task<int> RunAllDedupCleanupAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();

            var sql = @"
                -- 1. Hazard Report
                WITH cte_h AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(CAST(temuan AS NVARCHAR(200)), ''))))
                        ORDER BY id ASC
                    ) as rn FROM tbl_t_hazard_report WHERE is_deleted = 0
                )
                UPDATE tbl_t_hazard_report SET is_deleted = 1 WHERE id IN (SELECT id FROM cte_h WHERE rn > 1);

                -- 2. Inspeksi K3
                WITH cte_i AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(jenis_inspeksi, '')))), LTRIM(RTRIM(UPPER(ISNULL(lokasi, ''))))
                        ORDER BY id ASC
                    ) as rn FROM tbl_t_inspection WHERE is_deleted = 0
                )
                UPDATE tbl_t_inspection SET is_deleted = 1 WHERE id IN (SELECT id FROM cte_i WHERE rn > 1);

                -- 3. Safety Talk
                WITH cte_s AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(judul, ''))))
                        ORDER BY id ASC
                    ) as rn FROM tbl_t_safety_talk WHERE is_deleted = 0
                )
                UPDATE tbl_t_safety_talk SET is_deleted = 1 WHERE id IN (SELECT id FROM cte_s WHERE rn > 1);

                -- 4. Observasi K3
                WITH cte_o AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), date, LTRIM(RTRIM(UPPER(ISNULL(CAST(kegiatan_yang_diamati AS NVARCHAR(500)), '')))), LTRIM(RTRIM(UPPER(ISNULL(perihal_yang_diamati, ''))))
                        ORDER BY id ASC
                    ) as rn FROM tbl_t_observation WHERE is_deleted = 0
                )
                UPDATE tbl_t_observation SET is_deleted = 1 WHERE id IN (SELECT id FROM cte_o WHERE rn > 1);

                -- 5. Coaching K3
                WITH cte_c AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(tema, ''))))
                        ORDER BY id ASC
                    ) as rn FROM tbl_t_coaching WHERE is_deleted = 0
                )
                UPDATE tbl_t_coaching SET is_deleted = 1 WHERE id IN (SELECT id FROM cte_c WHERE rn > 1);

                -- 6. P5M Checklist
                WITH cte_p AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(CAST(list_pertanyaan AS NVARCHAR(500)), ''))))
                        ORDER BY id ASC
                    ) as rn FROM tbl_t_p5m WHERE is_deleted = 0
                )
                UPDATE tbl_t_p5m SET is_deleted = 1 WHERE id IN (SELECT id FROM cte_p WHERE rn > 1);
            ";

            try
            {
                var rowsAffected = await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                if (rowsAffected > 0)
                {
                    _logger.LogWarning("All-modules dedup cleanup soft-deleted {RowsAffected} duplicate rows.", rowsAffected);
                }
                else
                {
                    _logger.LogInformation("All-modules dedup cleanup: no active duplicates found.");
                }
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing all-modules dedup cleanup.");
                return 0;
            }
        }
    }
}