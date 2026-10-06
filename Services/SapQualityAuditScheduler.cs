using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MBS_SAP.Services
{
    /// <summary>
    /// Background scheduler that executes automatically every night at 12:00 AM (00:00 midnight)
    /// to audit and assess the quality of all submitted SAP reports using AI / ML Quality Engine.
    /// Also executes an initial audit pass shortly after application startup.
    /// </summary>
    public class SapQualityAuditScheduler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SapQualityAuditScheduler> _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public SapQualityAuditScheduler(
            IServiceScopeFactory scopeFactory,
            IHostApplicationLifetime appLifetime,
            ILogger<SapQualityAuditScheduler> logger)
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

            // Brief delay after startup to let database connection pools and web server settle
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // 1. Initial pass on startup to ensure recent reports are audited and ready for League
            _logger.LogInformation("SapQualityAuditScheduler: Menjalankan audit awal pasca-startup...");
            await RunAuditJobAsync("Startup Initial", stoppingToken);

            // 2. Loop waiting for midnight (12:00 AM) every day
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = GetDelayToNextMidnight();
                _logger.LogInformation("SapQualityAuditScheduler: Menunggu jadwal berikutnya pada jam 12:00 malam ({DelayHours:F1} jam ke depan, target: {NextRun}).", 
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

                _logger.LogInformation("SapQualityAuditScheduler: Menjalankan audit harian otomatis jam 12:00 malam...");
                await RunAuditJobAsync("Nightly Midnight (12 AM)", stoppingToken);

                // Small buffer delay so we don't accidentally re-trigger within the same minute
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

        private TimeSpan GetDelayToNextMidnight()
        {
            var now = DateTime.Now;
            var nextMidnight = DateTime.Today.AddDays(1); // 00:00:00 of tomorrow
            var delay = nextMidnight - now;

            // If delay is extremely small (< 10 seconds), push to tomorrow's midnight
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

        private async Task RunAuditJobAsync(string triggerSource, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var qualityService = scope.ServiceProvider.GetRequiredService<SapQualityService>();
                int auditedCount = await qualityService.AuditUnratedReportsAsync(cancellationToken);
                _logger.LogInformation("SapQualityAuditScheduler [{Trigger}]: Berhasil menyelesaikan audit {Count} laporan SAP.", triggerSource, auditedCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("SapQualityAuditScheduler [{Trigger}]: Dibatalkan karena shutdown.", triggerSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SapQualityAuditScheduler [{Trigger}]: Gagal mengeksekusi audit mutu SAP.", triggerSource);
            }
        }
    }
}
