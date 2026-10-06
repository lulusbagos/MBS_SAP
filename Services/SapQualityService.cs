using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MBS_SAP.Data;
using MBS_SAP.Models;

namespace MBS_SAP.Services
{
    public class QualityStat
    {
        public int TotalRated { get; set; }
        public double AvgRating { get; set; }
        public double ScoreKualitas => Math.Min(100.0, Math.Max(0.0, Math.Round((AvgRating / 5.0) * 100.0, 1)));
    }

    public class MonthlyQualitySummary
    {
        public Dictionary<string, QualityStat> NikStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, QualityStat> DeptStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, QualityStat> CompStats { get; set; } = new();
    }

    public class SapQualityService
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SapQualityService> _logger;

        public SapQualityService(AppDbContext context, IMemoryCache cache, ILogger<SapQualityService> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Audits unrated records in all 5 SAP programs and stores the results in tbl_m_penilaian_kualitas_sap.
        /// Executed daily at midnight (12:00 AM) and available on demand.
        /// </summary>
        public async Task<int> AuditUnratedReportsAsync(CancellationToken cancellationToken = default, int batchSize = 1000, int maxPerProgram = 20000)
        {
            _logger.LogInformation("SapQualityService: Memulai audit otomatis mutu laporan SAP...");
            var now = DateTime.Now;
            int totalInserted = 0;

            try
            {
                await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;", cancellationToken);

                // 1. Audit Hazard Reports
                int hProcessed = 0;
                while (hProcessed < maxPerProgram && !cancellationToken.IsCancellationRequested)
                {
                    var rawHazards = await (from h in _context.HazardReports
                                            where !h.IsDeleted && !_context.SapQualityAssessments.Any(a => a.ProgramType == "Hazard" && a.ProgramId == h.Id)
                                            orderby h.Id descending
                                            select new { h.Id, h.Temuan, h.Tanggal })
                                            .Take(batchSize)
                                            .ToListAsync(cancellationToken);

                    if (!rawHazards.Any()) break;

                    var list = new List<SapQualityAssessment>();
                    foreach (var h in rawHazards)
                    {
                        var (rating, notes) = SapQualityMlEngine.AssessQuality("Hazard", "Hazard Report", h.Temuan ?? "");
                        list.Add(new SapQualityAssessment
                        {
                            ProgramType = "Hazard",
                            ProgramId = h.Id,
                            Rating = rating,
                            Notes = notes,
                            CreatedBy = "System-ML-Scheduler",
                            CreatedAt = now,
                            ProgramCreatedAt = h.Tanggal
                        });
                    }

                    _context.SapQualityAssessments.AddRange(list);
                    await _context.SaveChangesAsync(cancellationToken);
                    totalInserted += list.Count;
                    hProcessed += list.Count;
                }
                _logger.LogInformation("SapQualityService: Selesai audit Hazard ({Count} laporan baru)", hProcessed);

                // 2. Audit Inspeksi K3
                int iProcessed = 0;
                while (iProcessed < maxPerProgram && !cancellationToken.IsCancellationRequested)
                {
                    var rawInspections = await (from i in _context.Inspections
                                                where !i.IsDeleted && !_context.SapQualityAssessments.Any(a => a.ProgramType == "Inspection" && a.ProgramId == i.Id)
                                                orderby i.Id descending
                                                select new {
                                                    i.Id, i.Catatan, i.Tanggal,
                                                    i.Q1_1, i.Q1_2, i.Q1_3,
                                                    i.Q2_1, i.Q2_2, i.Q2_3,
                                                    i.Q3_1, i.Q3_2, i.Q3_3,
                                                    i.Q4_1, i.Q4_2, i.Q4_3,
                                                    i.Q5_1, i.Q5_2, i.Q5_3
                                                })
                                                .Take(batchSize)
                                                .ToListAsync(cancellationToken);

                    if (!rawInspections.Any()) break;

                    var list = new List<SapQualityAssessment>();
                    foreach (var item in rawInspections)
                    {
                        int safeCount = 0;
                        int hazardCount = 0;
                        int naCount = 0;
                        int[] scores = new[] {
                            item.Q1_1, item.Q1_2, item.Q1_3,
                            item.Q2_1, item.Q2_2, item.Q2_3,
                            item.Q3_1, item.Q3_2, item.Q3_3,
                            item.Q4_1, item.Q4_2, item.Q4_3,
                            item.Q5_1, item.Q5_2, item.Q5_3
                        };
                        foreach (var s in scores)
                        {
                            if (s == 2) safeCount++;
                            else if (s == 0) hazardCount++;
                            else if (s == 1) naCount++;
                        }
                        string desc = $"INSPECTION_AUDIT | Catatan: {item.Catatan ?? "-"} | YA: {safeCount} | TIDAK: {hazardCount} | NA: {naCount}";
                        var (rating, notes) = SapQualityMlEngine.AssessQuality("Inspection", "Inspeksi K3", desc);

                        list.Add(new SapQualityAssessment
                        {
                            ProgramType = "Inspection",
                            ProgramId = item.Id,
                            Rating = rating,
                            Notes = notes,
                            CreatedBy = "System-ML-Scheduler",
                            CreatedAt = now,
                            ProgramCreatedAt = item.Tanggal
                        });
                    }

                    _context.SapQualityAssessments.AddRange(list);
                    await _context.SaveChangesAsync(cancellationToken);
                    totalInserted += list.Count;
                    iProcessed += list.Count;
                }
                _logger.LogInformation("SapQualityService: Selesai audit Inspeksi ({Count} laporan baru)", iProcessed);

                // 3. Audit Safety Talk
                int stProcessed = 0;
                while (stProcessed < maxPerProgram && !cancellationToken.IsCancellationRequested)
                {
                    var rawSafetyTalks = await (from s in _context.SafetyTalks
                                                where !s.IsDeleted && !_context.SapQualityAssessments.Any(a => a.ProgramType == "SafetyTalk" && a.ProgramId == s.Id)
                                                orderby s.Id descending
                                                select new { s.Id, s.Keterangan, s.Tanggal })
                                                .Take(batchSize)
                                                .ToListAsync(cancellationToken);

                    if (!rawSafetyTalks.Any()) break;

                    var list = new List<SapQualityAssessment>();
                    foreach (var s in rawSafetyTalks)
                    {
                        var (rating, notes) = SapQualityMlEngine.AssessQuality("SafetyTalk", "Safety Talk", s.Keterangan ?? "");
                        list.Add(new SapQualityAssessment
                        {
                            ProgramType = "SafetyTalk",
                            ProgramId = s.Id,
                            Rating = rating,
                            Notes = notes,
                            CreatedBy = "System-ML-Scheduler",
                            CreatedAt = now,
                            ProgramCreatedAt = s.Tanggal
                        });
                    }

                    _context.SapQualityAssessments.AddRange(list);
                    await _context.SaveChangesAsync(cancellationToken);
                    totalInserted += list.Count;
                    stProcessed += list.Count;
                }
                _logger.LogInformation("SapQualityService: Selesai audit Safety Talk ({Count} laporan baru)", stProcessed);

                // 4. Audit Observation
                int oProcessed = 0;
                while (oProcessed < maxPerProgram && !cancellationToken.IsCancellationRequested)
                {
                    var rawObservations = await (from o in _context.Observations
                                                 where !o.IsDeleted && !_context.SapQualityAssessments.Any(a => a.ProgramType == "Observation" && a.ProgramId == o.Id)
                                                 orderby o.Id descending
                                                 select new {
                                                     o.Id, o.Date,
                                                     o.KegiatanYangDiamati,
                                                     o.PerihalYangDiamati,
                                                     o.HasilObservasi,
                                                     o.Keterangan
                                                 })
                                                 .Take(batchSize)
                                                 .ToListAsync(cancellationToken);

                    if (!rawObservations.Any()) break;

                    var list = new List<SapQualityAssessment>();
                    foreach (var o in rawObservations)
                    {
                        string desc = $"OBSERVATION_AUDIT | Kegiatan: {o.KegiatanYangDiamati ?? "-"} | Perihal: {o.PerihalYangDiamati ?? "-"} | Hasil: {o.HasilObservasi ?? "-"} | Keterangan: {o.Keterangan ?? "-"}";
                        var (rating, notes) = SapQualityMlEngine.AssessQuality("Observation", "Observasi K3", desc);

                        list.Add(new SapQualityAssessment
                        {
                            ProgramType = "Observation",
                            ProgramId = o.Id,
                            Rating = rating,
                            Notes = notes,
                            CreatedBy = "System-ML-Scheduler",
                            CreatedAt = now,
                            ProgramCreatedAt = o.Date
                        });
                    }

                    _context.SapQualityAssessments.AddRange(list);
                    await _context.SaveChangesAsync(cancellationToken);
                    totalInserted += list.Count;
                    oProcessed += list.Count;
                }
                _logger.LogInformation("SapQualityService: Selesai audit Observasi ({Count} laporan baru)", oProcessed);

                // 5. Audit Coaching
                int cProcessed = 0;
                while (cProcessed < maxPerProgram && !cancellationToken.IsCancellationRequested)
                {
                    var rawCoachings = await (from c in _context.Coachings
                                              where !c.IsDeleted && !_context.SapQualityAssessments.Any(a => a.ProgramType == "Coaching" && a.ProgramId == c.Id)
                                              orderby c.Id descending
                                              select new { c.Id, c.Feedback, c.Tanggal })
                                              .Take(batchSize)
                                              .ToListAsync(cancellationToken);

                    if (!rawCoachings.Any()) break;

                    var list = new List<SapQualityAssessment>();
                    foreach (var c in rawCoachings)
                    {
                        var (rating, notes) = SapQualityMlEngine.AssessQuality("Coaching", "Coaching K3", c.Feedback ?? "");
                        list.Add(new SapQualityAssessment
                        {
                            ProgramType = "Coaching",
                            ProgramId = c.Id,
                            Rating = rating,
                            Notes = notes,
                            CreatedBy = "System-ML-Scheduler",
                            CreatedAt = now,
                            ProgramCreatedAt = c.Tanggal
                        });
                    }

                    _context.SapQualityAssessments.AddRange(list);
                    await _context.SaveChangesAsync(cancellationToken);
                    totalInserted += list.Count;
                    cProcessed += list.Count;
                }
                _logger.LogInformation("SapQualityService: Selesai audit Coaching ({Count} laporan baru)", cProcessed);

                // Invalidate memory cache for the current month so league gets fresh values immediately
                string curKey = $"MonthlyQualitySummary_{DateTime.Today.Year}_{DateTime.Today.Month}";
                _cache.Remove(curKey);
                _logger.LogInformation("SapQualityService: Selesai audit keseluruhan! Total {Total} laporan berhasil diaudit AI.", totalInserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SapQualityService: Terjadi kesalahan saat audit otomatis laporan SAP.");
            }

            return totalInserted;
        }

        /// <summary>
        /// Retrieves aggregated quality statistics (NIK, Departemen, Company) for a specific month and year.
        /// Cached for fast repeated loads on League and Performance pages.
        /// </summary>
        public async Task<MonthlyQualitySummary> GetMonthlyQualityStatsAsync(int year, int month)
        {
            string cacheKey = $"MonthlyQualitySummary_{year}_{month}";
            if (_cache.TryGetValue(cacheKey, out MonthlyQualitySummary? cached) && cached != null)
            {
                return cached;
            }

            var summary = new MonthlyQualitySummary();
            var startOfMonth = new DateTime(year, month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

            string sql = @"
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
                WITH AllRatedReports AS (
                    SELECT LTRIM(RTRIM(h.nik)) AS nik, LTRIM(RTRIM(h.departemen)) AS departemen, h.perusahaan_id, q.rating
                    FROM tbl_t_hazard_report h WITH (NOLOCK)
                    JOIN tbl_m_penilaian_kualitas_sap q WITH (NOLOCK) ON q.program_type = 'Hazard' AND q.program_id = h.id
                    WHERE h.is_deleted = 0 AND h.tanggal >= @start AND h.tanggal <= @end

                    UNION ALL

                    SELECT LTRIM(RTRIM(i.nik)) AS nik, LTRIM(RTRIM(i.departemen)) AS departemen, i.perusahaan_id, q.rating
                    FROM tbl_t_inspection i WITH (NOLOCK)
                    JOIN tbl_m_penilaian_kualitas_sap q WITH (NOLOCK) ON q.program_type = 'Inspection' AND q.program_id = i.id
                    WHERE i.is_deleted = 0 AND i.tanggal >= @start AND i.tanggal <= @end

                    UNION ALL

                    SELECT LTRIM(RTRIM(s.nik)) AS nik, LTRIM(RTRIM(s.departemen)) AS departemen, s.perusahaan_id, q.rating
                    FROM tbl_t_safety_talk s WITH (NOLOCK)
                    JOIN tbl_m_penilaian_kualitas_sap q WITH (NOLOCK) ON q.program_type = 'SafetyTalk' AND q.program_id = s.id
                    WHERE s.is_deleted = 0 AND s.tanggal >= @start AND s.tanggal <= @end

                    UNION ALL

                    SELECT LTRIM(RTRIM(o.nik)) AS nik, LTRIM(RTRIM(o.departemen)) AS departemen, o.perusahaan_id, q.rating
                    FROM tbl_t_observation o WITH (NOLOCK)
                    JOIN tbl_m_penilaian_kualitas_sap q WITH (NOLOCK) ON q.program_type = 'Observation' AND q.program_id = o.id
                    WHERE o.is_deleted = 0 AND o.date >= @start AND o.date <= @end

                    UNION ALL

                    SELECT LTRIM(RTRIM(c.nik)) AS nik, LTRIM(RTRIM(c.departemen)) AS departemen, c.perusahaan_id, q.rating
                    FROM tbl_t_coaching c WITH (NOLOCK)
                    JOIN tbl_m_penilaian_kualitas_sap q WITH (NOLOCK) ON q.program_type = 'Coaching' AND q.program_id = c.id
                    WHERE c.is_deleted = 0 AND c.tanggal >= @start AND c.tanggal <= @end
                )
                SELECT 'EMP' AS ScopeType, nik AS ScopeKey, COUNT(*) AS TotalRated, AVG(CAST(rating AS FLOAT)) AS AvgRating
                FROM AllRatedReports
                WHERE nik IS NOT NULL AND nik <> ''
                GROUP BY nik

                UNION ALL

                SELECT 'DEPT' AS ScopeType, departemen AS ScopeKey, COUNT(*) AS TotalRated, AVG(CAST(rating AS FLOAT)) AS AvgRating
                FROM AllRatedReports
                WHERE departemen IS NOT NULL AND departemen <> ''
                GROUP BY departemen

                UNION ALL

                SELECT 'COMP' AS ScopeType, CAST(perusahaan_id AS VARCHAR(20)) AS ScopeKey, COUNT(*) AS TotalRated, AVG(CAST(rating AS FLOAT)) AS AvgRating
                FROM AllRatedReports
                WHERE perusahaan_id IS NOT NULL
                GROUP BY perusahaan_id;
            ";

            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = 60;

                var pStart = cmd.CreateParameter();
                pStart.ParameterName = "@start";
                pStart.Value = startOfMonth;
                cmd.Parameters.Add(pStart);

                var pEnd = cmd.CreateParameter();
                pEnd.ParameterName = "@end";
                pEnd.Value = endOfMonth;
                cmd.Parameters.Add(pEnd);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string scopeType = reader.GetString(0);
                    string scopeKey = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                    int totalRated = reader.GetInt32(2);
                    double avgRating = reader.GetDouble(3);

                    if (string.IsNullOrEmpty(scopeKey)) continue;

                    var stat = new QualityStat
                    {
                        TotalRated = totalRated,
                        AvgRating = avgRating
                    };

                    if (scopeType == "EMP")
                    {
                        summary.NikStats[scopeKey] = stat;
                    }
                    else if (scopeType == "DEPT")
                    {
                        summary.DeptStats[scopeKey] = stat;
                    }
                    else if (scopeType == "COMP")
                    {
                        if (int.TryParse(scopeKey, out int compId))
                        {
                            summary.CompStats[compId] = stat;
                        }
                    }
                }

                _cache.Set(cacheKey, summary, TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SapQualityService: Gagal mengambil statistik mutu SAP bulanan {Year}-{Month}", year, month);
            }

            return summary;
        }
    }
}
