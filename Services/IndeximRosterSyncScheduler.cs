using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MBS_SAP.Services
{
    /// <summary>
    /// Background scheduler yang berjalan otomatis setiap jam 12:00 malam (00:00 midnight)
    /// untuk menyinkronkan data roster karyawan PT INDEXIM COALINDO dari [DB_ONE].[dbo].[tbl_m_roster_detail].
    /// Juga melakukan sinkronisasi awal sesaat setelah aplikasi startup.
    /// Mitra kerja lain tidak terdampak.
    /// </summary>
    public class IndeximRosterSyncScheduler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IndeximRosterSyncScheduler> _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public IndeximRosterSyncScheduler(
            IServiceScopeFactory scopeFactory,
            IHostApplicationLifetime appLifetime,
            ILogger<IndeximRosterSyncScheduler> logger)
        {
            _scopeFactory = scopeFactory;
            _appLifetime = appLifetime;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await WaitUntilApplicationStartedAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // Delay sebentar setelah startup
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // 1. Initial sync pasca-startup
            _logger.LogInformation("IndeximRosterSyncScheduler: Memulai sinkronisasi awal data roster DB_ONE (Khusus PT Indexim Coalindo)...");
            await RunSyncJobAsync("Startup Initial", stoppingToken);

            // 2. Loop menunggu jam 12:00 malam setiap hari
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = GetDelayToNextMidnight();
                _logger.LogInformation("IndeximRosterSyncScheduler: Menunggu jadwal sinkronisasi berikutnya jam 12:00 malam ({DelayHours:F1} jam ke depan, target: {NextRun}).", 
                    delay.TotalHours, DateTime.Now.Add(delay));

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

                _logger.LogInformation("IndeximRosterSyncScheduler: Menjalankan sinkronisasi harian jam 12:00 malam dari DB_ONE (Khusus PT Indexim Coalindo)...");
                await RunSyncJobAsync("Nightly Midnight (12 AM)", stoppingToken);

                // Small buffer delay
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunSyncJobAsync(string triggerSource, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var rosterService = scope.ServiceProvider.GetRequiredService<IndeximRosterService>();

                // Invalidate & refresh bulan berjalan dan bulan berikutnya
                var now = DateTime.Today;
                rosterService.InvalidateCache(now.Year, now.Month);
                var currentSummary = await rosterService.GetIndeximRosterSummariesAsync(now.Year, now.Month);

                var next = now.AddMonths(1);
                rosterService.InvalidateCache(next.Year, next.Month);
                var nextSummary = await rosterService.GetIndeximRosterSummariesAsync(next.Year, next.Month);

                _logger.LogInformation("IndeximRosterSyncScheduler [{Trigger}]: Sinkronisasi selesai. Bulan {Year}-{Month:D2} ({CurrentCount} karyawan), Bulan {NextYear}-{NextMonth:D2} ({NextCount} karyawan).",
                    triggerSource, now.Year, now.Month, currentSummary.Count, next.Year, next.Month, nextSummary.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndeximRosterSyncScheduler [{Trigger}]: Gagal melakukan sinkronisasi roster DB_ONE.", triggerSource);
            }
        }

        private TimeSpan GetDelayToNextMidnight()
        {
            var now = DateTime.Now;
            var nextMidnight = DateTime.Today.AddDays(1); // 00:00:00 of tomorrow
            var delay = nextMidnight - now;

            if (delay.TotalSeconds < 10)
            {
                nextMidnight = DateTime.Today.AddDays(2);
                delay = nextMidnight - now;
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
    }
}
