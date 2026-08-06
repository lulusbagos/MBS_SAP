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
using Microsoft.Extensions.Caching.Memory;

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

            // Exclude companies that should not be part of the SAP system at all
            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif && !ExcludedCompanies.Ids.Contains(p.PerusahaanId))
                .ToListAsync();
            var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

            var allowedCompanyIds = new HashSet<int>();
            if (companyId.HasValue)
            {
                // If the user's own company is excluded, return empty scope
                if (ExcludedCompanies.IsExcluded(companyId.Value))
                {
                    return (companyId, allowedCompanyIds);
                }

                allowedCompanyIds.Add(companyId.Value);

                void GetDescendants(int parentId)
                {
                    var childrenFromParentId = allCompanies.Where(c => c.PerusahaanIndukId == parentId).Select(c => c.PerusahaanId).ToList();
                    var childrenFromRelations = relations.Where(r => r.ParentCompanyId == parentId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                    var children = childrenFromParentId.Concat(childrenFromRelations).Distinct().ToList();

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

        private async Task<(int? companyId, HashSet<int> allowedCompanyIds)> ResolveMapCompanyScopeAsync()
        {
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;
            var isAdmin = User.IsInRole("Admin");

            // For map, ONLY Admins can see all.
            // Non-admins (including isSafetyRole) can only see their own company and children.
            if (isAdmin)
            {
                companyId = null;
            }

            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif && !ExcludedCompanies.Ids.Contains(p.PerusahaanId))
                .ToListAsync();
            var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

            var allowedCompanyIds = new HashSet<int>();
            if (companyId.HasValue)
            {
                if (ExcludedCompanies.IsExcluded(companyId.Value))
                {
                    return (companyId, allowedCompanyIds);
                }

                allowedCompanyIds.Add(companyId.Value);

                void GetDescendants(int parentId)
                {
                    var childrenFromParentId = allCompanies.Where(c => c.PerusahaanIndukId == parentId).Select(c => c.PerusahaanId).ToList();
                    var childrenFromRelations = relations.Where(r => r.ParentCompanyId == parentId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                    var children = childrenFromParentId.Concat(childrenFromRelations).Distinct().ToList();

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

            var result = await GetEmployeesComplianceData(companyId, departmentName);
            return Json(result);
        }

        private async Task<List<dynamic>> GetEmployeesComplianceData(int companyId, string? departmentNameFilter = null, int? year = null, int? month = null)
        {
            var selectedYear = year ?? DateTime.Today.Year;
            var selectedMonth = month ?? DateTime.Today.Month;
            
            var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"EmployeesComplianceData_{companyId}_{departmentNameFilter ?? "All"}_{selectedYear}_{selectedMonth}";
            
            bool forceRefresh = HttpContext.Request.Query.ContainsKey("refresh") && 
                               string.Equals(HttpContext.Request.Query["refresh"], "true", StringComparison.OrdinalIgnoreCase);

            if (!forceRefresh && cache.TryGetValue(cacheKey, out List<dynamic>? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            // Set transaction isolation level to READ UNCOMMITTED to prevent deadlocks/timeouts on heavy tables
            await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            // Block excluded companies
            if (ExcludedCompanies.IsExcluded(companyId))
            {
                return new List<dynamic>();
            }

            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

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
                departmentNameFilter == null || string.Equals(k.NamaDepartemen ?? "General", departmentNameFilter, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // MTD: filter by startOfMonth and endOfMonth based on employee NIKs
            var employeeNiks = deptKaryawansFiltered
                .Select(k => k.NoNik)
                .Where(nik => !string.IsNullOrWhiteSpace(nik))
                .Select(nik => nik.Trim())
                .ToList();

            var hazards = new List<string>();
            var inspections = new List<string>();
            var safetyTalks = new List<string>();
            var p5ms = new List<string>();
            var coachings = new List<string>();
            var observations = new List<string>();

            if (employeeNiks.Count > 0)
            {
                hazards = await _context.HazardReports
                    .Where(h => !h.IsDeleted && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth && employeeNiks.Contains(h.Nik))
                    .Select(h => h.Nik)
                    .ToListAsync();

                inspections = await _context.Inspections
                    .Where(i => !i.IsDeleted && i.Tanggal >= startOfMonth && i.Tanggal <= endOfMonth && employeeNiks.Contains(i.Nik))
                    .Select(i => i.Nik)
                    .ToListAsync();

                safetyTalks = await _context.SafetyTalks
                    .Where(s => !s.IsDeleted && s.Tanggal >= startOfMonth && s.Tanggal <= endOfMonth && employeeNiks.Contains(s.Nik))
                    .Select(s => s.Nik)
                    .ToListAsync();

                p5ms = await _context.P5ms
                    .Where(p => !p.IsDeleted && p.Tanggal >= startOfMonth && p.Tanggal <= endOfMonth && employeeNiks.Contains(p.Nik))
                    .Select(p => p.Nik)
                    .ToListAsync();

                var coachingCreators = await _context.Coachings
                    .Where(c => !c.IsDeleted && c.CreatedAt >= startOfMonth && c.CreatedAt <= endOfMonth && employeeNiks.Contains(c.Nik))
                    .Select(c => c.Nik)
                    .ToListAsync();

                var coachingParticipants = await _context.CoachingParticipants
                    .Where(p => p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfMonth && p.Coaching.CreatedAt <= endOfMonth && employeeNiks.Contains(p.Nik))
                    .Select(p => p.Nik)
                    .ToListAsync();

                coachings = coachingCreators.Concat(coachingParticipants).ToList();

                observations = await _context.Observations
                    .Where(o => !o.IsDeleted && o.CreatedAt >= startOfMonth && o.CreatedAt <= endOfMonth && employeeNiks.Contains(o.Nik))
                    .Select(o => o.Nik)
                    .ToListAsync();
            }

            var result = new List<dynamic>();
            foreach (var k in deptKaryawansFiltered)
            {
                var nik = (k.NoNik ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(nik)) continue;

                // MTD target = monthly target directly from mapping view
                int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0, p5mTar = 1;
                string jabatanName = "-";
                if (mappingsDict.TryGetValue(k.IdKaryawan, out var m))
                {
                    hTar = m.TargetHazardReport ?? 2;
                    insTar = m.TargetInspeksi ?? 1;
                    stTar = m.TargetSafetyTalk ?? 1;
                    obsTar = m.TargetObservasi ?? 0;
                    cTar = m.TargetCoaching ?? 0;
                    jabatanName = m.NamaJabatanStandar ?? m.NamaJabatanExisting ?? "-";
                }

                if (hTar + insTar + stTar + obsTar + cTar == 0)
                {
                    continue;
                }

                int mtdTgtH = hTar;
                int mtdTgtI = insTar;
                int mtdTgtST = stTar;
                int mtdTgtO = obsTar;
                int mtdTgtC = cTar;
                int mtdTgtP5 = p5mTar;

                int mtdActH = hazards.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int mtdActI = inspections.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int mtdActST = safetyTalks.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int mtdActO = observations.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int mtdActC = coachings.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));
                int mtdActP5 = p5ms.Count(n => string.Equals(n, nik, StringComparison.OrdinalIgnoreCase));

                int cappedActH = Math.Min(mtdActH, mtdTgtH);
                int cappedActI = Math.Min(mtdActI, mtdTgtI);
                int cappedActST = Math.Min(mtdActST, mtdTgtST);
                int cappedActO = Math.Min(mtdActO, mtdTgtO);
                int cappedActC = Math.Min(mtdActC, mtdTgtC);

                int totalTgt = mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC;
                int totalAct = cappedActH + cappedActI + cappedActST + cappedActO + cappedActC;

                double compliance = totalTgt > 0 ? Math.Round((double)totalAct / totalTgt * 100.0, 1) : 0;
                compliance = Math.Min(compliance, 100.0);

                result.Add(new {
                    karyawanName = k.NamaLengkap,
                    nik = nik,
                    departmentName = k.NamaDepartemen,
                    jabatanName = jabatanName,
                    companyId = companyId,
                    mtdTotalTarget = totalTgt,
                    mtdTotalActual = totalAct,
                    complianceRate = compliance,
                    hazard = new { target = mtdTgtH, actual = mtdActH },
                    inspeksi = new { target = mtdTgtI, actual = mtdActI },
                    safetyTalk = new { target = mtdTgtST, actual = mtdActST },
                    observasi = new { target = mtdTgtO, actual = mtdActO },
                    coaching = new { target = mtdTgtC, actual = mtdActC },
                    p5m = new { target = mtdTgtP5, actual = mtdActP5 }
                });
            }

            var complianceResult = result.OrderByDescending(r => r.complianceRate).ToList();
            cache.Set(cacheKey, complianceResult, TimeSpan.FromMinutes(5));
            return complianceResult;
        }

        private async Task<GeoSafetyRadarViewModel> BuildGeoSafetyRadarDataAsync(int? companyId, HashSet<int> allowedCompanyIds, string? requestedGeoArea, bool includePhotos = false, int? year = null, int? month = null)
        {
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;
            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

            var hazardPoints = new List<GeoSafetyPointViewModel>();
            var inspectionPoints = new List<GeoSafetyPointViewModel>();
            var p5mPoints = new List<GeoSafetyPointViewModel>();
            var safetyTalkPoints = new List<GeoSafetyPointViewModel>();

            var dbHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && (companyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))) && h.Lokasi != null && h.Lokasi.Contains(",") && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth)
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
                .Where(i => !i.IsDeleted && (companyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))) && i.Lokasi != null && i.Lokasi.Contains(",") && i.Tanggal >= startOfMonth && i.Tanggal <= endOfMonth)
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
                .Where(p => !p.IsDeleted && (companyId == null || (p.PerusahaanId.HasValue && allowedCompanyIds.Contains(p.PerusahaanId.Value))) && p.Lokasi != null && p.Lokasi.Contains(",") && p.Tanggal >= startOfMonth && p.Tanggal <= endOfMonth)
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
                .Where(s => !s.IsDeleted && (companyId == null || (s.PerusahaanId.HasValue && allowedCompanyIds.Contains(s.PerusahaanId.Value))) && s.Lokasi != null && s.Lokasi.Contains(",") && s.Tanggal >= startOfMonth && s.Tanggal <= endOfMonth)
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

        [HttpGet]
        [Route("/Performance")]
        [Route("/Performance/Index")]
        public async Task<IActionResult> Index(int? year = null, int? month = null)
        {
            if (!User.IsInRole("Admin"))
            {
                return RedirectToAction("Compliance", "Performance");
            }

            ViewData["HeaderTitle"] = "Pencapaian SAP";
            ViewData["ActiveTab"] = "Performance";
            
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?.Trim();
            var userCompanyIdClaim = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(userCompanyIdClaim, out int userCid) && userCid > 0 ? userCid : (int?)null;
            var (companyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            
            var cache = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var cacheKey = $"PerformanceIndexStats_{userNik}_{companyId}_{string.Join(",", allowedCompanyIds)}";
            
            if (cache.TryGetValue(cacheKey, out Dictionary<string, object?>? cachedViewData) && cachedViewData != null)
            {
                foreach (var kvp in cachedViewData)
                {
                    ViewData[kvp.Key] = kvp.Value;
                }
                return View();
            }
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
            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
            var startOfYear = startOfMonth; // Filter YTD views to MTD to optimize performance
            var trendStart = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
            var baseStartDate = trendStart < startOfYear ? trendStart : startOfYear;

            // Submissions query - filtered by baseStartDate to optimize performance
            var hazards = _context.HazardReports.Where(h => !h.IsDeleted && h.Tanggal >= baseStartDate && (companyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))));
            var inspections = _context.Inspections.Where(i => !i.IsDeleted && i.Tanggal >= baseStartDate && (companyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))));
            var safetyTalks = _context.SafetyTalks.Where(s => !s.IsDeleted && s.Tanggal >= baseStartDate && (companyId == null || (s.PerusahaanId.HasValue && allowedCompanyIds.Contains(s.PerusahaanId.Value))));
            var p5ms = _context.P5ms.Where(p => !p.IsDeleted && p.Tanggal >= baseStartDate && (companyId == null || (p.PerusahaanId.HasValue && allowedCompanyIds.Contains(p.PerusahaanId.Value))));
            var coachings = _context.Coachings.Where(c => !c.IsDeleted && c.CreatedAt >= baseStartDate && (companyId == null || (c.PerusahaanId.HasValue && allowedCompanyIds.Contains(c.PerusahaanId.Value))));

            var openHazardsBase = _context.HazardReports.Where(h => !h.IsDeleted && h.StatusTemuan == "Open" && (companyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))));

            var observationsQuery = _context.Observations.Where(o => !o.IsDeleted && o.CreatedAt >= baseStartDate);
            if (companyId.HasValue)
            {
                var allowedIds = allowedCompanyIds;
                observationsQuery = from o in observationsQuery
                                    join k in _context.Karyawans on o.Nik equals k.NoNik
                                    where allowedIds.Contains(k.IdPerusahaan)
                                    select o;
            }

            // 2. Realisasi Minggu Ini
            int weekHazards = await hazards.CountAsync(h => h.Tanggal >= startOfWeek);
            int weekInspections = await inspections.CountAsync(i => i.Tanggal >= startOfWeek);
            int weekSafetyTalks = await safetyTalks.CountAsync(s => s.Tanggal >= startOfWeek);
            int weekP5ms = await p5ms.CountAsync(p => p.Tanggal >= startOfWeek);
            int weekCoachings = await coachings.CountAsync(c => c.CreatedAt >= startOfWeek);
            int weekObservations = await observationsQuery.CountAsync(o => o.CreatedAt >= startOfWeek);
            int weekTotal = weekHazards + weekInspections + weekSafetyTalks + weekCoachings + weekObservations;

            // 3. Realisasi Bulan Ini
            int monthHazards = await hazards.CountAsync(h => h.Tanggal >= startOfMonth);
            int monthInspections = await inspections.CountAsync(i => i.Tanggal >= startOfMonth);
            int monthSafetyTalks = await safetyTalks.CountAsync(s => s.Tanggal >= startOfMonth);
            int monthP5ms = await p5ms.CountAsync(p => p.Tanggal >= startOfMonth);
            int monthCoachings = await coachings.CountAsync(c => c.CreatedAt >= startOfMonth);
            int monthObservations = await observationsQuery.CountAsync(o => o.CreatedAt >= startOfMonth);
            int monthTotal = monthHazards + monthInspections + monthSafetyTalks + monthCoachings + monthObservations;

            // Incident Pyramid from the same source used by Incident/Index (published incidents)
            var endOfYear = new DateTime(now.Year, 12, 31, 23, 59, 59);
            var incidentBaseQuery = _context.IncidentNewsList.Where(i => i.IsPublished && (companyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))));
            var incidentIndexTotal = await incidentBaseQuery.CountAsync();

            var actualStartOfYear = new DateTime(selectedYear, 1, 1);
            var incidentYearData = await incidentBaseQuery
                .Where(i => (i.TanggalKejadian ?? i.CreatedAt) >= actualStartOfYear && (i.TanggalKejadian ?? i.CreatedAt) <= endOfYear)
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

            foreach (var item in incidentYearData)
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
            ViewBag.IncidentYearTotal = incidentYearData.Count;

            // 4. Open Hazards breakdown by Risk Level (Low/Medium/High/Extreme)
            // Scoped list keeps existing behavior for KPI cards that follow user/company access scope.
            var openHazardsList = await openHazardsBase.Where(h => h.TingkatResiko != null).Select(h => h.TingkatResiko).ToListAsync();

            // Safety pyramid must show all companies regardless of login scope and status.
            var riskHazardsListAllCompanies = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.StatusTemuan == "Open" && h.TingkatResiko != null && h.Tanggal >= startOfYear)
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
            int totalOpenHazards = await openHazardsBase.CountAsync();
            int totalClosedHazards = await hazards.CountAsync(h => h.StatusTemuan == "Closed");

            // 5a. Monitoring Metrics
            int totalHazards = totalOpenHazards + totalClosedHazards;
            double complianceClose = totalHazards > 0 ? (double)totalClosedHazards / totalHazards * 100 : 0;

            var overdueDate = DateTime.Now.AddDays(-14);
            int overdueHazards = await hazards.CountAsync(h => h.StatusTemuan == "Open" && h.Tanggal < overdueDate);
            double overdueRate = totalOpenHazards > 0 ? (double)overdueHazards / totalOpenHazards * 100 : 0;

            int highRiskOpen = openExtreme + openHigh;
            double complianceRisk = totalOpenHazards > 0 ? (double)highRiskOpen / totalOpenHazards * 100 : 0;

            var allHazardRisks = await hazards.Where(h => h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth).Select(h => new { h.StatusTemuan, h.TingkatResiko }).ToListAsync();
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
                .Where(h => !string.IsNullOrWhiteSpace(h.Lokasi) && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth)
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


            var closedHazardsList = await hazards.Where(h => h.StatusTemuan == "Closed" && h.TingkatResiko != null && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth).Select(h => h.TingkatResiko).ToListAsync();
            int closedKritis = closedHazardsList.Count(r => string.Equals(r, "Kritis", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Critical", StringComparison.OrdinalIgnoreCase));
            int closedExtreme = closedHazardsList.Count(r => string.Equals(r, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sangat Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ekstrim", StringComparison.OrdinalIgnoreCase));
            int closedHigh = closedHazardsList.Count(r => string.Equals(r, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Tinggi", StringComparison.OrdinalIgnoreCase));
            int highRiskClosed = closedExtreme + closedHigh;
            int totalHighRisk = highRiskOpen + highRiskClosed;
            double highRiskResolution = totalHighRisk > 0 ? (double)highRiskClosed / totalHighRisk * 100 : 0;

            // 5b. Extra Professional Graphs Data
            var allKategori = await openHazardsBase.Where(h => h.KategoriBahaya != null && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth).Select(h => h.KategoriBahaya).ToListAsync();
            int unsafeActCount = allKategori.Count(k => k != null && (k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase)));
            int unsafeConditionCount = allKategori.Count(k => k != null && (k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("TTA", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase)));
            
            var topAreas = await openHazardsBase.Where(h => !string.IsNullOrEmpty(h.Area) && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth)
                                        .GroupBy(h => h.Area)
                                        .Select(g => new { Area = g.Key, Count = g.Count() })
                                        .OrderByDescending(x => x.Count)
                                        .Take(5)
                                        .ToListAsync();

            // 6. Leaderboard Perusahaan
            var allKaryawans = await _context.Karyawans
                .Where(k => k.StatusAktif && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan))
                .ToListAsync();

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

            // MTD: company leaderboard uses same basis as hierarchy (capped per-employee per-category)
            var compHazardsNik = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && h.Tanggal >= startOfMonth && !ExcludedCompanies.Ids.Contains(h.PerusahaanId!.Value))
                .Select(h => new { CompId = h.PerusahaanId!.Value, h.Nik })
                .ToListAsync();
            var compInspNik = await _context.Inspections
                .Where(i => !i.IsDeleted && i.PerusahaanId.HasValue && i.Tanggal >= startOfMonth && !ExcludedCompanies.Ids.Contains(i.PerusahaanId!.Value))
                .Select(i => new { CompId = i.PerusahaanId!.Value, i.Nik })
                .ToListAsync();
            var compSTNik = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && s.PerusahaanId.HasValue && s.Tanggal >= startOfMonth && !ExcludedCompanies.Ids.Contains(s.PerusahaanId!.Value))
                .Select(s => new { CompId = s.PerusahaanId!.Value, s.Nik })
                .ToListAsync();
            var compP5mNik = await _context.P5ms
                .Where(p => !p.IsDeleted && p.PerusahaanId.HasValue && p.Tanggal >= startOfMonth && !ExcludedCompanies.Ids.Contains(p.PerusahaanId!.Value))
                .Select(p => new { CompId = p.PerusahaanId!.Value, p.Nik })
                .ToListAsync();
            var compCoaNik = await _context.Coachings
                .Where(c => !c.IsDeleted && c.PerusahaanId.HasValue && c.CreatedAt >= startOfMonth && !ExcludedCompanies.Ids.Contains(c.PerusahaanId!.Value))
                .Select(c => new { CompId = c.PerusahaanId!.Value, c.Nik })
                .ToListAsync();
            var compObsNik = await (from o in _context.Observations
                                    join k in _context.Karyawans on o.Nik equals k.NoNik
                                    where !o.IsDeleted && o.CreatedAt >= startOfMonth && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan)
                                    select new { CompId = k.IdPerusahaan, o.Nik })
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

                var companyEmps = allKaryawans.Where(k => k.IdPerusahaan == c.PerusahaanId).ToList();
                int empCount = companyEmps.Count;
                if (empCount == 0) continue;

                // MTD actuals per NIK for this company (raw, uncapped)
                var hazByNik = compHazardsNik.Where(x => x.CompId == c.PerusahaanId)
                    .GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var insByNik = compInspNik.Where(x => x.CompId == c.PerusahaanId)
                    .GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var stByNik = compSTNik.Where(x => x.CompId == c.PerusahaanId)
                    .GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var p5mByNik = compP5mNik.Where(x => x.CompId == c.PerusahaanId)
                    .GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var coaByNik = compCoaNik.Where(x => x.CompId == c.PerusahaanId)
                    .GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var obsByNik = compObsNik.Where(x => x.CompId == c.PerusahaanId)
                    .GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                int companyMtdTarget = 0;
                int companyMtdActual = 0;
                int cHaz = 0, cIns = 0, cST = 0, cP5m = 0, cCoa = 0, cObs = 0;

                foreach (var emp in companyEmps)
                {
                    var nik = (emp.NoNik ?? string.Empty).Trim();

                    int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0, p5mTar = 1;
                    if (employeeTargets.TryGetValue(emp.IdKaryawan, out var et))
                    {
                        hTar = et.hTar;
                        insTar = et.insTar;
                        stTar = et.stTar;
                        obsTar = et.obsTar;
                        cTar = et.cTar;
                    }

                    int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                    int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                    int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                    int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                    int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);
                    int actP5 = string.IsNullOrEmpty(nik) ? 0 : (p5mByNik.TryGetValue(nik, out var ap) ? ap : 0);

                    // Cap per category
                    int cappedH = Math.Min(actH, hTar);
                    int cappedI = Math.Min(actI, insTar);
                    int cappedST = Math.Min(actST, stTar);
                    int cappedO = Math.Min(actO, obsTar);
                    int cappedC = Math.Min(actC, cTar);

                    int empTarget = hTar + insTar + stTar + obsTar + cTar;
                    int empActual = cappedH + cappedI + cappedST + cappedO + cappedC;

                    companyMtdTarget += empTarget;
                    companyMtdActual += empActual;

                    // Raw actuals for type chart (uncapped)
                    cHaz += actH; cIns += actI; cST += actST; cP5m += actP5; cCoa += actC; cObs += actO;

                    // Accumulate MTD targets for type breakdown chart
                    targetHazardTotal += hTar;
                    targetInspeksiTotal += insTar;
                    targetSafetyTalkTotal += stTar;
                    targetObservasiTotal += obsTar;
                    targetCoachingTotal += cTar;
                    targetP5mTotal += p5mTar;
                }

                realHazardTotal += cHaz;
                realInspeksiTotal += cIns;
                realSafetyTalkTotal += cST;
                realP5mTotal += cP5m;
                realCoachingTotal += cCoa;
                realObservasiTotal += cObs;

                double achievementRate = companyMtdTarget > 0 ? Math.Min(100.0, Math.Round((double)companyMtdActual / companyMtdTarget * 100.0, 1)) : 0.0;

                leaderboard.Add(new CompanyLeaderboardViewModel
                {
                    CompanyId = c.PerusahaanId,
                    CompanyName = c.NamaPerusahaan ?? "Unknown",
                    ActiveEmployees = empCount,
                    TotalSubmissions = companyMtdActual,
                    TargetSubmissions = companyMtdTarget,
                    AchievementRate = achievementRate
                });
            }

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

                int hCount = await hazards.CountAsync(h => h.Tanggal >= monthStart && h.CreatedAt < monthEnd);
                int iCount = await inspections.CountAsync(i => i.Tanggal >= monthStart && i.CreatedAt < monthEnd);
                int sCount = await safetyTalks.CountAsync(s => s.Tanggal >= monthStart && s.CreatedAt < monthEnd);
                int pCount = await p5ms.CountAsync(p => p.Tanggal >= monthStart && p.CreatedAt < monthEnd);

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

                myHazardsWeek = await myHazardsQuery.CountAsync(h => h.Tanggal >= startOfWeek);
                myInspectionsWeek = await myInspectionsQuery.CountAsync(i => i.Tanggal >= startOfWeek);
                mySafetyTalksWeek = await mySafetyTalksQuery.CountAsync(s => s.Tanggal >= startOfWeek);
                myP5msWeek = await myP5msQuery.CountAsync(p => p.Tanggal >= startOfWeek);
                myObservationsWeek = await myObservationsQuery.CountAsync(o => o.Date >= startOfWeek);
                myCoachingsWeek = await myCoachingsQuery.CountAsync(c => c.CreatedAt >= startOfWeek);

                myHazardsMonth = await myHazardsQuery.CountAsync(h => h.Tanggal >= startOfMonth);
                myInspectionsMonth = await myInspectionsQuery.CountAsync(i => i.Tanggal >= startOfMonth);
                mySafetyTalksMonth = await mySafetyTalksQuery.CountAsync(s => s.Tanggal >= startOfMonth);
                myP5msMonth = await myP5msQuery.CountAsync(p => p.Tanggal >= startOfMonth);
                myObservationsMonth = await myObservationsQuery.CountAsync(o => o.Date >= startOfMonth);
                myCoachingsMonth = await myCoachingsQuery.CountAsync(c => c.CreatedAt >= startOfMonth);
            }

            int wTarH = targetHazardReport > 0 ? Math.Max(1, (int)Math.Round(targetHazardReport / 4.0, MidpointRounding.AwayFromZero)) : 0;
            int wTarI = targetInspeksi > 0 ? Math.Max(1, (int)Math.Round(targetInspeksi / 4.0, MidpointRounding.AwayFromZero)) : 0;
            int wTarST = targetSafetyTalk > 0 ? Math.Max(1, (int)Math.Round(targetSafetyTalk / 4.0, MidpointRounding.AwayFromZero)) : 0;
            int wTarO = targetObservasi > 0 ? Math.Max(1, (int)Math.Round(targetObservasi / 4.0, MidpointRounding.AwayFromZero)) : 0;
            int wTarC = targetCoaching > 0 ? Math.Max(1, (int)Math.Round(targetCoaching / 4.0, MidpointRounding.AwayFromZero)) : 0;

            int myTotalWeek = Math.Min(myHazardsWeek, wTarH) +
                             Math.Min(myInspectionsWeek, wTarI) +
                             Math.Min(mySafetyTalksWeek, wTarST) +
                             Math.Min(myObservationsWeek, wTarO) +
                             Math.Min(myCoachingsWeek, wTarC);

            int myTotalMonth = Math.Min(myHazardsMonth, targetHazardReport) +
                              Math.Min(myInspectionsMonth, targetInspeksi) +
                              Math.Min(mySafetyTalksMonth, targetSafetyTalk) +
                              Math.Min(myObservationsMonth, targetObservasi) +
                              Math.Min(myCoachingsMonth, targetCoaching);

            int myTotalMonthTarget = targetHazardReport + targetInspeksi + targetSafetyTalk + targetObservasi + targetCoaching;
            int myWeeklyTarget = wTarH + wTarI + wTarST + wTarO + wTarC;

            // 9. Average Closure Days for Action Plans
            var closedActions = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.Status == "Closed" && a.TanggalPerbaikan != null && (companyId == null || a.PerusahaanId == companyId.Value) && a.CreatedAt >= startOfMonth && a.CreatedAt <= endOfMonth)
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
            ViewBag.MyWeeklyTarget = myWeeklyTarget;
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
            var weekHazNiks = await hazards.Where(h => h.Tanggal >= startOfWeek).Select(h => h.Nik).Distinct().ToListAsync();
            var weekInsNiks = await inspections.Where(i => i.Tanggal >= startOfWeek).Select(i => i.Nik).Distinct().ToListAsync();
            var weekSafNiks = await safetyTalks.Where(s => s.Tanggal >= startOfWeek).Select(s => s.Nik).Distinct().ToListAsync();
            var weekP5mNiks = await p5ms.Where(p => p.Tanggal >= startOfWeek).Select(p => p.Nik).Distinct().ToListAsync();
            
            foreach (var n in weekHazNiks.Concat(weekInsNiks).Concat(weekSafNiks).Concat(weekP5mNiks))
            {
                if (string.IsNullOrEmpty(n)) continue;
                var cleanNik = n.Trim();
                weekSubmitters[cleanNik] = weekSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
            }

            var weekCoachList = await coachings.Where(c => c.CreatedAt >= startOfWeek).Select(c => new { c.Id, c.Nik }).ToListAsync();
            var weekCoachIds = weekCoachList.Select(c => c.Id).ToList();
            var weekCoachParticipants = new List<string>();
            if (weekCoachIds.Any()) 
            {
                weekCoachParticipants = await _context.CoachingParticipants.Where(p => weekCoachIds.Contains(p.CoachingId)).Select(p => p.Nik).ToListAsync();
            }

            foreach (var item in weekCoachList)
            {
                if (!string.IsNullOrEmpty(item.Nik))
                {
                    var cleanNik = item.Nik.Trim();
                    weekSubmitters[cleanNik] = weekSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                }
            }
            foreach (var pNik in weekCoachParticipants)
            {
                if (!string.IsNullOrEmpty(pNik))
                {
                    var cleanNik = pNik.Trim();
                    weekSubmitters[cleanNik] = weekSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                }
            }

            // Get submitters for current month
            var monthSubmitters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var monthHazNiks = await hazards.Where(h => h.Tanggal >= startOfMonth).Select(h => new { h.Nik }).ToListAsync();
            var monthInsNiks = await inspections.Where(i => i.Tanggal >= startOfMonth).Select(i => new { i.Nik }).ToListAsync();
            var monthSafNiks = await safetyTalks.Where(s => s.Tanggal >= startOfMonth).Select(s => new { s.Nik }).ToListAsync();
            var monthP5mNiks = await p5ms.Where(p => p.Tanggal >= startOfMonth).Select(p => new { p.Nik }).ToListAsync();

            foreach (var item in monthHazNiks.Concat(monthInsNiks).Concat(monthSafNiks).Concat(monthP5mNiks))
            {
                if (string.IsNullOrEmpty(item.Nik)) continue;
                var cleanNik = item.Nik.Trim();
                monthSubmitters[cleanNik] = monthSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
            }

            var monthCoachList = await coachings.Where(c => c.CreatedAt >= startOfMonth).Select(c => new { c.Id, c.Nik }).ToListAsync();
            var monthCoachIds = monthCoachList.Select(c => c.Id).ToList();
            var monthCoachParticipants = new List<string>();
            if (monthCoachIds.Any())
            {
                monthCoachParticipants = await _context.CoachingParticipants.Where(p => monthCoachIds.Contains(p.CoachingId)).Select(p => p.Nik).ToListAsync();
            }

            foreach (var item in monthCoachList)
            {
                if (!string.IsNullOrEmpty(item.Nik))
                {
                    var cleanNik = item.Nik.Trim();
                    monthSubmitters[cleanNik] = monthSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
                }
            }
            foreach (var pNik in monthCoachParticipants)
            {
                if (!string.IsNullOrEmpty(pNik))
                {
                    var cleanNik = pNik.Trim();
                    monthSubmitters[cleanNik] = monthSubmitters.TryGetValue(cleanNik, out var count) ? count + 1 : 1;
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
                .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && h.Tanggal >= startOfYear && !ExcludedCompanies.Ids.Contains(h.PerusahaanId!.Value))
                .Select(h => new { CompanyId = h.PerusahaanId!.Value, h.Nik, h.CreatedAt })
                .ToListAsync();

            var hierarchyInspectionRows = await _context.Inspections
                .AsNoTracking()
                .Where(i => !i.IsDeleted && i.PerusahaanId.HasValue && i.Tanggal >= startOfYear && !ExcludedCompanies.Ids.Contains(i.PerusahaanId!.Value))
                .Select(i => new { CompanyId = i.PerusahaanId!.Value, i.Nik, i.CreatedAt })
                .ToListAsync();

            var hierarchySafetyTalkRows = await _context.SafetyTalks
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.PerusahaanId.HasValue && s.Tanggal >= startOfYear && !ExcludedCompanies.Ids.Contains(s.PerusahaanId!.Value))
                .Select(s => new { CompanyId = s.PerusahaanId!.Value, s.Nik, s.CreatedAt })
                .ToListAsync();

            var hierarchyP5mRows = await _context.P5ms
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.PerusahaanId.HasValue && p.Tanggal >= startOfYear && !ExcludedCompanies.Ids.Contains(p.PerusahaanId!.Value))
                .Select(p => new { CompanyId = p.PerusahaanId!.Value, p.Nik, p.CreatedAt })
                .ToListAsync();

            var coachingCreatorsRows = await _context.Coachings
                .AsNoTracking()
                .Where(c => !c.IsDeleted && c.PerusahaanId.HasValue && c.CreatedAt >= startOfYear && !ExcludedCompanies.Ids.Contains(c.PerusahaanId!.Value))
                .Select(c => new { CompanyId = c.PerusahaanId!.Value, c.Nik, c.CreatedAt })
                .ToListAsync();

            var coachingParticipantsRows = await (from p in _context.CoachingParticipants
                                                  join k in _context.Karyawans on p.Nik equals k.NoNik
                                                  where p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfYear && k.IdPerusahaan > 0 && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan)
                                                  select new { CompanyId = k.IdPerusahaan, p.Nik, CreatedAt = p.Coaching.CreatedAt })
                                                  .AsNoTracking()
                                                  .ToListAsync();

            var hierarchyCoachingRows = coachingCreatorsRows.Concat(coachingParticipantsRows).ToList();

            var hierarchyObservationRows = await (from o in _context.Observations
                                                  join k in _context.Karyawans on o.Nik equals k.NoNik
                                                  where !o.IsDeleted && o.CreatedAt >= startOfYear && k.IdPerusahaan > 0 && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan)
                                                  select new { CompanyId = k.IdPerusahaan, o.Nik, o.CreatedAt })
                                                 .AsNoTracking()
                                                 .ToListAsync();

            var ytdMetricsByCompanyNik = new Dictionary<int, Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>>();
            var mtdMetricsByCompanyNik = new Dictionary<int, Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>>();
            var weekMetricsByCompanyNik = new Dictionary<int, Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>>();

            void ProcessRow(int companyId, string? rawNik, DateTime created, string type)
            {
                var nik = (rawNik ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nik)) return;

                // 1. YTD
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

                // 2. MTD
                if (created >= startOfMonth)
                {
                    if (!mtdMetricsByCompanyNik.TryGetValue(companyId, out var mtdMap))
                    {
                        mtdMap = new Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>(StringComparer.OrdinalIgnoreCase);
                        mtdMetricsByCompanyNik[companyId] = mtdMap;
                    }
                    (int h, int i, int st, int o, int c, int p5m, int total) mCurrent = mtdMap.TryGetValue(nik, out var mVal) ? mVal : (0, 0, 0, 0, 0, 0, 0);
                    if (type == "H") mCurrent.h++;
                    else if (type == "I") mCurrent.i++;
                    else if (type == "ST") mCurrent.st++;
                    else if (type == "O") mCurrent.o++;
                    else if (type == "C") mCurrent.c++;
                    else if (type == "P5M") mCurrent.p5m++;
                    
                    if (type != "P5M") mCurrent.total++; // Exclude P5M from total SAP achievement
                    mtdMap[nik] = mCurrent;
                }

                // 3. WEEK
                if (created >= startOfWeek)
                {
                    if (!weekMetricsByCompanyNik.TryGetValue(companyId, out var weekMap))
                    {
                        weekMap = new Dictionary<string, (int h, int i, int st, int o, int c, int p5m, int total)>(StringComparer.OrdinalIgnoreCase);
                        weekMetricsByCompanyNik[companyId] = weekMap;
                    }
                    (int h, int i, int st, int o, int c, int p5m, int total) wCurrent = weekMap.TryGetValue(nik, out var wVal) ? wVal : (0, 0, 0, 0, 0, 0, 0);
                    if (type == "H") wCurrent.h++;
                    else if (type == "I") wCurrent.i++;
                    else if (type == "ST") wCurrent.st++;
                    else if (type == "O") wCurrent.o++;
                    else if (type == "C") wCurrent.c++;
                    else if (type == "P5M") wCurrent.p5m++;
                    
                    if (type != "P5M") wCurrent.total++; // Exclude P5M from total SAP achievement
                    weekMap[nik] = wCurrent;
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

                int weeklyCappedCount = 0;
                int monthlyCappedCount = 0;
                int ytdCappedCount = 0;

                foreach (var emp in companyEmps)
                {
                    var empNik = (emp.NoNik ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(empNik)) continue;

                    int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0;
                    if (employeeTargets.TryGetValue(emp.IdKaryawan, out var et))
                    {
                        hTar = et.hTar;
                        insTar = et.insTar;
                        stTar = et.stTar;
                        obsTar = et.obsTar;
                        cTar = et.cTar;
                    }

                    int mTgtH = hTar;
                    int mTgtI = insTar;
                    int mTgtST = stTar;
                    int mTgtO = obsTar;
                    int mTgtC = cTar;

                    int wTgtH = hTar > 0 ? Math.Max(1, (int)Math.Round(hTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                    int wTgtI = insTar > 0 ? Math.Max(1, (int)Math.Round(insTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                    int wTgtST = stTar > 0 ? Math.Max(1, (int)Math.Round(stTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                    int wTgtO = obsTar > 0 ? Math.Max(1, (int)Math.Round(obsTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                    int wTgtC = cTar > 0 ? Math.Max(1, (int)Math.Round(cTar / 4.0, MidpointRounding.AwayFromZero)) : 0;

                    int ytdTgtH = wTgtH * elapsedWeeksYtd;
                    int ytdTgtI = wTgtI * elapsedWeeksYtd;
                    int ytdTgtST = wTgtST * elapsedWeeksYtd;
                    int ytdTgtO = wTgtO * elapsedWeeksYtd;
                    int ytdTgtC = wTgtC * elapsedWeeksYtd;

                    int wActH = 0, wActI = 0, wActST = 0, wActO = 0, wActC = 0;
                    int mActH = 0, mActI = 0, mActST = 0, mActO = 0, mActC = 0;
                    int yActH = 0, yActI = 0, yActST = 0, yActO = 0, yActC = 0;

                    if (weekMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var weekNikMap) && weekNikMap.TryGetValue(empNik, out var wVal))
                    {
                        wActH = wVal.h; wActI = wVal.i; wActST = wVal.st; wActO = wVal.o; wActC = wVal.c;
                    }
                    if (mtdMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var mtdNikMap) && mtdNikMap.TryGetValue(empNik, out var mVal))
                    {
                        mActH = mVal.h; mActI = mVal.i; mActST = mVal.st; mActO = mVal.o; mActC = mVal.c;
                    }
                    if (ytdMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var ytdNikMap) && ytdNikMap.TryGetValue(empNik, out var yVal))
                    {
                        yActH = yVal.h; yActI = yVal.i; yActST = yVal.st; yActO = yVal.o; yActC = yVal.c;
                    }

                    weeklyCappedCount += Math.Min(wActH, wTgtH) + Math.Min(wActI, wTgtI) + Math.Min(wActST, wTgtST) + Math.Min(wActO, wTgtO) + Math.Min(wActC, wTgtC);
                    monthlyCappedCount += Math.Min(mActH, mTgtH) + Math.Min(mActI, mTgtI) + Math.Min(mActST, mTgtST) + Math.Min(mActO, mTgtO) + Math.Min(mActC, mTgtC);
                    ytdCappedCount += Math.Min(yActH, ytdTgtH) + Math.Min(yActI, ytdTgtI) + Math.Min(yActST, ytdTgtST) + Math.Min(yActO, ytdTgtO) + Math.Min(yActC, ytdTgtC);
                }

                int weeklyHazardCount = weeklyCappedCount;
                int monthlyHazardCount = monthlyCappedCount;
                int ytdHazardCount = ytdCappedCount;

                int hierarchyMonthlyTarget = companyMonthlyHazardTarget;
                int hierarchyWeeklyTarget = (int)Math.Round(hierarchyMonthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                if (hierarchyWeeklyTarget < 1 && hierarchyMonthlyTarget > 0) hierarchyWeeklyTarget = 1;
                int hierarchyYtdTarget = hierarchyWeeklyTarget * elapsedWeeksYtd;

                double weeklyRate = Math.Min(100.0, hierarchyWeeklyTarget > 0 ? (double)weeklyHazardCount / hierarchyWeeklyTarget * 100.0 : 0.0);
                double monthlyRate = Math.Min(100.0, hierarchyMonthlyTarget > 0 ? (double)monthlyHazardCount / hierarchyMonthlyTarget * 100.0 : 0.0);
                double ytdRate = Math.Min(100.0, hierarchyYtdTarget > 0 ? (double)ytdHazardCount / hierarchyYtdTarget * 100.0 : 0.0);

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
                        int deptMtdH = 0, deptMtdI = 0, deptMtdSt = 0, deptMtdO = 0, deptMtdC = 0, deptMtdP5m = 0;

                        int deptMtdTargetTotal = 0;
                        int deptWeekTargetTotal = 0;
                        int deptYtdTargetTotal = 0;
                        int ytdTargetH = 0, ytdTargetI = 0, ytdTargetSt = 0, ytdTargetO = 0, ytdTargetC = 0, ytdTargetP5m = 0;
                        int mtdTargetH = 0, mtdTargetI = 0, mtdTargetSt = 0, mtdTargetO = 0, mtdTargetC = 0, mtdTargetP5m = 0;

                        foreach (var nik in dept.Value)
                        {
                            int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0, p5mTar = 1;
                            if (employeeTargetsByNik.TryGetValue(nik, out var et))
                            {
                                hTar = et.hTar;
                                insTar = et.insTar;
                                stTar = et.stTar;
                                obsTar = et.obsTar;
                                cTar = et.cTar;
                                p5mTar = et.p5mTar;
                            }

                            int mTgtH = hTar;
                            int mTgtI = insTar;
                            int mTgtST = stTar;
                            int mTgtO = obsTar;
                            int mTgtC = cTar;
                            int mTgtP5M = p5mTar;

                            int wTgtH = hTar > 0 ? Math.Max(1, (int)Math.Round(hTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                            int wTgtI = insTar > 0 ? Math.Max(1, (int)Math.Round(insTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                            int wTgtST = stTar > 0 ? Math.Max(1, (int)Math.Round(stTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                            int wTgtO = obsTar > 0 ? Math.Max(1, (int)Math.Round(obsTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                            int wTgtC = cTar > 0 ? Math.Max(1, (int)Math.Round(cTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                            int wTgtP5M = p5mTar > 0 ? Math.Max(1, (int)Math.Round(p5mTar / 4.0, MidpointRounding.AwayFromZero)) : 0;

                            int ytdTgtH = wTgtH * elapsedWeeksYtd;
                            int ytdTgtI = wTgtI * elapsedWeeksYtd;
                            int ytdTgtST = wTgtST * elapsedWeeksYtd;
                            int ytdTgtO = wTgtO * elapsedWeeksYtd;
                            int ytdTgtC = wTgtC * elapsedWeeksYtd;
                            int ytdTgtP5M = wTgtP5M * elapsedWeeksYtd;

                            deptMtdTargetTotal += hTar + insTar + stTar + obsTar + cTar;
                            deptWeekTargetTotal += wTgtH + wTgtI + wTgtST + wTgtO + wTgtC;
                            deptYtdTargetTotal += ytdTgtH + ytdTgtI + ytdTgtST + ytdTgtO + ytdTgtC;

                            ytdTargetH += ytdTgtH;
                            ytdTargetI += ytdTgtI;
                            ytdTargetSt += ytdTgtST;
                            ytdTargetO += ytdTgtO;
                            ytdTargetC += ytdTgtC;
                            ytdTargetP5m += ytdTgtP5M;

                            mtdTargetH += mTgtH;
                            mtdTargetI += mTgtI;
                            mtdTargetSt += mTgtST;
                            mtdTargetO += mTgtO;
                            mtdTargetC += mTgtC;
                            mtdTargetP5m += mTgtP5M;

                            int wActH = 0, wActI = 0, wActST = 0, wActO = 0, wActC = 0, wActP5M = 0;
                            int mActH = 0, mActI = 0, mActST = 0, mActO = 0, mActC = 0, mActP5M = 0;
                            int yActH = 0, yActI = 0, yActST = 0, yActO = 0, yActC = 0, yActP5M = 0;

                            if (weekMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var weekNikMap) && weekNikMap.TryGetValue(nik, out var wVal))
                            {
                                wActH = wVal.h; wActI = wVal.i; wActST = wVal.st; wActO = wVal.o; wActC = wVal.c; wActP5M = wVal.p5m;
                            }
                            if (mtdMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var mtdNikMap) && mtdNikMap.TryGetValue(nik, out var mVal))
                            {
                                mActH = mVal.h; mActI = mVal.i; mActST = mVal.st; mActO = mVal.o; mActC = mVal.c; mActP5M = mVal.p5m;
                            }
                            if (ytdMetricsByCompanyNik.TryGetValue(hierarchyCompanyId, out var ytdNikMap) && ytdNikMap.TryGetValue(nik, out var yVal))
                            {
                                yActH = yVal.h; yActI = yVal.i; yActST = yVal.st; yActO = yVal.o; yActC = yVal.c; yActP5M = yVal.p5m;
                            }

                            deptWeekTotal += Math.Min(wActH, wTgtH) + Math.Min(wActI, wTgtI) + Math.Min(wActST, wTgtST) + Math.Min(wActO, wTgtO) + Math.Min(wActC, wTgtC);
                            deptMtdTotal += Math.Min(mActH, mTgtH) + Math.Min(mActI, mTgtI) + Math.Min(mActST, mTgtST) + Math.Min(mActO, mTgtO) + Math.Min(mActC, mTgtC);
                            deptYtdTotal += Math.Min(yActH, ytdTgtH) + Math.Min(yActI, ytdTgtI) + Math.Min(yActST, ytdTgtST) + Math.Min(yActO, ytdTgtO) + Math.Min(yActC, ytdTgtC);

                            deptYtdH += Math.Min(yActH, ytdTgtH);
                            deptYtdI += Math.Min(yActI, ytdTgtI);
                            deptYtdSt += Math.Min(yActST, ytdTgtST);
                            deptYtdO += Math.Min(yActO, ytdTgtO);
                            deptYtdC += Math.Min(yActC, ytdTgtC);
                            deptYtdP5m += Math.Min(yActP5M, ytdTgtP5M);

                            deptMtdH += Math.Min(mActH, mTgtH);
                            deptMtdI += Math.Min(mActI, mTgtI);
                            deptMtdSt += Math.Min(mActST, mTgtST);
                            deptMtdO += Math.Min(mActO, mTgtO);
                            deptMtdC += Math.Min(mActC, mTgtC);
                            deptMtdP5m += Math.Min(mActP5M, mTgtP5M);
                        }

                        int deptWeekTargetTotalVal = Math.Max(1, deptWeekTargetTotal);
                        int deptYtdTargetTotalVal = Math.Max(1, deptYtdTargetTotal);
                        int deptMtdTargetTotalVal = Math.Max(1, deptMtdTargetTotal);
                        int ytdTargetHVal = Math.Max(1, ytdTargetH);
                        int ytdTargetIVal = Math.Max(1, ytdTargetI);
                        int ytdTargetStVal = Math.Max(1, ytdTargetSt);
                        int ytdTargetOVal = Math.Max(1, ytdTargetO);
                        int ytdTargetCVal = Math.Max(1, ytdTargetC);
                        int ytdTargetP5mVal = Math.Max(1, ytdTargetP5m);

                        int mtdTargetHVal = Math.Max(1, mtdTargetH);
                        int mtdTargetIVal = Math.Max(1, mtdTargetI);
                        int mtdTargetStVal = Math.Max(1, mtdTargetSt);
                        int mtdTargetOVal = Math.Max(1, mtdTargetO);
                        int mtdTargetCVal = Math.Max(1, mtdTargetC);
                        int mtdTargetP5mVal = Math.Max(1, mtdTargetP5m);

                        departmentAchievements.Add(new DepartmentAchievementViewModel
                        {
                            DepartmentName = dept.Key,
                            EmployeeCount = deptEmployeeCount,
                            YtdAchievementRate = deptYtdTargetTotalVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdTotal / deptYtdTargetTotalVal * 100.0, 1)) : 0,
                            MtdAchievementRate = deptMtdTargetTotalVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdTotal / deptMtdTargetTotalVal * 100.0, 1)) : 0,
                            WeeklyAchievementRate = deptWeekTargetTotalVal > 0 ? Math.Min(100.0, Math.Round((double)deptWeekTotal / deptWeekTargetTotalVal * 100.0, 1)) : 0,
                            YtdHazardRate = ytdTargetHVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdH / ytdTargetHVal * 100.0, 1)) : 0,
                            YtdInspeksiRate = ytdTargetIVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdI / ytdTargetIVal * 100.0, 1)) : 0,
                            YtdSafetyTalkRate = ytdTargetStVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdSt / ytdTargetStVal * 100.0, 1)) : 0,
                            YtdObservasiRate = ytdTargetOVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdO / ytdTargetOVal * 100.0, 1)) : 0,
                            YtdCoachingRate = ytdTargetCVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdC / ytdTargetCVal * 100.0, 1)) : 0,
                            YtdP5mRate = ytdTargetP5mVal > 0 ? Math.Min(100.0, Math.Round((double)deptYtdP5m / ytdTargetP5mVal * 100.0, 1)) : 0,

                            MtdHazardRate = mtdTargetHVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdH / mtdTargetHVal * 100.0, 1)) : 0,
                            MtdInspeksiRate = mtdTargetIVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdI / mtdTargetIVal * 100.0, 1)) : 0,
                            MtdSafetyTalkRate = mtdTargetStVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdSt / mtdTargetStVal * 100.0, 1)) : 0,
                            MtdObservasiRate = mtdTargetOVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdO / mtdTargetOVal * 100.0, 1)) : 0,
                            MtdCoachingRate = mtdTargetCVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdC / mtdTargetCVal * 100.0, 1)) : 0,
                            MtdP5mRate = mtdTargetP5mVal > 0 ? Math.Min(100.0, Math.Round((double)deptMtdP5m / mtdTargetP5mVal * 100.0, 1)) : 0
                        });
                    }
                }

                // Sort departments by MTD Achievement Rate descending
                node.DepartmentAchievements = departmentAchievements.OrderByDescending(d => d.MtdAchievementRate).ToList();
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

            var (mapCompanyId, mapAllowedCompanyIds) = await ResolveMapCompanyScopeAsync();
            var geoSafetyData = await BuildGeoSafetyRadarDataAsync(mapCompanyId, mapAllowedCompanyIds, Request.Query["area"].FirstOrDefault()?.Trim(), true, selectedYear, selectedMonth);

            ViewBag.HazardPoints = geoSafetyData.HazardPoints;
            ViewBag.InspectionPoints = geoSafetyData.InspectionPoints;
            ViewBag.P5mPoints = geoSafetyData.P5mPoints;
            ViewBag.SafetyTalkPoints = geoSafetyData.SafetyTalkPoints;
            ViewBag.GeoAreaOptions = geoSafetyData.GeoAreaOptions;
            ViewBag.SelectedGeoArea = geoSafetyData.SelectedGeoArea;

            // Resolve logged-in user's department & rank
            if (!string.IsNullOrEmpty(userNik))
            {
                var myKaryawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == userNik && k.StatusAktif);
                if (myKaryawan != null)
                {
                    string userDeptName = "General";
                    if (myKaryawan.IdDepartemen.HasValue)
                    {
                        var dView = await _context.Departemens.FirstOrDefaultAsync(d => d.DepartemenId == myKaryawan.IdDepartemen.Value);
                        userDeptName = string.IsNullOrWhiteSpace(dView?.NamaDepartemen) ? "General" : dView.NamaDepartemen;
                    }

                    // 1. Department rank within their company
                    if (nodeMap.TryGetValue(myKaryawan.IdPerusahaan, out var userCompanyNode))
                    {
                        var sortedDepts = userCompanyNode.DepartmentAchievements.OrderByDescending(d => d.MtdAchievementRate).ToList();
                        var myDeptRankInfo = sortedDepts
                            .Select((d, idx) => new { Dept = d, Rank = idx + 1 })
                            .FirstOrDefault(x => string.Equals(x.Dept.DepartmentName, userDeptName, StringComparison.OrdinalIgnoreCase));

                        if (myDeptRankInfo != null)
                        {
                            ViewBag.UserDeptName = userDeptName;
                            ViewBag.UserDeptMtdRate = myDeptRankInfo.Dept.MtdAchievementRate;
                            ViewBag.UserDeptRank = myDeptRankInfo.Rank;
                            ViewBag.UserDeptTotalCount = sortedDepts.Count;
                            ViewBag.UserCompanyId = myKaryawan.IdPerusahaan;
                        }
                    }

                    // 2. Employee Rank
                    var companyEmployees = await GetEmployeesComplianceData(myKaryawan.IdPerusahaan);
                    
                    var myCompanyEmpRankInfo = companyEmployees
                        .Select((e, idx) => new { Emp = e, Rank = idx + 1 })
                        .FirstOrDefault(x => string.Equals((string)x.Emp.nik, userNik, StringComparison.OrdinalIgnoreCase));
                    if (myCompanyEmpRankInfo != null)
                    {
                        ViewBag.UserEmpCompanyRank = myCompanyEmpRankInfo.Rank;
                        ViewBag.UserEmpCompanyTotalCount = companyEmployees.Count;
                    }

                    var deptEmployees = companyEmployees.Where(e => string.Equals((string)e.departmentName, userDeptName, StringComparison.OrdinalIgnoreCase)).ToList();
                    var myDeptEmpRankInfo = deptEmployees
                        .Select((e, idx) => new { Emp = e, Rank = idx + 1 })
                        .FirstOrDefault(x => string.Equals((string)x.Emp.nik, userNik, StringComparison.OrdinalIgnoreCase));
                    if (myDeptEmpRankInfo != null)
                    {
                        ViewBag.UserEmpDeptRank = myDeptEmpRankInfo.Rank;
                        ViewBag.UserEmpDeptTotalCount = deptEmployees.Count;
                    }
                }
            }

            var viewDataCache = new Dictionary<string, object?>();
            foreach (var kvp in ViewData)
            {
                viewDataCache[kvp.Key] = kvp.Value;
            }
            cache.Set(cacheKey, viewDataCache, TimeSpan.FromMinutes(5));

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> League(int? companyId = null, string mode = "dept", int? year = null, int? month = null)
        {
            ViewData["HeaderTitle"] = "League SAP";
            ViewData["ActiveTab"] = "Performance";
            ViewBag.Mode = mode; // "dept" or "company"

            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

            var (resolvedCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);

            // Fetch all active companies
            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            List<PerusahaanView> allowedCompanies;
            if (isAdmin || isSafetyRole)
            {
                allowedCompanies = allCompanies;
            }
            else
            {
                allowedCompanies = allCompanies
                    .Where(p => allowedCompanyIds.Contains(p.PerusahaanId))
                    .ToList();
            }

            if (allowedCompanies == null || !allowedCompanies.Any())
            {
                return RedirectToAction("Index", "Home");
            }

            int selectedCompanyId = companyId ?? (resolvedCompanyId ?? allowedCompanies.First().PerusahaanId);
            
            // Security check: Non-admins cannot inspect other companies' internal dept list unless allowed by scope
            if (!isAdmin && !isSafetyRole && !allowedCompanyIds.Contains(selectedCompanyId))
            {
                selectedCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            var selectedCompany = allCompanies.FirstOrDefault(c => c.PerusahaanId == selectedCompanyId) ?? allowedCompanies.First();

            // FILTER dropdown list to only show the selected company and its child companies (subcons)
            var dropdownCompanyIds = new HashSet<int> { selectedCompany.PerusahaanId };
            var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();
            
            // Check if selected company has children
            var hasChildren = allCompanies.Any(c => c.PerusahaanIndukId == selectedCompany.PerusahaanId) || 
                              relations.Any(r => r.ParentCompanyId == selectedCompany.PerusahaanId);
            
            int rootId = selectedCompany.PerusahaanId;
            if (!hasChildren)
            {
                // If it has no children, set root to its parent so we show parent and siblings
                var directParentId = selectedCompany.PerusahaanIndukId;
                var relationParentId = relations.FirstOrDefault(r => r.ChildCompanyId == selectedCompany.PerusahaanId && r.ParentCompanyId.HasValue)?.ParentCompanyId;
                int? parentId = (directParentId != null && directParentId > 0) ? directParentId : relationParentId;
                if (parentId.HasValue && parentId > 0)
                {
                    rootId = parentId.Value;
                    dropdownCompanyIds.Add(rootId);
                }
            }

            // Also, always allow going back to the parent of the current root
            var rootCompany = allCompanies.FirstOrDefault(c => c.PerusahaanId == rootId);
            if (rootCompany != null)
            {
                var directParentId = rootCompany.PerusahaanIndukId;
                var relationParentId = relations.FirstOrDefault(r => r.ChildCompanyId == rootId && r.ParentCompanyId.HasValue)?.ParentCompanyId;
                int? rootParentId = (directParentId != null && directParentId > 0) ? directParentId : relationParentId;
                if (rootParentId.HasValue && rootParentId > 0)
                {
                    dropdownCompanyIds.Add(rootParentId.Value);
                }
            }
            
            void GetDescendants(int parentId)
            {
                var childrenFromParentId = allCompanies.Where(c => c.PerusahaanIndukId == parentId).Select(c => c.PerusahaanId).ToList();
                var childrenFromRelations = relations.Where(r => r.ParentCompanyId == parentId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                var children = childrenFromParentId.Concat(childrenFromRelations).Distinct().ToList();

                foreach (var childId in children)
                {
                    if (dropdownCompanyIds.Add(childId))
                    {
                        GetDescendants(childId);
                    }
                }
            }
            GetDescendants(rootId);

            // Do not filter the dropdown options by dropdownCompanyIds, so that all allowed companies (e.g. 172 companies for Indexim Coalindo or Admin) are always selectable.
            // allowedCompanies = allowedCompanies.Where(c => dropdownCompanyIds.Contains(c.PerusahaanId)).ToList();

            ViewBag.Companies = allowedCompanies;
            ViewBag.SelectedCompanyId = selectedCompany.PerusahaanId;
            ViewBag.CompanyName = selectedCompany.NamaPerusahaan;

            if (mode == "company")
            {
                // Liga Antar Company: Compare all companies
                var companyStandings = new List<dynamic>();
                var allEmployees = new List<dynamic>();

                var companiesToCompare = allCompanies;
                if (selectedCompanyId > 0)
                {
                    var childIds = allCompanies.Where(c => c.PerusahaanIndukId == selectedCompanyId).Select(c => c.PerusahaanId).ToList();
                    var relationChildIds = relations.Where(r => r.ParentCompanyId == selectedCompanyId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                    var allChildIds = childIds.Concat(relationChildIds).Distinct().ToList();
                    
                    if (allChildIds.Any())
                    {
                        var targetCompanyIds = new HashSet<int>(allChildIds) { selectedCompanyId };
                        companiesToCompare = allCompanies.Where(c => targetCompanyIds.Contains(c.PerusahaanId)).ToList();
                    }
                }

                foreach (var comp in companiesToCompare)
                {
                    var compEmps = await GetEmployeesComplianceData(comp.PerusahaanId, null, selectedYear, selectedMonth);
                    if (!compEmps.Any()) continue;

                    allEmployees.AddRange(compEmps);

                    int totalTarget = compEmps.Sum(e => (int)e.mtdTotalTarget);
                    int totalActual = compEmps.Sum(e => (int)e.mtdTotalActual);

                    int hAct = compEmps.Sum(e => Math.Min((int)e.hazard.actual, (int)e.hazard.target));
                    int hTgt = compEmps.Sum(e => (int)e.hazard.target);

                    int iAct = compEmps.Sum(e => Math.Min((int)e.inspeksi.actual, (int)e.inspeksi.target));
                    int iTgt = compEmps.Sum(e => (int)e.inspeksi.target);

                    int stAct = compEmps.Sum(e => Math.Min((int)e.safetyTalk.actual, (int)e.safetyTalk.target));
                    int stTgt = compEmps.Sum(e => (int)e.safetyTalk.target);

                    int oAct = compEmps.Sum(e => Math.Min((int)e.observasi.actual, (int)e.observasi.target));
                    int oTgt = compEmps.Sum(e => (int)e.observasi.target);

                    int cAct = compEmps.Sum(e => Math.Min((int)e.coaching.actual, (int)e.coaching.target));
                    int cTgt = compEmps.Sum(e => (int)e.coaching.target);

                    int p5mAct = compEmps.Sum(e => Math.Min((int)e.p5m.actual, (int)e.p5m.target));
                    int p5mTgt = compEmps.Sum(e => (int)e.p5m.target);

                    double mtdRate = totalTarget > 0 ? Math.Min(100.0, Math.Round((double)totalActual / totalTarget * 100.0, 1)) : 0;
                    double hRate = hTgt > 0 ? Math.Min(100.0, Math.Round((double)hAct / hTgt * 100.0, 1)) : -1;
                    double iRate = iTgt > 0 ? Math.Min(100.0, Math.Round((double)iAct / iTgt * 100.0, 1)) : -1;
                    double stRate = stTgt > 0 ? Math.Min(100.0, Math.Round((double)stAct / stTgt * 100.0, 1)) : -1;
                    double oRate = oTgt > 0 ? Math.Min(100.0, Math.Round((double)oAct / oTgt * 100.0, 1)) : -1;
                    double cRate = cTgt > 0 ? Math.Min(100.0, Math.Round((double)cAct / cTgt * 100.0, 1)) : -1;
                    double p5mRate = p5mTgt > 0 ? Math.Min(100.0, Math.Round((double)p5mAct / p5mTgt * 100.0, 1)) : -1;

                    companyStandings.Add(new {
                        CompanyId = comp.PerusahaanId,
                        CompanyName = comp.NamaPerusahaan,
                        PjoName = comp.NamaPjo,
                        EmployeeCount = compEmps.Count,
                        TotalTarget = totalTarget,
                        MtdAchievementRate = mtdRate,
                        MtdHazardRate = hRate,
                        MtdInspeksiRate = iRate,
                        MtdSafetyTalkRate = stRate,
                        MtdObservasiRate = oRate,
                        MtdCoachingRate = cRate,
                        MtdP5mRate = p5mRate
                    });
                }

                var allStandings = companyStandings.OrderByDescending(x => (double)x.MtdAchievementRate).ToList();
                ViewBag.CompanyStandings = allStandings.Where(x => !((int)x.TotalTarget > 0 && (double)x.MtdAchievementRate == 0)).ToList();
                ViewBag.CompanyRedZone = allStandings.Where(x => (int)x.TotalTarget > 0 && (double)x.MtdAchievementRate == 0).ToList();

                // Non-admin can only see their own squad players even in global league mode
                var sortedEmployees = allEmployees
                    .Where(e => isAdmin || isSafetyRole || (int)e.companyId == resolvedCompanyId)
                    .Select(e => new {
                        name = (string)e.karyawanName,
                        nik = (string)e.nik,
                        departmentName = (string)e.departmentName,
                        jabatanName = (string)e.jabatanName,
                        complianceRate = (double)e.complianceRate,
                        mtdTotalTarget = (int)e.mtdTotalTarget,
                        hazard = new { actual = (int)e.hazard.actual, target = (int)e.hazard.target },
                        inspeksi = new { actual = (int)e.inspeksi.actual, target = (int)e.inspeksi.target },
                        safetyTalk = new { actual = (int)e.safetyTalk.actual, target = (int)e.safetyTalk.target },
                        observasi = new { actual = (int)e.observasi.actual, target = (int)e.observasi.target },
                        coaching = new { actual = (int)e.coaching.actual, target = (int)e.coaching.target },
                        p5m = new { actual = (int)e.p5m.actual, target = (int)e.p5m.target }
                    })
                    .OrderBy(e => e.mtdTotalTarget == 0 ? 1 : 0)
                    .ThenByDescending(e => e.complianceRate)
                    .ThenByDescending(e => e.hazard.actual + e.inspeksi.actual + e.safetyTalk.actual + e.observasi.actual + e.coaching.actual + e.p5m.actual)
                    .ToList();

                ViewBag.Employees = sortedEmployees;
            }
            else
            {
                // Liga Internal: Departments
                var employees = await GetEmployeesComplianceData(selectedCompany.PerusahaanId, null, selectedYear, selectedMonth);
                
                var deptAchievements = employees
                    .GroupBy(e => (string)e.departmentName)
                    .Select(g => {
                        int employeeCount = g.Count();
                        
                        int totalTarget = g.Sum(e => (int)e.mtdTotalTarget);
                        int totalActual = g.Sum(e => (int)e.mtdTotalActual);
                        
                        int hAct = g.Sum(e => Math.Min((int)e.hazard.actual, (int)e.hazard.target));
                        int hTgt = g.Sum(e => (int)e.hazard.target);
                        
                        int iAct = g.Sum(e => Math.Min((int)e.inspeksi.actual, (int)e.inspeksi.target));
                        int iTgt = g.Sum(e => (int)e.inspeksi.target);
                        
                        int stAct = g.Sum(e => Math.Min((int)e.safetyTalk.actual, (int)e.safetyTalk.target));
                        int stTgt = g.Sum(e => (int)e.safetyTalk.target);
                        
                        int oAct = g.Sum(e => Math.Min((int)e.observasi.actual, (int)e.observasi.target));
                        int oTgt = g.Sum(e => (int)e.observasi.target);
                        
                        int cAct = g.Sum(e => Math.Min((int)e.coaching.actual, (int)e.coaching.target));
                        int cTgt = g.Sum(e => (int)e.coaching.target);
                        
                        int p5mAct = g.Sum(e => Math.Min((int)e.p5m.actual, (int)e.p5m.target));
                        int p5mTgt = g.Sum(e => (int)e.p5m.target);
                        
                        double mtdRate = totalTarget > 0 ? Math.Min(100.0, Math.Round((double)totalActual / totalTarget * 100.0, 1)) : 0;
                        double hRate = hTgt > 0 ? Math.Min(100.0, Math.Round((double)hAct / hTgt * 100.0, 1)) : -1;
                        double iRate = iTgt > 0 ? Math.Min(100.0, Math.Round((double)iAct / iTgt * 100.0, 1)) : -1;
                        double stRate = stTgt > 0 ? Math.Min(100.0, Math.Round((double)stAct / stTgt * 100.0, 1)) : -1;
                        double oRate = oTgt > 0 ? Math.Min(100.0, Math.Round((double)oAct / oTgt * 100.0, 1)) : -1;
                        double cRate = cTgt > 0 ? Math.Min(100.0, Math.Round((double)cAct / cTgt * 100.0, 1)) : -1;
                        double p5mRate = p5mTgt > 0 ? Math.Min(100.0, Math.Round((double)p5mAct / p5mTgt * 100.0, 1)) : -1;
                        
                        return new DepartmentAchievementViewModel
                        {
                            DepartmentName = g.Key,
                            EmployeeCount = employeeCount,
                            TotalTarget = totalTarget,
                            MtdAchievementRate = mtdRate,
                            MtdHazardRate = hRate,
                            MtdInspeksiRate = iRate,
                            MtdSafetyTalkRate = stRate,
                            MtdObservasiRate = oRate,
                            MtdCoachingRate = cRate,
                            MtdP5mRate = p5mRate
                        };
                    })
                    .OrderByDescending(d => d.MtdAchievementRate)
                    .ToList();

                ViewBag.DepartmentAchievements = deptAchievements.Where(d => !(d.TotalTarget > 0 && d.MtdAchievementRate == 0)).ToList();
                ViewBag.DepartmentRedZone = deptAchievements.Where(d => d.TotalTarget > 0 && d.MtdAchievementRate == 0).ToList();

                var sortedEmployees = employees.Select(e => new {
                    name = (string)e.karyawanName,
                    nik = (string)e.nik,
                    departmentName = (string)e.departmentName,
                    jabatanName = (string)e.jabatanName,
                    complianceRate = (double)e.complianceRate,
                    mtdTotalTarget = (int)e.mtdTotalTarget,
                    hazard = new { actual = (int)e.hazard.actual, target = (int)e.hazard.target },
                    inspeksi = new { actual = (int)e.inspeksi.actual, target = (int)e.inspeksi.target },
                    safetyTalk = new { actual = (int)e.safetyTalk.actual, target = (int)e.safetyTalk.target },
                    observasi = new { actual = (int)e.observasi.actual, target = (int)e.observasi.target },
                    coaching = new { actual = (int)e.coaching.actual, target = (int)e.coaching.target },
                    p5m = new { actual = (int)e.p5m.actual, target = (int)e.p5m.target }
                })
                .OrderBy(e => e.mtdTotalTarget == 0 ? 1 : 0)
                .ThenByDescending(e => e.complianceRate)
                .ThenByDescending(e => e.hazard.actual + e.inspeksi.actual + e.safetyTalk.actual + e.observasi.actual + e.coaching.actual + e.p5m.actual)
                .ToList();

                ViewBag.Employees = sortedEmployees;
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ExportLeagueToExcel(int? companyId = null, string mode = "dept")
        {
            var (resolvedCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);

            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            List<PerusahaanView> allowedCompanies;
            if (isAdmin || isSafetyRole)
            {
                allowedCompanies = allCompanies;
            }
            else
            {
                allowedCompanies = allCompanies
                    .Where(p => allowedCompanyIds.Contains(p.PerusahaanId))
                    .ToList();
            }

            if (allowedCompanies == null || !allowedCompanies.Any())
            {
                return RedirectToAction("Index", "Home");
            }

            int selectedCompanyId = companyId ?? (resolvedCompanyId ?? allowedCompanies.First().PerusahaanId);
            if (!isAdmin && !isSafetyRole && !allowedCompanyIds.Contains(selectedCompanyId))
            {
                selectedCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            var selectedCompany = allCompanies.FirstOrDefault(c => c.PerusahaanId == selectedCompanyId) ?? allowedCompanies.First();

            List<dynamic> employeesData = new List<dynamic>();

            if (mode == "company")
            {
                var allEmployees = new List<dynamic>();
                foreach (var comp in allCompanies)
                {
                    var compEmps = await GetEmployeesComplianceData(comp.PerusahaanId);
                    if (!compEmps.Any()) continue;
                    allEmployees.AddRange(compEmps);
                }

                employeesData = allEmployees
                    .Where(e => isAdmin || isSafetyRole || (int)e.companyId == resolvedCompanyId)
                    .ToList();
            }
            else
            {
                employeesData = await GetEmployeesComplianceData(selectedCompany.PerusahaanId);
            }

            var sorted = employeesData
                .Select(e => new {
                    name = (string)e.karyawanName,
                    nik = (string)e.nik,
                    departmentName = (string)e.departmentName,
                    jabatanName = (string)e.jabatanName,
                    complianceRate = (double)e.complianceRate,
                    mtdTotalTarget = (int)e.mtdTotalTarget,
                    hazard = new { actual = (int)e.hazard.actual, target = (int)e.hazard.target },
                    inspeksi = new { actual = (int)e.inspeksi.actual, target = (int)e.inspeksi.target },
                    safetyTalk = new { actual = (int)e.safetyTalk.actual, target = (int)e.safetyTalk.target },
                    observasi = new { actual = (int)e.observasi.actual, target = (int)e.observasi.target },
                    coaching = new { actual = (int)e.coaching.actual, target = (int)e.coaching.target },
                    p5m = new { actual = (int)e.p5m.actual, target = (int)e.p5m.target }
                })
                .OrderBy(e => e.mtdTotalTarget == 0 ? 1 : 0)
                .ThenByDescending(e => e.complianceRate)
                .ThenByDescending(e => e.hazard.actual + e.inspeksi.actual + e.safetyTalk.actual + e.observasi.actual + e.coaching.actual + e.p5m.actual)
                .ToList();

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Klasemen Skuad SAP");
                
                // Add header info
                ws.Cell(1, 1).Value = "LAPORAN KLASEMEN SKUAD KEPATUHAN SAP";
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Cell(1, 1).Style.Font.FontColor = XLColor.Navy;

                ws.Cell(2, 1).Value = $"Perusahaan: {selectedCompany.NamaPerusahaan}";
                ws.Cell(2, 1).Style.Font.Bold = true;
                ws.Cell(2, 1).Style.Font.FontSize = 11;

                ws.Cell(3, 1).Value = $"Mode: {(mode == "company" ? "Liga Company (Global)" : "Liga Departemen (Internal)")} | Tanggal Unduh: {DateTime.Now:yyyy-MM-dd HH:mm}";
                ws.Cell(3, 1).Style.Font.Italic = true;
                ws.Cell(3, 1).Style.Font.FontSize = 9.5;

                // Setup Table Headers
                string[] headers = new[] {
                    "Peringkat", "Nama Karyawan", "NIK", "Departemen", "Jabatan", "Kepatuhan (%)",
                    "Hazard Actual", "Hazard Target", "Inspeksi Actual", "Inspeksi Target",
                    "Safety Talk Actual", "Safety Talk Target", "Observasi Actual", "Observasi Target",
                    "Coaching Actual", "Coaching Target", "P5M Actual", "P5M Target"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(5, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a"); // Deep Navy
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
                ws.Row(5).Height = 25;

                int row = 6;
                int rank = 1;
                foreach (var emp in sorted)
                {
                    ws.Cell(row, 1).Value = rank;
                    ws.Cell(row, 2).Value = emp.name;
                    ws.Cell(row, 3).Value = emp.nik;
                    ws.Cell(row, 4).Value = emp.departmentName;
                    ws.Cell(row, 5).Value = emp.jabatanName;
                    
                    var compCell = ws.Cell(row, 6);
                    compCell.Value = emp.complianceRate;
                    compCell.Style.NumberFormat.Format = "0.0";

                    ws.Cell(row, 7).Value = emp.hazard.actual;
                    ws.Cell(row, 8).Value = emp.hazard.target;
                    
                    ws.Cell(row, 9).Value = emp.inspeksi.actual;
                    ws.Cell(row, 10).Value = emp.inspeksi.target;

                    ws.Cell(row, 11).Value = emp.safetyTalk.actual;
                    ws.Cell(row, 12).Value = emp.safetyTalk.target;

                    ws.Cell(row, 13).Value = emp.observasi.actual;
                    ws.Cell(row, 14).Value = emp.observasi.target;

                    ws.Cell(row, 15).Value = emp.coaching.actual;
                    ws.Cell(row, 16).Value = emp.coaching.target;

                    ws.Cell(row, 17).Value = emp.p5m.actual;
                    ws.Cell(row, 18).Value = emp.p5m.target;

                    // Align rank, NIK, rates, numbers to center
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    for (int c = 7; c <= 18; c++)
                    {
                        ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }

                    // Format values as number
                    for (int c = 7; c <= 18; c++)
                    {
                        ws.Cell(row, c).Style.NumberFormat.Format = "#,##0";
                        if (c % 2 != 0) // Actual columns
                        {
                            ws.Cell(row, c).Style.Font.Bold = true;
                        }
                    }

                    // Conditional Formatting for Compliance Rate
                    if (emp.complianceRate >= 100)
                        ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#16a34a"); // Green
                    else if (emp.complianceRate >= 80)
                        ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#2563eb"); // Blue
                    else
                        ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#dc2626"); // Red
                        
                    ws.Cell(row, 6).Style.Font.Bold = true;

                    // Border styling
                    var rowRange = ws.Range(row, 1, row, 18);
                    rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");

                    // Highlight top 3 (Champions Zone) with Gold, Silver, Bronze, else Zebra striping
                    if (rank == 1)
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef08a"); // Gold
                    else if (rank == 2)
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0"); // Silver
                    else if (rank == 3)
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#ffedd5"); // Bronze
                    else if (emp.mtdTotalTarget > 0 && emp.complianceRate == 0)
                    {
                        // Red Zone: Has target but no achievement
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2"); // Light red
                        ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#b91c1c"); // Dark red name
                    }
                    else
                    {
                        // Zebra striping for others
                        if (row % 2 == 0)
                            rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                        else
                            rowRange.Style.Fill.BackgroundColor = XLColor.White;
                    }

                    row++;
                    rank++;
                }

                // Freeze Header Row
                ws.SheetView.FreezeRows(5);
                
                // Add thick outer border to the entire table
                if (row > 6)
                {
                    ws.Range(5, 1, row - 1, 18).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    ws.Range(5, 1, row - 1, 18).Style.Border.OutsideBorderColor = XLColor.FromHtml("#0f172a");
                }

                // Auto fit columns
                ws.Columns(1, 18).AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string safeCompName = string.Concat((selectedCompany.NamaPerusahaan ?? "Company").Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                    string fileName = $"League_Kepatuhan_SAP_{safeCompName}_{DateTime.Now:yyyyMMdd}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> Compliance(int? companyId = null, string? departmentName = null, int page = 1, int? year = null, int? month = null)
        {
            // Set transaction isolation level to READ UNCOMMITTED to prevent deadlocks/timeouts on heavy tables
            await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            ViewData["HeaderTitle"] = "Pencapaian SAP";
            ViewData["ActiveTab"] = "Performance";

            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

            var (resolvedCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);

            // Fetch all active companies
            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif && !ExcludedCompanies.Ids.Contains(p.PerusahaanId))
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            List<PerusahaanView> allowedCompanies = allCompanies;

            ViewBag.Companies = allowedCompanies;

            if (allowedCompanies == null || !allowedCompanies.Any())
            {
                return RedirectToAction("Index", "Home");
            }

            int selectedCompanyId = companyId ?? (resolvedCompanyId ?? allowedCompanies.First().PerusahaanId);
            ViewBag.SelectedCompanyId = selectedCompanyId;

            // Fetch all active departments for this selected company to show in filter
            var departments = await (from k in _context.Karyawans
                                     join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId
                                     where k.IdPerusahaan == selectedCompanyId && k.StatusAktif == true
                                     select d.NamaDepartemen)
                                     .Distinct()
                                     .OrderBy(d => d)
                                     .ToListAsync();
            ViewBag.Departments = departments;
            ViewBag.SelectedDepartmentName = departmentName;

            // Heavy GetEmployeesComplianceData for all companies removed.
            // SAP Programs stats and ViewBags are now optimally calculated during Section 4 Group Metrics.


            // 1. Action Plans by Department (creator's department)
            var actionPlanDeptStats = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.PerusahaanId == selectedCompanyId)
                .GroupBy(a => a.Departemen ?? "Lain-lain")
                .Select(g => new ComplianceGroupStatViewModel
                {
                    GroupName = g.Key,
                    TotalCreated = g.Count(),
                    OpenCount = g.Count(a => a.Status == "Open"),
                    ClosedCount = g.Count(a => a.Status == "Closed")
                })
                .OrderByDescending(s => s.OpenCount)
                .Take(5)
                .ToListAsync();

            // 2. Action Plans by Area
            var actionPlanAreaStats = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.PerusahaanId == selectedCompanyId && a.Area != null && a.Area != "")
                .GroupBy(a => a.Area!)
                .Select(g => new ComplianceGroupStatViewModel
                {
                    GroupName = g.Key,
                    TotalCreated = g.Count(),
                    OpenCount = g.Count(a => a.Status == "Open"),
                    ClosedCount = g.Count(a => a.Status == "Closed")
                })
                .OrderByDescending(s => s.OpenCount)
                .Take(5)
                .ToListAsync();

            ViewBag.DeptActionPlanStats = actionPlanDeptStats;
            ViewBag.AreaActionPlanStats = actionPlanAreaStats;

            // 3. Subcontractor Achievements
            var childCompanies = await _context.Perusahaans
                .Where(p => p.PerusahaanIndukId == selectedCompanyId && p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            var subconComplianceList = new List<CompanyLeaderboardViewModel>();

            if (childCompanies.Any())
            {
                var childCompanyIds = childCompanies.Select(p => p.PerusahaanId).ToList();

                var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
                var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

                var allChildKaryawans = await _context.Karyawans
                    .Where(k => k.StatusAktif && childCompanyIds.Contains(k.IdPerusahaan))
                    .ToListAsync();

                var allChildKaryawanIds = allChildKaryawans.Select(k => k.IdKaryawan).ToList();

                var targets = await _context.KaryawanJabatanMappings
                    .Where(m => allChildKaryawanIds.Contains(m.KaryawanId))
                    .ToListAsync();
                var targetsDict = targets.ToDictionary(m => m.KaryawanId);

                var hazards = await _context.HazardReports
                    .Where(h => !h.IsDeleted && childCompanyIds.Contains(h.PerusahaanId ?? 0) && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth)
                    .Select(h => new { h.PerusahaanId, h.Nik })
                    .ToListAsync();

                var inspections = await _context.Inspections
                    .Where(i => !i.IsDeleted && childCompanyIds.Contains(i.PerusahaanId ?? 0) && i.Tanggal >= startOfMonth && i.Tanggal <= endOfMonth)
                    .Select(i => new { i.PerusahaanId, i.Nik })
                    .ToListAsync();

                var safetyTalks = await _context.SafetyTalks
                    .Where(s => !s.IsDeleted && childCompanyIds.Contains(s.PerusahaanId ?? 0) && s.Tanggal >= startOfMonth && s.Tanggal <= endOfMonth)
                    .Select(s => new { s.PerusahaanId, s.Nik })
                    .ToListAsync();

                var p5ms = await _context.P5ms
                    .Where(p => !p.IsDeleted && childCompanyIds.Contains(p.PerusahaanId ?? 0) && p.Tanggal >= startOfMonth && p.Tanggal <= endOfMonth)
                    .Select(p => new { p.PerusahaanId, p.Nik })
                    .ToListAsync();

                var coachingCreators = await _context.Coachings
                    .Where(co => !co.IsDeleted && childCompanyIds.Contains(co.PerusahaanId ?? 0) && co.CreatedAt >= startOfMonth && co.CreatedAt <= endOfMonth)
                    .Select(co => new { co.PerusahaanId, co.Nik })
                    .ToListAsync();

                var coachingParticipants = await (from p in _context.CoachingParticipants
                                                  join k in _context.Karyawans on p.Nik equals k.NoNik
                                                  where p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfMonth && p.Coaching.CreatedAt <= endOfMonth && childCompanyIds.Contains(k.IdPerusahaan)
                                                  select new { PerusahaanId = (int?)k.IdPerusahaan, p.Nik })
                                                  .ToListAsync();

                var coachings = coachingCreators.Concat(coachingParticipants).ToList();

                var observations = await (from o in _context.Observations
                                          join k in _context.Karyawans on o.Nik equals k.NoNik
                                          where !o.IsDeleted && o.CreatedAt >= startOfMonth && o.CreatedAt <= endOfMonth && childCompanyIds.Contains(k.IdPerusahaan)
                                          select new { PerusahaanId = (int?)k.IdPerusahaan, o.Nik })
                                          .ToListAsync();

                foreach (var sub in childCompanies)
                {
                    var companyEmps = allChildKaryawans.Where(k => k.IdPerusahaan == sub.PerusahaanId).ToList();
                    int empCount = companyEmps.Count;

                    int companyMtdTarget = 0;
                    int companyMtdActual = 0;

                    if (empCount > 0)
                    {
                        var subHaz = hazards.Where(x => x.PerusahaanId == sub.PerusahaanId).Select(x => x.Nik).ToList();
                        var subIns = inspections.Where(x => x.PerusahaanId == sub.PerusahaanId).Select(x => x.Nik).ToList();
                        var subSt = safetyTalks.Where(x => x.PerusahaanId == sub.PerusahaanId).Select(x => x.Nik).ToList();
                        var subP5m = p5ms.Where(x => x.PerusahaanId == sub.PerusahaanId).Select(x => x.Nik).ToList();
                        var subCoa = coachings.Where(x => x.PerusahaanId == sub.PerusahaanId).Select(x => x.Nik).ToList();
                        var subObs = observations.Where(x => x.PerusahaanId == sub.PerusahaanId).Select(x => x.Nik).ToList();

                        var hazByNik = subHaz.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                        var insByNik = subIns.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                        var stByNik = subSt.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                        var p5mByNik = subP5m.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                        var coaByNik = subCoa.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                        var obsByNik = subObs.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                        foreach (var emp in companyEmps)
                        {
                            var nik = (emp.NoNik ?? string.Empty).Trim();
                            int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0;
                            if (targetsDict.TryGetValue(emp.IdKaryawan, out var t))
                            {
                                hTar = t.TargetHazardReport ?? 0;
                                insTar = t.TargetInspeksi ?? 0;
                                stTar = t.TargetSafetyTalk ?? 0;
                                obsTar = t.TargetObservasi ?? 0;
                                cTar = t.TargetCoaching ?? 0;
                            }

                            if (hTar + insTar + stTar + obsTar + cTar == 0)
                            {
                                continue;
                            }

                            int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                            int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                            int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                            int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                            int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                            int cappedH = Math.Min(actH, hTar);
                            int cappedI = Math.Min(actI, insTar);
                            int cappedST = Math.Min(actST, stTar);
                            int cappedO = Math.Min(actO, obsTar);
                            int cappedC = Math.Min(actC, cTar);

                            companyMtdTarget += (hTar + insTar + stTar + obsTar + cTar);
                            companyMtdActual += (cappedH + cappedI + cappedST + cappedO + cappedC);
                        }
                    }

                    double achievementRate = companyMtdTarget > 0 ? Math.Min(100.0, Math.Round((double)companyMtdActual / companyMtdTarget * 100.0, 1)) : 0.0;

                    subconComplianceList.Add(new CompanyLeaderboardViewModel
                    {
                        CompanyId = sub.PerusahaanId,
                        CompanyName = sub.NamaPerusahaan ?? "Unknown",
                        ActiveEmployees = empCount,
                        TotalSubmissions = companyMtdActual,
                        TargetSubmissions = companyMtdTarget,
                        AchievementRate = achievementRate
                    });
                }
            }

            ViewBag.SubconComplianceList = subconComplianceList;

            // 4. Group Detailed Compliance Metrics (all allowed companies)
            var startOfMonthM = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonthM = startOfMonthM.AddMonths(1).AddTicks(-1);
            // Use ALL allowed company IDs so the KPI aggregates across every employee with a target
            var relatedCompanyIds = allowedCompanies.Select(c => c.PerusahaanId).ToList();

            var activeKaryawans = await _context.Karyawans
                .Where(k => k.StatusAktif && relatedCompanyIds.Contains(k.IdPerusahaan))
                .ToListAsync();

            var activeKaryawanIds = activeKaryawans.Select(k => k.IdKaryawan).ToList();

            var targetsList = await _context.KaryawanJabatanMappings
                .Where(m => activeKaryawanIds.Contains(m.KaryawanId))
                .ToListAsync();
            var groupTargetsDict = targetsList.ToDictionary(m => m.KaryawanId);

            var groupHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && relatedCompanyIds.Contains(h.PerusahaanId.Value) && h.Tanggal >= startOfMonthM && h.Tanggal <= endOfMonthM)
                .Select(h => new { h.PerusahaanId, h.Nik, h.StatusTemuan })
                .ToListAsync();

            var groupInspections = await _context.Inspections
                .Where(i => !i.IsDeleted && i.PerusahaanId.HasValue && relatedCompanyIds.Contains(i.PerusahaanId.Value) && i.Tanggal >= startOfMonthM && i.Tanggal <= endOfMonthM)
                .Select(i => new { i.PerusahaanId, i.Nik })
                .ToListAsync();

            var groupSafetyTalks = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && s.PerusahaanId.HasValue && relatedCompanyIds.Contains(s.PerusahaanId.Value) && s.Tanggal >= startOfMonthM && s.Tanggal <= endOfMonthM)
                .Select(s => new { s.PerusahaanId, s.Nik })
                .ToListAsync();

            var groupP5ms = await _context.P5ms
                .Where(p => !p.IsDeleted && p.PerusahaanId.HasValue && relatedCompanyIds.Contains(p.PerusahaanId.Value) && p.Tanggal >= startOfMonthM && p.Tanggal <= endOfMonthM)
                .Select(p => new { p.PerusahaanId, p.Nik })
                .ToListAsync();

            var groupCoachingCreators = await _context.Coachings
                .Where(co => !co.IsDeleted && co.PerusahaanId.HasValue && relatedCompanyIds.Contains(co.PerusahaanId.Value) && co.CreatedAt >= startOfMonthM && co.CreatedAt <= endOfMonthM)
                .Select(co => new { co.PerusahaanId, co.Nik })
                .ToListAsync();

            var groupCoachingParticipants = await (from p in _context.CoachingParticipants
                                                   join k in _context.Karyawans on p.Nik equals k.NoNik
                                                   where p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfMonthM && p.Coaching.CreatedAt <= endOfMonthM && relatedCompanyIds.Contains(k.IdPerusahaan)
                                                   select new { PerusahaanId = (int?)k.IdPerusahaan, p.Nik })
                                                   .ToListAsync();

            var groupCoachings = groupCoachingCreators.Concat(groupCoachingParticipants).ToList();

            var groupObservations = await (from o in _context.Observations
                                           join k in _context.Karyawans on o.Nik equals k.NoNik
                                           where !o.IsDeleted && o.CreatedAt >= startOfMonthM && o.CreatedAt <= endOfMonthM && relatedCompanyIds.Contains(k.IdPerusahaan)
                                           select new { PerusahaanId = (int?)k.IdPerusahaan, o.Nik })
                                           .ToListAsync();

            var gHazByNik = groupHazards.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var gInsByNik = groupInspections.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var gStByNik = groupSafetyTalks.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var gP5mByNik = groupP5ms.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var gCoaByNik = groupCoachings.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var gObsByNik = groupObservations.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            int fullyCompliantCount = 0;
            int participatingCount = 0;
            int inactiveCount = 0;

            int indeximBelum = 0;
            int uduBelum = 0;
            int kppBelum = 0;
            int mgeBelum = 0;

            int targetH = 0, actualH = 0, withTargetH = 0, fulfilledH = 0;
            int targetI = 0, actualI = 0, withTargetI = 0, fulfilledI = 0;
            int targetS = 0, actualS = 0, withTargetS = 0, fulfilledS = 0;
            int targetO = 0, actualO = 0, withTargetO = 0, fulfilledO = 0;
            int targetC = 0, actualC = 0, withTargetC = 0, fulfilledC = 0;

            foreach (var emp in activeKaryawans)
            {
                var nik = (emp.NoNik ?? string.Empty).Trim();
                int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0;
                if (groupTargetsDict.TryGetValue(emp.IdKaryawan, out var t))
                {
                    hTar = t.TargetHazardReport ?? 0;
                    insTar = t.TargetInspeksi ?? 0;
                    stTar = t.TargetSafetyTalk ?? 0;
                    obsTar = t.TargetObservasi ?? 0;
                    cTar = t.TargetCoaching ?? 0;
                }

                if (hTar + insTar + stTar + obsTar + cTar == 0)
                {
                    ViewData["AktifTidakAdaTarget"] = (int)(ViewData["AktifTidakAdaTarget"] ?? 0) + 1;
                    
                    int actHT = string.IsNullOrEmpty(nik) ? 0 : (gHazByNik.TryGetValue(nik, out var aht) ? aht : 0);
                    int actIT = string.IsNullOrEmpty(nik) ? 0 : (gInsByNik.TryGetValue(nik, out var ait) ? ait : 0);
                    int actSTT = string.IsNullOrEmpty(nik) ? 0 : (gStByNik.TryGetValue(nik, out var astt) ? astt : 0);
                    int actOT = string.IsNullOrEmpty(nik) ? 0 : (gObsByNik.TryGetValue(nik, out var aot) ? aot : 0);
                    int actCT = string.IsNullOrEmpty(nik) ? 0 : (gCoaByNik.TryGetValue(nik, out var actt) ? actt : 0);
                    
                    if (actHT + actIT + actSTT + actOT + actCT > 0)
                    {
                        ViewData["TidakAdaTargetTapiMengisi"] = (int)(ViewData["TidakAdaTargetTapiMengisi"] ?? 0) + 1;
                    }
                    continue;
                }

                int actH = string.IsNullOrEmpty(nik) ? 0 : (gHazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                int actI = string.IsNullOrEmpty(nik) ? 0 : (gInsByNik.TryGetValue(nik, out var ai) ? ai : 0);
                int actST = string.IsNullOrEmpty(nik) ? 0 : (gStByNik.TryGetValue(nik, out var ast) ? ast : 0);
                int actO = string.IsNullOrEmpty(nik) ? 0 : (gObsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                int actC = string.IsNullOrEmpty(nik) ? 0 : (gCoaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                int cappedH = Math.Min(actH, hTar);
                int cappedI = Math.Min(actI, insTar);
                int cappedST = Math.Min(actST, stTar);
                int cappedO = Math.Min(actO, obsTar);
                int cappedC = Math.Min(actC, cTar);

                int empTarget = hTar + insTar + stTar + obsTar + cTar;
                int empActual = cappedH + cappedI + cappedST + cappedO + cappedC;

                if (empTarget > 0)
                {
                    if (empActual >= empTarget) fullyCompliantCount++;
                    else if (empActual > 0) participatingCount++;
                    else
                    {
                        inactiveCount++;
                        // Target Companies specific counters
                        if (emp.IdPerusahaan == 1) indeximBelum++;
                        else if (emp.IdPerusahaan == 3) uduBelum++;
                        else if (emp.IdPerusahaan == 4) kppBelum++;
                        else if (emp.IdPerusahaan == 5) mgeBelum++;
                    }
                }
                else
                {
                    inactiveCount++;
                }

                targetH += hTar; actualH += cappedH;
                targetI += insTar; actualI += cappedI;
                targetS += stTar; actualS += cappedST;
                targetO += obsTar; actualO += cappedO;
                targetC += cTar; actualC += cappedC;

                if (hTar > 0) { withTargetH++; if (actH >= 1) fulfilledH++; }
                if (insTar > 0) { withTargetI++; if (actI >= 1) fulfilledI++; }
                if (stTar > 0) { withTargetS++; if (actST >= 1) fulfilledS++; }
                if (obsTar > 0) { withTargetO++; if (actO >= 1) fulfilledO++; }
                if (cTar > 0) { withTargetC++; if (actC >= 1) fulfilledC++; }
            }

            ViewBag.SapPrograms = new[]
            {
                new { Name = "Hazard Report", Icon = "bi-shield-exclamation", Color = "#6366f1", WithTarget = withTargetH, Fulfilled = fulfilledH, TotalActual = actualH, TotalTarget = targetH },
                new { Name = "Inspeksi", Icon = "bi-check2-square", Color = "#3b82f6", WithTarget = withTargetI, Fulfilled = fulfilledI, TotalActual = actualI, TotalTarget = targetI },
                new { Name = "Safety Talk", Icon = "bi-chat-left-quote-fill", Color = "#d97706", WithTarget = withTargetS, Fulfilled = fulfilledS, TotalActual = actualS, TotalTarget = targetS },
                new { Name = "Observasi", Icon = "bi-eye-fill", Color = "#ec4899", WithTarget = withTargetO, Fulfilled = fulfilledO, TotalActual = actualO, TotalTarget = targetO },
                new { Name = "Coaching", Icon = "bi-person-lines-fill", Color = "#a855f7", WithTarget = withTargetC, Fulfilled = fulfilledC, TotalActual = actualC, TotalTarget = targetC }
            };

            int totalBerTarget = fullyCompliantCount + participatingCount + inactiveCount;
            ViewBag.TotalCount = totalBerTarget;
            ViewBag.TotalEmployeesWithTarget = totalBerTarget;
            ViewBag.WajibSap = totalBerTarget;
            ViewBag.SudahMengisi = fullyCompliantCount + participatingCount;
            ViewBag.BelumMengisi = inactiveCount;
            
            var childBreakdowns = new List<dynamic>
            {
                new { CompanyName = "PT INDEXIM COALINDO", BelumMengisi = indeximBelum },
                new { CompanyName = "PT UNGGUL DINAMIKA UTAMA", BelumMengisi = uduBelum },
                new { CompanyName = "PT KALIMANTAN PRIMA PERSADA", BelumMengisi = kppBelum },
                new { CompanyName = "PT MEGA GLOBAL ENERGY", BelumMengisi = mgeBelum }
            };
            ViewBag.ChildBreakdowns = childBreakdowns.OrderByDescending(c => c.BelumMengisi).ToList();

            ViewBag.CurrentPage = 1;
            ViewBag.PageSize = 50;
            ViewBag.TotalPages = 1;

            // Hazard age distribution
            var openHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && relatedCompanyIds.Contains(h.PerusahaanId.Value) && h.StatusTemuan == "Open")
                .Select(h => h.Tanggal)
                .ToListAsync();

            int ageCritical = 0; // > 30 days
            int ageWarning = 0;  // 8-30 days
            int ageNew = 0;      // 0-7 days

            foreach (var dt in openHazards)
            {
                var days = (today - dt).TotalDays;
                if (days > 30) ageCritical++;
                else if (days > 7) ageWarning++;
                else ageNew++;
            }

            // P2H Kelayakan Kendaraan
            var companyNiksList = activeKaryawans.Select(k => k.NoNik).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var p2hReports = await _context.P2hReports
                .Where(r => !r.IsDeleted && r.Tanggal >= startOfMonthM && r.Tanggal <= endOfMonthM && companyNiksList.Contains(r.Nik))
                .Select(r => new { r.GolA_Json, r.SimperKimper })
                .ToListAsync();

            int totalP2h = p2hReports.Count;
            int criticalP2hDefects = p2hReports.Count(r => r.GolA_Json != null && r.GolA_Json.Contains("NOT_GOOD"));
            int simperViolations = p2hReports.Count(r => r.SimperKimper == "TIDAK");

            ViewBag.ActiveEmployeesCount = activeKaryawans.Count;
            ViewBag.FullyCompliantCount = fullyCompliantCount;
            ViewBag.ParticipatingCount = participatingCount;
            ViewBag.InactiveCount = inactiveCount;

            ViewBag.TargetH = targetH; ViewBag.ActualH = actualH;
            ViewBag.TargetI = targetI; ViewBag.ActualI = actualI;
            ViewBag.TargetS = targetS; ViewBag.ActualS = actualS;
            ViewBag.TargetO = targetO; ViewBag.ActualO = actualO;
            ViewBag.TargetC = targetC; ViewBag.ActualC = actualC;

            ViewBag.AgeCritical = ageCritical;
            ViewBag.AgeWarning = ageWarning;
            ViewBag.AgeNew = ageNew;
            ViewBag.TotalOpenHazards = openHazards.Count;

            ViewBag.TotalP2h = totalP2h;
            ViewBag.CriticalP2hDefects = criticalP2hDefects;
            ViewBag.SimperViolations = simperViolations;

            // 5. Maincon Group Comparison Calculation
            // UDU (3), KPP (4), dan MGE/PT Mega Global Energy (5) tampil sebagai grup mandiri masing-masing.
            // PT INDEXIM COALINDO (1) tampil sebagai grup tersendiri dengan anak-anaknya SELAIN ketiga promoted maincon tsb.
            var promotedMainconIds = new HashSet<int> { 3, 4, 5 }; // UDU (3), KPP (4), MGE/PT Mega Global Energy (5)

            var mainconList = new List<PerusahaanView>();

            // Bangun mainconList default: [PT INDEXIM] + [UDU, KPP, MGE] sebagai grup mandiri
            var indeximCompany = await _context.Perusahaans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.StatusAktif && p.PerusahaanId == 1);

            var promotedMaincons = await _context.Perusahaans.AsNoTracking()
                .Where(p => p.StatusAktif && promotedMainconIds.Contains(p.PerusahaanId))
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            if (indeximCompany != null) mainconList.Add(indeximCompany);
            mainconList.AddRange(promotedMaincons);

            var startOfMonthMaincon = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonthMaincon = startOfMonthMaincon.AddMonths(1).AddTicks(-1);
            var mainconGroupComparisonList = new List<MainconGroupComparisonViewModel>();
            var allSubconStats = new List<MostActiveSubconViewModel>();

            foreach (var mcon in mainconList)
            {
                var childIdsFromRelations = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                    .Where(r => r.ParentCompanyId == mcon.PerusahaanId && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                    .Select(r => r.ChildCompanyId!.Value)
                    .ToListAsync();

                var childIdsFromDirectParent = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.PerusahaanIndukId == mcon.PerusahaanId && p.StatusAktif)
                    .Select(p => p.PerusahaanId)
                    .ToListAsync();

                var allChildIds = childIdsFromRelations.Concat(childIdsFromDirectParent).Distinct().ToList();

                // Untuk PT INDEXIM, kecualikan promoted maincons dari daftar anaknya
                // agar UDU, KPP, MGE tidak dihitung ganda di grup Indexim
                if (mcon.PerusahaanId == 1)
                {
                    allChildIds = allChildIds.Where(id => !promotedMainconIds.Contains(id)).ToList();
                }

                var subcons = await _context.Perusahaans.AsNoTracking()
                    .Where(p => allChildIds.Contains(p.PerusahaanId) && p.StatusAktif)
                    .OrderBy(p => p.NamaPerusahaan)
                    .ToListAsync();

                var relatedCompanies = new List<PerusahaanView> { mcon };
                relatedCompanies.AddRange(subcons);

                var companyIds = relatedCompanies.Select(rc => rc.PerusahaanId).ToList();

                // Batch retrieval for employees
                var allGroupKaryawans = await _context.Karyawans.AsNoTracking()
                    .Where(k => k.StatusAktif && companyIds.Contains(k.IdPerusahaan))
                    .ToListAsync();
                
                var allGroupKaryawanIds = allGroupKaryawans.Select(k => k.IdKaryawan).ToList();
                var allGroupTargets = await _context.KaryawanJabatanMappings.AsNoTracking()
                    .Where(m => allGroupKaryawanIds.Contains(m.KaryawanId))
                    .ToDictionaryAsync(m => m.KaryawanId);

                // Fetch MTD actual logs
                var allGroupHazards = await _context.HazardReports.AsNoTracking()
                    .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && companyIds.Contains(h.PerusahaanId.Value) && h.Tanggal >= startOfMonthMaincon && h.Tanggal <= endOfMonthMaincon)
                    .Select(h => new { PerusahaanId = h.PerusahaanId ?? 0, h.Nik })
                    .ToListAsync();

                var allGroupInspections = await _context.Inspections.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.PerusahaanId.HasValue && companyIds.Contains(i.PerusahaanId.Value) && i.Tanggal >= startOfMonthMaincon && i.Tanggal <= endOfMonthMaincon)
                    .Select(i => new { PerusahaanId = i.PerusahaanId ?? 0, i.Nik })
                    .ToListAsync();

                var allGroupSafetyTalks = await _context.SafetyTalks.AsNoTracking()
                    .Where(s => !s.IsDeleted && s.PerusahaanId.HasValue && companyIds.Contains(s.PerusahaanId.Value) && s.Tanggal >= startOfMonthMaincon && s.Tanggal <= endOfMonthMaincon)
                    .Select(s => new { PerusahaanId = s.PerusahaanId ?? 0, s.Nik })
                    .ToListAsync();

                var allGroupCoachingCreators = await _context.Coachings.AsNoTracking()
                    .Where(co => !co.IsDeleted && co.PerusahaanId.HasValue && companyIds.Contains(co.PerusahaanId.Value) && co.CreatedAt >= startOfMonthMaincon && co.CreatedAt <= endOfMonthMaincon)
                    .Select(co => new { PerusahaanId = co.PerusahaanId ?? 0, co.Nik })
                    .ToListAsync();

                var allGroupCoachingParticipants = await (from p in _context.CoachingParticipants.AsNoTracking()
                                                          join k in _context.Karyawans.AsNoTracking() on p.Nik equals k.NoNik
                                                          where p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfMonthMaincon && p.Coaching.CreatedAt <= endOfMonthMaincon && companyIds.Contains(k.IdPerusahaan)
                                                          select new { PerusahaanId = k.IdPerusahaan, p.Nik })
                                                          .ToListAsync();

                var allGroupCoachings = allGroupCoachingCreators.Concat(allGroupCoachingParticipants).ToList();

                var allGroupObservations = await (from o in _context.Observations.AsNoTracking()
                                                   join k in _context.Karyawans.AsNoTracking() on o.Nik equals k.NoNik
                                                   where !o.IsDeleted && o.CreatedAt >= startOfMonthMaincon && o.CreatedAt <= endOfMonthMaincon && companyIds.Contains(k.IdPerusahaan)
                                                   select new { PerusahaanId = k.IdPerusahaan, o.Nik })
                                                   .ToListAsync();

                int totalGroupEmployees = 0;
                int employeesWithTargetCount = 0;
                int totalTargetH = 0, totalActualH = 0;
                int totalTargetI = 0, totalActualI = 0;
                int totalTargetS = 0, totalActualS = 0;
                int totalTargetO = 0, totalActualO = 0;
                int totalTargetC = 0, totalActualC = 0;

                var hazByNik = allGroupHazards.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var insByNik = allGroupInspections.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var stByNik = allGroupSafetyTalks.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var coaByNik = allGroupCoachings.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var obsByNik = allGroupObservations.GroupBy(n => n.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                foreach (var emp in allGroupKaryawans)
                {
                    var nik = (emp.NoNik ?? string.Empty).Trim();
                    int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0;
                    if (allGroupTargets.TryGetValue(emp.IdKaryawan, out var t))
                    {
                        hTar = t.TargetHazardReport ?? 0;
                        insTar = t.TargetInspeksi ?? 0;
                        stTar = t.TargetSafetyTalk ?? 0;
                        obsTar = t.TargetObservasi ?? 0;
                        cTar = t.TargetCoaching ?? 0;
                    }

                    if (hTar + insTar + stTar + obsTar + cTar == 0)
                    {
                        continue;
                    }

                    totalGroupEmployees++;
                    employeesWithTargetCount++;

                    int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                    int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                    int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                    int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                    int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                    int cappedH = Math.Min(actH, hTar);
                    int cappedI = Math.Min(actI, insTar);
                    int cappedST = Math.Min(actST, stTar);
                    int cappedO = Math.Min(actO, obsTar);
                    int cappedC = Math.Min(actC, cTar);

                    totalTargetH += hTar; totalActualH += cappedH;
                    totalTargetI += insTar; totalActualI += cappedI;
                    totalTargetS += stTar; totalActualS += cappedST;
                    totalTargetO += obsTar; totalActualO += cappedO;
                    totalTargetC += cTar; totalActualC += cappedC;
                }

                // Subcon calculations
                foreach (var sub in subcons)
                {
                    var subKaryawans = allGroupKaryawans.Where(k => k.IdPerusahaan == sub.PerusahaanId).ToList();
                    int subTargetH = 0, subActualH = 0;
                    int subTargetI = 0, subActualI = 0;
                    int subTargetS = 0, subActualS = 0;
                    int subTargetO = 0, subActualO = 0;
                    int subTargetC = 0, subActualC = 0;
                    int subEmpsWithTarget = 0;

                    foreach (var emp in subKaryawans)
                    {
                        var nik = (emp.NoNik ?? string.Empty).Trim();
                        int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0;
                        if (allGroupTargets.TryGetValue(emp.IdKaryawan, out var t))
                        {
                            hTar = t.TargetHazardReport ?? 0;
                            insTar = t.TargetInspeksi ?? 0;
                            stTar = t.TargetSafetyTalk ?? 0;
                            obsTar = t.TargetObservasi ?? 0;
                            cTar = t.TargetCoaching ?? 0;
                        }

                        if (hTar + insTar + stTar + obsTar + cTar == 0)
                        {
                            continue;
                        }

                        subEmpsWithTarget++;

                        int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                        int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                        int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                        int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                        int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                        int cappedH = Math.Min(actH, hTar);
                        int cappedI = Math.Min(actI, insTar);
                        int cappedST = Math.Min(actST, stTar);
                        int cappedO = Math.Min(actO, obsTar);
                        int cappedC = Math.Min(actC, cTar);

                        subTargetH += hTar; subActualH += cappedH;
                        subTargetI += insTar; subActualI += cappedI;
                        subTargetS += stTar; subActualS += cappedST;
                        subTargetO += obsTar; subActualO += cappedO;
                        subTargetC += cTar; subActualC += cappedC;
                    }

                    int subTargetTotal = subTargetH + subTargetI + subTargetS + subTargetO + subTargetC;
                    int subActualTotal = subActualH + subActualI + subActualS + subActualO + subActualC;

                    int subRawSubmissions = 
                        allGroupHazards.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupInspections.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupSafetyTalks.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupCoachings.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupObservations.Count(x => x.PerusahaanId == sub.PerusahaanId);

                    if (subEmpsWithTarget > 0)
                    {
                        allSubconStats.Add(new MostActiveSubconViewModel
                        {
                            PerusahaanId = sub.PerusahaanId,
                            PerusahaanName = sub.NamaPerusahaan ?? "Unknown",
                            ParentCompanyName = mcon.NamaPerusahaan ?? "Unknown",
                            TotalEmployees = subKaryawans.Count,
                            EmployeesWithTarget = subEmpsWithTarget,
                            ComplianceRate = subTargetTotal > 0 ? Math.Round((double)subActualTotal / subTargetTotal * 100.0, 1) : 0,
                            TotalSubmissions = subActualTotal,
                            TargetSubmissions = subTargetTotal
                        });
                    }
                }

                int totalGroupTarget = totalTargetH + totalTargetI + totalTargetS + totalTargetO + totalTargetC;
                int totalGroupActual = totalActualH + totalActualI + totalActualS + totalActualO + totalActualC;

                var uncompliantSubs = new List<string>();   // punya target tapi belum ada submisi
                var noTargetSubs = new List<string>();        // tidak ada karyawan ber-target sama sekali
                foreach (var sub in subcons)
                {
                    var subKarIds = allGroupKaryawans.Where(k => k.IdPerusahaan == sub.PerusahaanId).Select(k => k.IdKaryawan).ToList();
                    bool hasTarget = subKarIds.Any(id => allGroupTargets.TryGetValue(id, out var t)
                        && (t.TargetHazardReport > 0 || t.TargetInspeksi > 0 || t.TargetSafetyTalk > 0 || t.TargetObservasi > 0 || t.TargetCoaching > 0));

                    if (!hasTarget)
                    {
                        // Tidak ada karyawan dengan target — tidak relevan untuk compliance
                        noTargetSubs.Add(sub.NamaPerusahaan ?? "Unknown");
                        continue;
                    }

                    int subActualCount = 
                        allGroupHazards.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupInspections.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupSafetyTalks.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupCoachings.Count(x => x.PerusahaanId == sub.PerusahaanId) +
                        allGroupObservations.Count(x => x.PerusahaanId == sub.PerusahaanId);

                    if (subActualCount == 0)
                    {
                        uncompliantSubs.Add(sub.NamaPerusahaan ?? "Unknown");
                    }
                }

                var compVm = new MainconGroupComparisonViewModel
                {
                    MainconId = mcon.PerusahaanId,
                    MainconName = mcon.NamaPerusahaan ?? "Unknown",
                    TotalEmployees = totalGroupEmployees,
                    EmployeesWithTargetCount = employeesWithTargetCount,
                    ChildCompanyNames = subcons.Select(s => s.NamaPerusahaan ?? "Unknown").ToList(),
                    UncompliantChildCompanyNames = uncompliantSubs,
                    NoTargetChildCompanyNames = noTargetSubs,
                    OverallComplianceRate = totalGroupTarget > 0 ? Math.Round((double)totalGroupActual / totalGroupTarget * 100.0, 1) : 0,
                    HazardComplianceRate = totalTargetH > 0 ? Math.Round((double)totalActualH / totalTargetH * 100.0, 1) : 0,
                    InspeksiComplianceRate = totalTargetI > 0 ? Math.Round((double)totalActualI / totalTargetI * 100.0, 1) : 0,
                    SafetyTalkComplianceRate = totalTargetS > 0 ? Math.Round((double)totalActualS / totalTargetS * 100.0, 1) : 0,
                    ObservasiComplianceRate = totalTargetO > 0 ? Math.Round((double)totalActualO / totalTargetO * 100.0, 1) : 0,
                    CoachingComplianceRate = totalTargetC > 0 ? Math.Round((double)totalActualC / totalTargetC * 100.0, 1) : 0,
                    
                    TargetHazard = totalTargetH, ActualHazard = totalActualH,
                    TargetInspeksi = totalTargetI, ActualInspeksi = totalActualI,
                    TargetSafetyTalk = totalTargetS, ActualSafetyTalk = totalActualS,
                    TargetObservasi = totalTargetO, ActualObservasi = totalActualO,
                    TargetCoaching = totalTargetC, ActualCoaching = totalActualC
                };

                mainconGroupComparisonList.Add(compVm);
            }

            var orderedSubcons = allSubconStats.OrderByDescending(s => s.ComplianceRate).ThenByDescending(s => s.TotalSubmissions).ToList();
            ViewBag.MostActiveSubcon = orderedSubcons.FirstOrDefault();
            ViewBag.AllSubconStats = orderedSubcons;
            ViewBag.Top10BestSubcons = orderedSubcons.Take(10).ToList();
            ViewBag.Top10OverAchieverSubcons = allSubconStats
                .Where(s => s.ComplianceRate >= 100 && s.TotalSubmissions > s.TargetSubmissions)
                .OrderByDescending(s => (s.TotalSubmissions - s.TargetSubmissions))
                .Take(10)
                .ToList();
            ViewBag.MainconGroupComparison = mainconGroupComparisonList;

            // 5. Daily Awareness/Submission Trend (last 14 days)
            var last14Days = Enumerable.Range(0, 14)
                .Select(i => DateTime.Today.AddDays(-i))
                .OrderBy(d => d)
                .ToList();
            var startDate = last14Days.First();

            var dailyHazards = await _context.HazardReports
                .Where(h => !h.IsDeleted && h.Tanggal >= startDate)
                .GroupBy(h => h.Tanggal.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var dailyInspections = await _context.Inspections
                .Where(i => !i.IsDeleted && i.Tanggal >= startDate)
                .GroupBy(i => i.Tanggal.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var dailySafetyTalks = await _context.SafetyTalks
                .Where(s => !s.IsDeleted && s.Tanggal >= startDate)
                .GroupBy(s => s.Tanggal.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var dailyObservations = await _context.Observations
                .Where(o => !o.IsDeleted && o.CreatedAt >= startDate)
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var dailyCoachings = await _context.Coachings
                .Where(c => !c.IsDeleted && c.CreatedAt >= startDate)
                .GroupBy(c => c.CreatedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var dailyTrendLabels = new List<string>();
            var dailyTrendValues = new List<int>();

            foreach (var date in last14Days)
            {
                dailyTrendLabels.Add(date.ToString("dd MMM"));
                int sum = 
                    (dailyHazards.FirstOrDefault(d => d.Date == date)?.Count ?? 0) +
                    (dailyInspections.FirstOrDefault(d => d.Date == date)?.Count ?? 0) +
                    (dailySafetyTalks.FirstOrDefault(d => d.Date == date)?.Count ?? 0) +
                    (dailyObservations.FirstOrDefault(d => d.Date == date)?.Count ?? 0) +
                    (dailyCoachings.FirstOrDefault(d => d.Date == date)?.Count ?? 0);
                dailyTrendValues.Add(sum);
            }

            ViewBag.DailyAwarenessTrendLabels = dailyTrendLabels;
            ViewBag.DailyAwarenessTrendValues = dailyTrendValues;

            // Calculate Active Submitting Employees and Awareness Rate optimally
            int activeEmployees = fullyCompliantCount + participatingCount;
            int totalEmpWithTarget = fullyCompliantCount + participatingCount + inactiveCount;
            double employeeAwarenessRate = totalEmpWithTarget > 0 ? 
                Math.Round((double)activeEmployees / totalEmpWithTarget * 100.0, 1) : 0.0;

            ViewBag.ActiveEmployeesCount = activeEmployees;
            ViewBag.TotalEmployeesWithTarget = totalEmpWithTarget;
            ViewBag.EmployeeAwarenessRate = employeeAwarenessRate;
            // 6. Companies that have never logged in
            var loggedInCompanyIds = await _context.AppUsers
                .Where(u => u.IdPerusahaan.HasValue)
                .Select(u => u.IdPerusahaan!.Value)
                .Distinct()
                .ToListAsync();

            var companiesWithTargets = await _context.KaryawanJabatanMappings
                .Where(m => (m.TargetHazardReport ?? 0) > 0 || 
                            (m.TargetInspeksi ?? 0) > 0 || 
                            (m.TargetSafetyTalk ?? 0) > 0 || 
                            (m.TargetObservasi ?? 0) > 0 || 
                            (m.TargetCoaching ?? 0) > 0)
                .Select(m => m.PerusahaanId)
                .Distinct()
                .ToListAsync();

            var scopedCompanyIds = new HashSet<int> { selectedCompanyId };
            var childIdsScope = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                .Where(r => r.ParentCompanyId == selectedCompanyId && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                .Select(r => r.ChildCompanyId!.Value)
                .ToListAsync();
            foreach (var id in childIdsScope) scopedCompanyIds.Add(id);
            var directChildIds = await _context.Perusahaans.AsNoTracking()
                .Where(p => p.PerusahaanIndukId == selectedCompanyId && p.StatusAktif)
                .Select(p => p.PerusahaanId)
                .ToListAsync();
            foreach (var id in directChildIds) scopedCompanyIds.Add(id);
            
            // Untuk PT INDEXIM, jangan hitung promoted maincons di scopenya
            if (selectedCompanyId == 1)
            {
                scopedCompanyIds.ExceptWith(promotedMainconIds);
            }

            var scopedCompanies = allCompanies.Where(c => scopedCompanyIds.Contains(c.PerusahaanId)).ToList();

            var evalNeverLoggedInCompanies = companyId.HasValue ? scopedCompanies : allCompanies;
            var neverLoggedInCompanies = evalNeverLoggedInCompanies
                .Where(p => !loggedInCompanyIds.Contains(p.PerusahaanId) && companiesWithTargets.Contains(p.PerusahaanId))
                .ToList();

            var relationsForNeverLoggedIn = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

            var neverLoggedInGrouped = neverLoggedInCompanies
                .GroupBy(p => {
                    // Try direct lookup in allCompanies
                    var parent = allCompanies.FirstOrDefault(parentComp => parentComp.PerusahaanId == p.PerusahaanIndukId);
                    if (parent != null && !string.IsNullOrEmpty(parent.NamaPerusahaan))
                    {
                        return parent.NamaPerusahaan;
                    }
                    
                    // Try lookup in hierarchy relations
                    var rel = relationsForNeverLoggedIn.FirstOrDefault(r => r.ChildCompanyId == p.PerusahaanId && !string.IsNullOrEmpty(r.ParentCompanyName));
                    if (rel != null && !string.IsNullOrEmpty(rel.ParentCompanyName))
                    {
                        return rel.ParentCompanyName;
                    }

                    // Fallback to database lookup for PerusahaanIndukId
                    if (p.PerusahaanIndukId.HasValue && p.PerusahaanIndukId.Value > 0)
                    {
                        var dbParent = _context.Perusahaans.FirstOrDefault(dbP => dbP.PerusahaanId == p.PerusahaanIndukId.Value);
                        if (dbParent != null && !string.IsNullOrEmpty(dbParent.NamaPerusahaan))
                        {
                            return dbParent.NamaPerusahaan;
                        }
                    }

                    return "Independent / Maincon";
                })
                .Select(g => new {
                    ParentName = g.Key,
                    Companies = g.OrderBy(c => c.NamaPerusahaan).ToList()
                })
                .OrderBy(g => g.ParentName)
                .ToList<dynamic>();

            ViewBag.NeverLoggedInCompanies = neverLoggedInGrouped;

            // 7. Top 10 Best Performance Companies
            var hazardCounts = groupHazards.Where(h => h.PerusahaanId.HasValue).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var closedHazardCounts = groupHazards.Where(h => h.PerusahaanId.HasValue && string.Equals(h.StatusTemuan, "Closed", StringComparison.OrdinalIgnoreCase)).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var inspCounts = groupInspections.Where(h => h.PerusahaanId.HasValue).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var stCounts = groupSafetyTalks.Where(h => h.PerusahaanId.HasValue).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var obsCounts = groupObservations.Where(h => h.PerusahaanId.HasValue).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var p5mCounts = groupP5ms.Where(h => h.PerusahaanId.HasValue).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var coaCounts = groupCoachings.Where(h => h.PerusahaanId.HasValue).GroupBy(h => h.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.Count());

            var actionPlansMonth = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.PerusahaanId != null && a.CreatedAt >= startOfMonthM && a.CreatedAt <= endOfMonthM)
                .Select(a => new { a.PerusahaanId, a.Status, a.Tanggal, a.TanggalPerbaikan })
                .ToListAsync();
            
            var apGroups = actionPlansMonth.GroupBy(a => a.PerusahaanId!.Value).ToDictionary(g => g.Key, g => g.ToList());

            var targetDict = await _context.KaryawanJabatanMappings
                .Where(m => m.PerusahaanId != null)
                .GroupBy(m => m.PerusahaanId!.Value)
                .Select(g => new { Key = g.Key, Tgt = g.Sum(x => (x.TargetHazardReport ?? 2) + (x.TargetInspeksi ?? 1) + (x.TargetSafetyTalk ?? 1) + (x.TargetObservasi ?? 0) + (x.TargetCoaching ?? 0)) })
                .ToDictionaryAsync(x => x.Key, x => x.Tgt);

            var performanceList = new List<CompanyPerformanceViewModel>();
            int maxKuantitas = 1;
            int maxTargetAll = 1;
            foreach (var kv in targetDict) { if (kv.Value > maxTargetAll) maxTargetAll = kv.Value; }

            // Gunakan daftar perusahaan yang sesuai scope filter (termasuk dirinya dan subcon-nya)
            // Namun jika tidak ada companyId yang difilter (misal default admin), scopedCompanies berisi Indexim dan anak-anaknya.
            // Untuk memastikan Top 10 Best Performance berjalan global jika tidak difilter, kita cek apakah companyId.HasValue
            var rankingCompanies = companyId.HasValue ? scopedCompanies : allCompanies;

            foreach (var comp in rankingCompanies)
            {
                int cId = comp.PerusahaanId;
                int hC = hazardCounts.TryGetValue(cId, out int v1) ? v1 : 0;
                int hCClosed = closedHazardCounts.TryGetValue(cId, out int v7) ? v7 : 0;
                int iC = inspCounts.TryGetValue(cId, out int v2) ? v2 : 0;
                int sC = stCounts.TryGetValue(cId, out int v3) ? v3 : 0;
                int oC = obsCounts.TryGetValue(cId, out int v4) ? v4 : 0;
                int pC = p5mCounts.TryGetValue(cId, out int v5) ? v5 : 0;
                int coaC = coaCounts.TryGetValue(cId, out int v6) ? v6 : 0;
                
                int totalTemuan = hC + iC + sC + oC + pC + coaC;
                int totalTarget = targetDict.TryGetValue(cId, out int tgt) ? tgt : 0;
                
                // Batasi total temuan aktual dengan target maksimal agar persentase tidak bocor di atas 100% (contoh: 3233%)
                totalTemuan = Math.Min(totalTemuan, totalTarget);
                
                if (totalTemuan > maxKuantitas) maxKuantitas = totalTemuan;

                int totalAp = 0;
                int closedAp = 0;
                double sumDays = 0;
                int apWithDays = 0;

                if (apGroups.TryGetValue(cId, out var apList))
                {
                    totalAp = apList.Count;
                    foreach (var ap in apList)
                    {
                        if (ap.Status == "Closed")
                        {
                            closedAp++;
                            if (ap.TanggalPerbaikan.HasValue)
                            {
                                var diff = (ap.TanggalPerbaikan.Value - ap.Tanggal).TotalDays;
                                sumDays += Math.Max(0, diff);
                                apWithDays++;
                            }
                        }
                    }
                }

                double closeRate = hC > 0 ? (double)hCClosed / hC * 100 : (totalTemuan > 0 ? 100 : 0);
                double avgSpeed = apWithDays > 0 ? sumDays / apWithDays : (closedAp > 0 ? 1 : 14); // default speed

                if (totalTemuan > 0)
                {
                    performanceList.Add(new CompanyPerformanceViewModel
                    {
                        PerusahaanId = cId,
                        CompanyName = comp.NamaPerusahaan ?? "Unknown",
                        TotalTemuan = totalTemuan,
                        TotalTarget = totalTarget,
                        TotalHazard = hC,
                        TotalClosedHazard = hCClosed,
                        TotalActionPlan = totalAp,
                        TotalClosedActionPlan = closedAp,
                        CloseRate = closeRate,
                        AvgSpeedDays = avgSpeed,
                        AvgQuality = 5.0 // Default quality since SapQualityAssessment doesn't link to PerusahaanId easily
                    });
                }
            }

            foreach (var p in performanceList)
            {
                p.ScorePencapaian = p.TotalTarget > 0 ? Math.Min(100, ((double)p.TotalTemuan / p.TotalTarget) * 100) : (p.TotalTemuan > 0 ? 100 : 0);
                p.ScoreSkalaBeban = maxTargetAll > 0 ? (Math.Log10(p.TotalTarget + 1) / Math.Log10(maxTargetAll + 1)) * 100 : 0;
                p.ScoreCloseRate = p.CloseRate;
                p.ScoreKualitas = (p.AvgQuality / 5.0) * 100;
                
                // Speed Score: 0 days = 100%, >= 14 days = 0%
                p.ScoreKecepatan = Math.Max(0, 100 - (p.AvgSpeedDays / 14.0 * 100));

                p.TotalScore = (p.ScorePencapaian * 0.20) + (p.ScoreSkalaBeban * 0.15) + (p.ScoreCloseRate * 0.25) + (p.ScoreKualitas * 0.20) + (p.ScoreKecepatan * 0.20);
            }

            ViewBag.TopPerformanceList = performanceList.OrderByDescending(p => p.TotalScore).Take(10).ToList();

            return View(new List<ComplianceEmployeeViewModel>());
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetGeoSafetyRadar(string? area = null, int? year = null, int? month = null)
        {
            var (mapCompanyId, mapAllowedCompanyIds) = await ResolveMapCompanyScopeAsync();
            var geoSafetyData = await BuildGeoSafetyRadarDataAsync(mapCompanyId, mapAllowedCompanyIds, area?.Trim(), true, year, month);

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
                normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "/ImageProxy/Get?url=" + Uri.EscapeDataString(normalized);
            }
            if (normalized.StartsWith("/", StringComparison.Ordinal))
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
            var hazards = _context.HazardReports.Where(h => !h.IsDeleted && (companyId == null || h.PerusahaanId == companyId) && h.Tanggal >= targetStart);
            var inspections = _context.Inspections.Where(i => !i.IsDeleted && (companyId == null || i.PerusahaanId == companyId) && i.Tanggal >= targetStart);
            var safetyTalks = _context.SafetyTalks.Where(s => !s.IsDeleted && (companyId == null || s.PerusahaanId == companyId) && s.Tanggal >= targetStart);
            var p5ms = _context.P5ms.Where(p => !p.IsDeleted && (companyId == null || p.PerusahaanId == companyId) && p.Tanggal >= targetStart);
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
        public int TotalTarget { get; set; }
        public double YtdAchievementRate { get; set; }
        public double MtdAchievementRate { get; set; }
        public double WeeklyAchievementRate { get; set; }

        public double YtdHazardRate { get; set; }
        public double YtdInspeksiRate { get; set; }
        public double YtdSafetyTalkRate { get; set; }
        public double YtdP5mRate { get; set; }
        public double YtdObservasiRate { get; set; }
        public double YtdCoachingRate { get; set; }

        public double MtdHazardRate { get; set; }
        public double MtdInspeksiRate { get; set; }
        public double MtdSafetyTalkRate { get; set; }
        public double MtdP5mRate { get; set; }
        public double MtdObservasiRate { get; set; }
        public double MtdCoachingRate { get; set; }
    }

    public class ComplianceEmployeeViewModel
    {
        public string KaryawanName { get; set; } = string.Empty;
        public string Nik { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string JabatanName { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public int MtdTotalTarget { get; set; }
        public int MtdTotalActual { get; set; }
        public double ComplianceRate { get; set; }
        public ComplianceItemDetail Hazard { get; set; } = new();
        public ComplianceItemDetail Inspeksi { get; set; } = new();
        public ComplianceItemDetail SafetyTalk { get; set; } = new();
        public ComplianceItemDetail Observasi { get; set; } = new();
        public ComplianceItemDetail Coaching { get; set; } = new();
        public ComplianceItemDetail P5m { get; set; } = new();
    }

    public class ComplianceItemDetail
    {
        public int Target { get; set; }
        public int Actual { get; set; }
    }

    public class ComplianceGroupStatViewModel
    {
        public string GroupName { get; set; } = string.Empty;
        public int TotalCreated { get; set; }
        public int OpenCount { get; set; }
        public int ClosedCount { get; set; }
        public double ClosureRate => TotalCreated > 0 ? Math.Round((double)ClosedCount / TotalCreated * 100.0, 1) : 0;
    }

    public class MainconGroupComparisonViewModel
    {
        public int MainconId { get; set; }
        public string MainconName { get; set; } = string.Empty;
        public int TotalEmployees { get; set; }
        public int EmployeesWithTargetCount { get; set; }
        public List<string> ChildCompanyNames { get; set; } = new();
        public List<string> UncompliantChildCompanyNames { get; set; } = new();
        public List<string> NoTargetChildCompanyNames { get; set; } = new();
        public double OverallComplianceRate { get; set; }
        public double HazardComplianceRate { get; set; }
        public double InspeksiComplianceRate { get; set; }
        public double SafetyTalkComplianceRate { get; set; }
        public double ObservasiComplianceRate { get; set; }
        public double CoachingComplianceRate { get; set; }

        public int TargetHazard { get; set; }
        public int ActualHazard { get; set; }
        public int TargetInspeksi { get; set; }
        public int ActualInspeksi { get; set; }
        public int TargetSafetyTalk { get; set; }
        public int ActualSafetyTalk { get; set; }
        public int TargetObservasi { get; set; }
        public int ActualObservasi { get; set; }
        public int TargetCoaching { get; set; }
        public int ActualCoaching { get; set; }
    }

    public class MostActiveSubconViewModel
    {
        public int PerusahaanId { get; set; }
        public string PerusahaanName { get; set; } = string.Empty;
        public string ParentCompanyName { get; set; } = string.Empty;
        public int TotalEmployees { get; set; }
        public int EmployeesWithTarget { get; set; }
        public double ComplianceRate { get; set; }
        public int TotalSubmissions { get; set; }
        public int TargetSubmissions { get; set; }
    }

    public class MainconPerformanceReportViewModel
    {
        public int MainconId { get; set; }
        public string MainconName { get; set; } = string.Empty;
        public int TotalEmployees { get; set; }
        public int EmployeesWithTarget { get; set; }
        public double OverallComplianceRate { get; set; }
        public int TotalSubmissions { get; set; }
        
        public double HazardRate { get; set; }
        public double InspeksiRate { get; set; }
        public double SafetyTalkRate { get; set; }
        public double ObservasiRate { get; set; }
        public double CoachingRate { get; set; }

        public List<SubconPerformanceViewModel> SubconPerformances { get; set; } = new();
    }

    public class SubconPerformanceViewModel
    {
        public int PerusahaanId { get; set; }
        public string PerusahaanName { get; set; } = string.Empty;
        public bool IsMaincon { get; set; }
        public int TotalEmployees { get; set; }
        public int EmployeesWithTarget { get; set; }
        public double OverallComplianceRate { get; set; }
        public int TotalSubmissions { get; set; }

        public int ActualHazard { get; set; }
        public int TargetHazard { get; set; }
        public int ActualInspeksi { get; set; }
        public int TargetInspeksi { get; set; }
        public int ActualSafetyTalk { get; set; }
        public int TargetSafetyTalk { get; set; }
        public int ActualObservasi { get; set; }
        public int TargetObservasi { get; set; }
        public int ActualCoaching { get; set; }
        public int TargetCoaching { get; set; }
    }
}
