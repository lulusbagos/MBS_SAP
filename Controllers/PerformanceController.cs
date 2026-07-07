using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Text.Json;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class PerformanceController : Controller
    {
        private readonly AppDbContext _context;

        public PerformanceController(AppDbContext context)
        {
            _context = context;
        }

        private async Task<(int? companyId, HashSet<int> allowedCompanyIds)> ResolveCompanyScopeAsync()
        {
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);

            if (isAdmin || isSafetyRole)
            {
                companyId = null;
            }

            var allCompanies = await _context.Perusahaans.Where(p => p.StatusAktif).ToListAsync();
            var allowedCompanyIds = new HashSet<int>();
            if (companyId.HasValue)
            {
                allowedCompanyIds.Add(companyId.Value);

                void GetDescendants(int parentId)
                {
                    var children = allCompanies.Where(c => c.PerusahaanIndukId == parentId).Select(c => c.PerusahaanId).ToList();
                    foreach (var childId in children)
                    {
                        if (allowedCompanyIds.Add(childId))
                        {
                            GetDescendants(childId);
                        }
                    }
                }

                GetDescendants(companyId.Value);
            }

            return (companyId, allowedCompanyIds);
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentEmployees(int companyId, string departmentName)
        {
            var (scopeCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            if (scopeCompanyId.HasValue && !allowedCompanyIds.Contains(companyId))
            {
                return Forbid();
            }

            var startOfYear = new DateTime(DateTime.Today.Year, 1, 1);
            var elapsedWeeksYtd = Math.Max(1, ((DateTime.Today - startOfYear.Date).Days / 7) + 1);

            var deptKaryawans = await (from k in _context.Karyawans
                                      join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                      join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                      from d in dg.DefaultIfEmpty()
                                      where k.IdPerusahaan == companyId && k.StatusAktif == true
                                      select new {
                                          k.IdKaryawan,
                                          k.NoNik,
                                          NamaLengkap = p.NamaLengkap,
                                          NamaDepartemen = d != null ? d.NamaDepartemen : "General"
                                      }).ToListAsync();

            var targetMappingCompany = await _context.KaryawanJabatanMappings
                .AsNoTracking()
                .Where(m => m.PerusahaanId == companyId)
                .ToListAsync();

            var mappingsDict = targetMappingCompany.ToDictionary(m => m.KaryawanId);

            var deptKaryawansFiltered = deptKaryawans.Where(k => 
                string.Equals(k.NamaDepartemen ?? "General", departmentName, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            var hazards = await _context.HazardReports.Where(h => !h.IsDeleted && h.PerusahaanId == companyId && h.CreatedAt >= startOfYear).Select(h => h.Nik).ToListAsync();
            var inspections = await _context.Inspections.Where(i => !i.IsDeleted && i.PerusahaanId == companyId && i.CreatedAt >= startOfYear).Select(i => i.Nik).ToListAsync();
            var safetyTalks = await _context.SafetyTalks.Where(s => !s.IsDeleted && s.PerusahaanId == companyId && s.CreatedAt >= startOfYear).Select(s => s.Nik).ToListAsync();
            var p5ms = await _context.P5ms.Where(p => !p.IsDeleted && p.PerusahaanId == companyId && p.CreatedAt >= startOfYear).Select(p => p.Nik).ToListAsync();
            var coachings = await _context.Coachings.Where(c => !c.IsDeleted && c.PerusahaanId == companyId && c.CreatedAt >= startOfYear).Select(c => c.Nik).ToListAsync();
            
            var observations = await (from o in _context.Observations
                                  join k in _context.Karyawans on o.Nik equals k.NoNik
                                  where !o.IsDeleted && o.CreatedAt >= startOfYear && k.IdPerusahaan == companyId
                                  select o.Nik).ToListAsync();

            var result = new List<object>();
            foreach (var k in deptKaryawansFiltered)
            {
                var nik = (k.NoNik ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(nik)) continue;

                int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0, p5mTar = 1;
                if (mappingsDict.TryGetValue(k.IdKaryawan, out var m))
                {
                    hTar = m.TargetHazardReport ?? 2;
                    insTar = m.TargetInspeksi ?? 1;
                    stTar = m.TargetSafetyTalk ?? 1;
                    obsTar = m.TargetObservasi ?? 0;
                    cTar = m.TargetCoaching ?? 0;
                }

                int ytdTgtH = hTar * elapsedWeeksYtd;
                int ytdTgtI = insTar * elapsedWeeksYtd;
                int ytdTgtST = stTar * elapsedWeeksYtd;
                int ytdTgtO = obsTar * elapsedWeeksYtd;
                int ytdTgtC = cTar * elapsedWeeksYtd;
                int ytdTgtP5 = p5mTar * elapsedWeeksYtd;

                int ytdActH = hazards.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int ytdActI = inspections.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int ytdActST = safetyTalks.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int ytdActO = observations.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int ytdActC = coachings.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int ytdActP5 = p5ms.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));

                int totalTgt = ytdTgtH + ytdTgtI + ytdTgtST + ytdTgtO + ytdTgtC;
                int totalAct = ytdActH + ytdActI + ytdActST + ytdActO + ytdActC;

                double compliance = totalTgt > 0 ? Math.Round((double)totalAct / totalTgt * 100.0, 1) : 0;

                result.Add(new {
                    karyawanName = k.NamaLengkap,
                    nik = k.NoNik,
                    ytdTotalTarget = totalTgt,
                    ytdTotalActual = totalAct,
                    complianceRate = compliance,
                    hazard = new { target = ytdTgtH, actual = ytdActH },
                    inspeksi = new { target = ytdTgtI, actual = ytdActI },
                    safetyTalk = new { target = ytdTgtST, actual = ytdActST },
                    observasi = new { target = ytdTgtO, actual = ytdActO },
                    coaching = new { target = ytdTgtC, actual = ytdActC },
                    p5m = new { target = ytdTgtP5, actual = ytdActP5 }
                });
            }

            var sortedResult = result.Cast<dynamic>().OrderByDescending(r => r.complianceRate).ToList();
            return Json(sortedResult);
        }

        private async Task<GeoSafetyRadarViewModel> BuildGeoSafetyRadarDataAsync(int? companyId, HashSet<int> allowedCompanyIds, string? requestedGeoArea, bool includePhotos = false)
        {
            var hazardPoints = new List<GeoSafetyPointViewModel>();
            var inspectionPoints = new List<GeoSafetyPointViewModel>();
            var p5mPoints = new List<GeoSafetyPointViewModel>();
            var safetyTalkPoints = new List<GeoSafetyPointViewModel>();

            var dbHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && (companyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))) && h.Lokasi != null && h.Lokasi.Contains(","))
            .Select(h => new { h.Id, h.Tanggal, h.Nama, h.Area, h.Lokasi, h.Temuan, h.TingkatResiko, h.StatusTemuan, h.FotoTemuan })
                .ToListAsync();

            foreach (var h in dbHazards)
            {
                if (TryParseCoordinates(h.Lokasi, out double lat, out double lon))
                {
                    hazardPoints.Add(new GeoSafetyPointViewModel
                    {
                        Id = h.Id,
                        Lat = lat,
                        Lon = lon,
                        Tanggal = h.Tanggal.ToString("dd MMM yyyy"),
                        Nama = h.Nama,
                        Area = h.Area,
                        Detail = h.Temuan,
                        Resiko = h.TingkatResiko ?? "Medium",
                        Status = h.StatusTemuan,
                        PhotoUrl = includePhotos ? NormalizeImagePath(h.FotoTemuan) : null
                    });
                }
            }

            var dbInspections = await _context.Inspections
                .Where(i => !i.IsDeleted && (companyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))) && i.Lokasi != null && i.Lokasi.Contains(","))
                .Select(i => new { i.Id, i.Tanggal, i.Nama, i.Area, i.Lokasi, i.JenisInspeksi, i.LampiranJson })
                .ToListAsync();

            foreach (var i in dbInspections)
            {
                if (TryParseCoordinates(i.Lokasi, out double lat, out double lon))
                {
                    inspectionPoints.Add(new GeoSafetyPointViewModel
                    {
                        Id = i.Id,
                        Lat = lat,
                        Lon = lon,
                        Tanggal = i.Tanggal.ToString("dd MMM yyyy"),
                        Nama = i.Nama,
                        Area = i.Area,
                        Detail = i.JenisInspeksi,
                        PhotoUrl = includePhotos ? ExtractFirstInspectionImageUrl(i.LampiranJson) : null
                    });
                }
            }

            var dbP5ms = await _context.P5ms
                .Where(p => !p.IsDeleted && (companyId == null || (p.PerusahaanId.HasValue && allowedCompanyIds.Contains(p.PerusahaanId.Value))) && p.Lokasi != null && p.Lokasi.Contains(","))
                .Select(p => new { p.Id, p.Tanggal, p.Waktu, p.Nik, p.Nama, p.Area, p.Lokasi, p.Topik, p.Judul, p.Keterangan, p.FotoKegiatan })
                .ToListAsync();

            // P5M is stored per checklist item; group by one submission session to avoid duplicate map markers.
            var groupedP5ms = dbP5ms
                .GroupBy(p => new
                {
                    Date = p.Tanggal.Date,
                    p.Waktu,
                    p.Nik,
                    p.Nama,
                    p.Area,
                    p.Lokasi,
                    p.Topik,
                    p.Judul,
                    p.Keterangan,
                    p.FotoKegiatan
                })
                .Select(g => g.OrderByDescending(x => x.Id).First())
                .ToList();

            foreach (var p in groupedP5ms)
            {
                if (TryParseCoordinates(p.Lokasi, out double lat, out double lon))
                {
                    p5mPoints.Add(new GeoSafetyPointViewModel
                    {
                        Id = p.Id,
                        Lat = lat,
                        Lon = lon,
                        Tanggal = p.Tanggal.ToString("dd MMM yyyy"),
                        Nama = p.Nama,
                        Area = p.Area,
                        Detail = !string.IsNullOrWhiteSpace(p.Topik)
                            ? p.Topik
                            : (!string.IsNullOrWhiteSpace(p.Judul) ? p.Judul : p.Keterangan),
                        PhotoUrl = includePhotos ? NormalizeImagePath(p.FotoKegiatan) : null
                    });
                }
            }

            var dbSafetyTalks = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && (companyId == null || (s.PerusahaanId.HasValue && allowedCompanyIds.Contains(s.PerusahaanId.Value))) && s.Lokasi != null && s.Lokasi.Contains(","))
                .Select(s => new { s.Id, s.Tanggal, s.Nama, s.Area, s.Lokasi, s.Judul, s.Keterangan, s.FotoKegiatan })
                .ToListAsync();

            foreach (var s in dbSafetyTalks)
            {
                if (TryParseCoordinates(s.Lokasi, out double lat, out double lon))
                {
                    safetyTalkPoints.Add(new GeoSafetyPointViewModel
                    {
                        Id = s.Id,
                        Lat = lat,
                        Lon = lon,
                        Tanggal = s.Tanggal.ToString("dd MMM yyyy"),
                        Nama = s.Nama,
                        Area = s.Area,
                        Detail = !string.IsNullOrWhiteSpace(s.Judul) ? s.Judul : s.Keterangan,
                        PhotoUrl = includePhotos ? NormalizeImagePath(s.FotoKegiatan) : null
                    });
                }
            }

            var geoAreaOptions = hazardPoints.Select(h => h.Area)
                .Concat(inspectionPoints.Select(i => i.Area))
                .Concat(p5mPoints.Select(p => p.Area))
                .Concat(safetyTalkPoints.Select(s => s.Area))
                .Where(area => !string.IsNullOrWhiteSpace(area))
                .Select(area => area!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(area => area)
                .ToList();

            var selectedGeoArea = !string.IsNullOrWhiteSpace(requestedGeoArea) &&
                                  geoAreaOptions.Any(area => string.Equals(area, requestedGeoArea, StringComparison.OrdinalIgnoreCase))
                ? geoAreaOptions.First(area => string.Equals(area, requestedGeoArea, StringComparison.OrdinalIgnoreCase))
                : geoAreaOptions.FirstOrDefault();

            return new GeoSafetyRadarViewModel
            {
                HazardPoints = hazardPoints,
                InspectionPoints = inspectionPoints,
                P5mPoints = p5mPoints,
                SafetyTalkPoints = safetyTalkPoints,
                GeoAreaOptions = geoAreaOptions,
                SelectedGeoArea = selectedGeoArea
            };
        }

        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Pencapaian SAP";
            ViewData["ActiveTab"] = "Performance";

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?.Trim();
            var userCompanyIdClaim = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(userCompanyIdClaim, out int userCid) && userCid > 0 ? userCid : (int?)null;
            var (companyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);
            var allCompanies = await _context.Perusahaans.Where(p => p.StatusAktif).ToListAsync();

            // 1. Total Karyawan Aktif
            var totalKaryawan = await _context.Karyawans
                .CountAsync(k => k.StatusAktif && (companyId == null || allowedCompanyIds.Contains(k.IdPerusahaan)));

            var targetMappingCompany = await _context.KaryawanJabatanMappings
                .Where(m => companyId == null || (m.PerusahaanId.HasValue && allowedCompanyIds.Contains(m.PerusahaanId.Value)))
                .ToListAsync();

            int monthlyTarget = targetMappingCompany.Sum(m => (m.TargetHazardReport ?? 0) + (m.TargetInspeksi ?? 0) + (m.TargetSafetyTalk ?? 0) + (m.TargetObservasi ?? 0) + (m.TargetCoaching ?? 0));
            int weeklyTarget = (int)Math.Round(monthlyTarget / 4.0, MidpointRounding.AwayFromZero);
            if (weeklyTarget < 1 && monthlyTarget > 0) weeklyTarget = 1;

            // Date ranges
            var now = DateTime.Now;
            var startOfWeek = DateTime.Today.AddDays(-6); // rolling 7 calendar days (today inclusive)
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            // Submissions query
            var hazards = _context.HazardReports.Where(h => !h.IsDeleted && (companyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))));
            var inspections = _context.Inspections.Where(i => !i.IsDeleted && (companyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))));
            var safetyTalks = _context.SafetyTalks.Where(s => !s.IsDeleted && (companyId == null || (s.PerusahaanId.HasValue && allowedCompanyIds.Contains(s.PerusahaanId.Value))));
            var p5ms = _context.P5ms.Where(p => !p.IsDeleted && (companyId == null || (p.PerusahaanId.HasValue && allowedCompanyIds.Contains(p.PerusahaanId.Value))));
            var coachings = _context.Coachings.Where(c => !c.IsDeleted && (companyId == null || (c.PerusahaanId.HasValue && allowedCompanyIds.Contains(c.PerusahaanId.Value))));

            var observationsQuery = _context.Observations.Where(o => !o.IsDeleted);
            if (companyId.HasValue)
            {
                var allowedIds = allowedCompanyIds;
                observationsQuery = from o in observationsQuery
                                    join k in _context.Karyawans on o.Nik equals k.NoNik
                                    where allowedIds.Contains(k.IdPerusahaan)
                                    select o;
            }

            // 2. Realisasi Minggu Ini
            int weekHazards = await hazards.CountAsync(h => h.CreatedAt >= startOfWeek);
            int weekInspections = await inspections.CountAsync(i => i.CreatedAt >= startOfWeek);
            int weekSafetyTalks = await safetyTalks.CountAsync(s => s.CreatedAt >= startOfWeek);
            int weekP5ms = await p5ms.CountAsync(p => p.CreatedAt >= startOfWeek);
            int weekCoachings = await coachings.CountAsync(c => c.CreatedAt >= startOfWeek);
            int weekObservations = await observationsQuery.CountAsync(o => o.CreatedAt >= startOfWeek);
            int weekTotal = weekHazards + weekInspections + weekSafetyTalks + weekCoachings + weekObservations;

            // 3. Realisasi Bulan Ini
            int monthHazards = await hazards.CountAsync(h => h.CreatedAt >= startOfMonth);
            int monthInspections = await inspections.CountAsync(i => i.CreatedAt >= startOfMonth);
            int monthSafetyTalks = await safetyTalks.CountAsync(s => s.CreatedAt >= startOfMonth);
            int monthP5ms = await p5ms.CountAsync(p => p.CreatedAt >= startOfMonth);
            int monthCoachings = await coachings.CountAsync(c => c.CreatedAt >= startOfMonth);
            int monthObservations = await observationsQuery.CountAsync(o => o.CreatedAt >= startOfMonth);
            int monthTotal = monthHazards + monthInspections + monthSafetyTalks + monthCoachings + monthObservations;

            // Incident Pyramid from the same source used by Incident/Index (published incidents)
            var startOfYear = new DateTime(now.Year, 1, 1);
            var endOfYear = new DateTime(now.Year, 12, 31, 23, 59, 59);
            var incidentBaseQuery = _context.IncidentNewsList.Where(i => i.IsPublished);
            var incidentIndexTotal = await incidentBaseQuery.CountAsync();

            var incidentMonthData = await incidentBaseQuery
                .Where(i => (i.TanggalKejadian ?? i.CreatedAt) >= startOfYear && (i.TanggalKejadian ?? i.CreatedAt) <= endOfYear)
                .Select(i => new { i.Kategori, i.Judul, i.Konten })
                .ToListAsync();

            string BuildIncidentText(string? kategori, string? judul, string? konten)
            {
                return string.Join(" ", new[] { kategori ?? string.Empty, judul ?? string.Empty, konten ?? string.Empty })
                    .ToLowerInvariant();
            }

            string ResolveIncidentCategory(string? kategori, string? judul, string? konten)
            {
                var k = (kategori ?? string.Empty).Trim();
                if (k.Equals("Fatality", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Fatal", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Mati", StringComparison.OrdinalIgnoreCase))
                {
                    return "Fatality";
                }
                if (k.Equals("First Aid Injury", StringComparison.OrdinalIgnoreCase))
                {
                    return "First Aid Injury";
                }
                if (k.Equals("Kebakaran", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Fire", StringComparison.OrdinalIgnoreCase))
                {
                    return "Kebakaran";
                }
                if (k.Equals("Medical Treatment Injury", StringComparison.OrdinalIgnoreCase))
                {
                    return "Medical Treatment Injury";
                }
                if (k.Equals("Near Miss", StringComparison.OrdinalIgnoreCase))
                {
                    return "Near Miss";
                }
                if (k.Equals("Property Damage", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Property Damaged", StringComparison.OrdinalIgnoreCase))
                {
                    return "Property Damage";
                }

                var text = BuildIncidentText(kategori, judul, konten);
                if (text.Contains("meninggal") || text.Contains("death") || text.Contains("fatal")) return "Fatality";
                if (text.Contains("kebakaran") || text.Contains("fire")) return "Kebakaran";
                if (text.Contains("medical treatment") || text.Contains("rawat jalan") || text.Contains("klinik") || text.Contains("dokter")) return "Medical Treatment Injury";
                if (text.Contains("first aid") || text.Contains("p3k") || text.Contains("pertolongan pertama")) return "First Aid Injury";
                if (text.Contains("property") || text.Contains("damage") || text.Contains("damaged") || text.Contains("kerusakan") || text.Contains("aset") || text.Contains("alat rusak")) return "Property Damage";
                if (text.Contains("near miss") || text.Contains("nyaris") || text.Contains("hampir celaka")) return "Near Miss";

                return "Near Miss";
            }

            int incidentNearMiss = 0;
            int incidentPropertyDamage = 0;
            int incidentFirstAidInjury = 0;
            int incidentMedicalTreatmentInjury = 0;
            int incidentKebakaran = 0;
            int incidentFatality = 0;

            foreach (var item in incidentMonthData)
            {
                var canonicalCategory = ResolveIncidentCategory(item.Kategori, item.Judul, item.Konten);
                switch (canonicalCategory)
                {
                    case "Fatality":
                        incidentFatality++;
                        break;
                    case "First Aid Injury":
                        incidentFirstAidInjury++;
                        break;
                    case "Kebakaran":
                        incidentKebakaran++;
                        break;
                    case "Medical Treatment Injury":
                        incidentMedicalTreatmentInjury++;
                        break;
                    case "Property Damage":
                        incidentPropertyDamage++;
                        break;
                    default:
                        incidentNearMiss++;
                        break;
                }
            }

            ViewBag.IncidentIndexTotal = incidentIndexTotal;
            ViewBag.IncidentYearTotal = incidentMonthData.Count;

            // 4. Open Hazards breakdown by Risk Level (Low/Medium/High/Extreme)
            // Scoped list keeps existing behavior for KPI cards that follow user/company access scope.
            var openHazardsList = await hazards.Where(h => h.StatusTemuan == "Open" && h.TingkatResiko != null).Select(h => h.TingkatResiko).ToListAsync();

            // Safety pyramid must show all companies regardless of login scope and status.
            var riskHazardsListAllCompanies = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.TingkatResiko != null)
                .Select(h => h.TingkatResiko)
                .ToListAsync();

            int safetyOpenExtreme = riskHazardsListAllCompanies.Count(r => string.Equals(r, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sangat Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ekstrim", StringComparison.OrdinalIgnoreCase));
            int safetyOpenHigh = riskHazardsListAllCompanies.Count(r => string.Equals(r, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Tinggi", StringComparison.OrdinalIgnoreCase));
            int safetyOpenMedium = riskHazardsListAllCompanies.Count(r => string.Equals(r, "Medium", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sedang", StringComparison.OrdinalIgnoreCase));
            int safetyOpenLow = riskHazardsListAllCompanies.Count(r => string.Equals(r, "Low", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ringan", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Rendah", StringComparison.OrdinalIgnoreCase));

            int openInsiden = openHazardsList.Count(r => string.Equals(r, "Insiden", StringComparison.OrdinalIgnoreCase));
            int openKritis = openHazardsList.Count(r => string.Equals(r, "Kritis", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Critical", StringComparison.OrdinalIgnoreCase));
            int openExtreme = openHazardsList.Count(r => string.Equals(r, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sangat Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ekstrim", StringComparison.OrdinalIgnoreCase));
            int openHigh = openHazardsList.Count(r => string.Equals(r, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Tinggi", StringComparison.OrdinalIgnoreCase));
            int openMedium = openHazardsList.Count(r => string.Equals(r, "Medium", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sedang", StringComparison.OrdinalIgnoreCase));
            int openLow = openHazardsList.Count(r => string.Equals(r, "Low", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ringan", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Rendah", StringComparison.OrdinalIgnoreCase));

            // 5. Total Open vs Closed Hazards
            int totalOpenHazards = await hazards.CountAsync(h => h.StatusTemuan == "Open");
            int totalClosedHazards = await hazards.CountAsync(h => h.StatusTemuan == "Closed");

            // 5a. Monitoring Metrics
            int totalHazards = totalOpenHazards + totalClosedHazards;
            double complianceClose = totalHazards > 0 ? (double)totalClosedHazards / totalHazards * 100 : 0;

            var overdueDate = DateTime.Now.AddDays(-14);
            int overdueHazards = await hazards.CountAsync(h => h.StatusTemuan == "Open" && h.Tanggal < overdueDate);
            double overdueRate = totalOpenHazards > 0 ? (double)overdueHazards / totalOpenHazards * 100 : 0;

            int highRiskOpen = openExtreme + openHigh;
            double complianceRisk = totalOpenHazards > 0 ? (double)highRiskOpen / totalOpenHazards * 100 : 0;

            var allHazardRisks = await hazards.Select(h => new { h.StatusTemuan, h.TingkatResiko }).ToListAsync();
            int GetRiskWeight(string? r) {
                if (string.IsNullOrEmpty(r)) return 0;
                if (r.Contains("Extreme", StringComparison.OrdinalIgnoreCase) || r.Contains("Ekstrim", StringComparison.OrdinalIgnoreCase) || r.Contains("Sangat Berat", StringComparison.OrdinalIgnoreCase)) return 4;
                if (r.Contains("Kritis", StringComparison.OrdinalIgnoreCase) || r.Contains("Critical", StringComparison.OrdinalIgnoreCase)) return 4;
                if (r.Contains("High", StringComparison.OrdinalIgnoreCase) || r.Contains("Tinggi", StringComparison.OrdinalIgnoreCase) || r.Contains("Berat", StringComparison.OrdinalIgnoreCase)) return 3;
                if (r.Contains("Medium", StringComparison.OrdinalIgnoreCase) || r.Contains("Sedang", StringComparison.OrdinalIgnoreCase)) return 2;
                if (r.Contains("Low", StringComparison.OrdinalIgnoreCase) || r.Contains("Rendah", StringComparison.OrdinalIgnoreCase) || r.Contains("Ringan", StringComparison.OrdinalIgnoreCase)) return 1;
                return 0;
            }
            int totalRiskWeight = allHazardRisks.Sum(h => GetRiskWeight(h.TingkatResiko));
            int closedRiskWeight = allHazardRisks.Where(h => h.StatusTemuan == "Closed").Sum(h => GetRiskWeight(h.TingkatResiko));
            double rri = totalRiskWeight > 0 ? (double)closedRiskWeight / totalRiskWeight * 100 : 0;

            // RHR is calculated from repeated hazard locations (location-only basis).
            var hazardLocations = await hazards
                .Where(h => !string.IsNullOrWhiteSpace(h.Lokasi))
                .Select(h => h.Lokasi!.Trim())
                .ToListAsync();

            var groupedLocations = hazardLocations
                .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int repeatLocations = groupedLocations.Count(g => g.Count() > 1);
            int totalLocations = groupedLocations.Count;
            double rhr = totalLocations > 0 ? (double)repeatLocations / totalLocations * 100 : 0;

            var topRepeated = groupedLocations
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new {
                    Label = g.Key,
                    Count = g.Count()
                })
                .ToList();
            
            ViewBag.TopRepeatedLabels = topRepeated.Select(x => x.Label).ToList();
            ViewBag.TopRepeatedData = topRepeated.Select(x => x.Count).ToList();


            var closedHazardsList = await hazards.Where(h => h.StatusTemuan == "Closed" && h.TingkatResiko != null).Select(h => h.TingkatResiko).ToListAsync();
            int closedKritis = closedHazardsList.Count(r => string.Equals(r, "Kritis", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Critical", StringComparison.OrdinalIgnoreCase));
            int closedExtreme = closedHazardsList.Count(r => string.Equals(r, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sangat Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ekstrim", StringComparison.OrdinalIgnoreCase));
            int closedHigh = closedHazardsList.Count(r => string.Equals(r, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Tinggi", StringComparison.OrdinalIgnoreCase));
            int highRiskClosed = closedExtreme + closedHigh;
            int totalHighRisk = highRiskOpen + highRiskClosed;
            double highRiskResolution = totalHighRisk > 0 ? (double)highRiskClosed / totalHighRisk * 100 : 0;

            // 5b. Extra Professional Graphs Data
            var allKategori = await hazards.Where(h => h.StatusTemuan == "Open" && h.KategoriBahaya != null).Select(h => h.KategoriBahaya).ToListAsync();
            int unsafeActCount = allKategori.Count(k => k != null && (k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase)));
            int unsafeConditionCount = allKategori.Count(k => k != null && (k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("TTA", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase)));
            
            var topAreas = await hazards.Where(h => h.StatusTemuan == "Open" && !string.IsNullOrEmpty(h.Area))
                                        .GroupBy(h => h.Area)
                                        .Select(g => new { Area = g.Key, Count = g.Count() })
                                        .OrderByDescending(x => x.Count)
                                        .Take(5)
                                        .ToListAsync();

            // 6. Leaderboard Perusahaan
            var allKaryawans = await _context.Karyawans.Where(k => k.StatusAktif).ToListAsync();

            var mappingsDict = new Dictionary<int, KaryawanJabatanMappingPreviewView>();
            foreach (var m in targetMappingCompany)
            {
                mappingsDict[m.KaryawanId] = m;
            }

            var employeeTargets = new Dictionary<int, (int hTar, int insTar, int stTar, int obsTar, int cTar, int p5mTar, int total)>();
            var employeeTargetsByNik = new Dictionary<string, (int hTar, int insTar, int stTar, int obsTar, int cTar, int p5mTar, int total)>(StringComparer.OrdinalIgnoreCase);

            foreach (var k in allKaryawans)
            {
                int hTar = 2;
                int insTar = 1;
                int stTar = 1;
                int obsTar = 0;
                int cTar = 0;
                int p5mTar = 1;

                if (mappingsDict.TryGetValue(k.IdKaryawan, out var m))
                {
                    hTar = m.TargetHazardReport ?? 2;
                    insTar = m.TargetInspeksi ?? 1;
                    stTar = m.TargetSafetyTalk ?? 1;
                    obsTar = m.TargetObservasi ?? 0;
                    cTar = m.TargetCoaching ?? 0;
                }

                int totalTar = hTar + insTar + stTar + obsTar + cTar; // Exclude p5mTar from overall SAP calculation
                employeeTargets[k.IdKaryawan] = (hTar, insTar, stTar, obsTar, cTar, p5mTar, totalTar);

                var cleanNik = (k.NoNik ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleanNik))
                {
                    employeeTargetsByNik[cleanNik] = (hTar, insTar, stTar, obsTar, cTar, p5mTar, totalTar);
                }
            }

            var compHazards = await _context.HazardReports.Where(h => !h.IsDeleted && h.CreatedAt >= startOfYear).GroupBy(h => h.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compInspections = await _context.Inspections.Where(i => !i.IsDeleted && i.CreatedAt >= startOfYear).GroupBy(i => i.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compSafetyTalks = await _context.SafetyTalks.Where(s => !s.IsDeleted && s.CreatedAt >= startOfYear).GroupBy(s => s.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compP5ms = await _context.P5ms.Where(p => !p.IsDeleted && p.CreatedAt >= startOfYear).GroupBy(p => p.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compCoachings = await _context.Coachings.Where(c => !c.IsDeleted && c.CreatedAt >= startOfYear).GroupBy(c => c.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compObservations = await (from o in _context.Observations
                                          join k in _context.Karyawans on o.Nik equals k.NoNik
                                          where !o.IsDeleted && o.CreatedAt >= startOfYear
                                          group o by k.IdPerusahaan into g
                                          select new { CompId = (int?)g.Key, Count = g.Count() })
                                         .ToListAsync();

            var leaderboard = new List<CompanyLeaderboardViewModel>();
            
            int targetHazardTotal = 0;
            int targetInspeksiTotal = 0;
            int targetSafetyTalkTotal = 0;
            int targetObservasiTotal = 0;
            int targetCoachingTotal = 0;
            int targetP5mTotal = 0;

            int realHazardTotal = 0;
            int realInspeksiTotal = 0;
            int realSafetyTalkTotal = 0;
            int realP5mTotal = 0;
            int realCoachingTotal = 0;
            int realObservasiTotal = 0;

            foreach (var c in allCompanies)
            {
                if (!isAdmin && companyId.HasValue && !allowedCompanyIds.Contains(c.PerusahaanId))
                {
                    continue;
                }

                int empCount = allKaryawans.Count(k => k.IdPerusahaan == c.PerusahaanId);
                if (empCount == 0) continue;

                int cHaz = compHazards.FirstOrDefault(h => h.CompId == c.PerusahaanId)?.Count ?? 0;
                int cIns = compInspections.FirstOrDefault(i => i.CompId == c.PerusahaanId)?.Count ?? 0;
                int cST = compSafetyTalks.FirstOrDefault(s => s.CompId == c.PerusahaanId)?.Count ?? 0;
                int cP5m = compP5ms.FirstOrDefault(p => p.CompId == c.PerusahaanId)?.Count ?? 0;
                int cCoa = compCoachings.FirstOrDefault(co => co.CompId == c.PerusahaanId)?.Count ?? 0;
                int cObs = compObservations.FirstOrDefault(ob => ob.CompId == c.PerusahaanId)?.Count ?? 0;

                int subCount = cHaz + cIns + cST + cCoa + cObs;

                realHazardTotal += cHaz;
                realInspeksiTotal += cIns;
                realSafetyTalkTotal += cST;
                realP5mTotal += cP5m;
                realCoachingTotal += cCoa;
                realObservasiTotal += cObs;

                var companyEmps = allKaryawans.Where(k => k.IdPerusahaan == c.PerusahaanId).ToList();
                int companyMonthlyTarget = 0;
                foreach(var emp in companyEmps)
                {
                    if (employeeTargets.TryGetValue(emp.IdKaryawan, out var et))
                    {
                        companyMonthlyTarget += et.total;
                        targetHazardTotal += et.hTar;
                        targetInspeksiTotal += et.insTar;
                        targetSafetyTalkTotal += et.stTar;
                        targetObservasiTotal += et.obsTar;
                        targetCoachingTotal += et.cTar;
                        targetP5mTotal += et.p5mTar;
                    }
                    else
                    {
                        companyMonthlyTarget += 5; // Default overall total (2 + 1 + 1 + 1 P5M)
                        targetHazardTotal += 2;
                        targetInspeksiTotal += 1;
                        targetSafetyTalkTotal += 1;
                        targetP5mTotal += 1;
                    }
                }

                int monthsElapsed = Math.Max(1, now.Month);
                int target = companyMonthlyTarget * monthsElapsed;
                double achievementRate = target > 0 ? (double)subCount / target * 100.0 : 0.0;

                leaderboard.Add(new CompanyLeaderboardViewModel
                {
                    CompanyId = c.PerusahaanId,
                    CompanyName = c.NamaPerusahaan ?? "Unknown",
                    ActiveEmployees = empCount,
                    TotalSubmissions = subCount,
                    TargetSubmissions = target,
                    AchievementRate = Math.Round(achievementRate, 1)
                });
            }

            int totalMonthsElapsed = Math.Max(1, now.Month);
            targetHazardTotal *= totalMonthsElapsed;
            targetInspeksiTotal *= totalMonthsElapsed;
            targetSafetyTalkTotal *= totalMonthsElapsed;
            targetObservasiTotal *= totalMonthsElapsed;
            targetCoachingTotal *= totalMonthsElapsed;
            targetP5mTotal *= totalMonthsElapsed;

            ViewBag.SaTypeLabels = new List<string> { "Hazard Report", "Inspeksi", "Safety Talk", "P5M", "Observasi", "Coaching" };
            ViewBag.SaTypeTargetData = new List<int> { targetHazardTotal, targetInspeksiTotal, targetSafetyTalkTotal, targetP5mTotal, targetObservasiTotal, targetCoachingTotal };
            ViewBag.SaTypeRealData = new List<int> { realHazardTotal, realInspeksiTotal, realSafetyTalkTotal, realP5mTotal, realObservasiTotal, realCoachingTotal };

            leaderboard = leaderboard.OrderByDescending(l => l.AchievementRate).Take(10).ToList();
            ViewBag.Leaderboard = leaderboard;
            ViewBag.IsAdmin = isAdmin;

            // 7. Data Trend Bulanan (6 Bulan Terakhir)
            var monthlyTrend = new List<MonthlyTrendViewModel>();
            for (int i = 5; i >= 0; i--)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);

                int hCount = await hazards.CountAsync(h => h.CreatedAt >= monthStart && h.CreatedAt < monthEnd);
                int iCount = await inspections.CountAsync(i => i.CreatedAt >= monthStart && i.CreatedAt < monthEnd);
                int sCount = await safetyTalks.CountAsync(s => s.CreatedAt >= monthStart && s.CreatedAt < monthEnd);
                int pCount = await p5ms.CountAsync(p => p.CreatedAt >= monthStart && p.CreatedAt < monthEnd);

                monthlyTrend.Add(new MonthlyTrendViewModel
                {
                    MonthLabel = monthStart.ToString("MMM yyyy"),
                    Hazards = hCount,
                    Inspections = iCount,
                    SafetyTalks = sCount,
                    P5ms = pCount
                });
            }

            // 8. Individual (My) Achievement Stats
            int myHazardsWeek = 0;
            int myInspectionsWeek = 0;
            int mySafetyTalksWeek = 0;
            int myP5msWeek = 0;
            int myObservationsWeek = 0;
            int myCoachingsWeek = 0;

            int myHazardsMonth = 0;
            int myInspectionsMonth = 0;
            int mySafetyTalksMonth = 0;
            int myP5msMonth = 0;
            int myObservationsMonth = 0;
            int myCoachingsMonth = 0;

            int targetHazardReport = 2;
            int targetInspeksi = 1;
            int targetSafetyTalk = 1;
            int targetObservasi = 0;
            int targetCoaching = 0;
            int targetP5m = 1;
            string? kategoriPengawas = null;

            if (!string.IsNullOrEmpty(userNik))
            {
                // Retrieve employee target mapping
                var currentKaryawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik != null && k.NoNik == userNik && k.StatusAktif);
                if (currentKaryawan != null)
                {
                    ViewBag.KaryawanIdMitra = currentKaryawan.IdKaryawan;
                    var personalInfo = await _context.Personals.FirstOrDefaultAsync(p => p.IdPersonal == currentKaryawan.IdPersonal);
                    if (personalInfo != null)
                    {
                        ViewBag.NamaMitra = personalInfo.NamaLengkap;
                    }

                    var targetMapping = await _context.KaryawanJabatanMappings.FirstOrDefaultAsync(m => m.KaryawanId == currentKaryawan.IdKaryawan);
                    if (targetMapping != null)
                    {
                        targetHazardReport = targetMapping.TargetHazardReport ?? 2;
                        targetInspeksi = targetMapping.TargetInspeksi ?? 1;
                        targetSafetyTalk = targetMapping.TargetSafetyTalk ?? 1;
                        targetObservasi = targetMapping.TargetObservasi ?? 0;
                        targetCoaching = targetMapping.TargetCoaching ?? 0;
                        kategoriPengawas = targetMapping.KategoriPengawas;
                    }
                }

                // Personal metrics must always follow logged-in user identity (NIK), not company aggregation.
                var myHazardsQuery = _context.HazardReports.Where(h => !h.IsDeleted && h.Nik != null && h.Nik == userNik);
                var myInspectionsQuery = _context.Inspections.Where(i => !i.IsDeleted && i.Nik != null && i.Nik == userNik);
                var mySafetyTalksQuery = _context.SafetyTalks.Where(s => !s.IsDeleted && s.Nik != null && s.Nik == userNik);
                var myP5msQuery = _context.P5ms.Where(p => !p.IsDeleted && p.Nik != null && p.Nik == userNik);
                var myObservationsQuery = _context.Observations.Where(o => !o.IsDeleted && o.Nik != null && o.Nik == userNik);
                var myCoachingsQuery = _context.Coachings.Where(c => !c.IsDeleted && (c.Nik == userNik || _context.CoachingParticipants.Any(p => p.CoachingId == c.Id && p.Nik == userNik)));

                myHazardsWeek = await myHazardsQuery.CountAsync(h => h.CreatedAt >= startOfWeek);
                myInspectionsWeek = await myInspectionsQuery.CountAsync(i => i.CreatedAt >= startOfWeek);
                mySafetyTalksWeek = await mySafetyTalksQuery.CountAsync(s => s.CreatedAt >= startOfWeek);
                myP5msWeek = await myP5msQuery.CountAsync(p => p.CreatedAt >= startOfWeek);
                myObservationsWeek = await myObservationsQuery.CountAsync(o => o.Date >= startOfWeek);
                myCoachingsWeek = await myCoachingsQuery.CountAsync(c => c.CreatedAt >= startOfWeek);

                myHazardsMonth = await myHazardsQuery.CountAsync(h => h.CreatedAt >= startOfMonth);
                myInspectionsMonth = await myInspectionsQuery.CountAsync(i => i.CreatedAt >= startOfMonth);
                mySafetyTalksMonth = await mySafetyTalksQuery.CountAsync(s => s.CreatedAt >= startOfMonth);
                myP5msMonth = await myP5msQuery.CountAsync(p => p.CreatedAt >= startOfMonth);
                myObservationsMonth = await myObservationsQuery.CountAsync(o => o.Date >= startOfMonth);
                myCoachingsMonth = await myCoachingsQuery.CountAsync(c => c.CreatedAt >= startOfMonth);
            }

            int myTotalWeek = myHazardsWeek + myInspectionsWeek + mySafetyTalksWeek + myObservationsWeek + myCoachingsWeek;
            int myTotalMonth = myHazardsMonth + myInspectionsMonth + mySafetyTalksMonth + myObservationsMonth + myCoachingsMonth;
            int myTotalMonthTarget = targetHazardReport + targetInspeksi + targetSafetyTalk + targetObservasi + targetCoaching;

            // 9. Average Closure Days for Action Plans
            var closedActions = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.Status == "Closed" && a.TanggalPerbaikan != null && (companyId == null || a.PerusahaanId == companyId.Value))
                .Select(a => new { a.CreatedAt, a.TanggalPerbaikan })
                .ToListAsync();

            double avgClosureDays = 0;
            if (closedActions.Count > 0)
            {
                var totalDays = closedActions.Sum(a => ((a.TanggalPerbaikan ?? a.CreatedAt) - a.CreatedAt).TotalDays);
                avgClosureDays = Math.Round(totalDays / closedActions.Count, 1);
            }
            ViewBag.AvgClosureDays = avgClosureDays;

            ViewBag.TotalKaryawan = totalKaryawan;
            ViewBag.WeeklyTarget = weeklyTarget;
            ViewBag.WeeklyRealization = weekTotal;
            ViewBag.MonthlyTarget = monthlyTarget;
            ViewBag.MonthlyRealization = monthTotal;

            ViewBag.WeekHazards = weekHazards;
            ViewBag.WeekInspections = weekInspections;
            ViewBag.WeekSafetyTalks = weekSafetyTalks;
            ViewBag.WeekP5ms = weekP5ms;

            ViewBag.MonthHazards = monthHazards;
            ViewBag.MonthInspections = monthInspections;
            ViewBag.MonthSafetyTalks = monthSafetyTalks;
            ViewBag.MonthP5ms = monthP5ms;

            ViewBag.IncidentNearMiss = incidentNearMiss;
            ViewBag.IncidentPropertyDamage = incidentPropertyDamage;
            ViewBag.IncidentFirstAidInjury = incidentFirstAidInjury;
            ViewBag.IncidentMedicalTreatmentInjury = incidentMedicalTreatmentInjury;
            ViewBag.IncidentKebakaran = incidentKebakaran;
            ViewBag.IncidentFatality = incidentFatality;

            int accidentPyramidTotal = incidentNearMiss
                + incidentPropertyDamage
                + incidentFirstAidInjury
                + incidentMedicalTreatmentInjury
                + incidentKebakaran
                + incidentFatality;
            ViewBag.SafetyTopInsiden = accidentPyramidTotal;

            ViewBag.OpenInsiden = openInsiden;
            ViewBag.OpenKritis = openKritis;
            ViewBag.OpenExtreme = openExtreme;
            ViewBag.OpenHigh = openHigh;
            ViewBag.OpenMedium = openMedium;
            ViewBag.OpenLow = openLow;

            ViewBag.SafetyOpenExtreme = safetyOpenExtreme;
            ViewBag.SafetyOpenHigh = safetyOpenHigh;
            ViewBag.SafetyOpenMedium = safetyOpenMedium;
            ViewBag.SafetyOpenLow = safetyOpenLow;

            ViewBag.TotalOpenHazards = totalOpenHazards;
            ViewBag.TotalClosedHazards = totalClosedHazards;

            ViewBag.Leaderboard = leaderboard;
            ViewBag.MonthlyTrend = monthlyTrend;

            // 8. Monitoring Metrics ViewBags
            ViewBag.ComplianceClose = Math.Round(complianceClose, 1);
            ViewBag.OverdueRate = Math.Round(overdueRate, 1);
            ViewBag.ComplianceRisk = Math.Round(complianceRisk, 1);
            ViewBag.RRI = Math.Round(rri, 1);
            ViewBag.RHR = Math.Round(rhr, 1);
            ViewBag.RepeatHazards = repeatLocations;
            ViewBag.TotalLocations = totalLocations;
            ViewBag.TotalSignatures = totalLocations;
            ViewBag.HighRiskResolution = Math.Round(highRiskResolution, 1);

            ViewBag.UnsafeActCount = unsafeActCount;
            ViewBag.UnsafeConditionCount = unsafeConditionCount;
            ViewBag.TopAreasLabels = topAreas.Select(a => a.Area).ToList();
            ViewBag.TopAreasData = topAreas.Select(a => a.Count).ToList();

            // Individual ViewBag properties
            ViewBag.MyHazardsWeek = myHazardsWeek;
            ViewBag.MyInspectionsWeek = myInspectionsWeek;
            ViewBag.MySafetyTalksWeek = mySafetyTalksWeek;
            ViewBag.MyP5msWeek = myP5msWeek;
            ViewBag.MyObservationsWeek = myObservationsWeek;
            ViewBag.MyCoachingsWeek = myCoachingsWeek;
            ViewBag.MyTotalWeek = myTotalWeek;

            ViewBag.MyHazardsMonth = myHazardsMonth;
            ViewBag.MyInspectionsMonth = myInspectionsMonth;
            ViewBag.MySafetyTalksMonth = mySafetyTalksMonth;
            ViewBag.MyP5msMonth = myP5msMonth;
            ViewBag.MyObservationsMonth = myObservationsMonth;
            ViewBag.MyCoachingsMonth = myCoachingsMonth;
            ViewBag.MyTotalMonth = myTotalMonth;

            ViewBag.TargetHazardReport = targetHazardReport;
            ViewBag.TargetInspeksi = targetInspeksi;
            ViewBag.TargetSafetyTalk = targetSafetyTalk;
            ViewBag.TargetObservasi = targetObservasi;
            ViewBag.TargetCoaching = targetCoaching;
            ViewBag.TargetP5M = targetP5m;
            ViewBag.MyTotalMonthTarget = myTotalMonthTarget;
            ViewBag.KategoriPengawas = kategoriPengawas;

            // Individual Gamification Rank
            string myBadgeName = "Safety Novice";
            string myBadgeIcon = "bi-shield-slash";
            string myBadgeColor = "#9ca3af";
            
            double compliancePercentage = myTotalMonthTarget > 0 ? (double)myTotalMonth / myTotalMonthTarget * 100.0 : 0.0;
            
            if (myTotalMonthTarget > 0)
            {
                if (compliancePercentage >= 100.0)
                {
                    myBadgeName = "Safety Hero (Gold)";
                    myBadgeIcon = "bi-shield-fill-check";
                    myBadgeColor = "#fbbf24";
                }
                else if (compliancePercentage >= 50.0)
                {
                    myBadgeName = "Safety Champion (Silver)";
                    myBadgeIcon = "bi-shield-fill-star";
                    myBadgeColor = "#cbd5e1";
                }
                else if (compliancePercentage >= 10.0 || myTotalMonth >= 1)
                {
                    myBadgeName = "Safety Aware (Bronze)";
                    myBadgeIcon = "bi-shield-fill";
                    myBadgeColor = "#b45309";
                }
            }
            else
            {
                if (myTotalMonth >= 5)
                {
                    myBadgeName = "Safety Hero (Gold)";
                    myBadgeIcon = "bi-shield-fill-check";
                    myBadgeColor = "#fbbf24";
                }
                else if (myTotalMonth >= 3)
                {
                    myBadgeName = "Safety Champion (Silver)";
                    myBadgeIcon = "bi-shield-fill-star";
                    myBadgeColor = "#cbd5e1";
                }
                else if (myTotalMonth >= 1)
                {
                    myBadgeName = "Safety Aware (Bronze)";
                    myBadgeIcon = "bi-shield-fill";
                    myBadgeColor = "#b45309";
                }
            }

            ViewBag.MyBadgeName = myBadgeName;
            ViewBag.MyBadgeIcon = myBadgeIcon;
            ViewBag.MyBadgeColor = myBadgeColor;

            // My Contribution Share
            double myContributionShare = monthTotal > 0 ? (double)myTotalMonth / monthTotal * 100.0 : 0.0;
            ViewBag.MyContributionShare = Math.Round(myContributionShare, 1);

            // ==================== 10. Safety Role & Monitoring Queries ====================
            ViewBag.IsSafetyRole = isSafetyRole;

            // Query active employees of this company
            var allKaryawansQuery = from k in _context.Karyawans
                                    join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                    join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                    from d in dg.DefaultIfEmpty()
                                    join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                                    from c in cg.DefaultIfEmpty()
                                    where k.StatusAktif == true && (companyId == null || allowedCompanyIds.Contains(k.IdPerusahaan))
                                    select new
                                    {
                                        k.NoNik,
                                        p.NamaLengkap,
                                        NamaDepartemen = d != null ? d.NamaDepartemen : "General",
                                        NamaPerusahaan = c != null ? c.NamaPerusahaan : "Unknown"
                                    };
            var activeKaryawans = await allKaryawansQuery.ToListAsync();

            // Get submitters for current week
            var weekSubmitters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var weekHazNiks = await hazards.Where(h => h.CreatedAt >= startOfWeek).Select(h => h.Nik).Distinct().ToListAsync();
            var weekInsNiks = await inspections.Where(i => i.CreatedAt >= startOfWeek).Select(i => i.Nik).Distinct().ToListAsync();
            var weekSafNiks = await safetyTalks.Where(s => s.CreatedAt >= startOfWeek).Select(s => s.Nik).Distinct().ToListAsync();
            var weekP5mNiks = await p5ms.Where(p => p.CreatedAt >= startOfWeek).Select(p => p.Nik).Distinct().ToListAsync();
            
            foreach (var n in weekHazNiks.Concat(weekInsNiks).Concat(weekSafNiks).Concat(weekP5mNiks))
            {
                if (string.IsNullOrEmpty(n)) continue;
                var cleanNik = n.Trim();
                weekSubmitters[cleanNik] = weekSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
            }

            var weekCoachList = await coachings.Where(c => c.CreatedAt >= startOfWeek).Select(c => new { c.Id, c.Nik }).ToListAsync();
            foreach (var item in weekCoachList)
            {
                if (!string.IsNullOrEmpty(item.Nik))
                {
                    var cleanNik = item.Nik.Trim();
                    weekSubmitters[cleanNik] = weekSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                }
                var pts = await _context.CoachingParticipants.Where(p => p.CoachingId == item.Id).Select(p => p.Nik).ToListAsync();
                foreach (var pNik in pts)
                {
                    if (!string.IsNullOrEmpty(pNik))
                    {
                        var cleanNik = pNik.Trim();
                        weekSubmitters[cleanNik] = weekSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                    }
                }
            }

            // Get submitters for current month
            var monthSubmitters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var monthHazNiks = await hazards.Where(h => h.CreatedAt >= startOfMonth).Select(h => new { h.Nik }).ToListAsync();
            var monthInsNiks = await inspections.Where(i => i.CreatedAt >= startOfMonth).Select(i => new { i.Nik }).ToListAsync();
            var monthSafNiks = await safetyTalks.Where(s => s.CreatedAt >= startOfMonth).Select(s => new { s.Nik }).ToListAsync();
            var monthP5mNiks = await p5ms.Where(p => p.CreatedAt >= startOfMonth).Select(p => new { p.Nik }).ToListAsync();

            foreach (var item in monthHazNiks.Concat(monthInsNiks).Concat(monthSafNiks).Concat(monthP5mNiks))
            {
                if (string.IsNullOrEmpty(item.Nik)) continue;
                var cleanNik = item.Nik.Trim();
                monthSubmitters[cleanNik] = monthSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
            }

            var monthCoachList = await coachings.Where(c => c.CreatedAt >= startOfMonth).Select(c => new { c.Id, c.Nik }).ToListAsync();
            foreach (var item in monthCoachList)
            {
                if (!string.IsNullOrEmpty(item.Nik))
                {
                    var cleanNik = item.Nik.Trim();
                    monthSubmitters[cleanNik] = monthSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                }
                var pts = await _context.CoachingParticipants.Where(p => p.CoachingId == item.Id).Select(p => p.Nik).ToListAsync();
                foreach (var pNik in pts)
                {
                    if (!string.IsNullOrEmpty(pNik))
                    {
                        var cleanNik = pNik.Trim();
                        monthSubmitters[cleanNik] = monthSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                    }
                }
            }

            var uncompliantWeekList = new List<UncompliantEmployeeViewModel>();
            var uncompliantMonthList = new List<UncompliantEmployeeViewModel>();

            foreach (var k in activeKaryawans)
            {
                var cleanNik = k.NoNik.Trim();
                int monthCount = monthSubmitters.ContainsKey(cleanNik) ? monthSubmitters[cleanNik] : 0;
                int weekCount = weekSubmitters.ContainsKey(cleanNik) ? weekSubmitters[cleanNik] : 0;

                int empTotalMonthlyTarget = 4;
                if (employeeTargetsByNik.TryGetValue(cleanNik, out var et))
                {
                    empTotalMonthlyTarget = et.total;
                }

                if (empTotalMonthlyTarget > 0)
                {
                    int empWeeklyTarget = (int)Math.Round(empTotalMonthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                    if (empWeeklyTarget < 1) empWeeklyTarget = 1;

                    if (weekCount < empWeeklyTarget)
                    {
                        uncompliantWeekList.Add(new UncompliantEmployeeViewModel
                        {
                            Nik = k.NoNik,
                            Nama = k.NamaLengkap,
                            Departemen = k.NamaDepartemen,
                            Perusahaan = k.NamaPerusahaan,
                            SubmissionCount = weekCount
                        });
                    }

                    if (monthCount < empTotalMonthlyTarget)
                    {
                        uncompliantMonthList.Add(new UncompliantEmployeeViewModel
                        {
                            Nik = k.NoNik,
                            Nama = k.NamaLengkap,
                            Departemen = k.NamaDepartemen,
                            Perusahaan = k.NamaPerusahaan,
                            SubmissionCount = monthCount
                        });
                    }
                }
            }

            ViewBag.UncompliantWeek = uncompliantWeekList;
            ViewBag.UncompliantMonth = uncompliantMonthList;

            // 11. Company Activity History
            var histHazards = await hazards
                .OrderByDescending(h => h.CreatedAt)
                .Take(25)
                .Select(h => new PerformanceHistoryViewModel
                {
                    Type = "Hazard",
                    Title = "Hazard: " + (h.Lokasi ?? h.Area ?? "Umum"),
                    Description = h.Temuan ?? "",
                    Date = h.CreatedAt,
                    Nik = h.Nik,
                    User = h.Nama
                }).ToListAsync();

            var histInspections = await inspections
                .OrderByDescending(i => i.CreatedAt)
                .Take(25)
                .Select(i => new PerformanceHistoryViewModel
                {
                    Type = "Inspection",
                    Title = "Inspeksi: " + (i.JenisInspeksi ?? "Umum"),
                    Description = "Area " + (i.Area ?? "umum"),
                    Date = i.CreatedAt,
                    Nik = i.Nik,
                    User = i.Nama
                }).ToListAsync();

            var histSafetyTalks = await safetyTalks
                .OrderByDescending(s => s.CreatedAt)
                .Take(25)
                .Select(s => new PerformanceHistoryViewModel
                {
                    Type = "SafetyTalk",
                    Title = "Safety Talk: " + (s.Judul ?? "Talk"),
                    Description = s.Keterangan ?? "",
                    Date = s.CreatedAt,
                    Nik = s.Nik,
                    User = s.Nama
                }).ToListAsync();

            var histP5ms = await p5ms
                .OrderByDescending(p => p.CreatedAt)
                .Take(25)
                .Select(p => new PerformanceHistoryViewModel
                {
                    Type = "P5m",
                    Title = "P5M: " + (p.Judul ?? "Pre-Start"),
                    Description = p.Keterangan ?? "",
                    Date = p.CreatedAt,
                    Nik = p.Nik,
                    User = p.Nama
                }).ToListAsync();

            var companyHistory = histHazards
                .Concat(histInspections)
                .Concat(histSafetyTalks)
                .Concat(histP5ms)
                .OrderByDescending(x => x.Date)
                .Take(50)
                .ToList();

            ViewBag.CompanyHistory = companyHistory;

            // ==================== 12. Company Hierarchy Tree ====================
            // Build hierarchy strictly from DB_SAP.dbo.vw_m_hirarki_perusahaan active relations.
            var hierarchyRelations = await _context.PerusahaanHierarchyRelations
                .AsNoTracking()
                .ToListAsync();

            var activeCompanyNameById = new Dictionary<int, string>();
            var parentByCompanyId = new Dictionary<int, int?>();

            void UpsertActiveCompany(int companyId, string? companyName, int? parentCompanyId)
            {
                if (!activeCompanyNameById.ContainsKey(companyId))
                {
                    activeCompanyNameById[companyId] = string.IsNullOrWhiteSpace(companyName) ? $"Company {companyId}" : companyName!;
                }

                if (!parentByCompanyId.ContainsKey(companyId) && parentCompanyId.HasValue && parentCompanyId.Value > 0 && parentCompanyId.Value != companyId)
                {
                    parentByCompanyId[companyId] = parentCompanyId;
                }
            }

            foreach (var rel in hierarchyRelations)
            {
                if (rel.ParentCompanyId.HasValue && rel.ParentIsActive == true)
                {
                    UpsertActiveCompany(rel.ParentCompanyId.Value, rel.ParentCompanyName, null);
                }

                if (rel.ChildCompanyId.HasValue && rel.ChildIsActive == true)
                {
                    int? parentId = rel.ParentCompanyId.HasValue && rel.ParentCompanyId.Value > 0
                        ? rel.ParentCompanyId
                        : null;
                    UpsertActiveCompany(rel.ChildCompanyId.Value, rel.ChildCompanyName, parentId);
                }
            }

            // Hierarchy achievement is hazard-only:
            // - Weekly: rolling last 7 calendar days (today inclusive)
            // - Monthly: from day 1 of current month
            // - YTD: from Jan 1 of current year
            // - Weekly: rolling last 7 calendar days (today inclusive)
            // - Monthly: from day 1 of current month
            // - YTD: from Jan 1 of current year
            // Replaced DB GroupBy queries with in-memory aggregations below (ytdMetricsByCompanyNik)

            var elapsedWeeksYtd = Math.Max(1, ((DateTime.Today - startOfYear.Date).Days / 7) + 1);

            var departmentNameById = await _context.Departemens
                .AsNoTracking()
                .ToDictionaryAsync(d => d.DepartemenId, d => string.IsNullOrWhiteSpace(d.NamaDepartemen) ? "General" : d.NamaDepartemen!);

            var activeNikByCompanyDept = new Dictionary<int, Dictionary<string, HashSet<string>>>();
            foreach (var k in allKaryawans)
            {
                if (k.IdPerusahaan <= 0)
                {
                    continue;
                }

                var nik = (k.NoNik ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nik))
                {
                    continue;
                }

                var deptName = k.IdDepartemen.HasValue && departmentNameById.TryGetValue(k.IdDepartemen.Value, out var dName)
                    ? dName
                    : "General";

                if (!activeNikByCompanyDept.TryGetValue(k.IdPerusahaan, out var deptMap))
                {
                    deptMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    activeNikByCompanyDept[k.IdPerusahaan] = deptMap;
                }

                if (!deptMap.TryGetValue(deptName, out var nikSet))
                {
                    nikSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    deptMap[deptName] = nikSet;
                }

                nikSet.Add(nik);
            }

            var hierarchyHazardRows = await _context.HazardReports
                .AsNoTracking()
                .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && h.CreatedAt >= startOfYear)
                .Select(h => new { CompanyId = h.PerusahaanId!.Value, h.Nik, h.CreatedAt })
                .ToListAsync();

            var hierarchyInspectionRows = await _context.Inspections
                .AsNoTracking()
                .Where(i => !i.IsDeleted && i.PerusahaanId.HasValue && i.CreatedAt >= startOfYear)
                .Select(i => new { CompanyId = i.PerusahaanId!.Value, i.Nik, i.CreatedAt })
                .ToListAsync();

            var hierarchySafetyTalkRows = await _context.SafetyTalks
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.PerusahaanId.HasValue && s.CreatedAt >= startOfYear)
                .Select(s => new { CompanyId = s.PerusahaanId!.Value, s.Nik, s.CreatedAt })
                .ToListAsync();

            var hierarchyP5mRows = await _context.P5ms
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.PerusahaanId.HasValue && p.CreatedAt >= startOfYear)
                .Select(p => new { CompanyId = p.PerusahaanId!.Value, p.Nik, p.CreatedAt })
                .ToListAsync();

            var hierarchyCoachingRows = await _context.Coachings
                .AsNoTracking()
                .Where(c => !c.IsDeleted && c.PerusahaanId.HasValue && c.CreatedAt >= startOfYear)
                .Select(c => new { CompanyId = c.PerusahaanId!.Value, c.Nik, c.CreatedAt })
                .ToListAsync();

            var hierarchyObservationRows = await (from o in _context.Observations
                                                  join k in _context.Karyawans on o.Nik equals k.NoNik
                                                  where !o.IsDeleted && o.CreatedAt >= startOfYear && k.IdPerusahaan > 0
                                                  select new { CompanyId = k.IdPerusahaan, o.Nik, o.CreatedAt })
                                                 .AsNoTracking()
                                                 .ToListAsync();

            var ytdMetricsByCompanyNik = new Dictionary<int, Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>>();
            var mtdTotalByCompanyNik = new Dictionary<int, Dictionary<string, int>>();
            var weekTotalByCompanyNik = new Dictionary<int, Dictionary<string, int>>();

            void ProcessRow(int companyId, string? rawNik, DateTime created, string type)
            {
                var nik = (rawNik ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nik)) return;

                if (!ytdMetricsByCompanyNik.TryGetValue(companyId, out var nikMap))
                {
                    nikMap = new Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>(StringComparer.OrdinalIgnoreCase);
                    ytdMetricsByCompanyNik[companyId] = nikMap;
                }
                (int h, int i, int st, int o, int c, int p5m, int total) current = nikMap.TryGetValue(nik, out var cVal) ? cVal : (0, 0, 0, 0, 0, 0, 0);
                if (type == "H") current.h++;
                else if (type == "I") current.i++;
                else if (type == "ST") current.st++;
                else if (type == "O") current.o++;
                else if (type == "C") current.c++;
                else if (type == "P5M") current.p5m++;
                
                if (type != "P5M") current.total++; // Exclude P5M from total SAP achievement
                nikMap[nik] = current;

                if (created >= startOfMonth && type != "P5M")
                {
                    if (!mtdTotalByCompanyNik.TryGetValue(companyId, out var mtdMap))
                    {
                        mtdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        mtdTotalByCompanyNik[companyId] = mtdMap;
                    }
                    mtdMap[nik] = mtdMap.TryGetValue(nik, out var mCurrent) ? mCurrent + 1 : 1;
                }

                if (created >= startOfWeek && type != "P5M")
                {
                    if (!weekTotalByCompanyNik.TryGetValue(companyId, out var weekMap))
                    {
                        weekMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        weekTotalByCompanyNik[companyId] = weekMap;
                    }
                    weekMap[nik] = weekMap.TryGetValue(nik, out var wCurrent) ? wCurrent + 1 : 1;
                }
            }

            foreach (var row in hierarchyHazardRows) ProcessRow(row.CompanyId, row.Nik, row.CreatedAt, "H");
            foreach (var row in hierarchyInspectionRows) ProcessRow(row.CompanyId, row.Nik, row.CreatedAt, "I");
            foreach (var row in hierarchySafetyTalkRows) ProcessRow(row.CompanyId, row.Nik, row.CreatedAt, "ST");
            foreach (var row in hierarchyP5mRows) ProcessRow(row.CompanyId, row.Nik, row.CreatedAt, "P5M");
            foreach (var row in hierarchyCoachingRows) ProcessRow(row.CompanyId, row.Nik, row.CreatedAt, "C");
            foreach (var row in hierarchyObservationRows) ProcessRow(row.CompanyId, row.Nik, row.CreatedAt, "O");

            var nodeMap = new Dictionary<int, CompanyHierarchyNode>();
            foreach (var company in activeCompanyNameById.OrderBy(x => x.Value))
            {
                int hierarchyCompanyId = company.Key;
                int? parentCompanyId = parentByCompanyId.TryGetValue(hierarchyCompanyId, out var pId) ? pId : null;

                var companyEmps = allKaryawans.Where(k => k.IdPerusahaan == hierarchyCompanyId).ToList();
                int companyMonthlyHazardTarget = companyEmps.Sum(k => employeeTargets.TryGetValue(k.IdKaryawan, out var et) ? et.total : 7);

                int weeklyHazardCount = weekTotalByCompanyNik.TryGetValue(hierarchyCompanyId, out var wMap) ? wMap.Values.Sum() : 0;
                int monthlyHazardCount = mtdTotalByCompanyNik.TryGetValue(hierarchyCompanyId, out var mMap) ? mMap.Values.Sum() : 0;
                int ytdHazardCount = ytdMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var yMap) ? yMap.Values.Sum(v => v.total) : 0;

                int hierarchyMonthlyTarget = companyMonthlyHazardTarget;
                int hierarchyWeeklyTarget = (int)Math.Round(hierarchyMonthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                if (hierarchyWeeklyTarget < 1 && hierarchyMonthlyTarget > 0) hierarchyWeeklyTarget = 1;
                int hierarchyYtdTarget = hierarchyWeeklyTarget * elapsedWeeksYtd;

                double weeklyRate = hierarchyWeeklyTarget > 0 ? (double)weeklyHazardCount / hierarchyWeeklyTarget * 100.0 : 0.0;
                double monthlyRate = hierarchyMonthlyTarget > 0 ? (double)monthlyHazardCount / hierarchyMonthlyTarget * 100.0 : 0.0;
                double ytdRate = hierarchyYtdTarget > 0 ? (double)ytdHazardCount / hierarchyYtdTarget * 100.0 : 0.0;

                var node = new CompanyHierarchyNode
                {
                    CompanyId = hierarchyCompanyId,
                    CompanyName = company.Value,
                    ParentCompanyId = parentCompanyId,
                    OwnEmployees = companyEmps.Count,
                    OwnSubmissions = monthlyHazardCount,
                    OwnTarget = hierarchyMonthlyTarget,
                    OwnAchievementRate = Math.Round(monthlyRate, 1),
                    OwnWeeklyHazards = weeklyHazardCount,
                    OwnWeeklyTarget = hierarchyWeeklyTarget,
                    OwnYtdHazards = ytdHazardCount,
                    OwnYtdTarget = hierarchyYtdTarget,
                    OwnWeeklyAchievementRate = Math.Round(weeklyRate, 1),
                    OwnMonthlyAchievementRate = Math.Round(monthlyRate, 1),
                    OwnYtdAchievementRate = Math.Round(ytdRate, 1)
                };

                var departmentAchievements = new List<DepartmentAchievementViewModel>();
                if (activeNikByCompanyDept.TryGetValue(hierarchyCompanyId, out var deptNikMap))
                {
                    foreach (var dept in deptNikMap.OrderBy(x => x.Key))
                    {
                        var deptEmployeeCount = dept.Value.Count;
                        if (deptEmployeeCount <= 0)
                        {
                            continue;
                        }

                        int deptYtdTotal = 0, deptMtdTotal = 0, deptWeekTotal = 0;
                        int deptYtdH = 0, deptYtdI = 0, deptYtdSt = 0, deptYtdO = 0, deptYtdC = 0, deptYtdP5m = 0;

                        if (ytdMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var ytdNikMap))
                        {
                            foreach (var nik in dept.Value)
                            {
                                if (ytdNikMap.TryGetValue(nik, out var m))
                                {
                                    deptYtdTotal += m.total;
                                    deptYtdH += m.h;
                                    deptYtdI += m.i;
                                    deptYtdSt += m.st;
                                    deptYtdO += m.o;
                                    deptYtdC += m.c;
                                    deptYtdP5m += m.p5m;
                                }
                            }
                        }

                        if (mtdTotalByCompanyNik.TryGetValue(hierarchyCompanyId, out var mtdNikMap))
                        {
                            foreach (var nik in dept.Value)
                            {
                                if (mtdNikMap.TryGetValue(nik, out var value)) deptMtdTotal += value;
                            }
                        }

                        if (weekTotalByCompanyNik.TryGetValue(hierarchyCompanyId, out var weekNikMap))
                        {
                            foreach (var nik in dept.Value)
                            {
                                if (weekNikMap.TryGetValue(nik, out var value)) deptWeekTotal += value;
                            }
                        }

                        int deptMtdTargetTotal = 0;
                        int deptMtdTargetH = 0, deptMtdTargetI = 0, deptMtdTargetSt = 0, deptMtdTargetO = 0, deptMtdTargetC = 0, deptMtdTargetP5m = 0;

                        foreach (var nik in dept.Value)
                        {
                            if (employeeTargetsByNik.TryGetValue(nik, out var et))
                            {
                                deptMtdTargetTotal += et.total;
                                deptMtdTargetH += et.hTar;
                                deptMtdTargetI += et.insTar;
                                deptMtdTargetSt += et.stTar;
                                deptMtdTargetO += et.obsTar;
                                deptMtdTargetC += et.cTar;
                                deptMtdTargetP5m += et.p5mTar;
                            }
                            else
                            {
                                deptMtdTargetTotal += 6; // Exclude P5M
                                deptMtdTargetH += 2;
                                deptMtdTargetI += 1;
                                deptMtdTargetSt += 1;
                                deptMtdTargetO += 1;
                                deptMtdTargetC += 1;
                                deptMtdTargetP5m += 1; // P5m target tracked separately but not in total
                            }
                        }

                        int deptWeekTargetTotal = Math.Max(1, (int)Math.Round(deptMtdTargetTotal / 4.0, MidpointRounding.AwayFromZero));
                        if (deptWeekTargetTotal < 1 && deptMtdTargetTotal > 0) deptWeekTargetTotal = 1;

                        int deptYtdTargetTotal = deptWeekTargetTotal * elapsedWeeksYtd;

                        int ytdTargetH = Math.Max(1, (int)Math.Round(deptMtdTargetH / 4.0, MidpointRounding.AwayFromZero)) * elapsedWeeksYtd;
                        int ytdTargetI = Math.Max(1, (int)Math.Round(deptMtdTargetI / 4.0, MidpointRounding.AwayFromZero)) * elapsedWeeksYtd;
                        int ytdTargetSt = Math.Max(1, (int)Math.Round(deptMtdTargetSt / 4.0, MidpointRounding.AwayFromZero)) * elapsedWeeksYtd;
                        int ytdTargetO = Math.Max(1, (int)Math.Round(deptMtdTargetO / 4.0, MidpointRounding.AwayFromZero)) * elapsedWeeksYtd;
                        int ytdTargetC = Math.Max(1, (int)Math.Round(deptMtdTargetC / 4.0, MidpointRounding.AwayFromZero)) * elapsedWeeksYtd;
                        int ytdTargetP5m = Math.Max(1, (int)Math.Round(deptMtdTargetP5m / 4.0, MidpointRounding.AwayFromZero)) * elapsedWeeksYtd;

                        departmentAchievements.Add(new DepartmentAchievementViewModel
                        {
                            DepartmentName = dept.Key,
                            EmployeeCount = deptEmployeeCount,
                            YtdAchievementRate = deptYtdTargetTotal > 0 ? Math.Round((double)deptYtdTotal / deptYtdTargetTotal * 100.0, 1) : 0,
                            MtdAchievementRate = deptMtdTargetTotal > 0 ? Math.Round((double)deptMtdTotal / deptMtdTargetTotal * 100.0, 1) : 0,
                            WeeklyAchievementRate = deptWeekTargetTotal > 0 ? Math.Round((double)deptWeekTotal / deptWeekTargetTotal * 100.0, 1) : 0,
                            YtdHazardRate = ytdTargetH > 0 ? Math.Round((double)deptYtdH / ytdTargetH * 100.0, 1) : 0,
                            YtdInspeksiRate = ytdTargetI > 0 ? Math.Round((double)deptYtdI / ytdTargetI * 100.0, 1) : 0,
                            YtdSafetyTalkRate = ytdTargetSt > 0 ? Math.Round((double)deptYtdSt / ytdTargetSt * 100.0, 1) : 0,
                            YtdObservasiRate = ytdTargetO > 0 ? Math.Round((double)deptYtdO / ytdTargetO * 100.0, 1) : 0,
                            YtdCoachingRate = ytdTargetC > 0 ? Math.Round((double)deptYtdC / ytdTargetC * 100.0, 1) : 0,
                            YtdP5mRate = ytdTargetP5m > 0 ? Math.Round((double)deptYtdP5m / ytdTargetP5m * 100.0, 1) : 0
                        });
                    }
                }

                // Sort departments by YTD Achievement Rate descending
                node.DepartmentAchievements = departmentAchievements.OrderByDescending(d => d.YtdAchievementRate).ToList();
                nodeMap[hierarchyCompanyId] = node;
            }

            // Fix parent relationships using the authoritative source table (vw_perusahaan).
            // vw_m_hirarki_perusahaan can have ROOT rows where the parent is actually defined
            // in the underlying tbl_m_perusahaan — apply those overrides now.
            var sourceParentMap = await _context.Perusahaans
                .AsNoTracking()
                .Where(p => p.StatusAktif && p.PerusahaanIndukId.HasValue && p.PerusahaanIndukId.Value > 0)
                .Select(p => new { p.PerusahaanId, p.PerusahaanIndukId })
                .ToListAsync();

            foreach (var sp in sourceParentMap)
            {
                if (!parentByCompanyId.ContainsKey(sp.PerusahaanId)
                    && nodeMap.ContainsKey(sp.PerusahaanId)
                    && nodeMap.ContainsKey(sp.PerusahaanIndukId!.Value))
                {
                    // Override: correct the missing parent from authoritative source
                    parentByCompanyId[sp.PerusahaanId] = sp.PerusahaanIndukId;
                    nodeMap[sp.PerusahaanId].ParentCompanyId = sp.PerusahaanIndukId;
                }
            }

            var rootNodes = new List<CompanyHierarchyNode>();
            foreach (var kvp in nodeMap)
            {
                var node = kvp.Value;

                if (node.ParentCompanyId.HasValue && node.ParentCompanyId.Value != 0 && nodeMap.ContainsKey(node.ParentCompanyId.Value))
                {
                    var parentNode = nodeMap[node.ParentCompanyId.Value];
                    parentNode.Children.Add(node);
                }
                else
                {
                    rootNodes.Add(node);
                }
            }

            // Recursive cumulative logic local function
            void CalculateCumulative(CompanyHierarchyNode node)
            {
                node.CumulativeEmployees = node.OwnEmployees;
                node.CumulativeSubmissions = node.OwnSubmissions;
                node.CumulativeTarget = node.OwnTarget;
                node.CumulativeWeeklyHazards = node.OwnWeeklyHazards;
                node.CumulativeWeeklyTarget = node.OwnWeeklyTarget;
                node.CumulativeYtdHazards = node.OwnYtdHazards;
                node.CumulativeYtdTarget = node.OwnYtdTarget;

                foreach (var child in node.Children)
                {
                    CalculateCumulative(child);
                    node.CumulativeEmployees += child.CumulativeEmployees;
                    node.CumulativeSubmissions += child.CumulativeSubmissions;
                    node.CumulativeTarget += child.CumulativeTarget;
                    node.CumulativeWeeklyHazards += child.CumulativeWeeklyHazards;
                    node.CumulativeWeeklyTarget += child.CumulativeWeeklyTarget;
                    node.CumulativeYtdHazards += child.CumulativeYtdHazards;
                    node.CumulativeYtdTarget += child.CumulativeYtdTarget;
                }

                node.CumulativeAchievementRate = node.CumulativeTarget > 0
                    ? Math.Round((double)node.CumulativeSubmissions / node.CumulativeTarget * 100.0, 1) 
                    : 0.0;

                node.CumulativeMonthlyAchievementRate = node.CumulativeTarget > 0
                    ? Math.Round((double)node.CumulativeSubmissions / node.CumulativeTarget * 100.0, 1)
                    : 0.0;

                node.CumulativeWeeklyAchievementRate = node.CumulativeWeeklyTarget > 0
                    ? Math.Round((double)node.CumulativeWeeklyHazards / node.CumulativeWeeklyTarget * 100.0, 1)
                    : 0.0;

                node.CumulativeYtdAchievementRate = node.CumulativeYtdTarget > 0
                    ? Math.Round((double)node.CumulativeYtdHazards / node.CumulativeYtdTarget * 100.0, 1)
                    : 0.0;

                node.Children = node.Children.OrderBy(c => c.CompanyName).ToList();
            }

            foreach (var root in rootNodes)
            {
                CalculateCumulative(root);
            }

            rootNodes = rootNodes.OrderBy(r => r.CompanyName).ToList();

            HashSet<int> CollectHierarchyIds(IEnumerable<CompanyHierarchyNode> roots)
            {
                var visited = new HashSet<int>();
                var stack = new Stack<CompanyHierarchyNode>(roots);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    if (!visited.Add(node.CompanyId))
                    {
                        continue;
                    }

                    foreach (var child in node.Children)
                    {
                        stack.Push(child);
                    }
                }

                return visited;
            }

            string NormalizeCompanyKey(string? name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return string.Empty;
                }

                var compact = new string(name
                    .ToUpperInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());

                // Treat ENERGI and ENERGY as equivalent naming variants.
                return compact.Replace("ENERGI", "ENERGY");
            }

            var primaryChildTargets = new List<string>
            {
                "PT UNGGUL DINAMIKA UTAMA",
                "PT KALIMANTAN PRIMA PERSADA",
                "PT MEGA GLOBAL ENERGY",
                "PT PELAYARAN GANESA LAUT JAYA"
            };
            var resolvedPrimaryChildNames = new List<string>();
            var resolvedPrimaryChildIds = new List<int>();

            if (!isAdmin && !isSafetyRole && companyId.HasValue)
            {
                // Scoped company user (e.g. PT Kalimantan Prima Persada):
                // Filter the hierarchy to only show the user's company and its descendants.
                if (nodeMap.TryGetValue(companyId.Value, out var userCompanyNode))
                {
                    void SortHierarchySimple(CompanyHierarchyNode node)
                    {
                        node.Children = node.Children
                            .OrderBy(c => c.CompanyName)
                            .ToList();

                        foreach (var child in node.Children)
                        {
                            SortHierarchySimple(child);
                        }
                    }

                    CalculateCumulative(userCompanyNode);
                    SortHierarchySimple(userCompanyNode);
                    rootNodes = new List<CompanyHierarchyNode> { userCompanyNode };
                }
                else
                {
                    rootNodes = new List<CompanyHierarchyNode>();
                }
            }
            else
            {
                // Admin or Safety role can see the full hierarchy tree rooted at INDEXIM
                var indeximRoot = nodeMap.Values
                    .FirstOrDefault(r => (r.CompanyName ?? string.Empty).Contains("INDEXIM", StringComparison.OrdinalIgnoreCase));

                if (indeximRoot != null)
                {
                    // Detach helper for display tree re-parenting without changing source data.
                    void DetachFromCurrentParent(CompanyHierarchyNode child)
                    {
                        foreach (var node in nodeMap.Values)
                        {
                            node.Children.RemoveAll(c => c.CompanyId == child.CompanyId);
                        }
                        rootNodes.RemoveAll(r => r.CompanyId == child.CompanyId);
                    }

                    foreach (var childName in primaryChildTargets)
                    {
                        var childKey = NormalizeCompanyKey(childName);
                        var primaryChild = nodeMap.Values.FirstOrDefault(n => NormalizeCompanyKey(n.CompanyName) == childKey);
                        if (primaryChild == null || primaryChild.CompanyId == indeximRoot.CompanyId)
                        {
                            continue;
                        }

                        if (!resolvedPrimaryChildIds.Contains(primaryChild.CompanyId))
                        {
                            resolvedPrimaryChildIds.Add(primaryChild.CompanyId);
                            resolvedPrimaryChildNames.Add(primaryChild.CompanyName);
                        }

                        if (!indeximRoot.Children.Any(c => c.CompanyId == primaryChild.CompanyId))
                        {
                            DetachFromCurrentParent(primaryChild);
                            indeximRoot.Children.Add(primaryChild);
                        }
                    }

                    int PrimarySortWeight(CompanyHierarchyNode node)
                    {
                        var idx = resolvedPrimaryChildIds.IndexOf(node.CompanyId);
                        return idx >= 0 ? idx : int.MaxValue;
                    }

                    void SortHierarchy(CompanyHierarchyNode node)
                    {
                        node.Children = node.Children
                            .OrderBy(c => PrimarySortWeight(c))
                            .ThenBy(c => c.CompanyName)
                            .ToList();

                        foreach (var child in node.Children)
                        {
                            SortHierarchy(child);
                        }
                    }

                    // Recalculate after display re-parenting to keep aggregate metrics consistent.
                    CalculateCumulative(indeximRoot);
                    SortHierarchy(indeximRoot);
                    rootNodes = new List<CompanyHierarchyNode> { indeximRoot };
                }
            }

            // Ensure hierarchy still contains all active companies from vw_m_hirarki_perusahaan source.
            var renderedIds = CollectHierarchyIds(rootNodes);

            // Only add missing active nodes as root nodes for Admin or Safety Role
            if (isAdmin || isSafetyRole)
            {
                var missingActiveNodes = nodeMap.Values
                    .Where(n => !renderedIds.Contains(n.CompanyId))
                    .OrderBy(n => n.CompanyName)
                    .ToList();

                if (missingActiveNodes.Any())
                {
                    foreach (var missing in missingActiveNodes)
                    {
                        CalculateCumulative(missing);
                        rootNodes.Add(missing);
                    }

                    rootNodes = rootNodes
                        .GroupBy(n => n.CompanyId)
                        .Select(g => g.First())
                        .OrderBy(n => n.CompanyName)
                        .ToList();
                }
            }

            ViewBag.CompanyHierarchyPrimaryChildren = resolvedPrimaryChildNames;
            ViewBag.CompanyHierarchy = rootNodes;
            ViewBag.CompanyHierarchySource = "DB_SAP.vw_m_hirarki_perusahaan";
            ViewBag.CompanyHierarchyActualActiveCount = activeCompanyNameById.Count;
            ViewBag.CompanyHierarchyRenderedCount = CollectHierarchyIds(rootNodes).Count;

            var canViewGeoPhotos = User.IsInRole("Admin");
            var geoSafetyData = await BuildGeoSafetyRadarDataAsync(companyId, allowedCompanyIds, Request.Query["area"].FirstOrDefault()?.Trim(), canViewGeoPhotos);

            ViewBag.HazardPoints = geoSafetyData.HazardPoints;
            ViewBag.InspectionPoints = geoSafetyData.InspectionPoints;
            ViewBag.P5mPoints = geoSafetyData.P5mPoints;
            ViewBag.SafetyTalkPoints = geoSafetyData.SafetyTalkPoints;
            ViewBag.GeoAreaOptions = geoSafetyData.GeoAreaOptions;
            ViewBag.SelectedGeoArea = geoSafetyData.SelectedGeoArea;

            return View();
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetGeoSafetyRadar(string? area = null)
        {
            var (companyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var canViewGeoPhotos = User.IsInRole("Admin");
            var geoSafetyData = await BuildGeoSafetyRadarDataAsync(companyId, allowedCompanyIds, area?.Trim(), canViewGeoPhotos);

            return Json(new
            {
                hazardPoints = geoSafetyData.HazardPoints,
                inspectionPoints = geoSafetyData.InspectionPoints,
                p5mPoints = geoSafetyData.P5mPoints,
                safetyTalkPoints = geoSafetyData.SafetyTalkPoints,
                geoAreaOptions = geoSafetyData.GeoAreaOptions,
                selectedGeoArea = geoSafetyData.SelectedGeoArea
            });
        }

        private static bool TryParseCoordinates(string? lokasi, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;
            if (string.IsNullOrWhiteSpace(lokasi)) return false;

            var parts = lokasi.Split(',');
            if (parts.Length != 2) return false;

            return double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat) &&
                   double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon);
        }

        private static string? NormalizeImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var normalized = path.Trim();
            if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/", StringComparison.Ordinal))
            {
                return normalized;
            }

            return "/" + normalized.TrimStart('/');
        }

        private static string? ExtractFirstInspectionImageUrl(string? lampiranJson)
        {
            if (string.IsNullOrWhiteSpace(lampiranJson)) return null;
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(lampiranJson);
                if (dict == null || dict.Count == 0) return null;

                foreach (var value in dict.Values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return NormalizeImagePath(value);
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> DownloadUncompliantReport(string range = "week")
        {
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var isAdmin = User.IsInRole("Admin");
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);

            if (!isSafetyRole)
            {
                return Forbid();
            }

            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;

            if (isAdmin || isSafetyRole)
            {
                companyId = null;
            }

            // Date ranges
            var now = DateTime.Now;
            var startOfWeek = DateTime.Today.AddDays(-6); // rolling 7 calendar days (today inclusive)
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var targetStart = range == "month" ? startOfMonth : startOfWeek;

            // Query active employees
            var allKaryawansQuery = from k in _context.Karyawans
                                    join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                    join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                    from d in dg.DefaultIfEmpty()
                                    join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                                    from c in cg.DefaultIfEmpty()
                                    where k.StatusAktif == true && (companyId == null || k.IdPerusahaan == companyId.Value)
                                    select new
                                    {
                                        k.NoNik,
                                        p.NamaLengkap,
                                        NamaDepartemen = d != null ? d.NamaDepartemen : "General",
                                        NamaPerusahaan = c != null ? c.NamaPerusahaan : "Unknown"
                                    };
            var activeKaryawans = await allKaryawansQuery.ToListAsync();

            // Submissions query
            var hazards = _context.HazardReports.Where(h => !h.IsDeleted && (companyId == null || h.PerusahaanId == companyId) && h.CreatedAt >= targetStart);
            var inspections = _context.Inspections.Where(i => !i.IsDeleted && (companyId == null || i.PerusahaanId == companyId) && i.CreatedAt >= targetStart);
            var safetyTalks = _context.SafetyTalks.Where(s => !s.IsDeleted && (companyId == null || s.PerusahaanId == companyId) && s.CreatedAt >= targetStart);
            var p5ms = _context.P5ms.Where(p => !p.IsDeleted && (companyId == null || p.PerusahaanId == companyId) && p.CreatedAt >= targetStart);
            var coachings = _context.Coachings.Where(c => !c.IsDeleted && (companyId == null || c.PerusahaanId == companyId) && c.CreatedAt >= targetStart);

            // Average closure days for this company
            var closedActions = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.Status == "Closed" && a.TanggalPerbaikan != null && (companyId == null || a.PerusahaanId == companyId.Value))
                .Select(a => new { a.CreatedAt, a.TanggalPerbaikan })
                .ToListAsync();

            double avgClosureDays = 0;
            if (closedActions.Count > 0)
            {
                var totalDays = closedActions.Sum(a => ((a.TanggalPerbaikan ?? a.CreatedAt) - a.CreatedAt).TotalDays);
                avgClosureDays = Math.Round(totalDays / closedActions.Count, 1);
            }

            var allKaryawansList = await _context.Karyawans.Where(k => k.StatusAktif).ToListAsync();
            var targetMappings = await _context.KaryawanJabatanMappings.ToListAsync();
            var mappingsDict = new Dictionary<int, KaryawanJabatanMappingPreviewView>();
            foreach (var m in targetMappings)
            {
                mappingsDict[m.KaryawanId] = m;
            }

            var employeeTargetsByNik = new Dictionary<string, (int hazard, int total)>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in allKaryawansList)
            {
                int hTar = 2;
                int insTar = 1;
                int stTar = 1;
                int obsTar = 0;
                int cTar = 0;

                if (mappingsDict.TryGetValue(k.IdKaryawan, out var m))
                {
                    hTar = m.TargetHazardReport ?? 2;
                    insTar = m.TargetInspeksi ?? 1;
                    stTar = m.TargetSafetyTalk ?? 1;
                    obsTar = m.TargetObservasi ?? 0;
                    cTar = m.TargetCoaching ?? 0;
                }

                int totalTar = hTar + insTar + stTar + obsTar + cTar;
                var cleanNik = (k.NoNik ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleanNik))
                {
                    employeeTargetsByNik[cleanNik] = (hTar, totalTar);
                }
            }

            var submitters = new Dictionary<string, int>();
            var hazNiks = await hazards.Select(h => h.Nik).ToListAsync();
            var insNiks = await inspections.Select(i => i.Nik).ToListAsync();
            var safNiks = await safetyTalks.Select(s => s.Nik).ToListAsync();
            var p5mNiks = await p5ms.Select(p => p.Nik).ToListAsync();

            foreach (var nik in hazNiks.Concat(insNiks).Concat(safNiks).Concat(p5mNiks))
            {
                if (string.IsNullOrEmpty(nik)) continue;
                var cleanNik = nik.Trim();
                submitters[cleanNik] = submitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
            }

            var coachList = await coachings.Select(c => new { c.Id, c.Nik }).ToListAsync();
            foreach (var item in coachList)
            {
                if (!string.IsNullOrEmpty(item.Nik))
                {
                    var cleanNik = item.Nik.Trim();
                    submitters[cleanNik] = submitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                }
                var pts = await _context.CoachingParticipants.Where(p => p.CoachingId == item.Id).Select(p => p.Nik).ToListAsync();
                foreach (var pNik in pts)
                {
                    if (!string.IsNullOrEmpty(pNik))
                    {
                        var cleanNik = pNik.Trim();
                        submitters[cleanNik] = submitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                    }
                }
            }

            var uncompliantList = new List<UncompliantEmployeeViewModel>();
            foreach (var k in activeKaryawans)
            {
                var cleanNik = k.NoNik.Trim();
                int count = submitters.ContainsKey(cleanNik) ? submitters[cleanNik] : 0;

                int empTotalMonthlyTarget = 4;
                if (employeeTargetsByNik.TryGetValue(cleanNik, out var et))
                {
                    empTotalMonthlyTarget = et.total;
                }

                if (empTotalMonthlyTarget > 0)
                {
                    if (range == "week")
                    {
                        int empWeeklyTarget = (int)Math.Round(empTotalMonthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                        if (empWeeklyTarget < 1) empWeeklyTarget = 1;

                        if (count < empWeeklyTarget)
                        {
                            uncompliantList.Add(new UncompliantEmployeeViewModel
                            {
                                Nik = k.NoNik,
                                Nama = k.NamaLengkap,
                                Departemen = k.NamaDepartemen,
                                Perusahaan = k.NamaPerusahaan,
                                SubmissionCount = count
                            });
                        }
                    }
                    else if (range == "month")
                    {
                        if (count < empTotalMonthlyTarget)
                        {
                            uncompliantList.Add(new UncompliantEmployeeViewModel
                            {
                                Nik = k.NoNik,
                                Nama = k.NamaLengkap,
                                Departemen = k.NamaDepartemen,
                                Perusahaan = k.NamaPerusahaan,
                                SubmissionCount = count
                            });
                        }
                    }
                }
            }

            // Retrieve full submission history details
            var allHazards = await hazards.OrderByDescending(h => h.CreatedAt).Select(h => new PerformanceHistoryViewModel
            {
                Type = "Hazard",
                Title = h.Lokasi ?? h.Area ?? "Umum",
                Description = h.Temuan ?? "",
                Date = h.CreatedAt,
                Nik = h.Nik,
                User = h.Nama
            }).ToListAsync();

            var allInspections = await inspections.OrderByDescending(i => i.CreatedAt).Select(i => new PerformanceHistoryViewModel
            {
                Type = "Inspection",
                Title = i.JenisInspeksi ?? "Umum",
                Description = "Area " + (i.Area ?? "umum"),
                Date = i.CreatedAt,
                Nik = i.Nik,
                User = i.Nama
            }).ToListAsync();

            var allSafetyTalks = await safetyTalks.OrderByDescending(s => s.CreatedAt).Select(s => new PerformanceHistoryViewModel
            {
                Type = "SafetyTalk",
                Title = s.Judul ?? "Talk",
                Description = s.Keterangan ?? "",
                Date = s.CreatedAt,
                Nik = s.Nik,
                User = s.Nama
            }).ToListAsync();

            var allP5ms = await p5ms.OrderByDescending(p => p.CreatedAt).Select(p => new PerformanceHistoryViewModel
            {
                Type = "P5m",
                Title = p.Judul ?? "Pre-Start",
                Description = p.Keterangan ?? "",
                Date = p.CreatedAt,
                Nik = p.Nik,
                User = p.Nama
            }).ToListAsync();

            var fullHistory = allHazards.Concat(allInspections).Concat(allSafetyTalks).Concat(allP5ms)
                .OrderByDescending(x => x.Date)
                .ToList();

            // Generate ClosedXML Excel with Multi-Sheet
            using (var workbook = new XLWorkbook())
            {
                // ==================== SHEET 1: RINGKASAN ====================
                var wsSummary = workbook.Worksheets.Add("Ringkasan Performa");
                
                wsSummary.Cell(1, 1).Value = "LAPORAN RINGKASAN PERFORMA SAP";
                wsSummary.Cell(1, 1).Style.Font.Bold = true;
                wsSummary.Cell(1, 1).Style.Font.FontSize = 14;
                
                wsSummary.Cell(2, 1).Value = $"Periode: {(range == "week" ? "Minggu Ini" : "Bulan Ini")}";
                wsSummary.Cell(2, 1).Style.Font.Italic = true;
                
                wsSummary.Cell(3, 1).Value = $"Tanggal Export: {DateTime.Now.ToString("dd MMM yyyy HH:mm")}";
                wsSummary.Cell(3, 1).Style.Font.Italic = true;

                // Summary KPI Table
                wsSummary.Cell(5, 1).Value = "METRIK KINERJA SAFETY (KPI)";
                wsSummary.Cell(5, 2).Value = "NILAI";
                var sumHeaderRange = wsSummary.Range(5, 1, 5, 2);
                sumHeaderRange.Style.Font.Bold = true;
                sumHeaderRange.Style.Fill.BackgroundColor = XLColor.AirForceBlue;
                sumHeaderRange.Style.Font.FontColor = XLColor.White;

                wsSummary.Cell(6, 1).Value = "Total Karyawan Aktif";
                wsSummary.Cell(6, 2).Value = activeKaryawans.Count;

                wsSummary.Cell(7, 1).Value = "Total Laporan SAP Masuk";
                wsSummary.Cell(7, 2).Value = hazNiks.Count + insNiks.Count + safNiks.Count + p5mNiks.Count + coachList.Count;

                wsSummary.Cell(8, 1).Value = "Rata-rata Durasi Perbaikan Hazard (Hari)";
                wsSummary.Cell(8, 2).Value = avgClosureDays;

                wsSummary.Cell(9, 1).Value = "Kepatuhan Target SAP Perusahaan (%)";
                double totalReal = hazNiks.Count + insNiks.Count + safNiks.Count + p5mNiks.Count + coachList.Count;
                double totalTar = 0;
                foreach (var k in activeKaryawans)
                {
                    var cleanNik = k.NoNik.Trim();
                    int empTotalMonthlyTarget = 4;
                    if (employeeTargetsByNik.TryGetValue(cleanNik, out var et))
                    {
                        empTotalMonthlyTarget = et.total;
                    }

                    if (range == "month")
                    {
                        totalTar += empTotalMonthlyTarget;
                    }
                    else
                    {
                        int empWeeklyTarget = (int)Math.Round(empTotalMonthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                        if (empWeeklyTarget < 1 && empTotalMonthlyTarget > 0) empWeeklyTarget = 1;
                        totalTar += empWeeklyTarget;
                    }
                }

                wsSummary.Cell(9, 2).Value = totalTar > 0 ? $"{Math.Round(totalReal / totalTar * 100.0, 1)}%" : "0%";

                var tableBorderRange = wsSummary.Range(5, 1, 9, 2);
                tableBorderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                tableBorderRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                wsSummary.Columns().AdjustToContents();

                // ==================== SHEET 2: KEPATUHAN KARYAWAN ====================
                var worksheet = workbook.Worksheets.Add("Belum Buat SAP");
                
                worksheet.Cell(1, 1).Value = "LAPORAN KARYAWAN BELUM MEMENUHI KEPATUHAN SAP";
                worksheet.Cell(1, 1).Style.Font.Bold = true;
                worksheet.Cell(1, 1).Style.Font.FontSize = 14;
                
                worksheet.Cell(2, 1).Value = $"Periode: {(range == "week" ? "Minggu Ini (Target Dinamis Per Karyawan)" : "Bulan Ini (Target Dinamis Per Karyawan)")}";
                worksheet.Cell(2, 1).Style.Font.Italic = true;

                worksheet.Cell(4, 1).Value = "No NIK";
                worksheet.Cell(4, 2).Value = "Nama Lengkap";
                worksheet.Cell(4, 3).Value = "Departemen";
                worksheet.Cell(4, 4).Value = "Perusahaan";
                worksheet.Cell(4, 5).Value = "Jumlah Laporan Masuk";
                worksheet.Cell(4, 6).Value = "Status Target";

                var headerRange = worksheet.Range(4, 1, 4, 6);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.AirForceBlue;
                headerRange.Style.Font.FontColor = XLColor.White;

                int row = 5;
                foreach (var emp in uncompliantList)
                {
                    worksheet.Cell(row, 1).Value = emp.Nik;
                    worksheet.Cell(row, 2).Value = emp.Nama;
                    worksheet.Cell(row, 3).Value = emp.Departemen;
                    worksheet.Cell(row, 4).Value = emp.Perusahaan;
                    worksheet.Cell(row, 5).Value = emp.SubmissionCount;

                    int empTotalMonthlyTarget = 4;
                    if (employeeTargetsByNik.TryGetValue(emp.Nik.Trim(), out var et))
                    {
                        empTotalMonthlyTarget = et.total;
                    }
                    int empWeeklyTarget = (int)Math.Round(empTotalMonthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                    if (empWeeklyTarget < 1 && empTotalMonthlyTarget > 0) empWeeklyTarget = 1;

                    worksheet.Cell(row, 6).Value = range == "week" ? $"Kurang Laporan ({emp.SubmissionCount}/{empWeeklyTarget})" : $"Kurang Laporan ({emp.SubmissionCount}/{empTotalMonthlyTarget})";

                    row++;
                }

                worksheet.Columns().AdjustToContents();

                // ==================== SHEET 3: RIWAYAT AKTIVITAS ====================
                var wsHistory = workbook.Worksheets.Add("Riwayat Aktivitas SAP");
                
                wsHistory.Cell(1, 1).Value = "DAFTAR INPUTAN AKTIVITAS SAP KARYAWAN";
                wsHistory.Cell(1, 1).Style.Font.Bold = true;
                wsHistory.Cell(1, 1).Style.Font.FontSize = 14;
                
                wsHistory.Cell(2, 1).Value = $"Periode: {(range == "week" ? "Minggu Ini" : "Bulan Ini")}";
                wsHistory.Cell(2, 1).Style.Font.Italic = true;

                wsHistory.Cell(4, 1).Value = "Tanggal & Waktu";
                wsHistory.Cell(4, 2).Value = "NIK Pengirim";
                wsHistory.Cell(4, 3).Value = "Nama Pengirim";
                wsHistory.Cell(4, 4).Value = "Modul SAP";
                wsHistory.Cell(4, 5).Value = "Lokasi/Judul";
                wsHistory.Cell(4, 6).Value = "Deskripsi Temuan/Keterangan";

                var histHeaderRange = wsHistory.Range(4, 1, 4, 6);
                histHeaderRange.Style.Font.Bold = true;
                histHeaderRange.Style.Fill.BackgroundColor = XLColor.AirForceBlue;
                histHeaderRange.Style.Font.FontColor = XLColor.White;

                int hRow = 5;
                foreach (var hist in fullHistory)
                {
                    wsHistory.Cell(hRow, 1).Value = hist.Date.ToString("yyyy-MM-dd HH:mm");
                    wsHistory.Cell(hRow, 2).Value = hist.Nik;
                    wsHistory.Cell(hRow, 3).Value = hist.User;
                    wsHistory.Cell(hRow, 4).Value = hist.Type;
                    wsHistory.Cell(hRow, 5).Value = hist.Title;
                    wsHistory.Cell(hRow, 6).Value = hist.Description;

                    hRow++;
                }

                wsHistory.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string fileName = $"Laporan_SAP_Safety_{range}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }

        private bool CheckIsSafetyRole(string? jobTitle, string? department, bool isAdmin)
        {
            if (isAdmin) return true;
            if (string.IsNullOrEmpty(jobTitle) && string.IsNullOrEmpty(department)) return false;

            var subKeywords = new[] { "safety", "hse", "ohs" };
            
            if (!string.IsNullOrEmpty(jobTitle))
            {
                foreach (var kw in subKeywords)
                {
                    if (jobTitle.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(jobTitle, @"\b(she)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(department))
            {
                foreach (var kw in subKeywords)
                {
                    if (department.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(department, @"\b(she)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class CompanyLeaderboardViewModel
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int ActiveEmployees { get; set; }
        public int TotalSubmissions { get; set; }
        public int TargetSubmissions { get; set; }
        public double AchievementRate { get; set; }
    }

    public class MonthlyTrendViewModel
    {
        public string MonthLabel { get; set; } = string.Empty;
        public int Hazards { get; set; }
        public int Inspections { get; set; }
        public int SafetyTalks { get; set; }
        public int P5ms { get; set; }
    }

    public class UncompliantEmployeeViewModel
    {
        public string Nik { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Departemen { get; set; } = string.Empty;
        public string Perusahaan { get; set; } = string.Empty;
        public int SubmissionCount { get; set; }
    }

    public class PerformanceHistoryViewModel
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Nik { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
    }

    public class GeoSafetyPointViewModel
    {
        public int Id { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Tanggal { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string? Area { get; set; }
        public string? Detail { get; set; }
        public string? Resiko { get; set; }
        public string? Status { get; set; }
        public string? PhotoUrl { get; set; }
    }

    public class GeoSafetyRadarViewModel
    {
        public List<GeoSafetyPointViewModel> HazardPoints { get; set; } = new List<GeoSafetyPointViewModel>();
        public List<GeoSafetyPointViewModel> InspectionPoints { get; set; } = new List<GeoSafetyPointViewModel>();
        public List<GeoSafetyPointViewModel> P5mPoints { get; set; } = new List<GeoSafetyPointViewModel>();
        public List<GeoSafetyPointViewModel> SafetyTalkPoints { get; set; } = new List<GeoSafetyPointViewModel>();
        public List<string> GeoAreaOptions { get; set; } = new List<string>();
        public string? SelectedGeoArea { get; set; }
    }

    public class CompanyHierarchyNode
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int? ParentCompanyId { get; set; }
        
        // Own stats
        public int OwnEmployees { get; set; }
        public int OwnSubmissions { get; set; }
        public int OwnTarget { get; set; }
        public double OwnAchievementRate { get; set; }
        public int OwnWeeklyHazards { get; set; }
        public int OwnWeeklyTarget { get; set; }
        public int OwnYtdHazards { get; set; }
        public int OwnYtdTarget { get; set; }
        public double OwnWeeklyAchievementRate { get; set; }
        public double OwnMonthlyAchievementRate { get; set; }
        public double OwnYtdAchievementRate { get; set; }

        // Cumulative (Group) stats
        public int CumulativeEmployees { get; set; }
        public int CumulativeSubmissions { get; set; }
        public int CumulativeTarget { get; set; }
        public double CumulativeAchievementRate { get; set; }
        public int CumulativeWeeklyHazards { get; set; }
        public int CumulativeWeeklyTarget { get; set; }
        public int CumulativeYtdHazards { get; set; }
        public int CumulativeYtdTarget { get; set; }
        public double CumulativeWeeklyAchievementRate { get; set; }
        public double CumulativeMonthlyAchievementRate { get; set; }
        public double CumulativeYtdAchievementRate { get; set; }

        public List<DepartmentAchievementViewModel> DepartmentAchievements { get; set; } = new List<DepartmentAchievementViewModel>();

        public List<CompanyHierarchyNode> Children { get; set; } = new List<CompanyHierarchyNode>();
    }

    public class DepartmentAchievementViewModel
    {
        public string DepartmentName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
        public double YtdAchievementRate { get; set; }
        public double MtdAchievementRate { get; set; }
        public double WeeklyAchievementRate { get; set; }

        public double YtdHazardRate { get; set; }
        public double YtdInspeksiRate { get; set; }
        public double YtdSafetyTalkRate { get; set; }
        public double YtdP5mRate { get; set; }
        public double YtdObservasiRate { get; set; }
        public double YtdCoachingRate { get; set; }
    }
}
