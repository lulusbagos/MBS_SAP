using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MBS_SAP.Data;
using MBS_SAP.Models;
using Npgsql;

namespace MBS_SAP.Services
{
    public class PostgresReplicationOptions
    {
        public bool Enabled { get; set; } = false;
        public string ConnectionString { get; set; } = string.Empty;
        public string HazardSourceView { get; set; } = "vw_hazardreportdetail";
        public string InspectionSourceView { get; set; } = "vw_inspectiondetail";
        public int DefaultLookbackDays { get; set; } = 3650;
        public string[] AllowedCompanies { get; set; } =
        {
            "PT INDEXIM COALINDO",
            "PT UNGGUL DINAMIKA UTAMA",
            "PT UNGGUL ABADI INFRASTRUKTUR"
        };
    }

    public class PostgresReplicationResult
    {
        public int HazardInserted { get; set; }
        public int HazardUpdated { get; set; }
        public int HazardSkipped { get; set; }
        public int HazardSkippedCompany { get; set; }
        public int InspectionInserted { get; set; }
        public int InspectionUpdated { get; set; }
        public int InspectionSkipped { get; set; }
        public int InspectionSkippedCompany { get; set; }
        public int LookbackDays { get; set; }
    }

    internal record HazardSourceRow(
        string? SourceCode,
        DateTime Tanggal,
        TimeSpan Waktu,
        string Nama,
        string Nik,
        string? Departemen,
        string? CompanyName,
        string? Area,
        string? Lokasi,
        string? DetilLokasi,
        string Temuan,
        string? KategoriBahaya,
        string? JenisBahaya,
        string? JenisKetidaksesuaian,
        string? TingkatResiko,
        string? Perbaikan,
        string? TindakanPerbaikan,
        string? Pja,
        string? NikPja,
        string? DepartemenPja,
        string StatusTemuan,
        string? FotoTemuan,
        DateTime CreatedAt);

    internal record InspectionSourceRow(
        string? SourceCode,
        DateTime Tanggal,
        TimeSpan Waktu,
        string Nama,
        string Nik,
        string? Departemen,
        string? CompanyName,
        string? Area,
        string? Lokasi,
        string? DetilLokasi,
        string JenisInspeksi,
        string? Pja,
        string? NikPja,
        string? DepartemenPja,
        string? Catatan,
        string? LampiranJson,
        DateTime CreatedAt);

    public class PostgresReplicationService
    {
        private static readonly Regex SqlIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_\.]*$", RegexOptions.Compiled);

        private readonly AppDbContext _context;
        private readonly PostgresReplicationOptions _options;
        private readonly ILogger<PostgresReplicationService> _logger;

        public PostgresReplicationService(
            AppDbContext context,
            IOptions<PostgresReplicationOptions> options,
            ILogger<PostgresReplicationService> logger)
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<PostgresReplicationResult> ReplicateAsync(int? lookbackDays, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                throw new InvalidOperationException("Postgres replication belum diaktifkan. Set PostgresReplication:Enabled = true.");
            }

            if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                throw new InvalidOperationException("Connection string PostgreSQL belum diisi.");
            }

            if (_options.AllowedCompanies == null || _options.AllowedCompanies.Length == 0)
            {
                throw new InvalidOperationException("AllowedCompanies belum diisi.");
            }

            var hazardView = ValidateSqlIdentifier(_options.HazardSourceView, nameof(_options.HazardSourceView));
            var inspectionView = ValidateSqlIdentifier(_options.InspectionSourceView, nameof(_options.InspectionSourceView));

            var effectiveLookback = lookbackDays.GetValueOrDefault(_options.DefaultLookbackDays);
            if (effectiveLookback < 1)
            {
                effectiveLookback = 1;
            }

            var since = DateTime.Now.Date.AddDays(-effectiveLookback);

            var allowedCompanies = _options.AllowedCompanies
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeText)
                .ToHashSet();

            var companyIdMap = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .Select(p => new { p.PerusahaanId, p.NamaPerusahaan })
                .ToListAsync(cancellationToken);

            var normalizedCompanyIdMap = companyIdMap
                .Where(x => !string.IsNullOrWhiteSpace(x.NamaPerusahaan))
                .GroupBy(x => NormalizeText(x.NamaPerusahaan))
                .ToDictionary(g => g.Key, g => g.First().PerusahaanId);

            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var hazardSourceRows = await ReadHazardsAsync(connection, hazardView, since, cancellationToken);
            var inspectionSourceRows = await ReadInspectionsAsync(connection, inspectionView, since, cancellationToken);

            var result = new PostgresReplicationResult
            {
                LookbackDays = effectiveLookback
            };

            var existingHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var hazardMap = existingHazards
                .GroupBy(h => BuildHazardKey(h.Nik, h.Tanggal, h.Temuan, h.Lokasi, h.PerusahaanId, null))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in hazardSourceRows)
            {
                var normalizedCompany = NormalizeText(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.HazardSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildHazardKey(row.Nik, row.Tanggal, row.Temuan, row.Lokasi, perusahaanId, null);

                if (hazardMap.TryGetValue(key, out var existingHazard))
                {
                    var hasHazardChanges = false;

                    var newStatus = Truncate(row.StatusTemuan, 50) ?? "Open";
                    if (!string.Equals(existingHazard.StatusTemuan ?? string.Empty, newStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        existingHazard.StatusTemuan = newStatus;
                        hasHazardChanges = true;
                    }

                    var newRisk = Truncate(row.TingkatResiko, 50);
                    if (!string.Equals(existingHazard.TingkatResiko ?? string.Empty, newRisk ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingHazard.TingkatResiko = newRisk;
                        hasHazardChanges = true;
                    }

                    var newPja = Truncate(row.Pja, 150);
                    if (!string.Equals(existingHazard.Pja ?? string.Empty, newPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingHazard.Pja = newPja;
                        hasHazardChanges = true;
                    }

                    var newNikPja = Truncate(row.NikPja, 50);
                    if (!string.Equals(existingHazard.NikPja ?? string.Empty, newNikPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingHazard.NikPja = newNikPja;
                        hasHazardChanges = true;
                    }

                    var newDeptPja = Truncate(row.DepartemenPja, 150);
                    if (!string.Equals(existingHazard.DepartemenPja ?? string.Empty, newDeptPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingHazard.DepartemenPja = newDeptPja;
                        hasHazardChanges = true;
                    }

                    if (hasHazardChanges)
                    {
                        result.HazardUpdated++;
                    }
                    else
                    {
                        result.HazardSkipped++;
                    }

                    continue;
                }

                var report = new HazardReport
                {
                    FotoTemuan = row.FotoTemuan,
                    Tanggal = row.Tanggal,
                    Waktu = row.Waktu,
                    Nama = Truncate(row.Nama, 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 150),
                    Area = Truncate(row.Area, 150),
                    Lokasi = Truncate(row.Lokasi, 150),
                    DetilLokasi = Truncate(row.DetilLokasi, 250),
                    Temuan = Truncate(row.Temuan, 1000) ?? "-",
                    KategoriBahaya = Truncate(row.KategoriBahaya, 100),
                    JenisBahaya = Truncate(row.JenisBahaya, 100),
                    JenisKetidaksesuaian = Truncate(row.JenisKetidaksesuaian, 150),
                    TingkatResiko = Truncate(row.TingkatResiko, 50),
                    Perbaikan = row.Perbaikan,
                    TindakanPerbaikan = row.TindakanPerbaikan,
                    Pja = Truncate(row.Pja, 150),
                    NikPja = Truncate(row.NikPja, 50),
                    DepartemenPja = Truncate(row.DepartemenPja, 150),
                    StatusTemuan = Truncate(row.StatusTemuan, 50) ?? "Open",
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = row.CreatedAt
                };

                _context.HazardReports.Add(report);
                hazardMap[key] = report;
                result.HazardInserted++;
            }

            var existingInspections = await _context.Inspections
                .Where(i => !i.IsDeleted && i.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var inspectionMap = existingInspections
                .GroupBy(i => BuildInspectionKey(i.Nik, i.Tanggal, i.JenisInspeksi, i.Lokasi, i.PerusahaanId, null))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in inspectionSourceRows)
            {
                var normalizedCompany = NormalizeText(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.InspectionSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildInspectionKey(row.Nik, row.Tanggal, row.JenisInspeksi, row.Lokasi, perusahaanId, null);

                if (inspectionMap.TryGetValue(key, out var existingInspection))
                {
                    var hasInspectionChanges = false;

                    var newPja = Truncate(row.Pja, 150);
                    if (!string.Equals(existingInspection.Pja ?? string.Empty, newPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingInspection.Pja = newPja;
                        hasInspectionChanges = true;
                    }

                    var newNikPja = Truncate(row.NikPja, 50);
                    if (!string.Equals(existingInspection.NikPja ?? string.Empty, newNikPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingInspection.NikPja = newNikPja;
                        hasInspectionChanges = true;
                    }

                    var newDeptPja = Truncate(row.DepartemenPja, 150);
                    if (!string.Equals(existingInspection.DepartemenPja ?? string.Empty, newDeptPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingInspection.DepartemenPja = newDeptPja;
                        hasInspectionChanges = true;
                    }

                    var newCatatan = Truncate(row.Catatan, 2000);
                    if (!string.Equals(existingInspection.Catatan ?? string.Empty, newCatatan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingInspection.Catatan = newCatatan;
                        hasInspectionChanges = true;
                    }

                    if (hasInspectionChanges)
                    {
                        result.InspectionUpdated++;
                    }
                    else
                    {
                        result.InspectionSkipped++;
                    }

                    continue;
                }

                var report = new Inspection
                {
                    Tanggal = row.Tanggal,
                    Waktu = row.Waktu,
                    Nama = Truncate(row.Nama, 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 150),
                    Area = Truncate(row.Area, 150),
                    Lokasi = Truncate(row.Lokasi, 150),
                    DetilLokasi = Truncate(row.DetilLokasi, 250),
                    JenisInspeksi = Truncate(row.JenisInspeksi, 150) ?? "General",
                    Pja = Truncate(row.Pja, 150),
                    NikPja = Truncate(row.NikPja, 50),
                    DepartemenPja = Truncate(row.DepartemenPja, 150),
                    PerusahaanId = perusahaanId,
                    Catatan = Truncate(row.Catatan, 2000),
                    LampiranJson = row.LampiranJson,
                    IsDeleted = false,
                    CreatedAt = row.CreatedAt
                };

                _context.Inspections.Add(report);
                inspectionMap[key] = report;
                result.InspectionInserted++;
            }

            if (result.HazardInserted > 0 || result.HazardUpdated > 0 || result.InspectionInserted > 0 || result.InspectionUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Postgres replication completed. Hazard +{HazardInserted} ~{HazardUpdated} skip {HazardSkipped} skipCompany {HazardSkippedCompany}; Inspection +{InspectionInserted} ~{InspectionUpdated} skip {InspectionSkipped} skipCompany {InspectionSkippedCompany}",
                result.HazardInserted,
                result.HazardUpdated,
                result.HazardSkipped,
                result.HazardSkippedCompany,
                result.InspectionInserted,
                result.InspectionUpdated,
                result.InspectionSkipped,
                result.InspectionSkippedCompany);

            return result;
        }

        private static string ValidateSqlIdentifier(string raw, string name)
        {
            var value = (raw ?? string.Empty).Trim();
            if (!SqlIdentifierRegex.IsMatch(value))
            {
                throw new InvalidOperationException($"Konfigurasi {name} tidak valid.");
            }

            return value;
        }

        private static string BuildHazardKey(string nik, DateTime tanggal, string temuan, string? lokasi, int? perusahaanId, string? sourceCode)
        {
            var companyKey = perusahaanId?.ToString() ?? "0";
            var sourceKey = string.IsNullOrWhiteSpace(sourceCode) ? "-" : NormalizeText(sourceCode);
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{NormalizeText(temuan)}|{NormalizeText(lokasi)}|{companyKey}|{sourceKey}";
        }

        private static string BuildInspectionKey(string nik, DateTime tanggal, string jenisInspeksi, string? lokasi, int? perusahaanId, string? sourceCode)
        {
            var companyKey = perusahaanId?.ToString() ?? "0";
            var sourceKey = string.IsNullOrWhiteSpace(sourceCode) ? "-" : NormalizeText(sourceCode);
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{NormalizeText(jenisInspeksi)}|{NormalizeText(lokasi)}|{companyKey}|{sourceKey}";
        }

        private static string NormalizeText(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static async Task<List<HazardSourceRow>> ReadHazardsAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    title,
    area_name,
    location_name,
    location_detail,
    hazard_name,
    hazard_type_name,
    hazard_subtype_name,
    hazard_danger_name,
    remark,
    repair,
    repair_remark,
    status,
    pja_name,
    pja_nik,
    pja_departemen,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    foto_temuan
FROM {sourceView}
WHERE date >= @sinceDate
ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);

            var data = new List<HazardSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;
                var createdAt = tanggal.Add(waktu);

                var title = GetString(reader, "title");
                var hazardName = GetString(reader, "hazard_name");
                var remark = GetString(reader, "remark");

                var source = new HazardSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: GetString(reader, "employee_nik") ?? "UNKNOWN",
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: GetString(reader, "employee_company"),
                    Area: GetString(reader, "area_name"),
                    Lokasi: GetString(reader, "location_name"),
                    DetilLokasi: GetString(reader, "location_detail"),
                    Temuan: !string.IsNullOrWhiteSpace(hazardName)
                        ? hazardName!
                        : (!string.IsNullOrWhiteSpace(title) ? title! : "-"),
                    KategoriBahaya: GetString(reader, "hazard_name"),
                    JenisBahaya: GetString(reader, "hazard_type_name"),
                    JenisKetidaksesuaian: GetString(reader, "hazard_subtype_name"),
                    TingkatResiko: GetString(reader, "hazard_danger_name"),
                    Perbaikan: GetString(reader, "repair"),
                    TindakanPerbaikan: GetString(reader, "repair_remark"),
                    Pja: GetString(reader, "pja_name"),
                    NikPja: GetString(reader, "pja_nik"),
                    DepartemenPja: GetString(reader, "pja_departemen"),
                    StatusTemuan: NormalizeHazardStatus(GetString(reader, "status")),
                    FotoTemuan: GetString(reader, "foto_temuan"),
                    CreatedAt: createdAt);

                data.Add(source);
            }

            return data;
        }

        private static async Task<List<InspectionSourceRow>> ReadInspectionsAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    title,
    area_name,
    location_name,
    location_detail,
    remark,
    category,
    pja_name,
    pja_nik,
    pja_departemen,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    status
FROM {sourceView}
WHERE date >= @sinceDate
ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);

            var data = new List<InspectionSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;
                var createdAt = tanggal.Add(waktu);

                var title = GetString(reader, "title");
                var category = GetString(reader, "category");

                var source = new InspectionSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: GetString(reader, "employee_nik") ?? "UNKNOWN",
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: GetString(reader, "employee_company"),
                    Area: GetString(reader, "area_name"),
                    Lokasi: GetString(reader, "location_name"),
                    DetilLokasi: GetString(reader, "location_detail"),
                    JenisInspeksi: !string.IsNullOrWhiteSpace(category)
                        ? category!
                        : (!string.IsNullOrWhiteSpace(title) ? title! : "General"),
                    Pja: GetString(reader, "pja_name"),
                    NikPja: GetString(reader, "pja_nik"),
                    DepartemenPja: GetString(reader, "pja_departemen"),
                    Catatan: GetString(reader, "remark"),
                    LampiranJson: null,
                    CreatedAt: createdAt);

                data.Add(source);
            }

            return data;
        }

        private static string NormalizeHazardStatus(string? sourceStatus)
        {
            var normalized = NormalizeText(sourceStatus);
            if (normalized == "1" || normalized == "close" || normalized == "closed")
            {
                return "Closed";
            }

            if (normalized == "0" || normalized == "open")
            {
                return "Open";
            }

            return "Open";
        }

        private static string? GetString(IDataRecord reader, string column)
        {
            var ordinal = TryGetOrdinal(reader, column);
            if (ordinal < 0 || reader.IsDBNull(ordinal)) return null;
            return reader.GetValue(ordinal)?.ToString();
        }

        private static DateTime? GetDateTime(IDataRecord reader, string column)
        {
            var ordinal = TryGetOrdinal(reader, column);
            if (ordinal < 0 || reader.IsDBNull(ordinal)) return null;
            return Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static TimeSpan? GetTimeSpan(IDataRecord reader, string column)
        {
            var ordinal = TryGetOrdinal(reader, column);
            if (ordinal < 0 || reader.IsDBNull(ordinal)) return null;

            var raw = reader.GetValue(ordinal);
            if (raw is TimeSpan ts) return ts;

            if (TimeSpan.TryParse(raw?.ToString(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static int TryGetOrdinal(IDataRecord reader, string column)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}