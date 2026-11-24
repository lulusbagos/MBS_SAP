using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MBS_SAP.Services
{
    public class IndeximRosterMonthSummary
    {
        public string Nik { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public int TotalDaysInMonth { get; set; }
        public int WorkDays { get; set; }
        public int OffDays { get; set; }
        public bool HasOneRoster { get; set; }
        public Dictionary<DateTime, string> DailyStatuses { get; set; } = new();
        public Dictionary<DateTime, string> DailyCategories { get; set; } = new();
    }

    /// <summary>
    /// Service terisolasi KHUSUS untuk karyawan PT INDEXIM COALINDO (id_perusahaan = 1).
    /// Menarik dan mengagregasi data roster harian dari [DB_ONE].[dbo].[tbl_m_roster_detail].
    /// Status berkategori WORK dihitung sebagai hari onsite / dinas.
    /// Status berkategori OFF dihitung sebagai hari cuti / offsite.
    /// Mitra kerja lain tidak menggunakan layanan ini.
    /// </summary>
    public class IndeximRosterService
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<IndeximRosterService> _logger;

        public const int IndeximCompanyId = 1;

        public IndeximRosterService(
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<IndeximRosterService> logger)
        {
            _configuration = configuration;
            _cache = cache;
            _logger = logger;
        }

        public bool IsIndexim(int? idPerusahaan, string? namaPerusahaan = null)
        {
            if (idPerusahaan.HasValue && idPerusahaan.Value == IndeximCompanyId) return true;
            if (!string.IsNullOrEmpty(namaPerusahaan) && namaPerusahaan.Contains("INDEXIM", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public string GetCacheKey(int year, int month) => $"IndeximRosterSummaries_{year}_{month}";

        public void InvalidateCache(int? year = null, int? month = null)
        {
            if (year.HasValue && month.HasValue)
            {
                _cache.Remove(GetCacheKey(year.Value, month.Value));
            }
            else
            {
                var now = DateTime.Today;
                _cache.Remove(GetCacheKey(now.Year, now.Month));
                var prev = now.AddMonths(-1);
                _cache.Remove(GetCacheKey(prev.Year, prev.Month));
                var next = now.AddMonths(1);
                _cache.Remove(GetCacheKey(next.Year, next.Month));
            }
        }

        public async Task<Dictionary<string, IndeximRosterMonthSummary>> GetIndeximRosterSummariesAsync(int year, int month)
        {
            var cacheKey = GetCacheKey(year, month);
            if (_cache.TryGetValue(cacheKey, out Dictionary<string, IndeximRosterMonthSummary>? cached) && cached != null)
            {
                return cached;
            }

            var result = new Dictionary<string, IndeximRosterMonthSummary>(StringComparer.OrdinalIgnoreCase);
            int daysInMonth = DateTime.DaysInMonth(year, month);
            var startDate = new DateTime(year, month, 1);
            var endDate = new DateTime(year, month, daysInMonth);

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connStr))
            {
                _logger.LogWarning("IndeximRosterService: DefaultConnection string is empty.");
                return result;
            }

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                const string sql = @"
                    SELECT 
                        r.nik,
                        r.working_date,
                        r.status,
                        CASE 
                            WHEN r.status = 'Travel Day' OR r.status LIKE '%Travel%' THEN 'OFF'
                            WHEN k.kategori = 'WORK' THEN 'WORK'
                            WHEN k.kategori = 'OFF' THEN 'OFF'
                            WHEN r.status IN ('07:00-19:00', '19:00-07:00', '06:00-18:00', '06:30-18:30', 
                                              '18:00-06:00', '18:30-06:30', '15:00-23:00', '04:00-12:00', 
                                              '05:00-13:00', '07:00-17:00', 'IC_0700-1900', 'IC_1900-0700', 
                                              'IC_0600-1800', 'IC_0630-1830', 'IC_1830-0630', 'IC_LONG_SHIFT', 
                                              'IC_LONG_SHIFT_2', 'TGS', 'Tugas', 'SIANG', 'MALAM', 'SH_65') THEN 'WORK'
                            ELSE 'OFF'
                        END AS classified_type
                    FROM DB_ONE.dbo.tbl_m_roster_detail r WITH (NOLOCK)
                    LEFT JOIN (
                        SELECT kode, MAX(kategori) AS kategori
                        FROM DB_ONE.dbo.tbl_m_roster_keterangan WITH (NOLOCK)
                        GROUP BY kode
                    ) k ON r.status = k.kode
                    WHERE r.working_date >= @startDate AND r.working_date <= @endDate
                      AND r.nik IS NOT NULL AND r.nik <> ''
                    ORDER BY r.nik, r.working_date;
                ";

                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 45;
                cmd.Parameters.Add("@startDate", SqlDbType.Date).Value = startDate;
                cmd.Parameters.Add("@endDate", SqlDbType.Date).Value = endDate;

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string nik = reader.GetString(0).Trim();
                    DateTime wDate = reader.GetDateTime(1);
                    string status = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    string classifiedType = reader.IsDBNull(3) ? "OFF" : reader.GetString(3);

                    if (!result.TryGetValue(nik, out var summary))
                    {
                        summary = new IndeximRosterMonthSummary
                        {
                            Nik = nik,
                            Year = year,
                            Month = month,
                            TotalDaysInMonth = daysInMonth,
                            WorkDays = 0,
                            OffDays = 0,
                            HasOneRoster = true
                        };
                        result[nik] = summary;
                    }

                    if (classifiedType == "WORK")
                    {
                        summary.WorkDays++;
                    }
                    else
                    {
                        summary.OffDays++;
                    }

                    summary.DailyStatuses[wDate.Date] = status;
                    summary.DailyCategories[wDate.Date] = classifiedType;
                }

                _cache.Set(cacheKey, result, TimeSpan.FromHours(4));
                _logger.LogInformation("IndeximRosterService: Loaded roster from DB_ONE for {Year}-{Month:D2} for {Count} employees.",
                    year, month, result.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndeximRosterService: Error querying DB_ONE.dbo.tbl_m_roster_detail for {Year}-{Month:D2}", year, month);
            }

            return result;
        }

        public async Task<IndeximRosterMonthSummary?> GetEmployeeIndeximRosterAsync(string nik, int year, int month)
        {
            if (string.IsNullOrWhiteSpace(nik)) return null;
            var summaries = await GetIndeximRosterSummariesAsync(year, month);
            if (summaries.TryGetValue(nik.Trim(), out var summary))
            {
                return summary;
            }
            return null;
        }
    }
}
