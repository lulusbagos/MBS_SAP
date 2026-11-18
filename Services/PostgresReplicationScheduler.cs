using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;

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

            var steps = new (string Name, string Sql)[]
            {
                ("Hazard by foto_temuan", @"
                    WITH cte_hf AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY foto_temuan
                            ORDER BY 
                                CASE WHEN status_temuan = 'Closed' THEN 0 ELSE 1 END,
                                CASE WHEN foto_perbaikan IS NOT NULL AND foto_perbaikan <> '' THEN 0 ELSE 1 END,
                                CASE WHEN temuan IS NOT NULL AND temuan NOT IN ('Kondisi Tidak Aman', 'Tindakan Tidak Aman', '') THEN 0 ELSE 1 END,
                                id ASC
                        ) as rn 
                        FROM tbl_t_hazard_report WITH (NOLOCK)
                        WHERE is_deleted = 0 AND foto_temuan IS NOT NULL AND foto_temuan <> ''
                    )
                    UPDATE h WITH (ROWLOCK)
                    SET h.is_deleted = 1
                    FROM tbl_t_hazard_report h
                    INNER JOIN cte_hf ON h.id = cte_hf.id
                    WHERE cte_hf.rn > 1 AND h.is_deleted = 0;"),

                ("Hazard by nik/datetime/temuan", @"
                    WITH cte_h AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(CAST(temuan AS NVARCHAR(200)), ''))))
                            ORDER BY CASE WHEN status_temuan = 'Closed' THEN 0 ELSE 1 END, id ASC
                        ) as rn 
                        FROM tbl_t_hazard_report WITH (NOLOCK)
                        WHERE is_deleted = 0
                    )
                    UPDATE h WITH (ROWLOCK)
                    SET h.is_deleted = 1
                    FROM tbl_t_hazard_report h
                    INNER JOIN cte_h ON h.id = cte_h.id
                    WHERE cte_h.rn > 1 AND h.is_deleted = 0;"),

                ("Sync Action Plan with soft-deleted Hazard", @"
                    UPDATE a WITH (ROWLOCK)
                    SET a.is_deleted = 1,
                        a.status = 'Closed'
                    FROM tbl_t_action_plan a
                    INNER JOIN tbl_t_hazard_report h WITH (NOLOCK) ON a.item_sap = 'hazard:' + CAST(h.id AS VARCHAR(50))
                    WHERE h.is_deleted = 1 AND a.is_deleted = 0;"),

                ("Dedup Inspeksi K3", @"
                    WITH cte_i AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(jenis_inspeksi, '')))), LTRIM(RTRIM(UPPER(ISNULL(lokasi, ''))))
                            ORDER BY id ASC
                        ) as rn 
                        FROM tbl_t_inspection WITH (NOLOCK)
                        WHERE is_deleted = 0
                    )
                    UPDATE i WITH (ROWLOCK)
                    SET i.is_deleted = 1
                    FROM tbl_t_inspection i
                    INNER JOIN cte_i ON i.id = cte_i.id
                    WHERE cte_i.rn > 1 AND i.is_deleted = 0;"),

                ("Dedup Safety Talk", @"
                    WITH cte_s AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(judul, ''))))
                            ORDER BY id ASC
                        ) as rn 
                        FROM tbl_t_safety_talk WITH (NOLOCK)
                        WHERE is_deleted = 0
                    )
                    UPDATE s WITH (ROWLOCK)
                    SET s.is_deleted = 1
                    FROM tbl_t_safety_talk s
                    INNER JOIN cte_s ON s.id = cte_s.id
                    WHERE cte_s.rn > 1 AND s.is_deleted = 0;"),

                ("Dedup Observasi K3", @"
                    WITH cte_o AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), date, LTRIM(RTRIM(UPPER(ISNULL(CAST(kegiatan_yang_diamati AS NVARCHAR(500)), '')))), LTRIM(RTRIM(UPPER(ISNULL(perihal_yang_diamati, ''))))
                            ORDER BY id ASC
                        ) as rn 
                        FROM tbl_t_observation WITH (NOLOCK)
                        WHERE is_deleted = 0
                    )
                    UPDATE o WITH (ROWLOCK)
                    SET o.is_deleted = 1
                    FROM tbl_t_observation o
                    INNER JOIN cte_o ON o.id = cte_o.id
                    WHERE cte_o.rn > 1 AND o.is_deleted = 0;"),

                ("Dedup Coaching K3", @"
                    WITH cte_c AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(tema, ''))))
                            ORDER BY id ASC
                        ) as rn 
                        FROM tbl_t_coaching WITH (NOLOCK)
                        WHERE is_deleted = 0
                    )
                    UPDATE c WITH (ROWLOCK)
                    SET c.is_deleted = 1
                    FROM tbl_t_coaching c
                    INNER JOIN cte_c ON c.id = cte_c.id
                    WHERE cte_c.rn > 1 AND c.is_deleted = 0;"),

                ("Dedup P5M Checklist", @"
                    WITH cte_p AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY LTRIM(RTRIM(UPPER(ISNULL(nik, '')))), tanggal, waktu, LTRIM(RTRIM(UPPER(ISNULL(CAST(list_pertanyaan AS NVARCHAR(500)), ''))))
                            ORDER BY id ASC
                        ) as rn 
                        FROM tbl_t_p5m WITH (NOLOCK)
                        WHERE is_deleted = 0
                    )
                    UPDATE p WITH (ROWLOCK)
                    SET p.is_deleted = 1
                    FROM tbl_t_p5m p
                    INNER JOIN cte_p ON p.id = cte_p.id
                    WHERE cte_p.rn > 1 AND p.is_deleted = 0;"),

                ("Dedup Action Plan by item_sap", @"
                    WITH cte_ap AS (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY item_sap
                            ORDER BY CASE WHEN status = 'Closed' THEN 0 ELSE 1 END, id ASC
                        ) as rn
                        FROM tbl_t_action_plan WITH (NOLOCK)
                        WHERE is_deleted = 0 AND item_sap IS NOT NULL AND item_sap <> ''
                    )
                    UPDATE a WITH (ROWLOCK)
                    SET a.is_deleted = 1
                    FROM tbl_t_action_plan a
                    INNER JOIN cte_ap ON a.id = cte_ap.id
                    WHERE cte_ap.rn > 1 AND a.is_deleted = 0;")
            };

            int totalRows = 0;
            foreach (var step in steps)
            {
                if (cancellationToken.IsCancellationRequested) break;
                totalRows += await ExecuteDedupStepWithRetryAsync(context, step.Name, step.Sql, cancellationToken);
            }

            if (totalRows > 0)
            {
                _logger.LogWarning("All-modules dedup cleanup soft-deleted {TotalRows} duplicate rows.", totalRows);
            }
            else
            {
                _logger.LogInformation("All-modules dedup cleanup: no active duplicates found.");
            }

            return totalRows;
        }

        private async Task<int> ExecuteDedupStepWithRetryAsync(AppDbContext context, string stepName, string sql, CancellationToken cancellationToken)
        {
            const int maxRetries = 3;
            var commandSql = "SET DEADLOCK_PRIORITY LOW;\n" + sql;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await context.Database.ExecuteSqlRawAsync(commandSql, cancellationToken);
                }
                catch (SqlException ex) when (ex.Number == 1205 && attempt < maxRetries)
                {
                    int delayMs = Random.Shared.Next(400, 1200) * attempt;
                    _logger.LogWarning("Deadlock detected in dedup cleanup '{StepName}' (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms...",
                        stepName, attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing dedup step '{StepName}'.", stepName);
                    return 0;
                }
            }
            return 0;
        }
    }
}