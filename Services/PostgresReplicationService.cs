using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MBS_SAP.Data;
using MBS_SAP.Models;
using Npgsql;
using MBS_SAP.Controllers;

namespace MBS_SAP.Services
{
    public class PostgresReplicationOptions
    {
        public bool Enabled { get; set; } = false;
        public string ConnectionString { get; set; } = string.Empty;
        public string HazardSourceView { get; set; } = "vw_hazardreportdetail";
        public string InspectionSourceView { get; set; } = "vw_inspectiondetail";
        public string CoachingSourceView { get; set; } = "vw_coachingdetail";
        public string ObservationSourceView { get; set; } = "vw_observationdetail";
        public string P2hSourceView { get; set; } = "vw_p2hdetail";
        public string P5mSourceView { get; set; } = "vw_p5mdetail";
        public string SafetyTalkSourceView { get; set; } = "vw_safetytalkdetail";
        public int DefaultLookbackDays { get; set; } = 3650;
        public string[] AllowedCompanies { get; set; } =
        {
            "PT INDEXIM COALINDO",
            "PT UNGGUL DINAMIKA UTAMA",
            "PT UNGGUL ABADI INFRASTRUKTUR",
            "PT KALIMANTAN PRIMA PERSADA",
            "PT MEGA GLOBAL ENERGY",
            "PT PELAYARAN GANESHA LAUTJAYA"
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

        public int CoachingInserted { get; set; }
        public int CoachingUpdated { get; set; }
        public int CoachingSkipped { get; set; }
        public int CoachingSkippedCompany { get; set; }

        public int ObservationInserted { get; set; }
        public int ObservationUpdated { get; set; }
        public int ObservationSkipped { get; set; }
        public int ObservationSkippedCompany { get; set; }

        public int P2hInserted { get; set; }
        public int P2hUpdated { get; set; }
        public int P2hSkipped { get; set; }
        public int P2hSkippedCompany { get; set; }

        public int P5mInserted { get; set; }
        public int P5mUpdated { get; set; }
        public int P5mSkipped { get; set; }
        public int P5mSkippedCompany { get; set; }

        public int SafetyTalkInserted { get; set; }
        public int SafetyTalkUpdated { get; set; }
        public int SafetyTalkSkipped { get; set; }
        public int SafetyTalkSkippedCompany { get; set; }

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

    internal record CoachingSourceRow(
        string? SourceCode,
        DateTime Tanggal,
        TimeSpan Waktu,
        string TrainerNama,
        string TrainerNik,
        string EmployeeNama,
        string EmployeeNik,
        string? EmployeeDepartemen,
        string? EmployeeCompany,
        string? Area,
        string? Lokasi,
        string? DetilLokasi,
        string? Tema,
        string? Feedback,
        string? Komitmen,
        string? Foto);

    internal record ObservationSourceRow(
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
        string? Kegiatan,
        string? DeptDiamati,
        string? Doc,
        string? Resiko,
        string? Hasil,
        string? Perihal,
        string? Keterangan);

    internal record P2hSourceRow(
        string? SourceCode,
        DateTime Tanggal,
        TimeSpan Waktu,
        string Nama,
        string Nik,
        string? Departemen,
        string? CompanyName,
        string JenisKendaraan,
        string NoLambung,
        double Kilometer,
        string Merek,
        string SimperKimper,
        string Type,
        string YesNo,
        string? Remark,
        string Name,
        string? Status);

    internal record P5mSourceRow(
        string? SourceCode,
        DateTime Tanggal,
        TimeSpan Waktu,
        string Nama,
        string Nik,
        string? Departemen,
        string? CompanyName,
        string? DetilLokasi,
        string? Topik,
        string? Judul,
        string? Keterangan,
        string ListPertanyaan,
        string Jawaban,
        string? Catatan,
        string? Foto);

    internal record SafetyTalkSourceRow(
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
        string? Judul,
        string? Keterangan,
        string? FotoDiri,
        string? FotoKegiatan);

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
            var coachingView = ValidateSqlIdentifier(_options.CoachingSourceView, nameof(_options.CoachingSourceView));
            var observationView = ValidateSqlIdentifier(_options.ObservationSourceView, nameof(_options.ObservationSourceView));
            var p2hView = ValidateSqlIdentifier(_options.P2hSourceView, nameof(_options.P2hSourceView));
            var p5mView = ValidateSqlIdentifier(_options.P5mSourceView, nameof(_options.P5mSourceView));
            var safetyTalkView = ValidateSqlIdentifier(_options.SafetyTalkSourceView, nameof(_options.SafetyTalkSourceView));

            var effectiveLookback = lookbackDays.GetValueOrDefault(_options.DefaultLookbackDays);
            if (effectiveLookback < 1)
            {
                effectiveLookback = 1;
            }

            var since = DateTime.Now.Date.AddDays(-effectiveLookback);

            var allowedCompanies = _options.AllowedCompanies
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeCompanyName)
                .ToHashSet();

            var companyIdMap = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .Select(p => new { p.PerusahaanId, p.NamaPerusahaan })
                .ToListAsync(cancellationToken);

            var normalizedCompanyIdMap = companyIdMap
                .Where(x => !string.IsNullOrWhiteSpace(x.NamaPerusahaan))
                .GroupBy(x => NormalizeCompanyName(x.NamaPerusahaan))
                .ToDictionary(g => g.Key, g => g.First().PerusahaanId);

            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var hazardSourceRows = await ReadHazardsAsync(connection, hazardView, since, cancellationToken);
            var inspectionSourceRows = await ReadInspectionsAsync(connection, inspectionView, since, cancellationToken);
            var coachingSourceRows = await ReadCoachingsAsync(connection, coachingView, since, cancellationToken);
            var observationSourceRows = await ReadObservationsAsync(connection, observationView, since, cancellationToken);
            var p2hSourceRows = await ReadP2hsAsync(connection, p2hView, since, cancellationToken);
            var p5mSourceRows = await ReadP5msAsync(connection, p5mView, since, cancellationToken);
            var safetyTalkSourceRows = await ReadSafetyTalksAsync(connection, safetyTalkView, since, cancellationToken);

            // Pre-load official employee names from SQL Server to prevent name mismatches
            var allSourceNiks = hazardSourceRows.Select(r => r.Nik)
                .Concat(inspectionSourceRows.Select(r => r.Nik))
                .Concat(coachingSourceRows.Select(r => r.TrainerNik))
                .Concat(observationSourceRows.Select(r => r.Nik))
                .Concat(p2hSourceRows.Select(r => r.Nik))
                .Concat(p5mSourceRows.Select(r => r.Nik))
                .Concat(safetyTalkSourceRows.Select(r => r.Nik))
                .Where(nik => !string.IsNullOrWhiteSpace(nik))
                .Select(nik => nik.Trim())
                .Distinct()
                .ToList();

            var officialNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (allSourceNiks.Any())
            {
                var namesList = await (from k in _context.Karyawans
                                       join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                       where allSourceNiks.Contains(k.NoNik)
                                       select new { k.NoNik, p.NamaLengkap })
                                       .ToListAsync(cancellationToken);
                foreach (var x in namesList)
                {
                    if (!string.IsNullOrWhiteSpace(x.NoNik) && !officialNameMap.ContainsKey(x.NoNik))
                    {
                        officialNameMap[x.NoNik] = x.NamaLengkap;
                    }
                }
            }

            string GetOfficialName(string? sourceNik, string? fallbackName)
            {
                if (!string.IsNullOrWhiteSpace(sourceNik) && officialNameMap.TryGetValue(sourceNik.Trim(), out var officialName))
                {
                    return officialName;
                }
                return fallbackName ?? "Unknown";
            }

            var result = new PostgresReplicationResult
            {
                LookbackDays = effectiveLookback
            };

            var existingHazardsData = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.Tanggal >= since.AddDays(-7))
                .Select(h => new HazardReplicationDto
                {
                    Id = h.Id,
                    Nik = h.Nik,
                    Tanggal = h.Tanggal,
                    Waktu = h.Waktu,
                    Area = h.Area,
                    Temuan = h.Temuan,
                    Lokasi = h.Lokasi,
                    PerusahaanId = h.PerusahaanId,
                    StatusTemuan = h.StatusTemuan,
                    TingkatResiko = h.TingkatResiko,
                    Pja = h.Pja,
                    NikPja = h.NikPja,
                    DepartemenPja = h.DepartemenPja,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var hazardMap = existingHazardsData
                .GroupBy(h => BuildHazardKey(h.Nik ?? string.Empty, h.Tanggal, h.Waktu, h.Temuan ?? string.Empty, h.Area, h.Lokasi, h.PerusahaanId))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in hazardSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.HazardSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildHazardKey(row.Nik, row.Tanggal, row.Waktu, row.Temuan, row.Area, row.Lokasi, perusahaanId);

                if (hazardMap.TryGetValue(key, out var existingHazard))
                {
                    var hasHazardChanges = false;

                    var newStatus = Truncate(row.StatusTemuan, 50) ?? "Open";
                    var newRisk = Truncate(row.TingkatResiko, 50);
                    var newPja = Truncate(row.Pja, 150);
                    var newNikPja = Truncate(row.NikPja, 50);
                    var newDeptPja = Truncate(row.DepartemenPja, 150);

                    if (!string.Equals(existingHazard.StatusTemuan ?? string.Empty, newStatus, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.TingkatResiko ?? string.Empty, newRisk ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.Pja ?? string.Empty, newPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.NikPja ?? string.Empty, newNikPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.DepartemenPja ?? string.Empty, newDeptPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        var realHazard = await _context.HazardReports.FindAsync(new object[] { existingHazard.Id }, cancellationToken);
                        if (realHazard != null)
                        {
                            realHazard.StatusTemuan = newStatus;
                            realHazard.TingkatResiko = newRisk;
                            realHazard.Pja = newPja;
                            realHazard.NikPja = newNikPja;
                            realHazard.DepartemenPja = newDeptPja;
                            hasHazardChanges = true;
                        }
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
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
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
                hazardMap[key] = new HazardReplicationDto
                {
                    Id = report.Id,
                    Nik = report.Nik,
                    Tanggal = report.Tanggal,
                    Waktu = report.Waktu,
                    Area = report.Area,
                    Temuan = report.Temuan,
                    Lokasi = report.Lokasi,
                    PerusahaanId = report.PerusahaanId,
                    StatusTemuan = report.StatusTemuan,
                    TingkatResiko = report.TingkatResiko,
                    Pja = report.Pja,
                    NikPja = report.NikPja,
                    DepartemenPja = report.DepartemenPja,
                    CreatedAt = report.CreatedAt
                };
                result.HazardInserted++;
            }

            var existingInspectionsData = await _context.Inspections
                .Where(i => !i.IsDeleted && i.Tanggal >= since.AddDays(-7))
                .Select(i => new InspectionReplicationDto
                {
                    Id = i.Id,
                    Nik = i.Nik,
                    Tanggal = i.Tanggal,
                    Waktu = i.Waktu,
                    JenisInspeksi = i.JenisInspeksi,
                    Lokasi = i.Lokasi,
                    PerusahaanId = i.PerusahaanId,
                    Pja = i.Pja,
                    NikPja = i.NikPja,
                    DepartemenPja = i.DepartemenPja,
                    Catatan = i.Catatan,
                    CreatedAt = i.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var inspectionMap = existingInspectionsData
                .GroupBy(i => BuildInspectionKey(i.Nik ?? string.Empty, i.Tanggal, i.Waktu, i.JenisInspeksi ?? string.Empty, i.Lokasi, i.PerusahaanId))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in inspectionSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.InspectionSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildInspectionKey(row.Nik, row.Tanggal, row.Waktu, row.JenisInspeksi, row.Lokasi, perusahaanId);

                if (inspectionMap.TryGetValue(key, out var existingInspection))
                {
                    var hasInspectionChanges = false;

                    var newPja = Truncate(row.Pja, 150);
                    var newNikPja = Truncate(row.NikPja, 50);
                    var newDeptPja = Truncate(row.DepartemenPja, 150);
                    var newCatatan = Truncate(row.Catatan, 2000);

                    if (!string.Equals(existingInspection.Pja ?? string.Empty, newPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingInspection.NikPja ?? string.Empty, newNikPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingInspection.DepartemenPja ?? string.Empty, newDeptPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingInspection.Catatan ?? string.Empty, newCatatan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        var realInspection = await _context.Inspections.FindAsync(new object[] { existingInspection.Id }, cancellationToken);
                        if (realInspection != null)
                        {
                            realInspection.Pja = newPja;
                            realInspection.NikPja = newNikPja;
                            realInspection.DepartemenPja = newDeptPja;
                            realInspection.Catatan = newCatatan;
                            hasInspectionChanges = true;
                        }
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
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
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
                inspectionMap[key] = new InspectionReplicationDto
                {
                    Id = report.Id,
                    Nik = report.Nik,
                    Tanggal = report.Tanggal,
                    JenisInspeksi = report.JenisInspeksi,
                    Lokasi = report.Lokasi,
                    PerusahaanId = report.PerusahaanId,
                    Pja = report.Pja,
                    NikPja = report.NikPja,
                    DepartemenPja = report.DepartemenPja,
                    Catatan = report.Catatan,
                    CreatedAt = report.CreatedAt
                };
                result.InspectionInserted++;
            }

            var existingCoachings = await _context.Coachings
                .Include(c => c.Participants)
                .Where(c => !c.IsDeleted && c.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var coachingMap = existingCoachings
                .GroupBy(c => BuildCoachingKey(c.Nik, c.Tanggal, c.Waktu, c.Tema))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            var coachingGroups = coachingSourceRows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.SourceCode) ? BuildCoachingKey(row.TrainerNik, row.Tanggal, row.Waktu, row.Tema) : row.SourceCode);

            foreach (var group in coachingGroups)
            {
                var first = group.First();
                var normalizedCompany = NormalizeCompanyName(first.EmployeeCompany);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.CoachingSkippedCompany += group.Count();
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildCoachingKey(first.TrainerNik, first.Tanggal, first.Waktu, first.Tema);

                if (coachingMap.TryGetValue(key, out var existingCoaching))
                {
                    var hasChanges = false;
                    
                    var newFeedback = first.Feedback;
                    if (!string.Equals(existingCoaching.Feedback ?? string.Empty, newFeedback ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingCoaching.Feedback = newFeedback;
                        hasChanges = true;
                    }

                    var newKomitmen = first.Komitmen;
                    if (!string.Equals(existingCoaching.Komitmen ?? string.Empty, newKomitmen ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingCoaching.Komitmen = newKomitmen;
                        hasChanges = true;
                    }

                    var currentParticipantsMap = existingCoaching.Participants.ToDictionary(p => NormalizeText(p.Nik));
                    var incomingParticipants = group
                        .Select(g => new { g.EmployeeNik, g.EmployeeNama })
                        .DistinctBy(p => NormalizeText(p.EmployeeNik))
                        .ToList();

                    var participantChanges = false;
                    if (currentParticipantsMap.Count != incomingParticipants.Count)
                    {
                        participantChanges = true;
                    }
                    else
                    {
                        foreach (var inc in incomingParticipants)
                        {
                            if (!currentParticipantsMap.ContainsKey(NormalizeText(inc.EmployeeNik)))
                            {
                                participantChanges = true;
                                break;
                            }
                        }
                    }

                    if (participantChanges)
                    {
                        _context.CoachingParticipants.RemoveRange(existingCoaching.Participants);
                        existingCoaching.Participants = incomingParticipants.Select(inc => new CoachingParticipant
                        {
                            Nik = Truncate(inc.EmployeeNik, 50) ?? "UNKNOWN",
                            Nama = Truncate(inc.EmployeeNama, 150) ?? "Unknown"
                        }).ToList();
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.CoachingUpdated++;
                    }
                    else
                    {
                        result.CoachingSkipped++;
                    }
                    continue;
                }

                var coaching = new Coaching
                {
                    Foto = first.Foto,
                    Tanggal = first.Tanggal,
                    Waktu = first.Waktu,
                    Nama = Truncate(GetOfficialName(first.TrainerNik, first.TrainerNama), 150) ?? "Unknown",
                    Nik = Truncate(first.TrainerNik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(first.EmployeeDepartemen, 150),
                    Area = Truncate(first.Area, 150),
                    Lokasi = Truncate(first.Lokasi, 150),
                    DetilLokasi = Truncate(first.DetilLokasi, 250),
                    Tema = Truncate(first.Tema, 100),
                    Feedback = first.Feedback,
                    Komitmen = first.Komitmen,
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = first.Tanggal.Add(first.Waktu)
                };

                coaching.Participants = group
                    .Select(g => new { g.EmployeeNik, g.EmployeeNama })
                    .DistinctBy(p => NormalizeText(p.EmployeeNik))
                    .Select(inc => new CoachingParticipant
                    {
                        Nik = Truncate(inc.EmployeeNik, 50) ?? "UNKNOWN",
                        Nama = Truncate(inc.EmployeeNama, 150) ?? "Unknown"
                    }).ToList();

                _context.Coachings.Add(coaching);
                coachingMap[key] = coaching;
                result.CoachingInserted++;
            }

            var existingObservations = await _context.Observations
                .Where(o => !o.IsDeleted && o.Date >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var observationMap = existingObservations
                .GroupBy(o => BuildObservationKey(o.Nik, o.Date.Date, o.Date.TimeOfDay, o.KegiatanYangDiamati, o.PerihalYangDiamati))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in observationSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.ObservationSkippedCompany++;
                    continue;
                }

                var key = BuildObservationKey(row.Nik, row.Tanggal, row.Waktu, row.Kegiatan, row.Perihal);

                if (observationMap.TryGetValue(key, out var existingObs))
                {
                    var hasChanges = false;
                    var newKeterangan = Truncate(row.Keterangan, 2000);
                    if (!string.Equals(existingObs.Keterangan ?? string.Empty, newKeterangan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingObs.Keterangan = newKeterangan;
                        hasChanges = true;
                    }
                    var newHasil = Truncate(row.Hasil, 50);
                    if (!string.Equals(existingObs.HasilObservasi ?? string.Empty, newHasil ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingObs.HasilObservasi = newHasil;
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.ObservationUpdated++;
                    }
                    else
                    {
                        result.ObservationSkipped++;
                    }
                    continue;
                }

                var obs = new Observation
                {
                    Date = row.Tanggal.Add(row.Waktu),
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 100) ?? "General",
                    Area = Truncate(row.Area, 100) ?? "General",
                    Lokasi = Truncate(row.Lokasi, 150) ?? "General",
                    DetilLokasi = Truncate(row.DetilLokasi, 2000),
                    KegiatanYangDiamati = row.Kegiatan,
                    DepartemenYangDiamati = Truncate(row.DeptDiamati, 100),
                    DokumenPendukung = Truncate(row.Doc, 100),
                    ResikoKritis = Truncate(row.Resiko, 100),
                    TingkatResiko = Truncate(row.Kegiatan, 50),
                    PerihalYangDiamati = Truncate(row.Perihal, 150),
                    HasilObservasi = Truncate(row.Hasil, 50),
                    Keterangan = Truncate(row.Keterangan, 2000),
                    IsDeleted = false,
                    CreatedAt = row.Tanggal.Add(row.Waktu)
                };

                _context.Observations.Add(obs);
                observationMap[key] = obs;
                result.ObservationInserted++;
            }

            var existingP2hs = await _context.P2hReports
                .Where(p => !p.IsDeleted && p.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var p2hMap = existingP2hs
                .GroupBy(p => BuildP2hKey(p.Nik, p.Tanggal, p.Waktu, p.NoLambung))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            var p2hGroups = p2hSourceRows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.SourceCode) ? BuildP2hKey(row.Nik, row.Tanggal, row.Waktu, row.NoLambung) : row.SourceCode);

            foreach (var group in p2hGroups)
            {
                var first = group.First();
                var normalizedCompany = NormalizeCompanyName(first.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.P2hSkippedCompany += group.Count();
                    continue;
                }

                var key = BuildP2hKey(first.Nik, first.Tanggal, first.Waktu, first.NoLambung);

                var listA = new List<P2hController.ChecklistItem>();
                var listB = new List<P2hController.ChecklistItem>();
                var listC = new List<P2hController.ChecklistItem>();
                
                int idA = 1, idB = 1, idC = 1;

                foreach (var r in group)
                {
                    var statusStr = string.Equals(r.Status, "Good", StringComparison.OrdinalIgnoreCase) || string.Equals(r.YesNo, "Yes", StringComparison.OrdinalIgnoreCase) ? "GOOD" : "NOT_GOOD";
                    var item = new P2hController.ChecklistItem
                    {
                        Name = r.Name,
                        Status = statusStr
                    };

                    var typeLower = (r.Type ?? "").ToLower();
                    if (typeLower.Contains("gol. a") || typeLower.Contains("gol a"))
                    {
                        item.Id = idA++;
                        listA.Add(item);
                    }
                    else if (typeLower.Contains("gol. b") || typeLower.Contains("gol b"))
                    {
                        item.Id = idB++;
                        listB.Add(item);
                    }
                    else if (typeLower.Contains("gol. c") || typeLower.Contains("gol c"))
                    {
                        item.Id = idC++;
                        listC.Add(item);
                    }
                }

                var golAJson = System.Text.Json.JsonSerializer.Serialize(listA);
                var golBJson = System.Text.Json.JsonSerializer.Serialize(listB);
                var golCJson = System.Text.Json.JsonSerializer.Serialize(listC);

                if (p2hMap.TryGetValue(key, out var existingP2h))
                {
                    var hasChanges = false;
                    if (existingP2h.Kilometer != first.Kilometer)
                    {
                        existingP2h.Kilometer = first.Kilometer;
                        hasChanges = true;
                    }
                    if (existingP2h.GolA_Json != golAJson)
                    {
                        existingP2h.GolA_Json = golAJson;
                        hasChanges = true;
                    }
                    if (existingP2h.GolB_Json != golBJson)
                    {
                        existingP2h.GolB_Json = golBJson;
                        hasChanges = true;
                    }
                    if (existingP2h.GolC_Json != golCJson)
                    {
                        existingP2h.GolC_Json = golCJson;
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.P2hUpdated++;
                    }
                    else
                    {
                        result.P2hSkipped++;
                    }
                    continue;
                }

                var p2h = new P2hReport
                {
                    Nik = Truncate(first.Nik, 50) ?? "UNKNOWN",
                    Nama = Truncate(GetOfficialName(first.Nik, first.Nama), 150) ?? "Unknown",
                    Tanggal = first.Tanggal,
                    Waktu = first.Waktu,
                    JenisKendaraan = Truncate(first.JenisKendaraan, 100) ?? "LIGHT VEHICLE",
                    NoLambung = Truncate(first.NoLambung, 100) ?? "-",
                    Kilometer = first.Kilometer,
                    Merek = Truncate(first.Merek, 200) ?? "-",
                    SimperKimper = string.Equals(first.SimperKimper, "Yes", StringComparison.OrdinalIgnoreCase) || string.Equals(first.SimperKimper, "YA", StringComparison.OrdinalIgnoreCase) ? "YA" : "TIDAK",
                    GolA_Json = golAJson,
                    GolB_Json = golBJson,
                    GolC_Json = golCJson,
                    IsDeleted = false,
                    CreatedAt = first.Tanggal.Add(first.Waktu)
                };

                _context.P2hReports.Add(p2h);
                p2hMap[key] = p2h;
                result.P2hInserted++;
            }

            var existingP5ms = await _context.P5ms
                .Where(p => !p.IsDeleted && p.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var p5mMap = existingP5ms
                .GroupBy(p => BuildP5mKey(p.Nik, p.Tanggal, p.Waktu, p.ListPertanyaan))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in p5mSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.P5mSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildP5mKey(row.Nik, row.Tanggal, row.Waktu, row.ListPertanyaan);

                if (p5mMap.TryGetValue(key, out var existingP5m))
                {
                    var hasChanges = false;
                    var newJawaban = Truncate(row.Jawaban, 100);
                    if (!string.Equals(existingP5m.Jawaban ?? string.Empty, newJawaban ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingP5m.Jawaban = newJawaban;
                        hasChanges = true;
                    }
                    var newCatatan = row.Catatan;
                    if (!string.Equals(existingP5m.Catatan ?? string.Empty, newCatatan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingP5m.Catatan = newCatatan;
                        hasChanges = true;
                    }
                    if (existingP5m.FotoKegiatan != row.Foto)
                    {
                        existingP5m.FotoKegiatan = row.Foto;
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.P5mUpdated++;
                    }
                    else
                    {
                        result.P5mSkipped++;
                    }
                    continue;
                }

                var p5m = new P5m
                {
                    FotoKegiatan = row.Foto,
                    Tanggal = row.Tanggal,
                    Waktu = row.Waktu,
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 150),
                    DetilLokasi = Truncate(row.DetilLokasi, 250),
                    Topik = Truncate(row.Topik, 250) ?? "Pekerjaan Umum",
                    Judul = Truncate(row.Judul, 250) ?? "Siap Bekerja",
                    Keterangan = Truncate(row.Keterangan, 4000) ?? ".",
                    ListPertanyaan = row.ListPertanyaan,
                    Jawaban = Truncate(row.Jawaban, 100) ?? "No",
                    Catatan = row.Catatan,
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = row.Tanggal.Add(row.Waktu)
                };

                _context.P5ms.Add(p5m);
                p5mMap[key] = p5m;
                result.P5mInserted++;
            }

            var existingSafetyTalks = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && s.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var safetyTalkMap = existingSafetyTalks
                .GroupBy(s => BuildSafetyTalkKey(s.Nik, s.Tanggal, s.Waktu, s.Judul))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in safetyTalkSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.SafetyTalkSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildSafetyTalkKey(row.Nik, row.Tanggal, row.Waktu, row.Judul);

                if (safetyTalkMap.TryGetValue(key, out var existingTalk))
                {
                    var hasChanges = false;
                    var newKeterangan = row.Keterangan;
                    if (!string.Equals(existingTalk.Keterangan ?? string.Empty, newKeterangan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingTalk.Keterangan = newKeterangan;
                        hasChanges = true;
                    }
                    if (existingTalk.FotoDiri != row.FotoDiri)
                    {
                        existingTalk.FotoDiri = row.FotoDiri;
                        hasChanges = true;
                    }
                    if (existingTalk.FotoKegiatan != row.FotoKegiatan)
                    {
                        existingTalk.FotoKegiatan = row.FotoKegiatan;
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.SafetyTalkUpdated++;
                    }
                    else
                    {
                        result.SafetyTalkSkipped++;
                    }
                    continue;
                }

                var talk = new SafetyTalk
                {
                    FotoDiri = row.FotoDiri,
                    FotoKegiatan = row.FotoKegiatan,
                    Tanggal = row.Tanggal,
                    Waktu = row.Waktu,
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 150),
                    Area = Truncate(row.Area, 150),
                    Lokasi = Truncate(row.Lokasi, 150),
                    DetilLokasi = Truncate(row.DetilLokasi, 250),
                    Judul = Truncate(row.Judul, 250),
                    Keterangan = row.Keterangan,
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = row.Tanggal.Add(row.Waktu)
                };

                _context.SafetyTalks.Add(talk);
                safetyTalkMap[key] = talk;
                result.SafetyTalkInserted++;
            }

            if (result.HazardInserted > 0 || result.HazardUpdated > 0 ||
                result.InspectionInserted > 0 || result.InspectionUpdated > 0 ||
                result.CoachingInserted > 0 || result.CoachingUpdated > 0 ||
                result.ObservationInserted > 0 || result.ObservationUpdated > 0 ||
                result.P2hInserted > 0 || result.P2hUpdated > 0 ||
                result.P5mInserted > 0 || result.P5mUpdated > 0 ||
                result.SafetyTalkInserted > 0 || result.SafetyTalkUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Postgres replication completed. Hazard +{HazardInserted} ~{HazardUpdated}; Inspection +{InspectionInserted} ~{InspectionUpdated}; " +
                "Coaching +{CoachingInserted} ~{CoachingUpdated}; Observation +{ObservationInserted} ~{ObservationUpdated}; " +
                "P2H +{P2hInserted} ~{P2hUpdated}; P5M +{P5mInserted} ~{P5mUpdated}; SafetyTalk +{SafetyTalkInserted} ~{SafetyTalkUpdated}",
                result.HazardInserted, result.HazardUpdated,
                result.InspectionInserted, result.InspectionUpdated,
                result.CoachingInserted, result.CoachingUpdated,
                result.ObservationInserted, result.ObservationUpdated,
                result.P2hInserted, result.P2hUpdated,
                result.P5mInserted, result.P5mUpdated,
                result.SafetyTalkInserted, result.SafetyTalkUpdated);

            return result;
        }

        public async Task<PostgresReplicationResult> ReplicateForUserAsync(string targetNik, CancellationToken cancellationToken = default)
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
            var coachingView = ValidateSqlIdentifier(_options.CoachingSourceView, nameof(_options.CoachingSourceView));
            var observationView = ValidateSqlIdentifier(_options.ObservationSourceView, nameof(_options.ObservationSourceView));
            var p2hView = ValidateSqlIdentifier(_options.P2hSourceView, nameof(_options.P2hSourceView));
            var p5mView = ValidateSqlIdentifier(_options.P5mSourceView, nameof(_options.P5mSourceView));
            var safetyTalkView = ValidateSqlIdentifier(_options.SafetyTalkSourceView, nameof(_options.SafetyTalkSourceView));

            var since = DateTime.Today.AddDays(-30);

            var allowedCompanies = _options.AllowedCompanies
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeCompanyName)
                .ToHashSet();

            var companyIdMap = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .Select(p => new { p.PerusahaanId, p.NamaPerusahaan })
                .ToListAsync(cancellationToken);

            var normalizedCompanyIdMap = companyIdMap
                .Where(x => !string.IsNullOrWhiteSpace(x.NamaPerusahaan))
                .GroupBy(x => NormalizeCompanyName(x.NamaPerusahaan))
                .ToDictionary(g => g.Key, g => g.First().PerusahaanId);

            var targetNiks = GetPossibleSourceNiks(targetNik);

            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var hazardSourceRows = await ReadHazardsAsync(connection, hazardView, since, cancellationToken, targetNiks);
            var inspectionSourceRows = await ReadInspectionsAsync(connection, inspectionView, since, cancellationToken, targetNiks);
            var coachingSourceRows = await ReadCoachingsAsync(connection, coachingView, since, cancellationToken, targetNiks);
            var observationSourceRows = await ReadObservationsAsync(connection, observationView, since, cancellationToken, targetNiks);
            var p2hSourceRows = await ReadP2hsAsync(connection, p2hView, since, cancellationToken, targetNiks);
            var p5mSourceRows = await ReadP5msAsync(connection, p5mView, since, cancellationToken, targetNiks);
            var safetyTalkSourceRows = await ReadSafetyTalksAsync(connection, safetyTalkView, since, cancellationToken, targetNiks);

            var officialNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var namesList = await (from k in _context.Karyawans
                                   join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                   where k.NoNik == targetNik
                                   select new { k.NoNik, p.NamaLengkap })
                                   .ToListAsync(cancellationToken);
            foreach (var x in namesList)
            {
                if (!string.IsNullOrWhiteSpace(x.NoNik) && !officialNameMap.ContainsKey(x.NoNik))
                {
                    officialNameMap[x.NoNik] = x.NamaLengkap;
                }
            }

            string GetOfficialName(string? sourceNik, string? fallbackName)
            {
                if (!string.IsNullOrWhiteSpace(sourceNik) && officialNameMap.TryGetValue(sourceNik.Trim(), out var officialName))
                {
                    return officialName;
                }
                return fallbackName ?? "Unknown";
            }

            var result = new PostgresReplicationResult
            {
                LookbackDays = 30
            };

            // 1. Process Hazards
            var existingHazardsData = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.Nik == targetNik && h.Tanggal >= since.AddDays(-7))
                .Select(h => new HazardReplicationDto
                {
                    Id = h.Id,
                    Nik = h.Nik,
                    Tanggal = h.Tanggal,
                    Waktu = h.Waktu,
                    Area = h.Area,
                    Temuan = h.Temuan,
                    Lokasi = h.Lokasi,
                    PerusahaanId = h.PerusahaanId,
                    StatusTemuan = h.StatusTemuan,
                    TingkatResiko = h.TingkatResiko,
                    Pja = h.Pja,
                    NikPja = h.NikPja,
                    DepartemenPja = h.DepartemenPja,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var hazardMap = existingHazardsData
                .GroupBy(h => BuildHazardKey(h.Nik ?? string.Empty, h.Tanggal, h.Waktu, h.Temuan ?? string.Empty, h.Area, h.Lokasi, h.PerusahaanId))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in hazardSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.HazardSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildHazardKey(row.Nik, row.Tanggal, row.Waktu, row.Temuan, row.Area, row.Lokasi, perusahaanId);

                if (hazardMap.TryGetValue(key, out var existingHazard))
                {
                    var hasHazardChanges = false;
                    var newStatus = Truncate(row.StatusTemuan, 50) ?? "Open";
                    var newRisk = Truncate(row.TingkatResiko, 50);
                    var newPja = Truncate(row.Pja, 150);
                    var newNikPja = Truncate(row.NikPja, 50);
                    var newDeptPja = Truncate(row.DepartemenPja, 150);

                    if (!string.Equals(existingHazard.StatusTemuan ?? string.Empty, newStatus, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.TingkatResiko ?? string.Empty, newRisk ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.Pja ?? string.Empty, newPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.NikPja ?? string.Empty, newNikPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingHazard.DepartemenPja ?? string.Empty, newDeptPja ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        var realHazard = await _context.HazardReports.FindAsync(new object[] { existingHazard.Id }, cancellationToken);
                        if (realHazard != null)
                        {
                            realHazard.StatusTemuan = newStatus;
                            realHazard.TingkatResiko = newRisk;
                            realHazard.Pja = newPja;
                            realHazard.NikPja = newNikPja;
                            realHazard.DepartemenPja = newDeptPja;
                            hasHazardChanges = true;
                        }
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
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
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
                result.HazardInserted++;
            }

            // 2. Process Inspections
            var existingInspectionsData = await _context.Inspections
                .Where(i => !i.IsDeleted && i.Nik == targetNik && i.Tanggal >= since.AddDays(-7))
                .Select(i => new InspectionReplicationDto
                {
                    Id = i.Id,
                    Nik = i.Nik,
                    Tanggal = i.Tanggal,
                    Waktu = i.Waktu,
                    JenisInspeksi = i.JenisInspeksi,
                    Lokasi = i.Lokasi,
                    PerusahaanId = i.PerusahaanId,
                    Pja = i.Pja,
                    NikPja = i.NikPja,
                    DepartemenPja = i.DepartemenPja,
                    Catatan = i.Catatan,
                    CreatedAt = i.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var inspectionMap = existingInspectionsData
                .GroupBy(i => BuildInspectionKey(i.Nik ?? string.Empty, i.Tanggal, i.Waktu, i.JenisInspeksi ?? string.Empty, i.Lokasi, i.PerusahaanId))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in inspectionSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.InspectionSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildInspectionKey(row.Nik, row.Tanggal, row.Waktu, row.JenisInspeksi, row.Lokasi, perusahaanId);

                if (inspectionMap.TryGetValue(key, out var existingInspection))
                {
                    var hasInspectionChanges = false;
                    var newPja = Truncate(row.Pja, 150);
                    var newNikPja = Truncate(row.NikPja, 50);
                    var newDeptPja = Truncate(row.DepartemenPja, 150);
                    var newCatatan = Truncate(row.Catatan, 2000);

                    if (!string.Equals(existingInspection.Pja ?? string.Empty, newPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingInspection.NikPja ?? string.Empty, newNikPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingInspection.DepartemenPja ?? string.Empty, newDeptPja ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existingInspection.Catatan ?? string.Empty, newCatatan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        var realInspection = await _context.Inspections.FindAsync(new object[] { existingInspection.Id }, cancellationToken);
                        if (realInspection != null)
                        {
                            realInspection.Pja = newPja;
                            realInspection.NikPja = newNikPja;
                            realInspection.DepartemenPja = newDeptPja;
                            realInspection.Catatan = newCatatan;
                            hasInspectionChanges = true;
                        }
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
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
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
                result.InspectionInserted++;
            }

            // 3. Process Coachings
            var existingCoachings = await _context.Coachings
                .Include(c => c.Participants)
                .Where(c => !c.IsDeleted && c.Nik == targetNik && c.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var coachingMap = existingCoachings
                .GroupBy(c => BuildCoachingKey(c.Nik, c.Tanggal, c.Waktu, c.Tema))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            var coachingGroups = coachingSourceRows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.SourceCode) ? BuildCoachingKey(row.TrainerNik, row.Tanggal, row.Waktu, row.Tema) : row.SourceCode);

            foreach (var group in coachingGroups)
            {
                var first = group.First();
                var normalizedCompany = NormalizeCompanyName(first.EmployeeCompany);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.CoachingSkippedCompany += group.Count();
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildCoachingKey(first.TrainerNik, first.Tanggal, first.Waktu, first.Tema);

                if (coachingMap.TryGetValue(key, out var existingCoaching))
                {
                    var hasChanges = false;
                    var newFeedback = first.Feedback;
                    if (!string.Equals(existingCoaching.Feedback ?? string.Empty, newFeedback ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingCoaching.Feedback = newFeedback;
                        hasChanges = true;
                    }

                    var newKomitmen = first.Komitmen;
                    if (!string.Equals(existingCoaching.Komitmen ?? string.Empty, newKomitmen ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingCoaching.Komitmen = newKomitmen;
                        hasChanges = true;
                    }

                    var currentParticipantsMap = existingCoaching.Participants.ToDictionary(p => NormalizeText(p.Nik));
                    var incomingParticipants = group
                        .Select(g => new { g.EmployeeNik, g.EmployeeNama })
                        .DistinctBy(p => NormalizeText(p.EmployeeNik))
                        .ToList();

                    var participantChanges = false;
                    if (currentParticipantsMap.Count != incomingParticipants.Count)
                    {
                        participantChanges = true;
                    }
                    else
                    {
                        foreach (var inc in incomingParticipants)
                        {
                            if (!currentParticipantsMap.ContainsKey(NormalizeText(inc.EmployeeNik)))
                            {
                                participantChanges = true;
                                break;
                            }
                        }
                    }

                    if (participantChanges)
                    {
                        _context.CoachingParticipants.RemoveRange(existingCoaching.Participants);
                        existingCoaching.Participants = incomingParticipants.Select(inc => new CoachingParticipant
                        {
                            Nik = Truncate(inc.EmployeeNik, 50) ?? "UNKNOWN",
                            Nama = Truncate(inc.EmployeeNama, 150) ?? "Unknown"
                        }).ToList();
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.CoachingUpdated++;
                    }
                    else
                    {
                        result.CoachingSkipped++;
                    }
                    continue;
                }

                var coaching = new Coaching
                {
                    Foto = first.Foto,
                    Tanggal = first.Tanggal,
                    Waktu = first.Waktu,
                    Nama = Truncate(GetOfficialName(first.TrainerNik, first.TrainerNama), 150) ?? "Unknown",
                    Nik = Truncate(first.TrainerNik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(first.EmployeeDepartemen, 150),
                    Area = Truncate(first.Area, 150),
                    Lokasi = Truncate(first.Lokasi, 150),
                    DetilLokasi = Truncate(first.DetilLokasi, 250),
                    Tema = Truncate(first.Tema, 100),
                    Feedback = first.Feedback,
                    Komitmen = first.Komitmen,
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = first.Tanggal.Add(first.Waktu)
                };

                coaching.Participants = group
                    .Select(g => new { g.EmployeeNik, g.EmployeeNama })
                    .DistinctBy(p => NormalizeText(p.EmployeeNik))
                    .Select(inc => new CoachingParticipant
                    {
                        Nik = Truncate(inc.EmployeeNik, 50) ?? "UNKNOWN",
                        Nama = Truncate(inc.EmployeeNama, 150) ?? "Unknown"
                    }).ToList();

                _context.Coachings.Add(coaching);
                result.CoachingInserted++;
            }

            // 4. Process Observations
            var existingObservations = await _context.Observations
                .Where(o => !o.IsDeleted && o.Nik == targetNik && o.Date >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var observationMap = existingObservations
                .GroupBy(o => BuildObservationKey(o.Nik, o.Date.Date, o.Date.TimeOfDay, o.KegiatanYangDiamati, o.PerihalYangDiamati))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in observationSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.ObservationSkippedCompany++;
                    continue;
                }

                var key = BuildObservationKey(row.Nik, row.Tanggal, row.Waktu, row.Kegiatan, row.Perihal);

                if (observationMap.TryGetValue(key, out var existingObs))
                {
                    var hasChanges = false;
                    var newKeterangan = Truncate(row.Keterangan, 2000);
                    if (!string.Equals(existingObs.Keterangan ?? string.Empty, newKeterangan ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingObs.Keterangan = newKeterangan;
                        hasChanges = true;
                    }
                    var newHasil = Truncate(row.Hasil, 50);
                    if (!string.Equals(existingObs.HasilObservasi ?? string.Empty, newHasil ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        existingObs.HasilObservasi = newHasil;
                        hasChanges = true;
                    }

                    if (hasChanges)
                    {
                        result.ObservationUpdated++;
                    }
                    else
                    {
                        result.ObservationSkipped++;
                    }
                    continue;
                }

                var obs = new Observation
                {
                    Date = row.Tanggal.Add(row.Waktu),
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 100) ?? "General",
                    Area = Truncate(row.Area, 100) ?? "General",
                    Lokasi = Truncate(row.Lokasi, 150) ?? "General",
                    DetilLokasi = Truncate(row.DetilLokasi, 2000),
                    KegiatanYangDiamati = row.Kegiatan,
                    DepartemenYangDiamati = Truncate(row.DeptDiamati, 100),
                    DokumenPendukung = Truncate(row.Doc, 100),
                    ResikoKritis = Truncate(row.Resiko, 100),
                    TingkatResiko = Truncate(row.Kegiatan, 50),
                    PerihalYangDiamati = Truncate(row.Perihal, 150),
                    HasilObservasi = Truncate(row.Hasil, 50),
                    Keterangan = Truncate(row.Keterangan, 2000),
                    IsDeleted = false,
                    CreatedAt = row.Tanggal.Add(row.Waktu)
                };

                _context.Observations.Add(obs);
                result.ObservationInserted++;
            }

            // 5. Process P2H
            var existingP2hs = await _context.P2hReports
                .Where(p => !p.IsDeleted && p.Nik == targetNik && p.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var p2hMap = existingP2hs
                .GroupBy(p => BuildP2hKey(p.Nik, p.Tanggal, p.Waktu, p.NoLambung))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            var p2hGroups = p2hSourceRows
                .GroupBy(row => string.IsNullOrWhiteSpace(row.SourceCode) ? BuildP2hKey(row.Nik, row.Tanggal, row.Waktu, row.NoLambung) : row.SourceCode);

            foreach (var group in p2hGroups)
            {
                var first = group.First();
                var normalizedCompany = NormalizeCompanyName(first.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.P2hSkippedCompany += group.Count();
                    continue;
                }

                var key = BuildP2hKey(first.Nik, first.Tanggal, first.Waktu, first.NoLambung);

                var listA = new List<P2hController.ChecklistItem>();
                var listB = new List<P2hController.ChecklistItem>();
                var listC = new List<P2hController.ChecklistItem>();
                int idA = 1, idB = 1, idC = 1;

                foreach (var r in group)
                {
                    var statusStr = string.Equals(r.Status, "Good", StringComparison.OrdinalIgnoreCase) || string.Equals(r.YesNo, "Yes", StringComparison.OrdinalIgnoreCase) ? "GOOD" : "NOT_GOOD";
                    var item = new P2hController.ChecklistItem { Name = r.Name, Status = statusStr };
                    var typeLower = (r.Type ?? "").ToLower();
                    if (typeLower.Contains("gol. a") || typeLower.Contains("gol a")) { item.Id = idA++; listA.Add(item); }
                    else if (typeLower.Contains("gol. b") || typeLower.Contains("gol b")) { item.Id = idB++; listB.Add(item); }
                    else if (typeLower.Contains("gol. c") || typeLower.Contains("gol c")) { item.Id = idC++; listC.Add(item); }
                }

                var golAJson = System.Text.Json.JsonSerializer.Serialize(listA);
                var golBJson = System.Text.Json.JsonSerializer.Serialize(listB);
                var golCJson = System.Text.Json.JsonSerializer.Serialize(listC);

                if (p2hMap.TryGetValue(key, out var existingP2h))
                {
                    var hasChanges = false;
                    if (existingP2h.Kilometer != first.Kilometer) { existingP2h.Kilometer = first.Kilometer; hasChanges = true; }
                    if (existingP2h.GolA_Json != golAJson) { existingP2h.GolA_Json = golAJson; hasChanges = true; }
                    if (existingP2h.GolB_Json != golBJson) { existingP2h.GolB_Json = golBJson; hasChanges = true; }
                    if (existingP2h.GolC_Json != golCJson) { existingP2h.GolC_Json = golCJson; hasChanges = true; }

                    if (hasChanges) result.P2hUpdated++;
                    else result.P2hSkipped++;
                    continue;
                }

                var p2h = new P2hReport
                {
                    Nik = Truncate(first.Nik, 50) ?? "UNKNOWN",
                    Nama = Truncate(GetOfficialName(first.Nik, first.Nama), 150) ?? "Unknown",
                    Tanggal = first.Tanggal,
                    Waktu = first.Waktu,
                    JenisKendaraan = Truncate(first.JenisKendaraan, 100) ?? "LIGHT VEHICLE",
                    NoLambung = Truncate(first.NoLambung, 100) ?? "-",
                    Kilometer = first.Kilometer,
                    Merek = Truncate(first.Merek, 200) ?? "-",
                    SimperKimper = string.Equals(first.SimperKimper, "Yes", StringComparison.OrdinalIgnoreCase) || string.Equals(first.SimperKimper, "YA", StringComparison.OrdinalIgnoreCase) ? "YA" : "TIDAK",
                    GolA_Json = golAJson,
                    GolB_Json = golBJson,
                    GolC_Json = golCJson,
                    IsDeleted = false,
                    CreatedAt = first.Tanggal.Add(first.Waktu)
                };

                _context.P2hReports.Add(p2h);
                result.P2hInserted++;
            }

            // 6. Process P5M
            var existingP5ms = await _context.P5ms
                .Where(p => !p.IsDeleted && p.Nik == targetNik && p.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var p5mMap = existingP5ms
                .GroupBy(p => BuildP5mKey(p.Nik, p.Tanggal, p.Waktu, p.ListPertanyaan))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in p5mSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.P5mSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildP5mKey(row.Nik, row.Tanggal, row.Waktu, row.ListPertanyaan);

                if (p5mMap.TryGetValue(key, out var existingP5m))
                {
                    var hasChanges = false;
                    var newJawaban = Truncate(row.Jawaban, 100);
                    if (!string.Equals(existingP5m.Jawaban ?? string.Empty, newJawaban ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { existingP5m.Jawaban = newJawaban; hasChanges = true; }
                    var newCatatan = row.Catatan;
                    if (!string.Equals(existingP5m.Catatan ?? string.Empty, newCatatan ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { existingP5m.Catatan = newCatatan; hasChanges = true; }
                    if (existingP5m.FotoKegiatan != row.Foto) { existingP5m.FotoKegiatan = row.Foto; hasChanges = true; }

                    if (hasChanges) result.P5mUpdated++;
                    else result.P5mSkipped++;
                    continue;
                }

                var p5m = new P5m
                {
                    FotoKegiatan = row.Foto,
                    Tanggal = row.Tanggal,
                    Waktu = row.Waktu,
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 150),
                    DetilLokasi = Truncate(row.DetilLokasi, 250),
                    Topik = Truncate(row.Topik, 250) ?? "Pekerjaan Umum",
                    Judul = Truncate(row.Judul, 250) ?? "Siap Bekerja",
                    Keterangan = Truncate(row.Keterangan, 4000) ?? ".",
                    ListPertanyaan = row.ListPertanyaan,
                    Jawaban = Truncate(row.Jawaban, 100) ?? "No",
                    Catatan = row.Catatan,
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = row.Tanggal.Add(row.Waktu)
                };

                _context.P5ms.Add(p5m);
                result.P5mInserted++;
            }

            // 7. Process SafetyTalks
            var existingSafetyTalks = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && s.Nik == targetNik && s.Tanggal >= since.AddDays(-7))
                .ToListAsync(cancellationToken);

            var safetyTalkMap = existingSafetyTalks
                .GroupBy(s => BuildSafetyTalkKey(s.Nik, s.Tanggal, s.Waktu, s.Judul))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First());

            foreach (var row in safetyTalkSourceRows)
            {
                var normalizedCompany = NormalizeCompanyName(row.CompanyName);
                if (!allowedCompanies.Contains(normalizedCompany))
                {
                    result.SafetyTalkSkippedCompany++;
                    continue;
                }

                var perusahaanId = normalizedCompanyIdMap.TryGetValue(normalizedCompany, out var pid) ? pid : (int?)null;
                var key = BuildSafetyTalkKey(row.Nik, row.Tanggal, row.Waktu, row.Judul);

                if (safetyTalkMap.TryGetValue(key, out var existingTalk))
                {
                    var hasChanges = false;
                    var newKeterangan = row.Keterangan;
                    if (!string.Equals(existingTalk.Keterangan ?? string.Empty, newKeterangan ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { existingTalk.Keterangan = newKeterangan; hasChanges = true; }
                    if (existingTalk.FotoDiri != row.FotoDiri) { existingTalk.FotoDiri = row.FotoDiri; hasChanges = true; }
                    if (existingTalk.FotoKegiatan != row.FotoKegiatan) { existingTalk.FotoKegiatan = row.FotoKegiatan; hasChanges = true; }

                    if (hasChanges) result.SafetyTalkUpdated++;
                    else result.SafetyTalkSkipped++;
                    continue;
                }

                var talk = new SafetyTalk
                {
                    FotoDiri = row.FotoDiri,
                    FotoKegiatan = row.FotoKegiatan,
                    Tanggal = row.Tanggal,
                    Waktu = row.Waktu,
                    Nama = Truncate(GetOfficialName(row.Nik, row.Nama), 150) ?? "Unknown",
                    Nik = Truncate(row.Nik, 50) ?? "UNKNOWN",
                    Departemen = Truncate(row.Departemen, 150),
                    Area = Truncate(row.Area, 150),
                    Lokasi = Truncate(row.Lokasi, 150),
                    DetilLokasi = Truncate(row.DetilLokasi, 250),
                    Judul = Truncate(row.Judul, 250),
                    Keterangan = row.Keterangan,
                    PerusahaanId = perusahaanId,
                    IsDeleted = false,
                    CreatedAt = row.Tanggal.Add(row.Waktu)
                };

                _context.SafetyTalks.Add(talk);
                result.SafetyTalkInserted++;
            }

            // Deduplicate all safety data for this user to ensure absolutely no duplicates exist
            
            // Deduplicate HazardReports
            var userHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.Nik == targetNik)
                .OrderByDescending(h => h.CreatedAt)
                .ThenByDescending(h => h.Id)
                .ToListAsync(cancellationToken);
            var hazardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in userHazards)
            {
                var key = BuildHazardKey(h.Nik ?? string.Empty, h.Tanggal, h.Waktu, h.Temuan ?? string.Empty, h.Area, h.Lokasi, h.PerusahaanId);
                if (!hazardKeys.Add(key))
                {
                    h.IsDeleted = true;
                }
            }

            // Deduplicate Inspections
            var userInspections = await _context.Inspections
                .Where(i => !i.IsDeleted && i.Nik == targetNik)
                .OrderByDescending(i => i.CreatedAt)
                .ThenByDescending(i => i.Id)
                .ToListAsync(cancellationToken);
            var inspectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in userInspections)
            {
                var key = BuildInspectionKey(i.Nik ?? string.Empty, i.Tanggal, i.Waktu, i.JenisInspeksi ?? string.Empty, i.Lokasi, i.PerusahaanId);
                if (!inspectionKeys.Add(key))
                {
                    i.IsDeleted = true;
                }
            }

            // Deduplicate Coachings
            var userCoachings = await _context.Coachings
                .Where(c => !c.IsDeleted && c.Nik == targetNik)
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .ToListAsync(cancellationToken);
            var coachingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in userCoachings)
            {
                var key = BuildCoachingKey(c.Nik, c.Tanggal, c.Waktu, c.Tema);
                if (!coachingKeys.Add(key))
                {
                    c.IsDeleted = true;
                }
            }

            // Deduplicate Observations
            var userObservations = await _context.Observations
                .Where(o => !o.IsDeleted && o.Nik == targetNik)
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync(cancellationToken);
            var observationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in userObservations)
            {
                var key = BuildObservationKey(o.Nik, o.Date.Date, o.Date.TimeOfDay, o.KegiatanYangDiamati, o.PerihalYangDiamati);
                if (!observationKeys.Add(key))
                {
                    o.IsDeleted = true;
                }
            }

            // Deduplicate P2hReports
            var userP2hs = await _context.P2hReports
                .Where(p => !p.IsDeleted && p.Nik == targetNik)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .ToListAsync(cancellationToken);
            var p2hKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in userP2hs)
            {
                var key = BuildP2hKey(p.Nik, p.Tanggal, p.Waktu, p.NoLambung);
                if (!p2hKeys.Add(key))
                {
                    p.IsDeleted = true;
                }
            }

            // Deduplicate P5ms
            var userP5ms = await _context.P5ms
                .Where(p => !p.IsDeleted && p.Nik == targetNik)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .ToListAsync(cancellationToken);
            var p5mKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in userP5ms)
            {
                var key = BuildP5mKey(p.Nik, p.Tanggal, p.Waktu, p.ListPertanyaan);
                if (!p5mKeys.Add(key))
                {
                    p.IsDeleted = true;
                }
            }

            // Deduplicate SafetyTalks
            var userSafetyTalks = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && s.Nik == targetNik)
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id)
                .ToListAsync(cancellationToken);
            var safetyTalkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in userSafetyTalks)
            {
                var key = BuildSafetyTalkKey(s.Nik, s.Tanggal, s.Waktu, s.Judul);
                if (!safetyTalkKeys.Add(key))
                {
                    s.IsDeleted = true;
                }
            }

            if (result.HazardInserted > 0 || result.HazardUpdated > 0 ||
                result.InspectionInserted > 0 || result.InspectionUpdated > 0 ||
                result.CoachingInserted > 0 || result.CoachingUpdated > 0 ||
                result.ObservationInserted > 0 || result.ObservationUpdated > 0 ||
                result.P2hInserted > 0 || result.P2hUpdated > 0 ||
                result.P5mInserted > 0 || result.P5mUpdated > 0 ||
                result.SafetyTalkInserted > 0 || result.SafetyTalkUpdated > 0 ||
                _context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return result;
        }

        private static string[] GetPossibleSourceNiks(string targetNik)
        {
            return new string[] { targetNik };
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

        private static string BuildHazardKey(string nik, DateTime tanggal, TimeSpan waktu, string temuan, string? area, string? lokasi, int? perusahaanId)
        {
            var companyKey = perusahaanId?.ToString() ?? "0";
            var timeKey = $"{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}";
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{timeKey}|{NormalizeText(area)}|{NormalizeText(lokasi)}|{NormalizeText(temuan)}|{companyKey}";
        }

        private static string BuildInspectionKey(string nik, DateTime tanggal, TimeSpan waktu, string jenisInspeksi, string? lokasi, int? perusahaanId)
        {
            var companyKey = perusahaanId?.ToString() ?? "0";
            var timeKey = $"{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}";
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{timeKey}|{NormalizeText(jenisInspeksi)}|{NormalizeText(lokasi)}|{companyKey}";
        }

        private static string BuildCoachingKey(string nik, DateTime tanggal, TimeSpan waktu, string? tema)
        {
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}|{NormalizeText(tema)}";
        }

        private static string BuildObservationKey(string nik, DateTime tanggal, TimeSpan waktu, string? kegiatan, string? perihal)
        {
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}|{NormalizeText(kegiatan)}|{NormalizeText(perihal)}";
        }

        private static string BuildP2hKey(string nik, DateTime tanggal, TimeSpan waktu, string? noLambung)
        {
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}|{NormalizeText(noLambung)}";
        }

        private static string BuildP5mKey(string nik, DateTime tanggal, TimeSpan waktu, string? pertanyaan)
        {
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}|{NormalizeText(pertanyaan)}";
        }

        private static string BuildSafetyTalkKey(string nik, DateTime tanggal, TimeSpan waktu, string? judul)
        {
            return $"{NormalizeText(nik)}|{tanggal:yyyy-MM-dd}|{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}|{NormalizeText(judul)}";
        }

        private static string NormalizeText(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeCompanyName(string? value)
        {
            return NormalizeText(value).Replace(".", "");
        }

        private static async Task<List<HazardSourceRow>> ReadHazardsAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
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
WHERE (date >= @sinceDate OR status IS NULL OR LOWER(status::text) NOT IN ('close', 'closed', '1'))";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND employee_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

            var data = new List<HazardSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;
                var createdAt = tanggal.Add(waktu);

                var title = GetString(reader, "title");
                var hazardName = GetString(reader, "hazard_name");

                var source = new HazardSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: MapNik(GetString(reader, "employee_nik")),
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: ResolveCompany(GetString(reader, "employee_company"), GetString(reader, "pja_company")),
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
                    NikPja: MapNikNullable(GetString(reader, "pja_nik")),
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
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
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
WHERE date >= @sinceDate";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND employee_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

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
                    Nik: MapNik(GetString(reader, "employee_nik")),
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: ResolveCompany(GetString(reader, "employee_company"), GetString(reader, "pja_company")),
                    Area: GetString(reader, "area_name"),
                    Lokasi: GetString(reader, "location_name"),
                    DetilLokasi: GetString(reader, "location_detail"),
                    JenisInspeksi: !string.IsNullOrWhiteSpace(category)
                        ? category!
                        : (!string.IsNullOrWhiteSpace(title) ? title! : "General"),
                    Pja: GetString(reader, "pja_name"),
                    NikPja: MapNikNullable(GetString(reader, "pja_nik")),
                    DepartemenPja: GetString(reader, "pja_departemen"),
                    Catatan: GetString(reader, "remark"),
                    LampiranJson: null,
                    CreatedAt: createdAt);

                data.Add(source);
            }

            return data;
        }

        private static async Task<List<CoachingSourceRow>> ReadCoachingsAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    trainer_name,
    trainer_nik,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    area_name,
    location_name,
    location_detail,
    title,
    feedback,
    remark,
    foto
FROM {sourceView}
WHERE date >= @sinceDate";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND trainer_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

            var data = new List<CoachingSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;

                data.Add(new CoachingSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    TrainerNama: GetString(reader, "trainer_name") ?? "Unknown",
                    TrainerNik: MapNik(GetString(reader, "trainer_nik")),
                    EmployeeNama: GetString(reader, "employee_name") ?? "Unknown",
                    EmployeeNik: MapNik(GetString(reader, "employee_nik")),
                    EmployeeDepartemen: GetString(reader, "employee_departemen"),
                    EmployeeCompany: ResolveCompany(GetString(reader, "employee_company")),
                    Area: GetString(reader, "area_name"),
                    Lokasi: GetString(reader, "location_name"),
                    DetilLokasi: GetString(reader, "location_detail"),
                    Tema: GetString(reader, "title"),
                    Feedback: GetString(reader, "feedback"),
                    Komitmen: GetString(reader, "remark"),
                    Foto: GetString(reader, "foto")
                ));
            }
            return data;
        }

        private static async Task<List<ObservationSourceRow>> ReadObservationsAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    area_name,
    location_name,
    location_detail,
    activity,
    dept,
    doc,
    risk,
    typedesc,
    point,
    remark
FROM {sourceView}
WHERE date >= @sinceDate";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND employee_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

            var data = new List<ObservationSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;

                data.Add(new ObservationSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: MapNik(GetString(reader, "employee_nik")),
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: ResolveCompany(GetString(reader, "employee_company")),
                    Area: GetString(reader, "area_name"),
                    Lokasi: GetString(reader, "location_name"),
                    DetilLokasi: GetString(reader, "location_detail"),
                    Kegiatan: GetString(reader, "activity"),
                    DeptDiamati: GetString(reader, "dept"),
                    Doc: GetString(reader, "doc"),
                    Resiko: GetString(reader, "risk"),
                    Hasil: GetString(reader, "typedesc"),
                    Perihal: GetString(reader, "point"),
                    Keterangan: GetString(reader, "remark")
                ));
            }
            return data;
        }

        private static async Task<List<P2hSourceRow>> ReadP2hsAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    vehicle_type,
    vehicle_name,
    km,
    merek,
    simper,
    type,
    yesno,
    remark,
    name,
    status
FROM {sourceView}
WHERE date >= @sinceDate";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND employee_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

            var data = new List<P2hSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;

                double kmVal = 0;
                try
                {
                    int ord = reader.GetOrdinal("km");
                    if (!reader.IsDBNull(ord))
                    {
                        kmVal = Convert.ToDouble(reader.GetValue(ord));
                    }
                }
                catch {}

                data.Add(new P2hSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: MapNik(GetString(reader, "employee_nik")),
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: ResolveCompany(GetString(reader, "employee_company")),
                    JenisKendaraan: GetString(reader, "vehicle_type") ?? "LIGHT VEHICLE",
                    NoLambung: GetString(reader, "vehicle_name") ?? "-",
                    Kilometer: kmVal,
                    Merek: GetString(reader, "merek") ?? "-",
                    SimperKimper: GetString(reader, "simper") ?? "TIDAK",
                    Type: GetString(reader, "type") ?? "",
                    YesNo: GetString(reader, "yesno") ?? "",
                    Remark: GetString(reader, "remark"),
                    Name: GetString(reader, "name") ?? "",
                    Status: GetString(reader, "status")
                ));
            }
            return data;
        }

        private static async Task<List<P5mSourceRow>> ReadP5msAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    location_detail,
    topic_id,
    title,
    note,
    name,
    yesno,
    remark,
    foto
FROM {sourceView}
WHERE date >= @sinceDate";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND employee_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

            var data = new List<P5mSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;

                data.Add(new P5mSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: MapNik(GetString(reader, "employee_nik")),
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: ResolveCompany(GetString(reader, "employee_company")),
                    DetilLokasi: GetString(reader, "location_detail"),
                    Topik: GetString(reader, "topic_id"),
                    Judul: GetString(reader, "title"),
                    Keterangan: GetString(reader, "note"),
                    ListPertanyaan: GetString(reader, "name") ?? "",
                    Jawaban: GetString(reader, "yesno") ?? "",
                    Catatan: GetString(reader, "remark"),
                    Foto: GetString(reader, "foto")
                ));
            }
            return data;
        }

        private static async Task<List<SafetyTalkSourceRow>> ReadSafetyTalksAsync(
            NpgsqlConnection connection,
            string sourceView,
            DateTime since,
            CancellationToken cancellationToken = default,
            string[]? targetNiks = null)
        {
            var sql = $@"
SELECT
    code,
    date,
    time,
    employee_name,
    employee_nik,
    employee_departemen,
    employee_company,
    area_name,
    location_name,
    location_detail,
    title,
    remark,
    foto,
    foto_kegiatan
FROM {sourceView}
WHERE date >= @sinceDate";

            if (targetNiks != null && targetNiks.Length > 0)
            {
                sql += " AND employee_nik = ANY(@targetNiks)";
            }

            sql += " ORDER BY date, time, code;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("sinceDate", since.Date);
            if (targetNiks != null && targetNiks.Length > 0)
            {
                command.Parameters.AddWithValue("targetNiks", targetNiks);
            }

            var data = new List<SafetyTalkSourceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tanggal = GetDateTime(reader, "date")?.Date ?? DateTime.Today;
                var waktu = GetTimeSpan(reader, "time") ?? TimeSpan.Zero;

                data.Add(new SafetyTalkSourceRow(
                    SourceCode: GetString(reader, "code"),
                    Tanggal: tanggal,
                    Waktu: waktu,
                    Nama: GetString(reader, "employee_name") ?? "Unknown",
                    Nik: MapNik(GetString(reader, "employee_nik")),
                    Departemen: GetString(reader, "employee_departemen"),
                    CompanyName: ResolveCompany(GetString(reader, "employee_company")),
                    Area: GetString(reader, "area_name"),
                    Lokasi: GetString(reader, "location_name"),
                    DetilLokasi: GetString(reader, "location_detail"),
                    Judul: GetString(reader, "title"),
                    Keterangan: GetString(reader, "remark"),
                    FotoDiri: GetString(reader, "foto"),
                    FotoKegiatan: GetString(reader, "foto_kegiatan")
                ));
            }
            return data;
        }

        private static string MapNik(string? nik)
        {
            if (string.IsNullOrWhiteSpace(nik)) return "UNKNOWN";
            var clean = nik.Trim();
            if (clean == "18071690163") return "25031691104";
            if (clean == "11101700060") return "26071701184";
            return clean;
        }

        private static string? MapNikNullable(string? nik)
        {
            if (string.IsNullOrWhiteSpace(nik)) return null;
            var clean = nik.Trim();
            if (clean == "18071690163") return "25031691104";
            if (clean == "11101700060") return "26071701184";
            return clean;
        }

        private static string ResolveCompany(string? employeeCompany, string? pjaCompany = null)
        {
            if (!string.IsNullOrWhiteSpace(employeeCompany)) return employeeCompany.Trim();
            if (!string.IsNullOrWhiteSpace(pjaCompany)) return pjaCompany.Trim();
            return "PT INDEXIM COALINDO";
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

        private class HazardReplicationDto
        {
            public int Id { get; set; }
            public string? Nik { get; set; }
            public DateTime Tanggal { get; set; }
            public TimeSpan Waktu { get; set; }
            public string? Area { get; set; }
            public string? Temuan { get; set; }
            public string? Lokasi { get; set; }
            public int? PerusahaanId { get; set; }
            public string? StatusTemuan { get; set; }
            public string? TingkatResiko { get; set; }
            public string? Pja { get; set; }
            public string? NikPja { get; set; }
            public string? DepartemenPja { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private class InspectionReplicationDto
        {
            public int Id { get; set; }
            public string? Nik { get; set; }
            public DateTime Tanggal { get; set; }
            public TimeSpan Waktu { get; set; }
            public string? JenisInspeksi { get; set; }
            public string? Lokasi { get; set; }
            public int? PerusahaanId { get; set; }
            public string? Pja { get; set; }
            public string? NikPja { get; set; }
            public string? DepartemenPja { get; set; }
            public string? Catatan { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}