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

            if (isAdmin)
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

            var result = await GetEmployeesComplianceData(companyId, departmentName, null, null, scopeCompanyId);
            return Json(result);
        }

        private async Task<List<dynamic>> GetEmployeesComplianceData(int companyId, string? departmentNameFilter = null, int? year = null, int? month = null, int? parentIdFilter = null)
        {
            var selectedYear = year ?? DateTime.Today.Year;
            var selectedMonth = month ?? DateTime.Today.Month;
            
            var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"EmployeesComplianceData_{companyId}_{departmentNameFilter ?? "All"}_{selectedYear}_{selectedMonth}_{parentIdFilter ?? 0}";
            
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
                                      join j in _context.Jabatans on k.IdJabatan equals j.JabatanId into jg
                                      from j in jg.DefaultIfEmpty()
                                      where k.IdPerusahaan == companyId && k.StatusAktif == true
                                      select new {
                                          k.IdKaryawan,
                                          k.NoNik,
                                          NamaLengkap = p.NamaLengkap,
                                          NamaDepartemen = d != null ? d.NamaDepartemen : "General",
                                          NamaJabatan = j != null ? j.NamaJabatan : "Staff/Operator",
                                          PerusahaanNodeId = k.PerusahaanNodeId
                                      }).ToListAsync();

            if (parentIdFilter.HasValue && parentIdFilter.Value > 0)
            {
                var allCompanies = await _context.Perusahaans.AsNoTracking().ToListAsync();
                var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

                var currentCompany = allCompanies.FirstOrDefault(c => c.PerusahaanId == companyId);
                if (currentCompany != null)
                {
                    var parents = new HashSet<int>();
                    if (currentCompany.PerusahaanIndukId.HasValue && currentCompany.PerusahaanIndukId.Value > 0)
                    {
                        parents.Add(currentCompany.PerusahaanIndukId.Value);
                    }
                    var relParents = relations
                        .Where(r => r.ChildCompanyId == companyId && r.ParentCompanyId.HasValue && r.ParentIsActive == true)
                        .Select(r => r.ParentCompanyId!.Value);
                    foreach (var pId in relParents)
                    {
                        parents.Add(pId);
                    }

                    if (parents.Count > 1 && parentIdFilter.Value != companyId)
                    {
                        var relasiIds = relations
                            .Where(r => r.ChildCompanyId == companyId && r.ParentCompanyId == parentIdFilter.Value)
                            .Select(r => r.RelasiId)
                            .Where(id => id.HasValue)
                            .Select(id => id!.Value)
                            .ToList();

                        var allowedNodeIds = new HashSet<int> { parentIdFilter.Value };
                        foreach (var relId in relasiIds)
                        {
                            allowedNodeIds.Add(relId);
                        }

                        deptKaryawans = deptKaryawans
                            .Where(k => k.PerusahaanNodeId.HasValue && allowedNodeIds.Contains(k.PerusahaanNodeId.Value))
                            .ToList();
                    }
                }
            }

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
                var employeeNiksSet = new HashSet<string>(employeeNiks, StringComparer.OrdinalIgnoreCase);
                var reqCacheKey = $"MonthlyData_{selectedYear}_{selectedMonth}";

                List<string> dbHazards, dbInspections, dbSafetyTalks, dbP5ms, allCoachings, dbObservations;

                if (HttpContext.Items[reqCacheKey] is Tuple<List<string>, List<string>, List<string>, List<string>, List<string>, List<string>> cachedData)
                {
                    dbHazards = cachedData.Item1;
                    dbInspections = cachedData.Item2;
                    dbSafetyTalks = cachedData.Item3;
                    dbP5ms = cachedData.Item4;
                    allCoachings = cachedData.Item5;
                    dbObservations = cachedData.Item6;
                }
                else
                {
                    dbHazards = await _context.HazardReports
                        .Where(h => !h.IsDeleted && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth)
                        .Select(h => h.Nik)
                        .ToListAsync();

                    dbInspections = await _context.Inspections
                        .Where(i => !i.IsDeleted && i.Tanggal >= startOfMonth && i.Tanggal <= endOfMonth)
                        .Select(i => i.Nik)
                        .ToListAsync();

                    dbSafetyTalks = await _context.SafetyTalks
                        .Where(s => !s.IsDeleted && s.Tanggal >= startOfMonth && s.Tanggal <= endOfMonth)
                        .Select(s => s.Nik)
                        .ToListAsync();

                    dbP5ms = await _context.P5ms
                        .Where(p => !p.IsDeleted && p.Tanggal >= startOfMonth && p.Tanggal <= endOfMonth)
                        .Select(p => p.Nik)
                        .ToListAsync();

                    var coachingCreators = await _context.Coachings
                        .Where(c => !c.IsDeleted && c.CreatedAt >= startOfMonth && c.CreatedAt <= endOfMonth)
                        .Select(c => c.Nik)
                        .ToListAsync();

                    var coachingParticipants = await _context.CoachingParticipants
                        .Where(p => p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfMonth && p.Coaching.CreatedAt <= endOfMonth)
                        .Select(p => p.Nik)
                        .ToListAsync();

                    allCoachings = coachingCreators.Concat(coachingParticipants).Where(n => n != null).ToList();

                    dbObservations = await _context.Observations
                        .Where(o => !o.IsDeleted && o.CreatedAt >= startOfMonth && o.CreatedAt <= endOfMonth)
                        .Select(o => o.Nik)
                        .ToListAsync();

                    HttpContext.Items[reqCacheKey] = Tuple.Create(dbHazards, dbInspections, dbSafetyTalks, dbP5ms, allCoachings, dbObservations);
                }

                hazards = dbHazards.Where(n => n != null && employeeNiksSet.Contains(n)).ToList();
                inspections = dbInspections.Where(n => n != null && employeeNiksSet.Contains(n)).ToList();
                safetyTalks = dbSafetyTalks.Where(n => n != null && employeeNiksSet.Contains(n)).ToList();
                p5ms = dbP5ms.Where(n => n != null && employeeNiksSet.Contains(n)).ToList();
                coachings = allCoachings.Where(n => n != null && employeeNiksSet.Contains(n)).ToList();
                observations = dbObservations.Where(n => n != null && employeeNiksSet.Contains(n)).ToList();
            }

            var rosters = await _context.Rosters
                .Where(r => employeeNiks.Contains(r.Nik))
                .ToListAsync();
            var rostersByNik = rosters
                .GroupBy(r => r.Nik, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

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

                int totalDaysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);
                int onsiteDays = totalDaysInMonth; // default if no roster setting
                bool hasRoster = false;

                if (rostersByNik.TryGetValue(nik, out var empRosters))
                {
                    int computedOnsite = 0;
                    foreach (var r in empRosters)
                    {
                        var overlapStart = r.AwalDinas > startOfMonth ? r.AwalDinas : startOfMonth;
                        var overlapEnd = r.AkhirDinas < endOfMonth ? r.AkhirDinas : endOfMonth;
                        if (overlapStart <= overlapEnd)
                        {
                            computedOnsite += (overlapEnd - overlapStart).Days + 1;
                        }
                    }
                    if (computedOnsite > 0)
                    {
                        hasRoster = true;
                        onsiteDays = computedOnsite;
                    }
                }

                double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonth : 1.0;

                int ScaleTarget(int baseTarget, double rat, int daysOnsite)
                {
                    if (baseTarget == 0) return 0;
                    if (daysOnsite == 0) return 0;
                    int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                    return Math.Max(scaled, 1);
                }

                int mtdTgtH = hasRoster ? ScaleTarget(hTar, ratio, onsiteDays) : hTar;
                int mtdTgtI = hasRoster ? ScaleTarget(insTar, ratio, onsiteDays) : insTar;
                int mtdTgtST = hasRoster ? ScaleTarget(stTar, ratio, onsiteDays) : stTar;
                int mtdTgtO = hasRoster ? ScaleTarget(obsTar, ratio, onsiteDays) : obsTar;
                int mtdTgtC = hasRoster ? ScaleTarget(cTar, ratio, onsiteDays) : cTar;
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
                    onsiteDays = onsiteDays,
                    hasRoster = hasRoster,
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
        public async Task<IActionResult> GetCompanyAnalyticsData(int? year = null, int? month = null)
        {
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;

            var (scopeCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"CompanyAnalytics_{scopeCompanyId}_{selectedYear}_{selectedMonth}";

            bool forceRefresh = HttpContext.Request.Query.ContainsKey("refresh") &&
                                string.Equals(HttpContext.Request.Query["refresh"], "true", StringComparison.OrdinalIgnoreCase);

            if (!forceRefresh && cache.TryGetValue(cacheKey, out object? cachedResult) && cachedResult != null)
            {
                return Json(cachedResult);
            }

            await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
            var startOfYear = new DateTime(selectedYear, 1, 1);
            var endOfYear = new DateTime(selectedYear, 12, 31, 23, 59, 59);
            var trendStart = startOfMonth.AddMonths(-5);
            var baseStartDate = trendStart < startOfYear ? trendStart : startOfYear;

            int elapsedWeeksYtd = Math.Max(1, ((today < endOfMonth ? today : endOfMonth) - startOfYear.Date).Days / 7 + 1);

            // Companies
            var allCompanies = await _context.Perusahaans.AsNoTracking()
                .Where(p => p.StatusAktif && !ExcludedCompanies.Ids.Contains(p.PerusahaanId))
                .ToListAsync();

            var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

            // Active Karyawans
            var allKaryawans = await _context.Karyawans.AsNoTracking()
                .Where(k => k.StatusAktif && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan))
                .ToListAsync();

            var activeKaryawanIds = allKaryawans.Select(k => k.IdKaryawan).ToList();
            var targetMappings = await _context.KaryawanJabatanMappings.AsNoTracking()
                .Where(m => activeKaryawanIds.Contains(m.KaryawanId))
                .ToListAsync();
            var mappingsDict = targetMappings.ToDictionary(m => m.KaryawanId);

            var activeNiks = allKaryawans.Select(k => (k.NoNik ?? string.Empty).Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            var activeRosters = await _context.Rosters.AsNoTracking()
                .Where(r => activeNiks.Contains(r.Nik))
                .ToListAsync();
            var rostersByNik = activeRosters
                .GroupBy(r => r.Nik.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            int totalDaysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);

            int ScaleTarget(int baseTarget, double rat, int daysOnsite)
            {
                if (baseTarget == 0) return 0;
                if (daysOnsite == 0) return 0;
                int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                return Math.Max(scaled, 1);
            }

            var employeeTargets = new Dictionary<string, (int hTar, int insTar, int stTar, int obsTar, int cTar, int p5mTar, int totalMtd, int totalYtd, int wH, int wI, int wST, int wO, int wC)>(StringComparer.OrdinalIgnoreCase);

            foreach (var emp in allKaryawans)
            {
                var nik = (emp.NoNik ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(nik)) continue;

                int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0, p5mTar = 1;
                if (mappingsDict.TryGetValue(emp.IdKaryawan, out var m))
                {
                    hTar = m.TargetHazardReport ?? 2;
                    insTar = m.TargetInspeksi ?? 1;
                    stTar = m.TargetSafetyTalk ?? 1;
                    obsTar = m.TargetObservasi ?? 0;
                    cTar = m.TargetCoaching ?? 0;
                }

                int onsiteDays = totalDaysInMonth;
                bool hasRoster = false;
                if (rostersByNik.TryGetValue(nik, out var empRosters))
                {
                    int computedOnsite = 0;
                    foreach (var r in empRosters)
                    {
                        var overlapStart = r.AwalDinas > startOfMonth ? r.AwalDinas : startOfMonth;
                        var overlapEnd = r.AkhirDinas < endOfMonth ? r.AkhirDinas : endOfMonth;
                        if (overlapStart <= overlapEnd)
                        {
                            computedOnsite += (overlapEnd - overlapStart).Days + 1;
                        }
                    }
                    if (computedOnsite > 0)
                    {
                        hasRoster = true;
                        onsiteDays = computedOnsite;
                    }
                }

                double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonth : 1.0;
                int mH = hasRoster ? ScaleTarget(hTar, ratio, onsiteDays) : hTar;
                int mI = hasRoster ? ScaleTarget(insTar, ratio, onsiteDays) : insTar;
                int mST = hasRoster ? ScaleTarget(stTar, ratio, onsiteDays) : stTar;
                int mO = hasRoster ? ScaleTarget(obsTar, ratio, onsiteDays) : obsTar;
                int mC = hasRoster ? ScaleTarget(cTar, ratio, onsiteDays) : cTar;

                int wH = hTar > 0 ? Math.Max(1, (int)Math.Round(hTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wI = insTar > 0 ? Math.Max(1, (int)Math.Round(insTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wST = stTar > 0 ? Math.Max(1, (int)Math.Round(stTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wO = obsTar > 0 ? Math.Max(1, (int)Math.Round(obsTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wC = cTar > 0 ? Math.Max(1, (int)Math.Round(cTar / 4.0, MidpointRounding.AwayFromZero)) : 0;

                int totalMtd = mH + mI + mST + mO + mC;
                int totalYtd = (wH + wI + wST + wO + wC) * elapsedWeeksYtd;

                employeeTargets[nik] = (mH, mI, mST, mO, mC, p5mTar, totalMtd, totalYtd, wH, wI, wST, wO, wC);
            }

            // Fetch submissions from baseStartDate
            var dbHazards = await _context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && h.PerusahaanId.HasValue && h.Tanggal >= baseStartDate && h.Tanggal <= endOfYear && !ExcludedCompanies.Ids.Contains(h.PerusahaanId!.Value))
                .Select(h => new { CompId = h.PerusahaanId!.Value, Nik = h.Nik.Trim(), Date = h.Tanggal, KategoriBahaya = h.KategoriBahaya })
                .ToListAsync();

            bool IsTtaCategory(string? cat) => cat != null && (cat.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || cat.Contains("Act", StringComparison.OrdinalIgnoreCase) || cat.Contains("TTA", StringComparison.OrdinalIgnoreCase));
            bool IsKtaCategory(string? cat) => cat != null && (cat.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || cat.Contains("Condition", StringComparison.OrdinalIgnoreCase) || cat.Contains("KTA", StringComparison.OrdinalIgnoreCase) || cat.Contains("KTC", StringComparison.OrdinalIgnoreCase));

            var dbInspections = await _context.Inspections.AsNoTracking()
                .Where(i => !i.IsDeleted && i.PerusahaanId.HasValue && i.Tanggal >= baseStartDate && i.Tanggal <= endOfYear && !ExcludedCompanies.Ids.Contains(i.PerusahaanId!.Value))
                .Select(i => new { CompId = i.PerusahaanId!.Value, Nik = i.Nik.Trim(), Date = i.Tanggal })
                .ToListAsync();

            var dbSafetyTalks = await _context.SafetyTalks.AsNoTracking()
                .Where(s => !s.IsDeleted && s.PerusahaanId.HasValue && s.Tanggal >= baseStartDate && s.Tanggal <= endOfYear && !ExcludedCompanies.Ids.Contains(s.PerusahaanId!.Value))
                .Select(s => new { CompId = s.PerusahaanId!.Value, Nik = s.Nik.Trim(), Date = s.Tanggal })
                .ToListAsync();

            var dbP5ms = await _context.P5ms.AsNoTracking()
                .Where(p => !p.IsDeleted && p.PerusahaanId.HasValue && p.Tanggal >= baseStartDate && p.Tanggal <= endOfYear && !ExcludedCompanies.Ids.Contains(p.PerusahaanId!.Value))
                .Select(p => new { CompId = p.PerusahaanId!.Value, Nik = p.Nik.Trim(), Date = p.Tanggal })
                .ToListAsync();

            var dbCoachings = await _context.Coachings.AsNoTracking()
                .Where(c => !c.IsDeleted && c.PerusahaanId.HasValue && c.CreatedAt >= baseStartDate && c.CreatedAt <= endOfYear && !ExcludedCompanies.Ids.Contains(c.PerusahaanId!.Value))
                .Select(c => new { CompId = c.PerusahaanId!.Value, Nik = c.Nik.Trim(), Date = c.CreatedAt })
                .ToListAsync();

            var dbObservations = await (from o in _context.Observations.AsNoTracking()
                                        join k in _context.Karyawans.AsNoTracking() on o.Nik equals k.NoNik
                                        where !o.IsDeleted && o.CreatedAt >= baseStartDate && o.CreatedAt <= endOfYear && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan)
                                        select new { CompId = k.IdPerusahaan, Nik = o.Nik.Trim(), Date = o.CreatedAt })
                                       .ToListAsync();

            // Determine Company Tier
            var parentCompanyIds = new HashSet<int>(allCompanies.Where(c => c.PerusahaanIndukId.HasValue && c.PerusahaanIndukId.Value > 0).Select(c => c.PerusahaanIndukId!.Value));
            foreach (var r in relations.Where(r => r.ParentCompanyId.HasValue && r.ParentCompanyId.Value > 0))
            {
                parentCompanyIds.Add(r.ParentCompanyId!.Value);
            }

            string DetermineTier(PerusahaanView c)
            {
                var name = (c.NamaPerusahaan ?? string.Empty).ToUpperInvariant();
                if (name.Contains("INDEXIM")) return "Owner";
                if (parentCompanyIds.Contains(c.PerusahaanId) || name.Contains("KALIMANTAN PRIMA PERSADA") || name.Contains("UNGGUL DINAMIKA") || name.Contains("MEGA GLOBAL") || name.Contains("GANESA"))
                {
                    return "Maincon";
                }
                return "Subcon";
            }

            var companyStatsList = new List<object>();

            int grandMtdTarget = 0, grandMtdActual = 0;
            int grandYtdTarget = 0, grandYtdActual = 0;

            int saMtdHazTarget = 0, saMtdHazActual = 0;
            int saMtdInsTarget = 0, saMtdInsActual = 0;
            int saMtdStTarget = 0, saMtdStActual = 0;
            int saMtdObsTarget = 0, saMtdObsActual = 0;
            int saMtdCoaTarget = 0, saMtdCoaActual = 0;
            int saMtdP5mTarget = 0, saMtdP5mActual = 0;

            int saYtdHazTarget = 0, saYtdHazActual = 0;
            int saYtdInsTarget = 0, saYtdInsActual = 0;
            int saYtdStTarget = 0, saYtdStActual = 0;
            int saYtdObsTarget = 0, saYtdObsActual = 0;
            int saYtdCoaTarget = 0, saYtdCoaActual = 0;
            int saYtdP5mTarget = 0, saYtdP5mActual = 0;

            foreach (var comp in allCompanies)
            {
                if (scopeCompanyId.HasValue && !allowedCompanyIds.Contains(comp.PerusahaanId))
                {
                    continue;
                }

                var compEmployees = allKaryawans.Where(k => k.IdPerusahaan == comp.PerusahaanId).ToList();
                int empCount = compEmployees.Count;
                if (empCount == 0) continue;

                // Submissions for this company
                var cHaz = dbHazards.Where(x => x.CompId == comp.PerusahaanId).ToList();
                var cIns = dbInspections.Where(x => x.CompId == comp.PerusahaanId).ToList();
                var cST = dbSafetyTalks.Where(x => x.CompId == comp.PerusahaanId).ToList();
                var cP5m = dbP5ms.Where(x => x.CompId == comp.PerusahaanId).ToList();
                var cCoa = dbCoachings.Where(x => x.CompId == comp.PerusahaanId).ToList();
                var cObs = dbObservations.Where(x => x.CompId == comp.PerusahaanId).ToList();

                var hazMtdNik = cHaz.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var insMtdNik = cIns.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var stMtdNik = cST.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var p5mMtdNik = cP5m.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var coaMtdNik = cCoa.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var obsMtdNik = cObs.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                var hazYtdNik = cHaz.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var insYtdNik = cIns.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var stYtdNik = cST.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var p5mYtdNik = cP5m.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var coaYtdNik = cCoa.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var obsYtdNik = cObs.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                int compMtdTarget = 0, compMtdActual = 0;
                int compYtdTarget = 0, compYtdActual = 0;

                int compMtdHaz = 0, compMtdIns = 0, compMtdST = 0, compMtdObs = 0, compMtdCoa = 0, compMtdP5m = 0;
                int compYtdHaz = 0, compYtdIns = 0, compYtdST = 0, compYtdObs = 0, compYtdCoa = 0, compYtdP5m = 0;

                int compMtdHazTgt = 0, compMtdInsTgt = 0, compMtdStTgt = 0, compMtdObsTgt = 0, compMtdCoaTgt = 0, compMtdP5mTgt = 0;
                int compYtdHazTgt = 0, compYtdInsTgt = 0, compYtdStTgt = 0, compYtdObsTgt = 0, compYtdCoaTgt = 0, compYtdP5mTgt = 0;

                foreach (var emp in compEmployees)
                {
                    var nik = (emp.NoNik ?? string.Empty).Trim();
                    int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0, p5mTar = 1;
                    int wH = 1, wI = 1, wST = 1, wO = 0, wC = 0;
                    int empTotalMtd = 4, empTotalYtd = 4 * elapsedWeeksYtd;

                    if (employeeTargets.TryGetValue(nik, out var et))
                    {
                        hTar = et.hTar; insTar = et.insTar; stTar = et.stTar; obsTar = et.obsTar; cTar = et.cTar; p5mTar = et.p5mTar;
                        wH = et.wH; wI = et.wI; wST = et.wST; wO = et.wO; wC = et.wC;
                        empTotalMtd = et.totalMtd; empTotalYtd = et.totalYtd;
                    }

                    int yH = wH * elapsedWeeksYtd;
                    int yI = wI * elapsedWeeksYtd;
                    int yST = wST * elapsedWeeksYtd;
                    int yO = wO * elapsedWeeksYtd;
                    int yC = wC * elapsedWeeksYtd;
                    int yP5 = p5mTar * elapsedWeeksYtd;

                    compMtdTarget += empTotalMtd;
                    compYtdTarget += empTotalYtd;

                    compMtdHazTgt += hTar; compMtdInsTgt += insTar; compMtdStTgt += stTar; compMtdObsTgt += obsTar; compMtdCoaTgt += cTar; compMtdP5mTgt += p5mTar;
                    compYtdHazTgt += yH; compYtdInsTgt += yI; compYtdStTgt += yST; compYtdObsTgt += yO; compYtdCoaTgt += yC; compYtdP5mTgt += yP5;

                    int aHazM = hazMtdNik.TryGetValue(nik, out var hM) ? hM : 0;
                    int aInsM = insMtdNik.TryGetValue(nik, out var iM) ? iM : 0;
                    int aSTM = stMtdNik.TryGetValue(nik, out var stM) ? stM : 0;
                    int aObsM = obsMtdNik.TryGetValue(nik, out var oM) ? oM : 0;
                    int aCoaM = coaMtdNik.TryGetValue(nik, out var cM) ? cM : 0;
                    int aP5M = p5mMtdNik.TryGetValue(nik, out var pM) ? pM : 0;

                    int aHazY = hazYtdNik.TryGetValue(nik, out var hY) ? hY : 0;
                    int aInsY = insYtdNik.TryGetValue(nik, out var iY) ? iY : 0;
                    int aSTY = stYtdNik.TryGetValue(nik, out var stY) ? stY : 0;
                    int aObsY = obsYtdNik.TryGetValue(nik, out var oY) ? oY : 0;
                    int aCoaY = coaYtdNik.TryGetValue(nik, out var cY) ? cY : 0;
                    int aP5Y = p5mYtdNik.TryGetValue(nik, out var pY) ? pY : 0;

                    // MTD Capping
                    compMtdActual += Math.Min(aHazM, hTar) + Math.Min(aInsM, insTar) + Math.Min(aSTM, stTar) + Math.Min(aObsM, obsTar) + Math.Min(aCoaM, cTar);
                    compMtdHaz += Math.Min(aHazM, hTar);
                    compMtdIns += Math.Min(aInsM, insTar);
                    compMtdST += Math.Min(aSTM, stTar);
                    compMtdObs += Math.Min(aObsM, obsTar);
                    compMtdCoa += Math.Min(aCoaM, cTar);
                    compMtdP5m += aP5M;

                    // YTD Capping
                    compYtdActual += Math.Min(aHazY, yH) + Math.Min(aInsY, yI) + Math.Min(aSTY, yST) + Math.Min(aObsY, yO) + Math.Min(aCoaY, yC);
                    compYtdHaz += Math.Min(aHazY, yH);
                    compYtdIns += Math.Min(aInsY, yI);
                    compYtdST += Math.Min(aSTY, yST);
                    compYtdObs += Math.Min(aObsY, yO);
                    compYtdCoa += Math.Min(aCoaY, yC);
                    compYtdP5m += aP5Y;
                }

                double mtdRate = compMtdTarget > 0 ? Math.Min(100.0, Math.Round((double)compMtdActual / compMtdTarget * 100.0, 1)) : 0.0;
                double ytdRate = compYtdTarget > 0 ? Math.Min(100.0, Math.Round((double)compYtdActual / compYtdTarget * 100.0, 1)) : 0.0;

                grandMtdTarget += compMtdTarget;
                grandMtdActual += compMtdActual;
                grandYtdTarget += compYtdTarget;
                grandYtdActual += compYtdActual;

                saMtdHazTarget += compMtdHazTgt; saMtdHazActual += compMtdHaz;
                saMtdInsTarget += compMtdInsTgt; saMtdInsActual += compMtdIns;
                saMtdStTarget += compMtdStTgt; saMtdStActual += compMtdST;
                saMtdObsTarget += compMtdObsTgt; saMtdObsActual += compMtdObs;
                saMtdCoaTarget += compMtdCoaTgt; saMtdCoaActual += compMtdCoa;
                saMtdP5mTarget += compMtdP5mTgt; saMtdP5mActual += compMtdP5m;

                saYtdHazTarget += compYtdHazTgt; saYtdHazActual += compYtdHaz;
                saYtdInsTarget += compYtdInsTgt; saYtdInsActual += compYtdIns;
                saYtdStTarget += compYtdStTgt; saYtdStActual += compYtdST;
                saYtdObsTarget += compYtdObsTgt; saYtdObsActual += compYtdObs;
                saYtdCoaTarget += compYtdCoaTgt; saYtdCoaActual += compYtdCoa;
                saYtdP5mTarget += compYtdP5mTgt; saYtdP5mActual += compYtdP5m;

                int compMtdHazKta = cHaz.Count(x => x.Date >= startOfMonth && x.Date <= endOfMonth && IsKtaCategory(x.KategoriBahaya));
                int compMtdHazTta = cHaz.Count(x => x.Date >= startOfMonth && x.Date <= endOfMonth && IsTtaCategory(x.KategoriBahaya));
                int compYtdHazKta = cHaz.Count(x => x.Date >= startOfYear && x.Date <= endOfMonth && IsKtaCategory(x.KategoriBahaya));
                int compYtdHazTta = cHaz.Count(x => x.Date >= startOfYear && x.Date <= endOfMonth && IsTtaCategory(x.KategoriBahaya));

                companyStatsList.Add(new
                {
                    CompanyId = comp.PerusahaanId,
                    CompanyName = comp.NamaPerusahaan ?? "Unknown",
                    Tier = DetermineTier(comp),
                    ActiveEmployees = empCount,
                    MtdTarget = compMtdTarget,
                    MtdActual = compMtdActual,
                    MtdRate = mtdRate,
                    YtdTarget = compYtdTarget,
                    YtdActual = compYtdActual,
                    YtdRate = ytdRate,
                    HazardMtd = compMtdHaz,
                    HazardYtd = compYtdHaz,
                    HazardMtdKta = compMtdHazKta,
                    HazardMtdTta = compMtdHazTta,
                    HazardYtdKta = compYtdHazKta,
                    HazardYtdTta = compYtdHazTta,
                    InspeksiMtd = compMtdIns,
                    InspeksiYtd = compYtdIns,
                    SafetyTalkMtd = compMtdST,
                    SafetyTalkYtd = compYtdST,
                    ObservasiMtd = compMtdObs,
                    ObservasiYtd = compYtdObs,
                    CoachingMtd = compMtdCoa,
                    CoachingYtd = compYtdCoa,
                    P5mMtd = compMtdP5m,
                    P5mYtd = compYtdP5m
                });
            }

            // 6-Month Trend (in-memory aggregation)
            var monthlyTrend = new List<object>();
            for (int i = 5; i >= 0; i--)
            {
                var mStart = new DateTime(selectedYear, selectedMonth, 1).AddMonths(-i);
                var mEnd = mStart.AddMonths(1).AddTicks(-1);

                int hCount = dbHazards.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int hKta = dbHazards.Count(x => x.Date >= mStart && x.Date <= mEnd && IsKtaCategory(x.KategoriBahaya));
                int hTta = dbHazards.Count(x => x.Date >= mStart && x.Date <= mEnd && IsTtaCategory(x.KategoriBahaya));
                int iCount = dbInspections.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int sCount = dbSafetyTalks.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int pCount = dbP5ms.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int cCount = dbCoachings.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int oCount = dbObservations.Count(x => x.Date >= mStart && x.Date <= mEnd);

                monthlyTrend.Add(new
                {
                    MonthLabel = mStart.ToString("MMM yyyy"),
                    Hazards = hCount,
                    HazardsKta = hKta,
                    HazardsTta = hTta,
                    Inspections = iCount,
                    SafetyTalks = sCount,
                    P5ms = pCount,
                    Coachings = cCount,
                    Observations = oCount,
                    Total = hCount + iCount + sCount + pCount + cCount + oCount
                });
            }

            // YTD Monthly Progression & Growth Breakdown (Jan to Dec of selectedYear)
            var ytdMonthlyProgression = new List<object>();
            int runningCumulative = 0;
            int prevMonthTotal = 0;

            int maxMonthToCompute = (selectedYear < today.Year) ? 12 : Math.Max(selectedMonth, today.Month);
            for (int m = 1; m <= maxMonthToCompute; m++)
            {
                var mStart = new DateTime(selectedYear, m, 1);
                var mEnd = mStart.AddMonths(1).AddTicks(-1);

                int hCount = dbHazards.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int hKta = dbHazards.Count(x => x.Date >= mStart && x.Date <= mEnd && IsKtaCategory(x.KategoriBahaya));
                int hTta = dbHazards.Count(x => x.Date >= mStart && x.Date <= mEnd && IsTtaCategory(x.KategoriBahaya));
                int iCount = dbInspections.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int sCount = dbSafetyTalks.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int pCount = dbP5ms.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int cCount = dbCoachings.Count(x => x.Date >= mStart && x.Date <= mEnd);
                int oCount = dbObservations.Count(x => x.Date >= mStart && x.Date <= mEnd);

                int monthTotal = hCount + iCount + sCount + pCount + cCount + oCount;
                runningCumulative += monthTotal;

                double growthRate = 0;
                if (prevMonthTotal > 0)
                {
                    growthRate = Math.Round(((double)(monthTotal - prevMonthTotal) / prevMonthTotal) * 100.0, 1);
                }

                prevMonthTotal = monthTotal;

                ytdMonthlyProgression.Add(new
                {
                    Month = m,
                    MonthName = mStart.ToString("MMMM", new System.Globalization.CultureInfo("id-ID")),
                    MonthShort = mStart.ToString("MMM", new System.Globalization.CultureInfo("id-ID")),
                    MonthYear = mStart.ToString("MMM yyyy", new System.Globalization.CultureInfo("id-ID")),
                    Hazards = hCount,
                    HazardsKta = hKta,
                    HazardsTta = hTta,
                    Inspections = iCount,
                    SafetyTalks = sCount,
                    P5ms = pCount,
                    Coachings = cCount,
                    Observations = oCount,
                    Total = monthTotal,
                    Cumulative = runningCumulative,
                    GrowthRate = growthRate
                });
            }

            double avgMtdRate = grandMtdTarget > 0 ? Math.Min(100.0, Math.Round((double)grandMtdActual / grandMtdTarget * 100.0, 1)) : 0.0;
            double avgYtdRate = grandYtdTarget > 0 ? Math.Min(100.0, Math.Round((double)grandYtdActual / grandYtdTarget * 100.0, 1)) : 0.0;

            // Overall Hazard KTA vs TTA Analysis for MTD & YTD
            var mtdHazards = dbHazards.Where(x => x.Date >= startOfMonth && x.Date <= endOfMonth).ToList();
            int mtdHazTotal = mtdHazards.Count;
            int mtdHazKta = mtdHazards.Count(x => IsKtaCategory(x.KategoriBahaya));
            int mtdHazTta = mtdHazards.Count(x => IsTtaCategory(x.KategoriBahaya));
            double mtdHazKtaPct = mtdHazTotal > 0 ? Math.Round((double)mtdHazKta / mtdHazTotal * 100.0, 1) : 0.0;
            double mtdHazTtaPct = mtdHazTotal > 0 ? Math.Round((double)mtdHazTta / mtdHazTotal * 100.0, 1) : 0.0;

            var ytdHazards = dbHazards.Where(x => x.Date >= startOfYear && x.Date <= endOfMonth).ToList();
            int ytdHazTotal = ytdHazards.Count;
            int ytdHazKta = ytdHazards.Count(x => IsKtaCategory(x.KategoriBahaya));
            int ytdHazTta = ytdHazards.Count(x => IsTtaCategory(x.KategoriBahaya));
            double ytdHazKtaPct = ytdHazTotal > 0 ? Math.Round((double)ytdHazKta / ytdHazTotal * 100.0, 1) : 0.0;
            double ytdHazTtaPct = ytdHazTotal > 0 ? Math.Round((double)ytdHazTta / ytdHazTotal * 100.0, 1) : 0.0;

            var hazardBreakdown = new
            {
                mtd = new
                {
                    total = mtdHazTotal,
                    kta = mtdHazKta,
                    tta = mtdHazTta,
                    ktaPct = mtdHazKtaPct,
                    ttaPct = mtdHazTtaPct
                },
                ytd = new
                {
                    total = ytdHazTotal,
                    kta = ytdHazKta,
                    tta = ytdHazTta,
                    ktaPct = ytdHazKtaPct,
                    ttaPct = ytdHazTtaPct
                }
            };

            var responseData = new
            {
                success = true,
                selectedYear,
                selectedMonth,
                summary = new
                {
                    totalEmployees = allKaryawans.Count(k => scopeCompanyId == null || allowedCompanyIds.Contains(k.IdPerusahaan)),
                    grandMtdTarget,
                    grandMtdActual,
                    avgMtdRate,
                    grandYtdTarget,
                    grandYtdActual,
                    avgYtdRate,
                    totalCompanies = companyStatsList.Count
                },
                hazardBreakdown,
                companies = companyStatsList,
                saTypes = new
                {
                    labels = new[] { "Hazard Report", "Inspeksi", "Safety Talk", "Observasi", "Coaching", "P5M" },
                    mtdTargets = new[] { saMtdHazTarget, saMtdInsTarget, saMtdStTarget, saMtdObsTarget, saMtdCoaTarget, saMtdP5mTarget },
                    mtdActuals = new[] { saMtdHazActual, saMtdInsActual, saMtdStActual, saMtdObsActual, saMtdCoaActual, saMtdP5mActual },
                    ytdTargets = new[] { saYtdHazTarget, saYtdInsTarget, saYtdStTarget, saYtdObsTarget, saYtdCoaTarget, saYtdP5mTarget },
                    ytdActuals = new[] { saYtdHazActual, saYtdInsActual, saYtdStActual, saYtdObsActual, saYtdCoaActual, saYtdP5mActual }
                },
                monthlyTrend,
                ytdMonthlyProgression
            };

            cache.Set(cacheKey, responseData, TimeSpan.FromMinutes(5));
            return Json(responseData);
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeAnalyticsData(int? companyId = null, string? departmentName = null, int? year = null, int? month = null)
        {
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;

            var (scopeCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            int? filterCompId = companyId ?? scopeCompanyId;

            var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"EmployeeAnalytics_{filterCompId ?? 0}_{departmentName ?? "All"}_{selectedYear}_{selectedMonth}";

            bool forceRefresh = HttpContext.Request.Query.ContainsKey("refresh") &&
                                string.Equals(HttpContext.Request.Query["refresh"], "true", StringComparison.OrdinalIgnoreCase);

            if (!forceRefresh && cache.TryGetValue(cacheKey, out object? cachedResult) && cachedResult != null)
            {
                return Json(cachedResult);
            }

            await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
            var startOfYear = new DateTime(selectedYear, 1, 1);
            int elapsedWeeksYtd = Math.Max(1, ((today < endOfMonth ? today : endOfMonth) - startOfYear.Date).Days / 7 + 1);

            var query = from k in _context.Karyawans.AsNoTracking()
                        join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal
                        join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into dg
                        from d in dg.DefaultIfEmpty()
                        join c in _context.Perusahaans.AsNoTracking() on k.IdPerusahaan equals c.PerusahaanId into cg
                        from c in cg.DefaultIfEmpty()
                        where k.StatusAktif == true && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan)
                        select new
                        {
                            k.IdKaryawan,
                            k.NoNik,
                            Nama = p.NamaLengkap,
                            k.IdPerusahaan,
                            NamaPerusahaan = c != null ? c.NamaPerusahaan : "Unknown",
                            NamaDepartemen = d != null ? d.NamaDepartemen : "General"
                        };

            if (filterCompId.HasValue && filterCompId.Value > 0)
            {
                query = query.Where(k => k.IdPerusahaan == filterCompId.Value);
            }
            else if (scopeCompanyId.HasValue)
            {
                query = query.Where(k => allowedCompanyIds.Contains(k.IdPerusahaan));
            }

            if (!string.IsNullOrWhiteSpace(departmentName) && !string.Equals(departmentName, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(k => k.NamaDepartemen == departmentName);
            }

            var employees = await query.ToListAsync();
            var empIds = employees.Select(e => e.IdKaryawan).ToList();
            var empNiks = employees.Select(e => (e.NoNik ?? string.Empty).Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

            var targetMappings = await _context.KaryawanJabatanMappings.AsNoTracking()
                .Where(m => empIds.Contains(m.KaryawanId))
                .ToListAsync();
            var mappingsDict = targetMappings.ToDictionary(m => m.KaryawanId);

            // Submissions
            var hazards = await _context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && h.Tanggal >= startOfYear && h.Tanggal <= endOfMonth && empNiks.Contains(h.Nik.Trim()))
                .Select(h => new { Nik = h.Nik.Trim(), Date = h.Tanggal })
                .ToListAsync();

            var inspections = await _context.Inspections.AsNoTracking()
                .Where(i => !i.IsDeleted && i.Tanggal >= startOfYear && i.Tanggal <= endOfMonth && empNiks.Contains(i.Nik.Trim()))
                .Select(i => new { Nik = i.Nik.Trim(), Date = i.Tanggal })
                .ToListAsync();

            var safetyTalks = await _context.SafetyTalks.AsNoTracking()
                .Where(s => !s.IsDeleted && s.Tanggal >= startOfYear && s.Tanggal <= endOfMonth && empNiks.Contains(s.Nik.Trim()))
                .Select(s => new { Nik = s.Nik.Trim(), Date = s.Tanggal })
                .ToListAsync();

            var coachings = await _context.Coachings.AsNoTracking()
                .Where(c => !c.IsDeleted && c.CreatedAt >= startOfYear && c.CreatedAt <= endOfMonth && empNiks.Contains(c.Nik.Trim()))
                .Select(c => new { Nik = c.Nik.Trim(), Date = c.CreatedAt })
                .ToListAsync();

            var observations = await _context.Observations.AsNoTracking()
                .Where(o => !o.IsDeleted && o.CreatedAt >= startOfYear && o.CreatedAt <= endOfMonth && empNiks.Contains(o.Nik.Trim()))
                .Select(o => new { Nik = o.Nik.Trim(), Date = o.CreatedAt })
                .ToListAsync();

            var hazMtdNik = hazards.Where(x => x.Date >= startOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var insMtdNik = inspections.Where(x => x.Date >= startOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var stMtdNik = safetyTalks.Where(x => x.Date >= startOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var coaMtdNik = coachings.Where(x => x.Date >= startOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var obsMtdNik = observations.Where(x => x.Date >= startOfMonth).GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var hazYtdNik = hazards.GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var insYtdNik = inspections.GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var stYtdNik = safetyTalks.GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var coaYtdNik = coachings.GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var obsYtdNik = observations.GroupBy(x => x.Nik, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var employeeStats = new List<dynamic>();
            int tier100Count = 0, tier80Count = 0, tier50Count = 0, tierLowCount = 0;

            foreach (var emp in employees)
            {
                var nik = (emp.NoNik ?? string.Empty).Trim();
                int hTar = 2, insTar = 1, stTar = 1, obsTar = 0, cTar = 0;
                if (mappingsDict.TryGetValue(emp.IdKaryawan, out var m))
                {
                    hTar = m.TargetHazardReport ?? 2;
                    insTar = m.TargetInspeksi ?? 1;
                    stTar = m.TargetSafetyTalk ?? 1;
                    obsTar = m.TargetObservasi ?? 0;
                    cTar = m.TargetCoaching ?? 0;
                }

                int wH = hTar > 0 ? Math.Max(1, (int)Math.Round(hTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wI = insTar > 0 ? Math.Max(1, (int)Math.Round(insTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wST = stTar > 0 ? Math.Max(1, (int)Math.Round(stTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wO = obsTar > 0 ? Math.Max(1, (int)Math.Round(obsTar / 4.0, MidpointRounding.AwayFromZero)) : 0;
                int wC = cTar > 0 ? Math.Max(1, (int)Math.Round(cTar / 4.0, MidpointRounding.AwayFromZero)) : 0;

                int yH = wH * elapsedWeeksYtd;
                int yI = wI * elapsedWeeksYtd;
                int yST = wST * elapsedWeeksYtd;
                int yO = wO * elapsedWeeksYtd;
                int yC = wC * elapsedWeeksYtd;

                int mtdTarget = hTar + insTar + stTar + obsTar + cTar;
                int ytdTarget = yH + yI + yST + yO + yC;

                int aHazM = hazMtdNik.TryGetValue(nik, out var hm) ? hm : 0;
                int aInsM = insMtdNik.TryGetValue(nik, out var im) ? im : 0;
                int aSTM = stMtdNik.TryGetValue(nik, out var stm) ? stm : 0;
                int aObsM = obsMtdNik.TryGetValue(nik, out var om) ? om : 0;
                int aCoaM = coaMtdNik.TryGetValue(nik, out var cm) ? cm : 0;

                int aHazY = hazYtdNik.TryGetValue(nik, out var hy) ? hy : 0;
                int aInsY = insYtdNik.TryGetValue(nik, out var iy) ? iy : 0;
                int aSTY = stYtdNik.TryGetValue(nik, out var sty) ? sty : 0;
                int aObsY = obsYtdNik.TryGetValue(nik, out var oy) ? oy : 0;
                int aCoaY = coaYtdNik.TryGetValue(nik, out var cy) ? cy : 0;

                int mtdCapped = Math.Min(aHazM, hTar) + Math.Min(aInsM, insTar) + Math.Min(aSTM, stTar) + Math.Min(aObsM, obsTar) + Math.Min(aCoaM, cTar);
                int mtdRaw = aHazM + aInsM + aSTM + aObsM + aCoaM;
                double mtdRate = mtdTarget > 0 ? Math.Min(100.0, Math.Round((double)mtdCapped / mtdTarget * 100.0, 1)) : 100.0;

                int ytdCapped = Math.Min(aHazY, yH) + Math.Min(aInsY, yI) + Math.Min(aSTY, yST) + Math.Min(aObsY, yO) + Math.Min(aCoaY, yC);
                int ytdRaw = aHazY + aInsY + aSTY + aObsY + aCoaY;
                double ytdRate = ytdTarget > 0 ? Math.Min(100.0, Math.Round((double)ytdCapped / ytdTarget * 100.0, 1)) : 100.0;

                if (mtdRate >= 100.0) tier100Count++;
                else if (mtdRate >= 80.0) tier80Count++;
                else if (mtdRate >= 50.0) tier50Count++;
                else tierLowCount++;

                employeeStats.Add(new
                {
                    Nik = nik,
                    Nama = emp.Nama ?? "Unknown",
                    CompanyId = emp.IdPerusahaan,
                    NamaPerusahaan = emp.NamaPerusahaan,
                    NamaDepartemen = emp.NamaDepartemen,
                    MtdTarget = mtdTarget,
                    MtdActual = mtdCapped,
                    MtdRaw = mtdRaw,
                    MtdRate = mtdRate,
                    YtdTarget = ytdTarget,
                    YtdActual = ytdCapped,
                    YtdRaw = ytdRaw,
                    YtdRate = ytdRate,
                    HazardMtd = aHazM,
                    InspeksiMtd = aInsM,
                    SafetyTalkMtd = aSTM,
                    ObservasiMtd = aObsM,
                    CoachingMtd = aCoaM
                });
            }

            var topPerformersMtd = employeeStats.OrderByDescending(e => e.MtdRate).ThenByDescending(e => e.MtdRaw).Take(10).ToList();
            var topPerformersYtd = employeeStats.OrderByDescending(e => e.YtdRate).ThenByDescending(e => e.YtdRaw).Take(10).ToList();
            var lowCompliance = employeeStats.Where(e => (double)e.MtdRate < 50.0).OrderBy(e => e.MtdRate).ThenBy(e => e.MtdRaw).Take(20).ToList();

            var deptSummary = employeeStats.GroupBy(e => (string)e.NamaDepartemen)
                .Select(g => new
                {
                    Department = g.Key,
                    EmployeeCount = g.Count(),
                    AvgMtdRate = Math.Round(g.Average(e => (double)e.MtdRate), 1),
                    AvgYtdRate = Math.Round(g.Average(e => (double)e.YtdRate), 1),
                    TotalMtdSubmissions = g.Sum(e => (int)e.MtdRaw),
                    TotalYtdSubmissions = g.Sum(e => (int)e.YtdRaw)
                })
                .OrderByDescending(d => d.AvgMtdRate)
                .ToList();

            var responseData = new
            {
                success = true,
                selectedYear,
                selectedMonth,
                totalEmployees = employeeStats.Count,
                complianceDistribution = new
                {
                    tier100 = tier100Count,
                    tier80 = tier80Count,
                    tier50 = tier50Count,
                    tierLow = tierLowCount
                },
                topPerformersMtd,
                topPerformersYtd,
                lowCompliance,
                deptSummary
            };

            cache.Set(cacheKey, responseData, TimeSpan.FromMinutes(5));
            return Json(responseData);
        }

        [HttpGet]
        public async Task<IActionResult> GetMonitoringMetricsData(int? year = null, int? month = null)
        {
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;

            var (scopeCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"MonitoringMetrics_{scopeCompanyId}_{selectedYear}_{selectedMonth}";

            bool forceRefresh = HttpContext.Request.Query.ContainsKey("refresh") &&
                                string.Equals(HttpContext.Request.Query["refresh"], "true", StringComparison.OrdinalIgnoreCase);

            if (!forceRefresh && cache.TryGetValue(cacheKey, out object? cachedResult) && cachedResult != null)
            {
                return Json(cachedResult);
            }

            await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
            var startOfYear = new DateTime(selectedYear, 1, 1);

            var hazardsBase = _context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && (scopeCompanyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))));

            var openHazards = await hazardsBase.Where(h => h.StatusTemuan == "Open").Select(h => new { h.TingkatResiko, h.Tanggal, h.Lokasi, h.Area, h.KategoriBahaya }).ToListAsync();
            var monthHazards = await hazardsBase.Where(h => h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth).Select(h => new { h.StatusTemuan, h.TingkatResiko, h.Lokasi, h.KategoriBahaya }).ToListAsync();

            int totalOpen = openHazards.Count;
            int totalClosedMonth = monthHazards.Count(h => h.StatusTemuan == "Closed");
            int totalMonthHazards = monthHazards.Count;

            double complianceClose = totalMonthHazards > 0 ? Math.Round((double)totalClosedMonth / totalMonthHazards * 100.0, 1) : 0.0;

            var overdueDate = DateTime.Now.AddDays(-14);
            int overdueHazards = openHazards.Count(h => h.Tanggal < overdueDate);
            double overdueRate = totalOpen > 0 ? Math.Round((double)overdueHazards / totalOpen * 100.0, 1) : 0.0;

            int openExtreme = openHazards.Count(h => string.Equals(h.TingkatResiko, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Ekstrim", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Sangat Berat", StringComparison.OrdinalIgnoreCase));
            int openHigh = openHazards.Count(h => string.Equals(h.TingkatResiko, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Tinggi", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Berat", StringComparison.OrdinalIgnoreCase));
            int openMedium = openHazards.Count(h => string.Equals(h.TingkatResiko, "Medium", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Sedang", StringComparison.OrdinalIgnoreCase));
            int openLow = openHazards.Count(h => string.Equals(h.TingkatResiko, "Low", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Rendah", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "Ringan", StringComparison.OrdinalIgnoreCase));

            int highRiskOpen = openExtreme + openHigh;
            double complianceRisk = totalOpen > 0 ? Math.Round((double)highRiskOpen / totalOpen * 100.0, 1) : 0.0;

            int GetRiskWeight(string? r)
            {
                if (string.IsNullOrEmpty(r)) return 0;
                if (r.Contains("Extreme", StringComparison.OrdinalIgnoreCase) || r.Contains("Ekstrim", StringComparison.OrdinalIgnoreCase)) return 4;
                if (r.Contains("High", StringComparison.OrdinalIgnoreCase) || r.Contains("Tinggi", StringComparison.OrdinalIgnoreCase)) return 3;
                if (r.Contains("Medium", StringComparison.OrdinalIgnoreCase) || r.Contains("Sedang", StringComparison.OrdinalIgnoreCase)) return 2;
                return 1;
            }

            int totalRiskWeight = monthHazards.Sum(h => GetRiskWeight(h.TingkatResiko));
            int closedRiskWeight = monthHazards.Where(h => h.StatusTemuan == "Closed").Sum(h => GetRiskWeight(h.TingkatResiko));
            double rri = totalRiskWeight > 0 ? Math.Round((double)closedRiskWeight / totalRiskWeight * 100.0, 1) : 0.0;

            var locGroups = monthHazards
                .Where(h => !string.IsNullOrWhiteSpace(h.Lokasi))
                .GroupBy(h => h.Lokasi!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int repeatLocations = locGroups.Count(g => g.Count() > 1);
            int totalLocations = locGroups.Count;
            double rhr = totalLocations > 0 ? Math.Round((double)repeatLocations / totalLocations * 100.0, 1) : 0.0;

            var topRepeated = locGroups
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new { Label = g.Key, Count = g.Count() })
                .ToList();

            // High risk resolution
            int closedHighRisk = monthHazards.Count(h => h.StatusTemuan == "Closed" && (string.Equals(h.TingkatResiko, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "High", StringComparison.OrdinalIgnoreCase)));
            int totalHighRiskMonth = monthHazards.Count(h => string.Equals(h.TingkatResiko, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(h.TingkatResiko, "High", StringComparison.OrdinalIgnoreCase));
            double highRiskResolution = totalHighRiskMonth > 0 ? Math.Round((double)closedHighRisk / totalHighRiskMonth * 100.0, 1) : 0.0;

            // Kategori Bahaya (TTA vs KTA) MTD & YTD
            var kategoriListMtd = monthHazards.Where(h => !string.IsNullOrEmpty(h.KategoriBahaya)).Select(h => h.KategoriBahaya!).ToList();
            int unsafeActCount = kategoriListMtd.Count(k => k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("TTA", StringComparison.OrdinalIgnoreCase));
            int unsafeConditionCount = kategoriListMtd.Count(k => k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase));

            var ytdHazardsList = await hazardsBase.Where(h => h.Tanggal >= startOfYear && h.Tanggal <= endOfMonth && !string.IsNullOrEmpty(h.KategoriBahaya)).Select(h => h.KategoriBahaya!).ToListAsync();
            int unsafeActCountYtd = ytdHazardsList.Count(k => k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("TTA", StringComparison.OrdinalIgnoreCase));
            int unsafeConditionCountYtd = ytdHazardsList.Count(k => k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase));

            // Incident pyramid
            var incidents = await _context.IncidentNewsList.AsNoTracking()
                .Where(i => i.IsPublished && (scopeCompanyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))) && (i.TanggalKejadian ?? i.CreatedAt) >= startOfYear)
                .Select(i => new { i.Kategori, i.Judul, i.Konten })
                .ToListAsync();

            int incFatality = incidents.Count(i => (i.Kategori ?? "").Contains("Fatal", StringComparison.OrdinalIgnoreCase));
            int incFirstAid = incidents.Count(i => (i.Kategori ?? "").Contains("First Aid", StringComparison.OrdinalIgnoreCase));
            int incMedical = incidents.Count(i => (i.Kategori ?? "").Contains("Medical", StringComparison.OrdinalIgnoreCase));
            int incProperty = incidents.Count(i => (i.Kategori ?? "").Contains("Property", StringComparison.OrdinalIgnoreCase));
            int incNearMiss = incidents.Count(i => (i.Kategori ?? "").Contains("Near Miss", StringComparison.OrdinalIgnoreCase));
            int incKebakaran = incidents.Count(i => (i.Kategori ?? "").Contains("Kebakaran", StringComparison.OrdinalIgnoreCase) || (i.Kategori ?? "").Contains("Fire", StringComparison.OrdinalIgnoreCase));

            var responseData = new
            {
                success = true,
                gauges = new
                {
                    complianceClose,
                    overdueRate,
                    complianceRisk,
                    rri,
                    rhr,
                    highRiskResolution,
                    totalOpen,
                    repeatLocations,
                    totalLocations
                },
                pyramid = new
                {
                    fatality = incFatality,
                    firstAid = incFirstAid,
                    medicalTreatment = incMedical,
                    propertyDamage = incProperty,
                    nearMiss = incNearMiss,
                    kebakaran = incKebakaran,
                    extreme = openExtreme,
                    high = openHigh,
                    medium = openMedium,
                    low = openLow
                },
                topRepeated,
                kategoriBahaya = new
                {
                    unsafeAct = unsafeActCount,
                    unsafeCondition = unsafeConditionCount,
                    unsafeActMtd = unsafeActCount,
                    unsafeConditionMtd = unsafeConditionCount,
                    unsafeActYtd = unsafeActCountYtd,
                    unsafeConditionYtd = unsafeConditionCountYtd
                }
            };

            cache.Set(cacheKey, responseData, TimeSpan.FromMinutes(5));
            return Json(responseData);
        }

        [HttpGet]
        [Route("/Performance")]
        [Route("/Performance/Index")]
        public async Task<IActionResult> Index(int? year = null, int? month = null)
        {
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
            // Date ranges
            var now = DateTime.Now;
            var startOfWeek = DateTime.Today.AddDays(-6); // rolling 7 calendar days (today inclusive)
            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
            var startOfYear = startOfMonth; // Filter YTD views to MTD to optimize performance
            var trendStart = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
            var baseStartDate = trendStart < startOfYear ? trendStart : startOfYear;

            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);
            var allCompanies = await _context.Perusahaans.Where(p => p.StatusAktif).ToListAsync();

            // 1. Total Karyawan Aktif
            var totalKaryawan = await _context.Karyawans
                .CountAsync(k => k.StatusAktif && (companyId == null || allowedCompanyIds.Contains(k.IdPerusahaan)));

            var activeKaryawanList = await _context.Karyawans.AsNoTracking()
                .Where(k => k.StatusAktif && (companyId == null || allowedCompanyIds.Contains(k.IdPerusahaan)))
                .ToListAsync();

            var activeKaryawanIds = activeKaryawanList.Select(k => k.IdKaryawan).ToList();
            var targetMappingCompany = await _context.KaryawanJabatanMappings.AsNoTracking()
                .Where(m => activeKaryawanIds.Contains(m.KaryawanId))
                .ToListAsync();
            var targetMappingCompanyDict = targetMappingCompany.ToDictionary(m => m.KaryawanId);

            var activeNiks = activeKaryawanList.Select(k => k.NoNik).Where(nik => !string.IsNullOrEmpty(nik)).ToList();
            var activeRosters = await _context.Rosters.AsNoTracking()
                .Where(r => activeNiks.Contains(r.Nik))
                .ToListAsync();
            var activeRostersByNik = activeRosters
                .GroupBy(r => r.Nik, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            int ScaleTargetIndex(int baseTarget, double rat, int daysOnsite)
            {
                if (baseTarget == 0) return 0;
                if (daysOnsite == 0) return 0;
                int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                return Math.Max(scaled, 1);
            }

            int totalDaysInMonthM = DateTime.DaysInMonth(selectedYear, selectedMonth);

            int monthlyTarget = 0;
            foreach (var emp in activeKaryawanList)
            {
                int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0;
                if (targetMappingCompanyDict.TryGetValue(emp.IdKaryawan, out var t))
                {
                    hTar = t.TargetHazardReport ?? 2;
                    insTar = t.TargetInspeksi ?? 1;
                    stTar = t.TargetSafetyTalk ?? 1;
                    obsTar = t.TargetObservasi ?? 0;
                    cTar = t.TargetCoaching ?? 0;
                }

                if (hTar + insTar + stTar + obsTar + cTar == 0)
                {
                    continue;
                }

                var nik = (emp.NoNik ?? string.Empty).Trim();
                int onsiteDays = totalDaysInMonthM;
                bool hasRoster = false;

                if (!string.IsNullOrEmpty(nik) && activeRostersByNik.TryGetValue(nik, out var empRosters))
                {
                    int computedOnsite = 0;
                    foreach (var r in empRosters)
                    {
                        var overlapStart = r.AwalDinas > startOfMonth ? r.AwalDinas : startOfMonth;
                        var overlapEnd = r.AkhirDinas < endOfMonth ? r.AkhirDinas : endOfMonth;
                        if (overlapStart <= overlapEnd)
                        {
                            computedOnsite += (overlapEnd - overlapStart).Days + 1;
                        }
                    }
                    if (computedOnsite > 0)
                    {
                        hasRoster = true;
                        onsiteDays = computedOnsite;
                    }
                }

                double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonthM : 1.0;

                int mtdTgtH = hasRoster ? ScaleTargetIndex(hTar, ratio, onsiteDays) : hTar;
                int mtdTgtI = hasRoster ? ScaleTargetIndex(insTar, ratio, onsiteDays) : insTar;
                int mtdTgtST = hasRoster ? ScaleTargetIndex(stTar, ratio, onsiteDays) : stTar;
                int mtdTgtO = hasRoster ? ScaleTargetIndex(obsTar, ratio, onsiteDays) : obsTar;
                int mtdTgtC = hasRoster ? ScaleTargetIndex(cTar, ratio, onsiteDays) : cTar;

                monthlyTarget += (mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC);
            }

            int weeklyTarget = (int)Math.Round(monthlyTarget / 4.0, MidpointRounding.AwayFromZero);
            if (weeklyTarget < 1 && monthlyTarget > 0) weeklyTarget = 1;



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

            // 5b. Extra Professional Graphs Data - Hazard KTA vs TTA (MTD & YTD)
            var allKategoriMtd = await hazards.Where(h => h.KategoriBahaya != null && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth).Select(h => h.KategoriBahaya).ToListAsync();
            int unsafeActCountMtd = allKategoriMtd.Count(k => k != null && (k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("TTA", StringComparison.OrdinalIgnoreCase)));
            int unsafeConditionCountMtd = allKategoriMtd.Count(k => k != null && (k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase)));

            var allKategoriYtd = await hazards.Where(h => h.KategoriBahaya != null && h.Tanggal >= actualStartOfYear && h.Tanggal <= endOfMonth).Select(h => h.KategoriBahaya).ToListAsync();
            int unsafeActCountYtd = allKategoriYtd.Count(k => k != null && (k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("TTA", StringComparison.OrdinalIgnoreCase)));
            int unsafeConditionCountYtd = allKategoriYtd.Count(k => k != null && (k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase)));

            var topAreas = await hazards.Where(h => !string.IsNullOrEmpty(h.Area) && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth)
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
                int oCount = await observationsQuery.CountAsync(o => o.Date >= monthStart && o.CreatedAt < monthEnd);
                int cCount = await coachings.CountAsync(c => c.CreatedAt >= monthStart && c.CreatedAt < monthEnd);
                int incCount = await incidentBaseQuery.CountAsync(inc => (inc.TanggalKejadian ?? inc.CreatedAt) >= monthStart && (inc.TanggalKejadian ?? inc.CreatedAt) < monthEnd);

                int totalSap = hCount + iCount + sCount + pCount + oCount + cCount;

                monthlyTrend.Add(new MonthlyTrendViewModel
                {
                    MonthLabel = monthStart.ToString("MMM yyyy"),
                    Hazards = hCount,
                    Inspections = iCount,
                    SafetyTalks = sCount,
                    P5ms = pCount,
                    Observations = oCount,
                    Coachings = cCount,
                    TotalSap = totalSap,
                    Incidents = incCount
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

            ViewBag.UnsafeActCount = unsafeActCountMtd;
            ViewBag.UnsafeConditionCount = unsafeConditionCountMtd;
            ViewBag.UnsafeActCountYtd = unsafeActCountYtd;
            ViewBag.UnsafeConditionCountYtd = unsafeConditionCountYtd;
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
                                                  select new { CompanyId = k.IdPerusahaan, p.Nik, CreatedAt = p.Coaching!.CreatedAt })
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
                    var companyEmployees = await GetEmployeesComplianceData(myKaryawan.IdPerusahaan, null, null, null, myKaryawan.PerusahaanNodeId);
                    
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
            ViewBag.IsSafetyRole = isSafetyRole;
            ViewBag.IsAdmin = isAdmin;

            // Fetch all active companies
            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            List<PerusahaanView> allowedCompanies;
            if (isAdmin)
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

            int defaultCompanyId = 0;
            var userCompanyStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(userCompanyStr, out int parsedUserCompanyId) && parsedUserCompanyId > 0)
            {
                defaultCompanyId = parsedUserCompanyId;
            }
            else
            {
                defaultCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            int selectedCompanyId = companyId ?? defaultCompanyId;
            
            // Security check: Non-admins cannot inspect other companies' internal dept list unless allowed by scope
            if (!isAdmin && !allowedCompanyIds.Contains(selectedCompanyId))
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

            if (mode == "company" || mode == "core")
            {
                // Liga Antar Company: Compare all companies
                var companyStandings = new List<dynamic>();
                var allEmployees = new List<dynamic>();

                var companiesToCompare = allowedCompanies;
                if (selectedCompanyId > 0)
                {
                    var childIds = allCompanies.Where(c => c.PerusahaanIndukId == selectedCompanyId).Select(c => c.PerusahaanId).ToList();
                    var relationChildIds = relations.Where(r => r.ParentCompanyId == selectedCompanyId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                    var allChildIds = childIds.Concat(relationChildIds).Distinct().ToList();
                    
                    if (allChildIds.Any())
                    {
                        var targetCompanyIds = new HashSet<int>(allChildIds) { selectedCompanyId };
                        companiesToCompare = allowedCompanies.Where(c => targetCompanyIds.Contains(c.PerusahaanId)).ToList();
                    }
                    else
                    {
                        companiesToCompare = allowedCompanies.Where(c => c.PerusahaanId == selectedCompanyId).ToList();
                    }
                }

                if (mode == "core")
                {
                    var coreCompaniesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                        "PT PELAYARAN GANESHA LAUTJAYA",
                        "PT SUCOFINDO",
                        "PT KALIMANTAN PRIMA PERSADA",
                        "PT ELA SANGATTA",
                        "PT ADHITAMA WIJAYA PERKASA",
                        "PT TUNAS JAYA PERKASA",
                        "PT SEMESTA MANDIRI INDONESIA",
                        "PT BANDANG MINING COAL",
                        "PT ORICA MINING SERVICE",
                        "PT DIVA CAHAYA SEJAHTERA",
                        "PT UNGGUL DINAMIKA UTAMA",
                        "PT REZEKI BORNEO SEBUKU",
                        "PT DAHANA",
                        "PT MEGA GLOBAL ENERGY",
                        "PT BERLIAN DUTA ENERGI",
                        "PT SAMUDERA MAJU PERKASA",
                        "PT GRAHA PRIMA ENERGI",
                        "PT KARUNIA ARMADA INDONESIA"
                    };
                    companiesToCompare = allCompanies.Where(c => coreCompaniesList.Contains(c.NamaPerusahaan ?? "")).ToList();
                }

                foreach (var comp in companiesToCompare)
                {
                    var compEmps = await GetEmployeesComplianceData(comp.PerusahaanId, null, selectedYear, selectedMonth, selectedCompanyId);

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

                var allStandings = companyStandings.Where(x => (int)x.TotalTarget > 0).OrderByDescending(x => (double)x.MtdAchievementRate).ToList();
                ViewBag.CompanyStandings = allStandings.Where(x => !((int)x.TotalTarget > 0 && (double)x.MtdAchievementRate == 0)).ToList();
                ViewBag.CompanyRedZone = allStandings.Where(x => (int)x.TotalTarget > 0 && (double)x.MtdAchievementRate == 0).ToList();

                // Non-admin can only see their own squad players even in global league mode
                var sortedEmployees = allEmployees
                    .Where(e => isAdmin || (isSafetyRole && allowedCompanyIds.Contains((int)e.companyId)) || (int)e.companyId == resolvedCompanyId)
                    .Select(e => new {
                        name = (string)e.karyawanName,
                        nik = (string)e.nik,
                        departmentName = (string)e.departmentName,
                        jabatanName = (string)e.jabatanName,
                        complianceRate = (double)e.complianceRate,
                        mtdTotalTarget = (int)e.mtdTotalTarget,
                        onsiteDays = (int)e.onsiteDays,
                        hasRoster = (bool)e.hasRoster,
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
                var employees = await GetEmployeesComplianceData(selectedCompany.PerusahaanId, null, selectedYear, selectedMonth, selectedCompanyId);
                
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

                var activeDeptAchievements = deptAchievements.Where(d => d.TotalTarget > 0).ToList();
                ViewBag.DepartmentAchievements = activeDeptAchievements.Where(d => !(d.TotalTarget > 0 && d.MtdAchievementRate == 0)).ToList();
                ViewBag.DepartmentRedZone = activeDeptAchievements.Where(d => d.TotalTarget > 0 && d.MtdAchievementRate == 0).ToList();

                var sortedEmployees = employees.Select(e => new {
                    name = (string)e.karyawanName,
                    nik = (string)e.nik,
                    departmentName = (string)e.departmentName,
                    jabatanName = (string)e.jabatanName,
                    complianceRate = (double)e.complianceRate,
                    mtdTotalTarget = (int)e.mtdTotalTarget,
                    onsiteDays = (int)e.onsiteDays,
                    hasRoster = (bool)e.hasRoster,
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
        public async Task<IActionResult> ExportLeagueToExcel(int? companyId = null, string mode = "dept", string? departmentName = null, int? year = null, int? month = null)
        {
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;

            string[] monthNames = { "", "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
            string monthName = (selectedMonth >= 1 && selectedMonth <= 12) ? monthNames[selectedMonth] : selectedMonth.ToString();
            string periodFormatted = $"{monthName.ToUpper()} {selectedYear}";

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
            if (isAdmin)
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

            int defaultCompanyId = 0;
            var userCompanyStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(userCompanyStr, out int parsedUserCompanyId) && parsedUserCompanyId > 0)
            {
                defaultCompanyId = parsedUserCompanyId;
            }
            else
            {
                defaultCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            int selectedCompanyId = companyId ?? defaultCompanyId;
            if (!isAdmin && !allowedCompanyIds.Contains(selectedCompanyId))
            {
                selectedCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            var selectedCompany = allCompanies.FirstOrDefault(c => c.PerusahaanId == selectedCompanyId) ?? allowedCompanies.First();

            List<dynamic> employeesData = new List<dynamic>();
            var companyStandings = new List<dynamic>();
            var deptAchievements = new List<DepartmentAchievementViewModel>();

            string modeLabel = mode == "company" ? "Super League (Antar Perusahaan)" : (mode == "core" ? "Liga Perusahaan Inti" : "Klasemen Internal (Departemen)");

            if (mode == "company" || mode == "core")
            {
                var companiesToCompare = allowedCompanies;
                
                if (mode == "core")
                {
                    var coreCompaniesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                        "PT PELAYARAN GANESHA LAUTJAYA",
                        "PT SUCOFINDO",
                        "PT KALIMANTAN PRIMA PERSADA",
                        "PT ELA SANGATTA",
                        "PT ADHITAMA WIJAYA PERKASA",
                        "PT TUNAS JAYA PERKASA",
                        "PT SEMESTA MANDIRI INDONESIA",
                        "PT BANDANG MINING COAL",
                        "PT ORICA MINING SERVICE",
                        "PT DIVA CAHAYA SEJAHTERA",
                        "PT UNGGUL DINAMIKA UTAMA",
                        "PT REZEKI BORNEO SEBUKU",
                        "PT DAHANA",
                        "PT MEGA GLOBAL ENERGY",
                        "PT BERLIAN DUTA ENERGI",
                        "PT SAMUDERA MAJU PERKASA",
                        "PT GRAHA PRIMA ENERGI",
                        "PT KARUNIA ARMADA INDONESIA"
                    };
                    companiesToCompare = allCompanies.Where(c => coreCompaniesList.Contains(c.NamaPerusahaan ?? "")).ToList();
                }
                else if (selectedCompanyId > 0)
                {
                    var childIds = allCompanies.Where(c => c.PerusahaanIndukId == selectedCompanyId).Select(c => c.PerusahaanId).ToList();
                    var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();
                    var relationChildIds = relations.Where(r => r.ParentCompanyId == selectedCompanyId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                    var allChildIds = childIds.Concat(relationChildIds).Distinct().ToList();
                    
                    if (allChildIds.Any())
                    {
                        var targetCompanyIds = new HashSet<int>(allChildIds) { selectedCompanyId };
                        companiesToCompare = allowedCompanies.Where(c => targetCompanyIds.Contains(c.PerusahaanId)).ToList();
                    }
                    else
                    {
                        companiesToCompare = allowedCompanies.Where(c => c.PerusahaanId == selectedCompanyId).ToList();
                    }
                }

                var allEmployees = new List<dynamic>();
                foreach (var comp in companiesToCompare)
                {
                    var compEmps = await GetEmployeesComplianceData(comp.PerusahaanId, null, selectedYear, selectedMonth, selectedCompanyId);
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

                employeesData = allEmployees
                    .Where(e => isAdmin || (isSafetyRole && allowedCompanyIds.Contains((int)e.companyId)) || (int)e.companyId == resolvedCompanyId)
                    .ToList();
            }
            else
            {
                var rawEmployees = await GetEmployeesComplianceData(selectedCompany.PerusahaanId, null, selectedYear, selectedMonth, selectedCompanyId);
                employeesData = rawEmployees;

                deptAchievements = rawEmployees
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
            }

            if (!string.IsNullOrEmpty(departmentName))
            {
                employeesData = employeesData
                    .Where(e => string.Equals((string)e.departmentName, departmentName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var sorted = employeesData
                .Select(e => new {
                    name = (string)e.karyawanName,
                    nik = (string)e.nik,
                    departmentName = (string)e.departmentName,
                    jabatanName = (string)e.jabatanName,
                    complianceRate = (double)e.complianceRate,
                    mtdTotalTarget = (int)e.mtdTotalTarget,
                    onsiteDays = (int)e.onsiteDays,
                    hasRoster = (bool)e.hasRoster,
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
                // ==========================================
                // SHEET 1: KLASEMEN KLUB / PERUSAHAAN
                // ==========================================
                string standingsSheetName = (mode == "company" || mode == "core") ? "Klasemen Perusahaan" : "Klasemen Klub Departemen";
                var wsStandings = workbook.Worksheets.Add(standingsSheetName);
                wsStandings.ShowGridLines = true;

                // Header info
                wsStandings.Cell(1, 1).Value = "LAPORAN KLASEMEN SAFETY ACCOUNTABILITY PROGRAM (SAP)";
                wsStandings.Cell(1, 1).Style.Font.Bold = true;
                wsStandings.Cell(1, 1).Style.Font.FontSize = 14;
                wsStandings.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0f172a");

                wsStandings.Cell(2, 1).Value = $"PERIODE: {periodFormatted}";
                wsStandings.Cell(2, 1).Style.Font.Bold = true;
                wsStandings.Cell(2, 1).Style.Font.FontSize = 11;
                wsStandings.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#1e40af");

                wsStandings.Cell(3, 1).Value = $"Perusahaan: {selectedCompany.NamaPerusahaan}";
                wsStandings.Cell(3, 1).Style.Font.Bold = true;
                wsStandings.Cell(3, 1).Style.Font.FontSize = 10;

                wsStandings.Cell(4, 1).Value = $"Kategori: {modeLabel} | Waktu Ekspor: {DateTime.Now:dd-MM-yyyy HH:mm} WIB";
                wsStandings.Cell(4, 1).Style.Font.Italic = true;
                wsStandings.Cell(4, 1).Style.Font.FontSize = 9;
                wsStandings.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");

                string clubHeader = (mode == "company" || mode == "core") ? "Klub (Perusahaan)" : "Klub (Departemen)";
                string[] stdHeaders = new[] {
                    "Pos", clubHeader, "Skuad (Orang)", "Total Target", "Kepatuhan SAP (%)",
                    "Hazard (%)", "Inspeksi (%)", "Safety Talk (%)", "Observasi (%)", "Coaching (%)", "P5M (%) *"
                };

                for (int i = 0; i < stdHeaders.Length; i++)
                {
                    var cell = wsStandings.Cell(6, i + 1);
                    cell.Value = stdHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    if (i == 10)
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#78350f"); // Amber P5M
                    else
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a"); // Navy
                    cell.Style.Font.FontColor = XLColor.White;
                }
                wsStandings.Row(6).Height = 25;

                int sRow = 7;
                int sRank = 1;

                void SetRateCell(IXLCell cell, double rate)
                {
                    if (rate < 0)
                    {
                        cell.Value = "N/A";
                        cell.Style.Font.FontColor = XLColor.FromHtml("#64748b");
                    }
                    else
                    {
                        cell.Value = rate;
                        cell.Style.NumberFormat.Format = "0.0\"%\"";
                        if (rate >= 80) cell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                        else if (rate >= 40) cell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");
                        else cell.Style.Font.FontColor = XLColor.FromHtml("#000000");
                    }
                }

                if (mode == "company" || mode == "core")
                {
                    var sortedCompanyStandings = companyStandings
                        .Where(x => (int)x.TotalTarget > 0)
                        .OrderByDescending(x => (double)x.MtdAchievementRate)
                        .ToList();

                    foreach (var comp in sortedCompanyStandings)
                    {
                        wsStandings.Cell(sRow, 1).Value = sRank;
                        wsStandings.Cell(sRow, 2).Value = (string)comp.CompanyName;
                        wsStandings.Cell(sRow, 3).Value = (int)comp.EmployeeCount;
                        wsStandings.Cell(sRow, 4).Value = (int)comp.TotalTarget;

                        wsStandings.Cell(sRow, 5).Value = (double)comp.MtdAchievementRate;
                        wsStandings.Cell(sRow, 5).Style.NumberFormat.Format = "0.0\"%\"";
                        wsStandings.Cell(sRow, 5).Style.Font.Bold = true;

                        SetRateCell(wsStandings.Cell(sRow, 6), (double)comp.MtdHazardRate);
                        SetRateCell(wsStandings.Cell(sRow, 7), (double)comp.MtdInspeksiRate);
                        SetRateCell(wsStandings.Cell(sRow, 8), (double)comp.MtdSafetyTalkRate);
                        SetRateCell(wsStandings.Cell(sRow, 9), (double)comp.MtdObservasiRate);
                        SetRateCell(wsStandings.Cell(sRow, 10), (double)comp.MtdCoachingRate);
                        SetRateCell(wsStandings.Cell(sRow, 11), (double)comp.MtdP5mRate);

                        // Alignments
                        wsStandings.Cell(sRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsStandings.Cell(sRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        wsStandings.Cell(sRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsStandings.Cell(sRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsStandings.Cell(sRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        for (int c = 6; c <= 11; c++)
                        {
                            wsStandings.Cell(sRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }

                        var sRowRange = wsStandings.Range(sRow, 1, sRow, 11);
                        sRowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        sRowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                        sRowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        sRowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");

                        double achRate = (double)comp.MtdAchievementRate;
                        int totTgt = (int)comp.TotalTarget;
                        if (sRank == 1) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef08a");
                        else if (sRank == 2) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                        else if (sRank == 3) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#ffedd5");
                        else if (totTgt > 0 && achRate == 0) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
                        else if (sRow % 2 == 0) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                        sRow++;
                        sRank++;
                    }
                }
                else
                {
                    var sortedDept = deptAchievements
                        .Where(d => d.TotalTarget > 0)
                        .OrderByDescending(d => d.MtdAchievementRate)
                        .ToList();

                    foreach (var dept in sortedDept)
                    {
                        wsStandings.Cell(sRow, 1).Value = sRank;
                        wsStandings.Cell(sRow, 2).Value = dept.DepartmentName;
                        wsStandings.Cell(sRow, 3).Value = dept.EmployeeCount;
                        wsStandings.Cell(sRow, 4).Value = dept.TotalTarget;

                        wsStandings.Cell(sRow, 5).Value = dept.MtdAchievementRate;
                        wsStandings.Cell(sRow, 5).Style.NumberFormat.Format = "0.0\"%\"";
                        wsStandings.Cell(sRow, 5).Style.Font.Bold = true;

                        SetRateCell(wsStandings.Cell(sRow, 6), dept.MtdHazardRate);
                        SetRateCell(wsStandings.Cell(sRow, 7), dept.MtdInspeksiRate);
                        SetRateCell(wsStandings.Cell(sRow, 8), dept.MtdSafetyTalkRate);
                        SetRateCell(wsStandings.Cell(sRow, 9), dept.MtdObservasiRate);
                        SetRateCell(wsStandings.Cell(sRow, 10), dept.MtdCoachingRate);
                        SetRateCell(wsStandings.Cell(sRow, 11), dept.MtdP5mRate);

                        // Alignments
                        wsStandings.Cell(sRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsStandings.Cell(sRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        wsStandings.Cell(sRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsStandings.Cell(sRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsStandings.Cell(sRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        for (int c = 6; c <= 11; c++)
                        {
                            wsStandings.Cell(sRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }

                        var sRowRange = wsStandings.Range(sRow, 1, sRow, 11);
                        sRowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        sRowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                        sRowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        sRowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");

                        if (sRank == 1) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef08a");
                        else if (sRank == 2) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                        else if (sRank == 3) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#ffedd5");
                        else if (dept.TotalTarget > 0 && dept.MtdAchievementRate == 0) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
                        else if (sRow % 2 == 0) sRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                        sRow++;
                        sRank++;
                    }
                }

                wsStandings.SheetView.FreezeRows(6);
                if (sRow > 7)
                {
                    wsStandings.Range(6, 1, sRow - 1, 11).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    wsStandings.Range(6, 1, sRow - 1, 11).Style.Border.OutsideBorderColor = XLColor.FromHtml("#0f172a");
                }
                wsStandings.Columns().AdjustToContents();
                foreach (var col in wsStandings.ColumnsUsed())
                {
                    if (col.Width < 12) col.Width = 12;
                }

                // ==========================================
                // SHEET 2: DETAIL SKUAD PEMAIN K3
                // ==========================================
                var wsSquad = workbook.Worksheets.Add("Skuad Pemain K3");
                wsSquad.ShowGridLines = true;
                
                // Add header info
                wsSquad.Cell(1, 1).Value = "LAPORAN KLASEMEN SKUAD KEPATUHAN PEMAIN SAP";
                wsSquad.Cell(1, 1).Style.Font.Bold = true;
                wsSquad.Cell(1, 1).Style.Font.FontSize = 14;
                wsSquad.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0f172a");

                wsSquad.Cell(2, 1).Value = $"PERIODE: {periodFormatted}";
                wsSquad.Cell(2, 1).Style.Font.Bold = true;
                wsSquad.Cell(2, 1).Style.Font.FontSize = 11;
                wsSquad.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#1e40af");

                string deptFilterText = !string.IsNullOrEmpty(departmentName) ? $" | Filter Departemen: {departmentName}" : "";
                wsSquad.Cell(3, 1).Value = $"Perusahaan: {selectedCompany.NamaPerusahaan}{deptFilterText}";
                wsSquad.Cell(3, 1).Style.Font.Bold = true;
                wsSquad.Cell(3, 1).Style.Font.FontSize = 10;

                wsSquad.Cell(4, 1).Value = $"Kategori: {modeLabel} | Total Pemain: {sorted.Count} Orang | Waktu Ekspor: {DateTime.Now:dd-MM-yyyy HH:mm} WIB";
                wsSquad.Cell(4, 1).Style.Font.Italic = true;
                wsSquad.Cell(4, 1).Style.Font.FontSize = 9;
                wsSquad.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");

                // Setup Table Headers
                string[] squadHeaders = new[] {
                    "Peringkat", "Nama Karyawan", "NIK", "Departemen", "Jabatan", "Status Roster", "Kepatuhan (%)",
                    "Hazard Actual", "Hazard Target", "Inspeksi Actual", "Inspeksi Target",
                    "Safety Talk Actual", "Safety Talk Target", "Observasi Actual", "Observasi Target",
                    "Coaching Actual", "Coaching Target", "P5M Actual *", "P5M Target"
                };

                for (int i = 0; i < squadHeaders.Length; i++)
                {
                    var cell = wsSquad.Cell(6, i + 1);
                    cell.Value = squadHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    if (i == 17 || i == 18) // P5M
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#78350f"); // Amber
                    else
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a"); // Navy
                    cell.Style.Font.FontColor = XLColor.White;
                }
                wsSquad.Row(6).Height = 25;

                int row = 7;
                int rank = 1;
                foreach (var emp in sorted)
                {
                    wsSquad.Cell(row, 1).Value = rank;
                    wsSquad.Cell(row, 2).Value = emp.name;
                    wsSquad.Cell(row, 3).Value = emp.nik;
                    wsSquad.Cell(row, 4).Value = emp.departmentName;
                    wsSquad.Cell(row, 5).Value = emp.jabatanName;
                    wsSquad.Cell(row, 6).Value = emp.hasRoster ? $"{emp.onsiteDays} Hari Onsite" : "Belum Roster";
                    
                    var compCell = wsSquad.Cell(row, 7);
                    compCell.Value = emp.complianceRate;
                    compCell.Style.NumberFormat.Format = "0.0\"%\"";

                    wsSquad.Cell(row, 8).Value = emp.hazard.actual;
                    wsSquad.Cell(row, 9).Value = emp.hazard.target;
                    
                    wsSquad.Cell(row, 10).Value = emp.inspeksi.actual;
                    wsSquad.Cell(row, 11).Value = emp.inspeksi.target;

                    wsSquad.Cell(row, 12).Value = emp.safetyTalk.actual;
                    wsSquad.Cell(row, 13).Value = emp.safetyTalk.target;

                    wsSquad.Cell(row, 14).Value = emp.observasi.actual;
                    wsSquad.Cell(row, 15).Value = emp.observasi.target;

                    wsSquad.Cell(row, 16).Value = emp.coaching.actual;
                    wsSquad.Cell(row, 17).Value = emp.coaching.target;

                    wsSquad.Cell(row, 18).Value = emp.p5m.actual;
                    wsSquad.Cell(row, 19).Value = emp.p5m.target;

                    // Alignments
                    wsSquad.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSquad.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    wsSquad.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSquad.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    wsSquad.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    wsSquad.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSquad.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    for (int c = 8; c <= 19; c++)
                    {
                        wsSquad.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }

                    // Format values as number
                    for (int c = 8; c <= 19; c++)
                    {
                        wsSquad.Cell(row, c).Style.NumberFormat.Format = "#,##0";
                        if (c % 2 == 0) // Actual columns
                        {
                            wsSquad.Cell(row, c).Style.Font.Bold = true;
                        }
                    }

                    // Conditional Formatting for Compliance Rate
                    if (emp.complianceRate >= 100)
                        compCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a"); // Green
                    else if (emp.complianceRate >= 80)
                        compCell.Style.Font.FontColor = XLColor.FromHtml("#2563eb"); // Blue
                    else
                        compCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626"); // Red
                        
                    compCell.Style.Font.Bold = true;

                    // Border styling
                    var rowRange = wsSquad.Range(row, 1, row, 19);
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
                        wsSquad.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#b91c1c"); // Dark red name
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
                wsSquad.SheetView.FreezeRows(6);
                
                // Add thick outer border to the entire table
                if (row > 7)
                {
                    wsSquad.Range(6, 1, row - 1, 19).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    wsSquad.Range(6, 1, row - 1, 19).Style.Border.OutsideBorderColor = XLColor.FromHtml("#0f172a");
                }

                // Auto fit columns
                wsSquad.Columns().AdjustToContents();
                foreach (var col in wsSquad.ColumnsUsed())
                {
                    if (col.Width < 12) col.Width = 12;
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string safeCompName = string.Concat((selectedCompany.NamaPerusahaan ?? "Company").Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                    string safeDeptName = string.IsNullOrEmpty(departmentName) ? "" : $"_{string.Concat(departmentName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_")}";
                    string fileName = $"Laporan_League_SAP_{safeCompName}{safeDeptName}_{monthName}_{selectedYear}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportSapDetailToExcel(int? companyId = null, string mode = "dept", string? departmentName = null, int? year = null, int? month = null)
        {
            await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;

            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            string[] monthNames = { "", "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
            string monthName = (selectedMonth >= 1 && selectedMonth <= 12) ? monthNames[selectedMonth] : selectedMonth.ToString();
            string periodFormatted = $"{monthName.ToUpper()} {selectedYear}";

            var (resolvedCompanyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);

            // Access Control: Only Admin or Safety / OHS / HSE departments can download Detail SAP + AI
            if (!isAdmin && !isSafetyRole)
            {
                return Forbid();
            }

            var allCompanies = await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            List<PerusahaanView> allowedCompanies;
            if (isAdmin)
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

            int defaultCompanyId = 0;
            var userCompanyStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(userCompanyStr, out int parsedUserCompanyId) && parsedUserCompanyId > 0)
            {
                defaultCompanyId = parsedUserCompanyId;
            }
            else
            {
                defaultCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            int selectedCompanyId = companyId ?? defaultCompanyId;
            if (!isAdmin && !allowedCompanyIds.Contains(selectedCompanyId))
            {
                selectedCompanyId = resolvedCompanyId ?? allowedCompanies.First().PerusahaanId;
            }

            var selectedCompany = allCompanies.FirstOrDefault(c => c.PerusahaanId == selectedCompanyId) ?? allowedCompanies.First();

            var targetCompanyIds = new HashSet<int>();
            if (mode == "core")
            {
                var coreCompaniesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "PT PELAYARAN GANESHA LAUTJAYA", "PT SUCOFINDO", "PT KALIMANTAN PRIMA PERSADA",
                    "PT ELA SANGATTA", "PT ADHITAMA WIJAYA PERKASA", "PT TUNAS JAYA PERKASA",
                    "PT SEMESTA MANDIRI INDONESIA", "PT BANDANG MINING COAL", "PT ORICA MINING SERVICE",
                    "PT DIVA CAHAYA SEJAHTERA", "PT UNGGUL DINAMIKA UTAMA", "PT REZEKI BORNEO SEBUKU",
                    "PT DAHANA", "PT MEGA GLOBAL ENERGY", "PT BERLIAN DUTA ENERGI",
                    "PT SAMUDERA MAJU PERKASA", "PT GRAHA PRIMA ENERGI", "PT KARUNIA ARMADA INDONESIA"
                };
                var coreComps = allCompanies.Where(c => coreCompaniesList.Contains(c.NamaPerusahaan ?? "")).Select(c => c.PerusahaanId);
                foreach (var id in coreComps) targetCompanyIds.Add(id);
            }
            else if (mode == "company" && selectedCompanyId > 0)
            {
                var childIds = allCompanies.Where(c => c.PerusahaanIndukId == selectedCompanyId).Select(c => c.PerusahaanId).ToList();
                var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();
                var relationChildIds = relations.Where(r => r.ParentCompanyId == selectedCompanyId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
                var allChildIds = childIds.Concat(relationChildIds).Distinct().ToList();

                targetCompanyIds.Add(selectedCompanyId);
                foreach (var id in allChildIds) targetCompanyIds.Add(id);
            }
            else
            {
                targetCompanyIds.Add(selectedCompany.PerusahaanId);
            }

            // Employee scope
            var employeesQuery = _context.Karyawans.AsNoTracking()
                .Where(k => targetCompanyIds.Contains(k.IdPerusahaan) && k.StatusAktif == true);

            if (!string.IsNullOrEmpty(departmentName))
            {
                employeesQuery = from k in employeesQuery
                                 join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId
                                 where d.NamaDepartemen == departmentName
                                 select k;
            }

            var employeeNiksList = await employeesQuery.Select(k => k.NoNik).Where(n => !string.IsNullOrEmpty(n)).ToListAsync();
            var employeeNiksSet = new HashSet<string>(employeeNiksList.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);

            string modeLabel = mode == "company" ? "Super League (Antar Perusahaan)" : (mode == "core" ? "Liga Perusahaan Inti" : "Klasemen Internal (Departemen)");

            // Fetch 7 SAP entities
            var hazards = await _context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth && (targetCompanyIds.Contains(h.PerusahaanId ?? 0) || employeeNiksSet.Contains(h.Nik)))
                .OrderByDescending(h => h.Tanggal).ThenByDescending(h => h.Waktu)
                .ToListAsync();

            var inspections = await _context.Inspections.AsNoTracking()
                .Where(i => !i.IsDeleted && i.Tanggal >= startOfMonth && i.Tanggal <= endOfMonth && (targetCompanyIds.Contains(i.PerusahaanId ?? 0) || employeeNiksSet.Contains(i.Nik)))
                .OrderByDescending(i => i.Tanggal).ThenByDescending(i => i.Waktu)
                .ToListAsync();

            var actionPlans = await _context.ActionPlans.AsNoTracking()
                .Where(a => !a.IsDeleted && a.Tanggal >= startOfMonth && a.Tanggal <= endOfMonth && (targetCompanyIds.Contains(a.PerusahaanId ?? 0) || employeeNiksSet.Contains(a.Nik)))
                .OrderByDescending(a => a.Tanggal).ThenByDescending(a => a.Waktu)
                .ToListAsync();

            var safetyTalks = await _context.SafetyTalks.AsNoTracking()
                .Where(s => !s.IsDeleted && s.Tanggal >= startOfMonth && s.Tanggal <= endOfMonth && (targetCompanyIds.Contains(s.PerusahaanId ?? 0) || employeeNiksSet.Contains(s.Nik)))
                .OrderByDescending(s => s.Tanggal).ThenByDescending(s => s.Waktu)
                .ToListAsync();

            var observations = await _context.Observations.AsNoTracking()
                .Where(o => !o.IsDeleted && o.Date >= startOfMonth && o.Date <= endOfMonth && employeeNiksSet.Contains(o.Nik))
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            var coachings = await _context.Coachings.AsNoTracking()
                .Where(c => !c.IsDeleted && c.Tanggal >= startOfMonth && c.Tanggal <= endOfMonth && (targetCompanyIds.Contains(c.PerusahaanId ?? 0) || employeeNiksSet.Contains(c.Nik)))
                .OrderByDescending(c => c.Tanggal).ThenByDescending(c => c.Waktu)
                .ToListAsync();

            var p5ms = await _context.P5ms.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Tanggal >= startOfMonth && p.Tanggal <= endOfMonth && (targetCompanyIds.Contains(p.PerusahaanId ?? 0) || employeeNiksSet.Contains(p.Nik)))
                .OrderByDescending(p => p.Tanggal).ThenByDescending(p => p.Waktu)
                .ToListAsync();

            // Load assessment cache
            var assessments = await _context.SapQualityAssessments.AsNoTracking().ToListAsync();
            var assessDict = assessments
                .GroupBy(a => $"{a.ProgramType}_{a.ProgramId}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            (int Rating, string Notes) GetQuality(string progType, int progId, string title, string description)
            {
                string key = $"{progType}_{progId}";
                if (assessDict.TryGetValue(key, out var existing))
                {
                    return (existing.Rating, existing.Notes ?? "");
                }
                return Services.SapQualityMlEngine.AssessQuality(progType, title, description);
            }

            string GetStarString(int rating)
            {
                return rating switch
                {
                    5 => "⭐⭐⭐⭐⭐ (5/5)",
                    4 => "⭐⭐⭐⭐ (4/5)",
                    3 => "⭐⭐⭐ (3/5)",
                    2 => "⭐⭐ (2/5)",
                    1 => "⭐ (1/5)",
                    _ => $"{rating}/5"
                };
            }

            XLColor GetStarColor(int rating)
            {
                return rating switch
                {
                    5 => XLColor.FromHtml("#15803d"), // Dark green
                    4 => XLColor.FromHtml("#1d4ed8"), // Dark blue
                    3 => XLColor.FromHtml("#b45309"), // Dark amber
                    2 => XLColor.FromHtml("#be123c"), // Dark rose
                    1 => XLColor.FromHtml("#991b1b"), // Dark red
                    _ => XLColor.Black
                };
            }

            void StyleSheetHeader(IXLWorksheet ws, string title, int colCount, int dataCount)
            {
                ws.ShowGridLines = true;
                ws.Cell(1, 1).Value = $"LAPORAN DETAIL SAP - {title.ToUpper()}";
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 13;
                ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0f172a");

                ws.Cell(2, 1).Value = $"PERIODE: {periodFormatted}";
                ws.Cell(2, 1).Style.Font.Bold = true;
                ws.Cell(2, 1).Style.Font.FontSize = 11;
                ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#1e40af");

                string deptFilterText = !string.IsNullOrEmpty(departmentName) ? $" | Filter Departemen: {departmentName}" : "";
                ws.Cell(3, 1).Value = $"Perusahaan: {selectedCompany.NamaPerusahaan}{deptFilterText}";
                ws.Cell(3, 1).Style.Font.Bold = true;
                ws.Cell(3, 1).Style.Font.FontSize = 10;

                ws.Cell(4, 1).Value = $"Kategori: {modeLabel} | Total Data: {dataCount} Laporan | Tanggal Ekspor: {DateTime.Now:dd-MM-yyyy HH:mm} WIB";
                ws.Cell(4, 1).Style.Font.Italic = true;
                ws.Cell(4, 1).Style.Font.FontSize = 9;
                ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");
            }

            void SetupTableHeaders(IXLWorksheet ws, int row, string[] headers)
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(row, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    if (headers[i].Contains("AI") || headers[i].Contains("Rating") || headers[i].Contains("Bintang"))
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f766e"); // Teal for AI Quality
                    }
                    else if (headers[i].Contains("P5M"))
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#78350f"); // Amber
                    }
                    else
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a"); // Navy
                    }
                    cell.Style.Font.FontColor = XLColor.White;
                }
                ws.Row(row).Height = 26;
            }

            // Quality metrics tracking for summary
            var qualityStats = new Dictionary<string, (int Total, int S5, int S4, int S3, int S2, int S1, double SumScore)>();
            var contributorStats = new Dictionary<string, (string Nama, string NIK, string Dept, int Total, int HighQuality, double SumScore)>(StringComparer.OrdinalIgnoreCase);

            void TrackItemQuality(string progName, string? nama, string? nik, string? dept, int rating)
            {
                if (!qualityStats.ContainsKey(progName))
                {
                    qualityStats[progName] = (0, 0, 0, 0, 0, 0, 0);
                }
                var cur = qualityStats[progName];
                qualityStats[progName] = (
                    cur.Total + 1,
                    cur.S5 + (rating == 5 ? 1 : 0),
                    cur.S4 + (rating == 4 ? 1 : 0),
                    cur.S3 + (rating == 3 ? 1 : 0),
                    cur.S2 + (rating == 2 ? 1 : 0),
                    cur.S1 + (rating == 1 ? 1 : 0),
                    cur.SumScore + rating
                );

                string cleanNik = (nik ?? "").Trim();
                if (!string.IsNullOrEmpty(cleanNik))
                {
                    if (!contributorStats.ContainsKey(cleanNik))
                    {
                        contributorStats[cleanNik] = (nama ?? cleanNik, cleanNik, dept ?? "-", 0, 0, 0);
                    }
                    var c = contributorStats[cleanNik];
                    contributorStats[cleanNik] = (
                        c.Nama,
                        c.NIK,
                        c.Dept,
                        c.Total + 1,
                        c.HighQuality + (rating >= 4 ? 1 : 0),
                        c.SumScore + rating
                    );
                }
            }

            using (var workbook = new XLWorkbook())
            {
                // =========================================================================
                // SHEET 2: HAZARD REPORT
                // =========================================================================
                var wsHazard = workbook.Worksheets.Add("Hazard Report");
                StyleSheetHeader(wsHazard, "Hazard Report", 19, hazards.Count);
                string[] hazardHeaders = new[] {
                    "No", "Tanggal", "Waktu", "Nama Pelapor", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Rating Kualitas AI", "Catatan Evaluasi AI", "Temuan / Deskripsi Bahaya", "Kategori Bahaya",
                    "Jenis Bahaya", "Jenis Ketidaksesuaian", "Tingkat Resiko", "Tindakan Perbaikan", "PJA", "Status Temuan"
                };
                SetupTableHeaders(wsHazard, 6, hazardHeaders);

                int hRow = 7;
                for (int i = 0; i < hazards.Count; i++)
                {
                    var r = hazards[i];
                    var (rating, notes) = GetQuality("Hazard", r.Id, "Hazard Report", $"{r.Temuan} {r.TindakanPerbaikan}");
                    TrackItemQuality("Hazard Report", r.Nama, r.Nik, r.Departemen, rating);

                    wsHazard.Cell(hRow, 1).Value = i + 1;
                    wsHazard.Cell(hRow, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                    wsHazard.Cell(hRow, 3).Value = r.Waktu.ToString("hh\\:mm");
                    wsHazard.Cell(hRow, 4).Value = r.Nama;
                    wsHazard.Cell(hRow, 5).Value = r.Nik;
                    wsHazard.Cell(hRow, 6).Value = r.Departemen ?? "";
                    wsHazard.Cell(hRow, 7).Value = r.Area ?? "";
                    wsHazard.Cell(hRow, 8).Value = r.Lokasi ?? "";
                    wsHazard.Cell(hRow, 9).Value = r.DetilLokasi ?? "";
                    
                    var rateCell = wsHazard.Cell(hRow, 10);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsHazard.Cell(hRow, 11).Value = notes;
                    wsHazard.Cell(hRow, 12).Value = r.Temuan;
                    wsHazard.Cell(hRow, 13).Value = r.KategoriBahaya ?? "";
                    wsHazard.Cell(hRow, 14).Value = r.JenisBahaya ?? "";
                    wsHazard.Cell(hRow, 15).Value = r.JenisKetidaksesuaian ?? "";
                    wsHazard.Cell(hRow, 16).Value = r.TingkatResiko ?? "";
                    wsHazard.Cell(hRow, 17).Value = r.TindakanPerbaikan ?? "";
                    wsHazard.Cell(hRow, 18).Value = r.Pja ?? "";
                    wsHazard.Cell(hRow, 19).Value = r.StatusTemuan ?? "";

                    wsHazard.Cell(hRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsHazard.Cell(hRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsHazard.Cell(hRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsHazard.Cell(hRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsHazard.Cell(hRow, 16).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsHazard.Cell(hRow, 19).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsHazard.Range(hRow, 1, hRow, 19);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (hRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    hRow++;
                }
                wsHazard.SheetView.FreezeRows(6);
                wsHazard.Columns().AdjustToContents();
                foreach (var col in wsHazard.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // SHEET 3: INSPEKSI K3
                // =========================================================================
                var wsInspection = workbook.Worksheets.Add("Inspeksi K3");
                StyleSheetHeader(wsInspection, "Inspeksi K3", 14, inspections.Count);
                string[] inspectionHeaders = new[] {
                    "No", "Tanggal", "Waktu", "Nama Pelapor", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Jenis Inspeksi", "Rating Kualitas AI", "Catatan Evaluasi AI", "PJA", "Catatan Temuan Lapangan"
                };
                SetupTableHeaders(wsInspection, 6, inspectionHeaders);

                int insRow = 7;
                for (int i = 0; i < inspections.Count; i++)
                {
                    var r = inspections[i];
                    var (rating, notes) = GetQuality("Inspection", r.Id, $"Inspeksi {r.JenisInspeksi}", r.Catatan ?? "");
                    TrackItemQuality("Inspeksi K3", r.Nama, r.Nik, r.Departemen, rating);

                    wsInspection.Cell(insRow, 1).Value = i + 1;
                    wsInspection.Cell(insRow, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                    wsInspection.Cell(insRow, 3).Value = r.Waktu.ToString("hh\\:mm");
                    wsInspection.Cell(insRow, 4).Value = r.Nama;
                    wsInspection.Cell(insRow, 5).Value = r.Nik;
                    wsInspection.Cell(insRow, 6).Value = r.Departemen ?? "";
                    wsInspection.Cell(insRow, 7).Value = r.Area ?? "";
                    wsInspection.Cell(insRow, 8).Value = r.Lokasi ?? "";
                    wsInspection.Cell(insRow, 9).Value = r.DetilLokasi ?? "";
                    wsInspection.Cell(insRow, 10).Value = r.JenisInspeksi ?? "";

                    var rateCell = wsInspection.Cell(insRow, 11);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsInspection.Cell(insRow, 12).Value = notes;
                    wsInspection.Cell(insRow, 13).Value = r.Pja ?? "";
                    wsInspection.Cell(insRow, 14).Value = r.Catatan ?? "";

                    wsInspection.Cell(insRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsInspection.Cell(insRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsInspection.Cell(insRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsInspection.Cell(insRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsInspection.Range(insRow, 1, insRow, 14);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (insRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    insRow++;
                }
                wsInspection.SheetView.FreezeRows(6);
                wsInspection.Columns().AdjustToContents();
                foreach (var col in wsInspection.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // SHEET 4: ACTION PLAN
                // =========================================================================
                var wsActionPlan = workbook.Worksheets.Add("Action Plan");
                StyleSheetHeader(wsActionPlan, "Action Plan", 22, actionPlans.Count);
                string[] apHeaders = new[] {
                    "No", "Tanggal", "Waktu", "Nama Pelapor", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Item SAP", "Kategori Temuan", "Rating Kualitas AI", "Catatan Evaluasi AI", "Detil Temuan", "Status", "PIC",
                    "Rencana Perbaikan", "Tgl Rencana", "Realisasi Perbaikan", "Tgl Realisasi", "Overdue", "Alasan Overdue"
                };
                SetupTableHeaders(wsActionPlan, 6, apHeaders);

                int apRow = 7;
                for (int i = 0; i < actionPlans.Count; i++)
                {
                    var r = actionPlans[i];
                    var (rating, notes) = GetQuality("Hazard", r.Id, $"Action Plan {r.ItemSap}", $"{r.DetilTemuan} {r.RencanaPerbaikan} {r.Perbaikan}");
                    TrackItemQuality("Action Plan", r.Nama, r.Nik, r.Departemen, rating);

                    wsActionPlan.Cell(apRow, 1).Value = i + 1;
                    wsActionPlan.Cell(apRow, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                    wsActionPlan.Cell(apRow, 3).Value = r.Waktu.ToString("hh\\:mm");
                    wsActionPlan.Cell(apRow, 4).Value = r.Nama;
                    wsActionPlan.Cell(apRow, 5).Value = r.Nik;
                    wsActionPlan.Cell(apRow, 6).Value = r.Departemen ?? "";
                    wsActionPlan.Cell(apRow, 7).Value = r.Area ?? "";
                    wsActionPlan.Cell(apRow, 8).Value = r.Lokasi ?? "";
                    wsActionPlan.Cell(apRow, 9).Value = r.DetilLokasi ?? "";
                    wsActionPlan.Cell(apRow, 10).Value = r.ItemSap ?? "";
                    wsActionPlan.Cell(apRow, 11).Value = r.KategoriTemuan ?? "";

                    var rateCell = wsActionPlan.Cell(apRow, 12);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsActionPlan.Cell(apRow, 13).Value = notes;
                    wsActionPlan.Cell(apRow, 14).Value = r.DetilTemuan ?? "";
                    wsActionPlan.Cell(apRow, 15).Value = r.Status ?? "";
                    wsActionPlan.Cell(apRow, 16).Value = r.Pic ?? "";
                    wsActionPlan.Cell(apRow, 17).Value = r.RencanaPerbaikan ?? "";
                    wsActionPlan.Cell(apRow, 18).Value = r.TanggalRencanaPerbaikan.HasValue ? r.TanggalRencanaPerbaikan.Value.ToString("yyyy-MM-dd") : "-";
                    wsActionPlan.Cell(apRow, 19).Value = r.Perbaikan ?? "";
                    wsActionPlan.Cell(apRow, 20).Value = r.TanggalPerbaikan.HasValue ? r.TanggalPerbaikan.Value.ToString("yyyy-MM-dd") : "-";
                    wsActionPlan.Cell(apRow, 21).Value = r.Overdue ?? "";
                    wsActionPlan.Cell(apRow, 22).Value = r.AlasanOverdue ?? "";

                    wsActionPlan.Cell(apRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsActionPlan.Cell(apRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsActionPlan.Cell(apRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsActionPlan.Cell(apRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsActionPlan.Cell(apRow, 15).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsActionPlan.Cell(apRow, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsActionPlan.Cell(apRow, 20).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsActionPlan.Range(apRow, 1, apRow, 22);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (apRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    apRow++;
                }
                wsActionPlan.SheetView.FreezeRows(6);
                wsActionPlan.Columns().AdjustToContents();
                foreach (var col in wsActionPlan.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // SHEET 5: SAFETY TALK
                // =========================================================================
                var wsSafetyTalk = workbook.Worksheets.Add("Safety Talk");
                StyleSheetHeader(wsSafetyTalk, "Safety Talk", 13, safetyTalks.Count);
                string[] stHeaders = new[] {
                    "No", "Tanggal", "Waktu", "Nama Pembicara", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Judul Safety Talk", "Rating Kualitas AI", "Catatan Evaluasi AI", "Keterangan Materi"
                };
                SetupTableHeaders(wsSafetyTalk, 6, stHeaders);

                int stRow = 7;
                for (int i = 0; i < safetyTalks.Count; i++)
                {
                    var r = safetyTalks[i];
                    var (rating, notes) = GetQuality("SafetyTalk", r.Id, r.Judul ?? "Safety Talk", $"{r.Judul} {r.Keterangan}");
                    TrackItemQuality("Safety Talk", r.Nama, r.Nik, r.Departemen, rating);

                    wsSafetyTalk.Cell(stRow, 1).Value = i + 1;
                    wsSafetyTalk.Cell(stRow, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                    wsSafetyTalk.Cell(stRow, 3).Value = r.Waktu.ToString("hh\\:mm");
                    wsSafetyTalk.Cell(stRow, 4).Value = r.Nama;
                    wsSafetyTalk.Cell(stRow, 5).Value = r.Nik;
                    wsSafetyTalk.Cell(stRow, 6).Value = r.Departemen ?? "";
                    wsSafetyTalk.Cell(stRow, 7).Value = r.Area ?? "";
                    wsSafetyTalk.Cell(stRow, 8).Value = r.Lokasi ?? "";
                    wsSafetyTalk.Cell(stRow, 9).Value = r.DetilLokasi ?? "";
                    wsSafetyTalk.Cell(stRow, 10).Value = r.Judul ?? "";

                    var rateCell = wsSafetyTalk.Cell(stRow, 11);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsSafetyTalk.Cell(stRow, 12).Value = notes;
                    wsSafetyTalk.Cell(stRow, 13).Value = r.Keterangan ?? "";

                    wsSafetyTalk.Cell(stRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSafetyTalk.Cell(stRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSafetyTalk.Cell(stRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSafetyTalk.Cell(stRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSafetyTalk.Range(stRow, 1, stRow, 13);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (stRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    stRow++;
                }
                wsSafetyTalk.SheetView.FreezeRows(6);
                wsSafetyTalk.Columns().AdjustToContents();
                foreach (var col in wsSafetyTalk.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // SHEET 6: OBSERVASI K3
                // =========================================================================
                var wsObservation = workbook.Worksheets.Add("Observasi K3");
                StyleSheetHeader(wsObservation, "Observasi K3", 17, observations.Count);
                string[] obsHeaders = new[] {
                    "No", "Tanggal", "Nama Observer", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Kegiatan Diamati", "Departemen Diamati", "Resiko Kritis", "Tingkat Resiko", "Perihal Diamati",
                    "Hasil Observasi", "Rating Kualitas AI", "Catatan Evaluasi AI", "Keterangan"
                };
                SetupTableHeaders(wsObservation, 6, obsHeaders);

                int obsRow = 7;
                for (int i = 0; i < observations.Count; i++)
                {
                    var r = observations[i];
                    var (rating, notes) = GetQuality("Observation", r.Id, $"Observasi {r.PerihalYangDiamati}", $"{r.KegiatanYangDiamati} | Hasil: {r.HasilObservasi} | Ket: {r.Keterangan}");
                    TrackItemQuality("Observasi K3", r.Nama, r.Nik, r.Departemen, rating);

                    wsObservation.Cell(obsRow, 1).Value = i + 1;
                    wsObservation.Cell(obsRow, 2).Value = r.Date.ToString("yyyy-MM-dd");
                    wsObservation.Cell(obsRow, 3).Value = r.Nama;
                    wsObservation.Cell(obsRow, 4).Value = r.Nik;
                    wsObservation.Cell(obsRow, 5).Value = r.Departemen;
                    wsObservation.Cell(obsRow, 6).Value = r.Area;
                    wsObservation.Cell(obsRow, 7).Value = r.Lokasi;
                    wsObservation.Cell(obsRow, 8).Value = r.DetilLokasi ?? "";
                    wsObservation.Cell(obsRow, 9).Value = r.KegiatanYangDiamati ?? "";
                    wsObservation.Cell(obsRow, 10).Value = r.DepartemenYangDiamati ?? "";
                    wsObservation.Cell(obsRow, 11).Value = r.ResikoKritis ?? "";
                    wsObservation.Cell(obsRow, 12).Value = r.TingkatResiko ?? "";
                    wsObservation.Cell(obsRow, 13).Value = r.PerihalYangDiamati ?? "";
                    wsObservation.Cell(obsRow, 14).Value = r.HasilObservasi ?? "";

                    var rateCell = wsObservation.Cell(obsRow, 15);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsObservation.Cell(obsRow, 16).Value = notes;
                    wsObservation.Cell(obsRow, 17).Value = r.Keterangan ?? "";

                    wsObservation.Cell(obsRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsObservation.Cell(obsRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsObservation.Cell(obsRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsObservation.Cell(obsRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsObservation.Cell(obsRow, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsObservation.Range(obsRow, 1, obsRow, 17);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (obsRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    obsRow++;
                }
                wsObservation.SheetView.FreezeRows(6);
                wsObservation.Columns().AdjustToContents();
                foreach (var col in wsObservation.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // SHEET 7: COACHING K3
                // =========================================================================
                var wsCoaching = workbook.Worksheets.Add("Coaching K3");
                StyleSheetHeader(wsCoaching, "Coaching K3", 14, coachings.Count);
                string[] coachHeaders = new[] {
                    "No", "Tanggal", "Waktu", "Nama Coach", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Tema Coaching", "Rating Kualitas AI", "Catatan Evaluasi AI", "Feedback", "Komitmen Pekerja"
                };
                SetupTableHeaders(wsCoaching, 6, coachHeaders);

                int cRow = 7;
                for (int i = 0; i < coachings.Count; i++)
                {
                    var r = coachings[i];
                    var (rating, notes) = GetQuality("Coaching", r.Id, r.Tema ?? "Coaching K3", $"{r.Feedback} {r.Komitmen}");
                    TrackItemQuality("Coaching K3", r.Nama, r.Nik, r.Departemen, rating);

                    wsCoaching.Cell(cRow, 1).Value = i + 1;
                    wsCoaching.Cell(cRow, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                    wsCoaching.Cell(cRow, 3).Value = r.Waktu.ToString("hh\\:mm");
                    wsCoaching.Cell(cRow, 4).Value = r.Nama;
                    wsCoaching.Cell(cRow, 5).Value = r.Nik;
                    wsCoaching.Cell(cRow, 6).Value = r.Departemen ?? "";
                    wsCoaching.Cell(cRow, 7).Value = r.Area ?? "";
                    wsCoaching.Cell(cRow, 8).Value = r.Lokasi ?? "";
                    wsCoaching.Cell(cRow, 9).Value = r.DetilLokasi ?? "";
                    wsCoaching.Cell(cRow, 10).Value = r.Tema ?? "";

                    var rateCell = wsCoaching.Cell(cRow, 11);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsCoaching.Cell(cRow, 12).Value = notes;
                    wsCoaching.Cell(cRow, 13).Value = r.Feedback ?? "";
                    wsCoaching.Cell(cRow, 14).Value = r.Komitmen ?? "";

                    wsCoaching.Cell(cRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsCoaching.Cell(cRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsCoaching.Cell(cRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsCoaching.Cell(cRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsCoaching.Range(cRow, 1, cRow, 14);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (cRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    cRow++;
                }
                wsCoaching.SheetView.FreezeRows(6);
                wsCoaching.Columns().AdjustToContents();
                foreach (var col in wsCoaching.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // SHEET 8: P5M
                // =========================================================================
                var wsP5m = workbook.Worksheets.Add("P5M");
                StyleSheetHeader(wsP5m, "P5M", 17, p5ms.Count);
                string[] p5mHeaders = new[] {
                    "No", "Tanggal", "Waktu", "Nama Leader", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi",
                    "Topik P5M", "Judul", "Rating Kualitas AI", "Catatan Evaluasi AI", "Keterangan", "List Pertanyaan", "Jawaban", "Catatan"
                };
                SetupTableHeaders(wsP5m, 6, p5mHeaders);

                int pRow = 7;
                for (int i = 0; i < p5ms.Count; i++)
                {
                    var r = p5ms[i];
                    var (rating, notes) = GetQuality("SafetyTalk", r.Id, r.Judul ?? "P5M", $"{r.Keterangan} {r.ListPertanyaan} {r.Catatan}");
                    TrackItemQuality("P5M", r.Nama, r.Nik, r.Departemen, rating);

                    wsP5m.Cell(pRow, 1).Value = i + 1;
                    wsP5m.Cell(pRow, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                    wsP5m.Cell(pRow, 3).Value = r.Waktu.ToString("hh\\:mm");
                    wsP5m.Cell(pRow, 4).Value = r.Nama;
                    wsP5m.Cell(pRow, 5).Value = r.Nik;
                    wsP5m.Cell(pRow, 6).Value = r.Departemen ?? "";
                    wsP5m.Cell(pRow, 7).Value = r.Area ?? "";
                    wsP5m.Cell(pRow, 8).Value = r.Lokasi ?? "";
                    wsP5m.Cell(pRow, 9).Value = r.DetilLokasi ?? "";
                    wsP5m.Cell(pRow, 10).Value = r.Topik ?? "";
                    wsP5m.Cell(pRow, 11).Value = r.Judul ?? "";

                    var rateCell = wsP5m.Cell(pRow, 12);
                    rateCell.Value = GetStarString(rating);
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.Font.FontColor = GetStarColor(rating);
                    rateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    wsP5m.Cell(pRow, 13).Value = notes;
                    wsP5m.Cell(pRow, 14).Value = r.Keterangan ?? "";
                    wsP5m.Cell(pRow, 15).Value = r.ListPertanyaan ?? "";
                    wsP5m.Cell(pRow, 16).Value = r.Jawaban ?? "";
                    wsP5m.Cell(pRow, 17).Value = r.Catatan ?? "";

                    wsP5m.Cell(pRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsP5m.Cell(pRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsP5m.Cell(pRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsP5m.Cell(pRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsP5m.Range(pRow, 1, pRow, 17);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    if (pRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    pRow++;
                }
                wsP5m.SheetView.FreezeRows(6);
                wsP5m.Columns().AdjustToContents();
                foreach (var col in wsP5m.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                // =========================================================================
                // RETRIEVE COMPLIANCE DATA FOR DEPARTMENTS & EMPLOYEES
                // =========================================================================
                var rawEmployeesCompliance = new List<dynamic>();
                foreach (var targetCompId in targetCompanyIds)
                {
                    var compEmps = await GetEmployeesComplianceData(targetCompId, departmentName, selectedYear, selectedMonth, selectedCompanyId);
                    rawEmployeesCompliance.AddRange(compEmps);
                }

                int GetEmpActual(dynamic emp)
                {
                    try
                    {
                        return (int)emp.hazard.actual + (int)emp.inspeksi.actual + (int)emp.safetyTalk.actual + (int)emp.observasi.actual + (int)emp.coaching.actual + (int)emp.p5m.actual;
                    }
                    catch
                    {
                        return 0;
                    }
                }

                // Department Quality & Contribution mapping
                var deptQualityStats = new Dictionary<string, (int Total, int S5, int S4, int S3, int S2, int S1, double SumScore)>(StringComparer.OrdinalIgnoreCase);
                var deptContribution = new Dictionary<string, (int Hz, int Ins, int Ap, int St, int Obs, int Coach, int P5m, int Total)>(StringComparer.OrdinalIgnoreCase);

                void AddDeptRecord(string? dName, string prog, int rating)
                {
                    string cleanDept = string.IsNullOrWhiteSpace(dName) ? "General" : dName.Trim();
                    
                    if (!deptQualityStats.ContainsKey(cleanDept)) deptQualityStats[cleanDept] = (0, 0, 0, 0, 0, 0, 0);
                    var q = deptQualityStats[cleanDept];
                    deptQualityStats[cleanDept] = (
                        q.Total + 1,
                        q.S5 + (rating == 5 ? 1 : 0),
                        q.S4 + (rating == 4 ? 1 : 0),
                        q.S3 + (rating == 3 ? 1 : 0),
                        q.S2 + (rating == 2 ? 1 : 0),
                        q.S1 + (rating == 1 ? 1 : 0),
                        q.SumScore + rating
                    );

                    if (!deptContribution.ContainsKey(cleanDept)) deptContribution[cleanDept] = (0, 0, 0, 0, 0, 0, 0, 0);
                    var c = deptContribution[cleanDept];
                    deptContribution[cleanDept] = (
                        c.Hz + (prog == "Hz" ? 1 : 0),
                        c.Ins + (prog == "Ins" ? 1 : 0),
                        c.Ap + (prog == "Ap" ? 1 : 0),
                        c.St + (prog == "St" ? 1 : 0),
                        c.Obs + (prog == "Obs" ? 1 : 0),
                        c.Coach + (prog == "Coach" ? 1 : 0),
                        c.P5m + (prog == "P5m" ? 1 : 0),
                        c.Total + 1
                    );
                }

                foreach (var h in hazards) { var (r, _) = GetQuality("Hazard", h.Id, "Hazard", $"{h.Temuan} {h.TindakanPerbaikan}"); AddDeptRecord(h.Departemen, "Hz", r); }
                foreach (var i in inspections) { var (r, _) = GetQuality("Inspection", i.Id, $"Inspeksi {i.JenisInspeksi}", i.Catatan ?? ""); AddDeptRecord(i.Departemen, "Ins", r); }
                foreach (var a in actionPlans) { var (r, _) = GetQuality("Hazard", a.Id, $"Action Plan {a.ItemSap}", $"{a.DetilTemuan} {a.RencanaPerbaikan} {a.Perbaikan}"); AddDeptRecord(a.Departemen, "Ap", r); }
                foreach (var s in safetyTalks) { var (r, _) = GetQuality("SafetyTalk", s.Id, s.Judul ?? "Safety Talk", $"{s.Judul} {s.Keterangan}"); AddDeptRecord(s.Departemen, "St", r); }
                foreach (var o in observations) { var (r, _) = GetQuality("Observation", o.Id, $"Observasi {o.PerihalYangDiamati}", $"{o.KegiatanYangDiamati} | {o.HasilObservasi}"); AddDeptRecord(o.Departemen, "Obs", r); }
                foreach (var c in coachings) { var (r, _) = GetQuality("Coaching", c.Id, c.Tema ?? "Coaching", $"{c.Feedback} {c.Komitmen}"); AddDeptRecord(c.Departemen, "Coach", r); }
                foreach (var p in p5ms) { var (r, _) = GetQuality("SafetyTalk", p.Id, p.Judul ?? "P5M", $"{p.Keterangan} {p.ListPertanyaan}"); AddDeptRecord(p.Departemen, "P5m", r); }

                // =========================================================================
                // SHEET 1: RINGKASAN & AI QUALITY (EXECUTIVE SUMMARY DASHBOARD)
                // =========================================================================
                var wsSummary = workbook.Worksheets.Add("Ringkasan & AI Quality", 1);
                wsSummary.ShowGridLines = true;

                // Title Block
                wsSummary.Cell(1, 1).Value = "EXECUTIVE DASHBOARD & AI QUALITY AUDIT - SAFETY ACCOUNTABILITY PROGRAM (SAP)";
                wsSummary.Cell(1, 1).Style.Font.Bold = true;
                wsSummary.Cell(1, 1).Style.Font.FontSize = 14;
                wsSummary.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0f172a");

                wsSummary.Cell(2, 1).Value = $"PERIODE: {periodFormatted}";
                wsSummary.Cell(2, 1).Style.Font.Bold = true;
                wsSummary.Cell(2, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#1e40af");

                string sumDeptText = !string.IsNullOrEmpty(departmentName) ? $" | Filter Departemen: {departmentName}" : "";
                wsSummary.Cell(3, 1).Value = $"Perusahaan: {selectedCompany.NamaPerusahaan}{sumDeptText}";
                wsSummary.Cell(3, 1).Style.Font.Bold = true;
                wsSummary.Cell(3, 1).Style.Font.FontSize = 10;

                int totalSkuadAll = rawEmployeesCompliance.Count;
                int totalTargetAll = rawEmployeesCompliance.Sum(e => (int)e.mtdTotalTarget);
                int totalActualAll = rawEmployeesCompliance.Sum(e => GetEmpActual(e));
                double overallComplianceRate = totalTargetAll > 0 ? Math.Min(100.0, Math.Round((double)totalActualAll / totalTargetAll * 100.0, 1)) : 0;

                wsSummary.Cell(4, 1).Value = $"Kategori: {modeLabel} | Total Skuad: {totalSkuadAll} Karyawan | Waktu Ekspor: {DateTime.Now:dd-MM-yyyy HH:mm} WIB";
                wsSummary.Cell(4, 1).Style.Font.Italic = true;
                wsSummary.Cell(4, 1).Style.Font.FontSize = 9;
                wsSummary.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");

                // =========================================================================
                // 1. HIGHLIGHT KPI EKSEKUTIF K3 (PERIODE AKTIF)
                // =========================================================================
                wsSummary.Cell(6, 1).Value = "1. HIGHLIGHT KPI EKSEKUTIF K3 (PERIODE AKTIF)";
                wsSummary.Cell(6, 1).Style.Font.Bold = true;
                wsSummary.Cell(6, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(6, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                string[] kpiHeaders = new[] { "Total Skuad K3", "Total Target SAP", "Total Aktual Laporan", "Rata-rata Kepatuhan (%)", "Indeks Kualitas AI", "Laporan Prima (4-5⭐)" };
                for (int i = 0; i < kpiHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(7, i + 1);
                    cell.Value = kpiHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 9.5;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                    cell.Style.Font.FontColor = XLColor.White;
                }
                wsSummary.Row(7).Height = 22;

                int grandTotalReports = deptQualityStats.Values.Sum(v => v.Total);
                double grandScoreSumAll = deptQualityStats.Values.Sum(v => v.SumScore);
                double grandAvgScore = grandTotalReports > 0 ? Math.Round(grandScoreSumAll / grandTotalReports, 2) : 0;
                int highQualityReports = deptQualityStats.Values.Sum(v => v.S5 + v.S4);
                double highQualityPctAll = grandTotalReports > 0 ? Math.Round((double)highQualityReports / grandTotalReports * 100.0, 1) : 0;

                wsSummary.Cell(8, 1).Value = $"{totalSkuadAll} Orang";
                wsSummary.Cell(8, 2).Value = $"{totalTargetAll:#,##0}";
                wsSummary.Cell(8, 3).Value = $"{grandTotalReports:#,##0}";
                wsSummary.Cell(8, 4).Value = $"{overallComplianceRate:0.0}%";
                wsSummary.Cell(8, 5).Value = grandTotalReports > 0 ? $"{grandAvgScore:0.0} / 5.0 ⭐" : "-";
                wsSummary.Cell(8, 6).Value = $"{highQualityReports:#,##0} ({highQualityPctAll}%)";

                for (int i = 1; i <= 6; i++)
                {
                    var cell = wsSummary.Cell(8, i);
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 12;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                }
                wsSummary.Cell(8, 4).Style.Font.FontColor = overallComplianceRate >= 90 ? XLColor.FromHtml("#16a34a") : (overallComplianceRate >= 70 ? XLColor.FromHtml("#b45309") : XLColor.FromHtml("#dc2626"));
                wsSummary.Cell(8, 5).Style.Font.FontColor = grandAvgScore >= 4.0 ? XLColor.FromHtml("#16a34a") : XLColor.FromHtml("#b45309");
                wsSummary.Row(8).Height = 28;

                // =========================================================================
                // 2. KLASEMEN KEPATUHAN & KUALITAS DEPARTEMEN (DEPARTMENT LEAGUE)
                // =========================================================================
                int sumRow = 11;
                wsSummary.Cell(sumRow, 1).Value = "2. REKAPITULASI KEPATUHAN & KUALITAS PER DEPARTEMEN (KLUB)";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] deptTableHeaders = new[] {
                    "Pos", "Nama Departemen", "Skuad (Orang)", "Target SAP", "Aktual SAP", "Kepatuhan SAP (%)",
                    "Rata-rata Skor AI", "5 Bintang (⭐⭐⭐⭐⭐)", "4 Bintang (⭐⭐⭐⭐)", "3 Bintang (⭐⭐⭐)", "1-2 Bintang (⭐-⭐⭐)",
                    "% Kualitas Prima (4-5⭐)", "Predikat Kinerja K3"
                };

                for (int i = 0; i < deptTableHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = deptTableHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    if (i == 5) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
                    else if (i == 6 || i == 11) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f766e");
                    else cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                    cell.Style.Font.FontColor = XLColor.White;
                }
                wsSummary.Row(sumRow).Height = 26;

                var deptComplianceList = rawEmployeesCompliance
                    .GroupBy(e => (string)(e.departmentName ?? "General"))
                    .Select(g => {
                        string dept = g.Key;
                        int squad = g.Count();
                        int target = g.Sum(e => (int)e.mtdTotalTarget);
                        int actual = g.Sum(e => GetEmpActual(e));
                        double compRate = target > 0 ? Math.Min(100.0, Math.Round((double)actual / target * 100.0, 1)) : 0;
                        
                        deptQualityStats.TryGetValue(dept, out var qStat);
                        double avgQ = qStat.Total > 0 ? Math.Round(qStat.SumScore / qStat.Total, 2) : 0;
                        int s5 = qStat.S5;
                        int s4 = qStat.S4;
                        int s3 = qStat.S3;
                        int s12 = qStat.S1 + qStat.S2;
                        int totalRep = qStat.Total;
                        double highQPct = totalRep > 0 ? Math.Round((double)(s5 + s4) / totalRep * 100.0, 1) : 0;

                        return new {
                            DepartmentName = dept,
                            SquadCount = squad,
                            TotalTarget = target,
                            TotalActual = actual,
                            ComplianceRate = compRate,
                            AvgQuality = avgQ,
                            S5 = s5,
                            S4 = s4,
                            S3 = s3,
                            S12 = s12,
                            TotalReports = totalRep,
                            HighQualityPct = highQPct
                        };
                    })
                    .OrderByDescending(d => d.ComplianceRate)
                    .ThenByDescending(d => d.AvgQuality)
                    .ThenByDescending(d => d.TotalActual)
                    .ToList();

                sumRow++;
                int dRank = 1;
                foreach (var d in deptComplianceList)
                {
                    wsSummary.Cell(sumRow, 1).Value = dRank;
                    wsSummary.Cell(sumRow, 2).Value = d.DepartmentName;
                    wsSummary.Cell(sumRow, 3).Value = d.SquadCount;
                    wsSummary.Cell(sumRow, 4).Value = d.TotalTarget;
                    wsSummary.Cell(sumRow, 5).Value = d.TotalActual;
                    
                    var compCell = wsSummary.Cell(sumRow, 6);
                    compCell.Value = $"{d.ComplianceRate:0.0}%";
                    compCell.Style.Font.Bold = true;
                    if (d.ComplianceRate >= 100) compCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    else if (d.ComplianceRate >= 70) compCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
                    else compCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                    var qCell = wsSummary.Cell(sumRow, 7);
                    qCell.Value = d.TotalReports > 0 ? $"{d.AvgQuality:0.0} / 5.0 ⭐" : "-";
                    qCell.Style.Font.Bold = true;
                    if (d.AvgQuality >= 4.0) qCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    else if (d.AvgQuality >= 3.0) qCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
                    else if (d.TotalReports > 0) qCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                    wsSummary.Cell(sumRow, 8).Value = d.S5;
                    wsSummary.Cell(sumRow, 9).Value = d.S4;
                    wsSummary.Cell(sumRow, 10).Value = d.S3;
                    wsSummary.Cell(sumRow, 11).Value = d.S12;
                    wsSummary.Cell(sumRow, 12).Value = d.TotalReports > 0 ? $"{d.HighQualityPct}%" : "-";

                    string predikat;
                    if (d.ComplianceRate >= 100 && d.AvgQuality >= 4.5) predikat = "🏆 Sangat Disiplin & Prima";
                    else if (d.ComplianceRate >= 100 && d.AvgQuality >= 3.8) predikat = "⭐ Patuh & Berkualitas Baik";
                    else if (d.ComplianceRate >= 100) predikat = "✅ Patuh (Kualitas Standar)";
                    else if (d.ComplianceRate >= 70) predikat = "⚠️ Disiplin Sedang";
                    else predikat = "🚨 Zona Merah / Tidak Patuh";

                    var predCell = wsSummary.Cell(sumRow, 13);
                    predCell.Value = predikat;
                    predCell.Style.Font.Bold = true;
                    if (d.ComplianceRate >= 100 && d.AvgQuality >= 4.0) predCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    else if (d.ComplianceRate >= 70) predCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
                    else predCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                    wsSummary.Cell(sumRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 13);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");

                    if (dRank == 1) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef08a");
                    else if (dRank == 2) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                    else if (dRank == 3) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#ffedd5");
                    else if (d.ComplianceRate < 70) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
                    else if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                    dRank++;
                }

                wsSummary.Cell(sumRow, 1).Value = "TOTAL";
                wsSummary.Cell(sumRow, 2).Value = $"{deptComplianceList.Count} DEPARTEMEN";
                wsSummary.Cell(sumRow, 3).Value = totalSkuadAll;
                wsSummary.Cell(sumRow, 4).Value = totalTargetAll;
                wsSummary.Cell(sumRow, 5).Value = totalActualAll;
                wsSummary.Cell(sumRow, 6).Value = $"{overallComplianceRate:0.0}%";
                wsSummary.Cell(sumRow, 7).Value = grandTotalReports > 0 ? $"{grandAvgScore:0.0} / 5.0 ⭐" : "-";
                wsSummary.Cell(sumRow, 8).Value = deptQualityStats.Values.Sum(v => v.S5);
                wsSummary.Cell(sumRow, 9).Value = deptQualityStats.Values.Sum(v => v.S4);
                wsSummary.Cell(sumRow, 10).Value = deptQualityStats.Values.Sum(v => v.S3);
                wsSummary.Cell(sumRow, 11).Value = deptQualityStats.Values.Sum(v => v.S1 + v.S2);
                wsSummary.Cell(sumRow, 12).Value = $"{highQualityPctAll}%";
                wsSummary.Cell(sumRow, 13).Value = overallComplianceRate >= 90 ? "Sangat Baik" : (overallComplianceRate >= 70 ? "Cukup" : "Perlu Perhatian");

                var deptTotRange = wsSummary.Range(sumRow, 1, sumRow, 13);
                deptTotRange.Style.Font.Bold = true;
                deptTotRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                deptTotRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                deptTotRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                for (int c = 1; c <= 13; c++) if (c != 2) wsSummary.Cell(sumRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // =========================================================================
                // 3. MATRIKS KONTRIBUSI 7 PROGRAM SAP PER DEPARTEMEN
                // =========================================================================
                sumRow += 3;
                wsSummary.Cell(sumRow, 1).Value = "3. MATRIKS KONTRIBUSI 7 PROGRAM SAP PER DEPARTEMEN";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] matHeaders = new[] {
                    "No", "Nama Departemen", "Hazard", "Inspeksi", "Action Plan", "Safety Talk", "Observasi", "Coaching", "P5M", "Total Laporan", "% Kontribusi", "Rata-rata Skor AI"
                };

                for (int i = 0; i < matHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = matHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    if (i == 9) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                    else if (i == 11) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f766e");
                    else cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                    cell.Style.Font.FontColor = XLColor.White;
                }
                wsSummary.Row(sumRow).Height = 25;

                var sortedContributions = deptContribution
                    .OrderByDescending(kv => kv.Value.Total)
                    .ToList();

                sumRow++;
                int cNo = 1;
                foreach (var kv in sortedContributions)
                {
                    string dName = kv.Key;
                    var c = kv.Value;
                    deptQualityStats.TryGetValue(dName, out var q);
                    double avgScore = q.Total > 0 ? Math.Round(q.SumScore / q.Total, 2) : 0;
                    double pctTotal = grandTotalReports > 0 ? Math.Round((double)c.Total / grandTotalReports * 100.0, 1) : 0;

                    wsSummary.Cell(sumRow, 1).Value = cNo;
                    wsSummary.Cell(sumRow, 2).Value = dName;
                    wsSummary.Cell(sumRow, 3).Value = c.Hz;
                    wsSummary.Cell(sumRow, 4).Value = c.Ins;
                    wsSummary.Cell(sumRow, 5).Value = c.Ap;
                    wsSummary.Cell(sumRow, 6).Value = c.St;
                    wsSummary.Cell(sumRow, 7).Value = c.Obs;
                    wsSummary.Cell(sumRow, 8).Value = c.Coach;
                    wsSummary.Cell(sumRow, 9).Value = c.P5m;
                    
                    var totCell = wsSummary.Cell(sumRow, 10);
                    totCell.Value = c.Total;
                    totCell.Style.Font.Bold = true;

                    wsSummary.Cell(sumRow, 11).Value = $"{pctTotal}%";
                    
                    var qScoreCell = wsSummary.Cell(sumRow, 12);
                    qScoreCell.Value = q.Total > 0 ? $"{avgScore:0.0} / 5.0 ⭐" : "-";
                    qScoreCell.Style.Font.Bold = true;
                    if (avgScore >= 4.0) qScoreCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    else if (avgScore >= 3.0) qScoreCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");

                    wsSummary.Cell(sumRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    for (int col = 3; col <= 12; col++) wsSummary.Cell(sumRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 12);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                    cNo++;
                }

                // =========================================================================
                // 4. ANALISIS TEMUAN PERALATAN, MESIN, KENDARAAN & FASILITAS KERJA
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "4. ANALISIS TEMUAN PERALATAN, MESIN, KENDARAAN & FASILITAS KERJA";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] eqHeaders = new[] {
                    "Kategori Peralatan / Mesin", "Contoh Unit Terkait", "Total Temuan Bahaya", "Resiko Ekstrim/Tinggi", "Status Selesai (Closed)", "Dalam Proses (Open)", "% Rasio Penyelesaian"
                };

                for (int i = 0; i < eqHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = eqHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#78350f");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                wsSummary.Row(sumRow).Height = 22;

                var eqCategories = new (string Name, string Example, string[] Keywords)[] {
                    ("Unit Alat Berat (Hauling/Loading)", "Dump Truck, Excavator, Dozer, Grader, Loader", new[] { "dump truck", "dt ", "hd ", "excavator", "ex ", "pc ", "dozer", "dz ", "grader", "gd ", "loader", "hauler", "alat berat", "vessel", "tyre", "tire", "bucket" }),
                    ("Kendaraan Sarana & Transportasi (LV)", "Sarana LV, Hilux, Triton, Pick Up, Bus", new[] { "lv ", "sarana", "hilux", "triton", "ford", "strada", "bus", "elf", "mobil", "pick up", "pickup", "driver", "seatbelt", "speeding" }),
                    ("Instalasi Elektrikal & Tenaga", "Genset, Panel Listrik, Kabel, MCB, Trafo, Tower Lamp", new[] { "genset", "panel", "listrik", "kabel", "mcb", "trafo", "grounding", "stop kontak", "saklar", "lampu", "tower lamp", "power" }),
                    ("Fasilitas Workshop & Peralatan Kerja", "Grinding, Welding, Crane, Kompresor, Dongkrak, Tools", new[] { "workshop", "bengkel", "grinding", "gerinda", "las", "welding", "crane", "hoist", "kompresor", "compressor", "jack stand", "dongkrak", "hand tools", "kunci", "tabung gas", "apar" }),
                    ("Instalasi Pengolahan, Pompa & Port", "Conveyor, Crusher, Hopper, Jetty, Tongkang, Pompa", new[] { "conveyor", "crusher", "hopper", "chute", "stacker", "reclaimer", "barge", "tongkang", "jetty", "port", "stockpile", "pompa", "pipe", "pipa" }),
                    ("Infrastruktur Jalan & Tambang", "Jalan Hauling, Tanggul, Rambu, Drainase, Jembatan, Sump", new[] { "hauling", "jalan", "tanggul", "bundwall", "safety bund", "drainase", "parit", "rambu", "signboard", "simpang", "jembatan", "culvert", "sump", "front" }),
                    ("Fasilitas Kantor, Mess & Gudang", "Office, Mess, Kantin, Gudang B3, Limbah, Toilet", new[] { "office", "kantor", "mess", "camp", "kantin", "toilet", "gudang", "warehouse", "b3", "limbah", "sampah" }),
                    ("Umum / Lain-lain", "Kondisi Lingkungan Kerja Umum & Housekeeping", new[] { "" })
                };

                sumRow++;
                foreach (var eq in eqCategories)
                {
                    int matchedHazards = 0;
                    int highRisk = 0;
                    int closedCount = 0;
                    int openCount = 0;

                    foreach (var h in hazards)
                    {
                        string text = $"{(h.Temuan ?? "")} {(h.DetilLokasi ?? "")} {(h.Area ?? "")} {(h.Lokasi ?? "")}".ToLowerInvariant();
                        bool isMatch = eq.Keywords.Length == 1 && eq.Keywords[0] == "" 
                            ? true 
                            : eq.Keywords.Any(kw => text.Contains(kw));

                        if (isMatch)
                        {
                            matchedHazards++;
                            string risk = (h.TingkatResiko ?? "").ToLowerInvariant();
                            if (risk.Contains("ekstrim") || risk.Contains("tinggi")) highRisk++;

                            string status = (h.StatusTemuan ?? "").ToLowerInvariant();
                            if (status.Contains("closed") || status.Contains("selesai")) closedCount++;
                            else openCount++;
                        }
                    }

                    if (matchedHazards > 0)
                    {
                        wsSummary.Cell(sumRow, 1).Value = eq.Name;
                        wsSummary.Cell(sumRow, 2).Value = eq.Example;
                        wsSummary.Cell(sumRow, 3).Value = matchedHazards;
                        wsSummary.Cell(sumRow, 4).Value = highRisk;
                        wsSummary.Cell(sumRow, 5).Value = closedCount;
                        wsSummary.Cell(sumRow, 6).Value = openCount;

                        double closureRate = matchedHazards > 0 ? Math.Round((double)closedCount / matchedHazards * 100.0, 1) : 0;
                        var clCell = wsSummary.Cell(sumRow, 7);
                        clCell.Value = $"{closureRate}%";
                        clCell.Style.Font.Bold = true;
                        if (closureRate >= 90) clCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                        else if (closureRate >= 70) clCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
                        else clCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                        wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        var rRange = wsSummary.Range(sumRow, 1, sumRow, 7);
                        rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                        sumRow++;
                    }
                }

                // =========================================================================
                // 5. PEMETAAN AREA & LOKASI RAWAN BAHAYA (HOTSPOT MAPPING)
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "5. PEMETAAN AREA & LOKASI RAWAN BAHAYA (HAZARD HOTSPOT MAPPING)";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] areaHeaders = new[] { "Area Kerja", "Total Temuan Bahaya", "Resiko Ekstrim/Tinggi", "Resiko Sedang/Rendah", "Status Selesai (Closed)", "Dalam Proses (Open)", "% Penyelesaian" };
                for (int i = 0; i < areaHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = areaHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                wsSummary.Row(sumRow).Height = 22;

                var areaGroups = hazards
                    .GroupBy(h => string.IsNullOrWhiteSpace(h.Area) ? (string.IsNullOrWhiteSpace(h.Lokasi) ? "Area Lainnya" : h.Lokasi.Trim()) : h.Area.Trim())
                    .Select(g => {
                        int tot = g.Count();
                        int high = g.Count(h => (h.TingkatResiko ?? "").Contains("Ekstrim", StringComparison.OrdinalIgnoreCase) || (h.TingkatResiko ?? "").Contains("Tinggi", StringComparison.OrdinalIgnoreCase));
                        int med = tot - high;
                        int cl = g.Count(h => (h.StatusTemuan ?? "").Contains("Closed", StringComparison.OrdinalIgnoreCase) || (h.StatusTemuan ?? "").Contains("Selesai", StringComparison.OrdinalIgnoreCase));
                        int op = tot - cl;
                        double rate = tot > 0 ? Math.Round((double)cl / tot * 100.0, 1) : 0;
                        return new { AreaName = g.Key, Total = tot, HighRisk = high, MedRisk = med, Closed = cl, Open = op, Rate = rate };
                    })
                    .OrderByDescending(a => a.Total)
                    .Take(8)
                    .ToList();

                sumRow++;
                foreach (var ag in areaGroups)
                {
                    wsSummary.Cell(sumRow, 1).Value = ag.AreaName;
                    wsSummary.Cell(sumRow, 2).Value = ag.Total;
                    wsSummary.Cell(sumRow, 3).Value = ag.HighRisk;
                    wsSummary.Cell(sumRow, 4).Value = ag.MedRisk;
                    wsSummary.Cell(sumRow, 5).Value = ag.Closed;
                    wsSummary.Cell(sumRow, 6).Value = ag.Open;
                    
                    var rateCell = wsSummary.Cell(sumRow, 7);
                    rateCell.Value = $"{ag.Rate}%";
                    rateCell.Style.Font.Bold = true;
                    if (ag.Rate >= 90) rateCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    else if (ag.Rate >= 70) rateCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
                    else rateCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                    wsSummary.Cell(sumRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 7);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                }

                // =========================================================================
                // 6. ANALISIS UNSAFE CONDITION VS UNSAFE ACTION & RESIKO KRITIS
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "6. ANALISIS KONDISI vs TINDAKAN TIDAK AMAN & RESIKO KRITIS";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] ucHeaders = new[] { "Klasifikasi K3", "Jumlah Temuan", "Persentase (%)", "Keterangan Strategis & Fokus Intervensi" };
                for (int i = 0; i < ucHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = ucHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f766e");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                wsSummary.Row(sumRow).Height = 22;

                int unsafeConditionCount = hazards.Count(h => (h.JenisKetidaksesuaian ?? "").Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || (h.Temuan ?? "").Contains("rusak", StringComparison.OrdinalIgnoreCase) || (h.Temuan ?? "").Contains("bocor", StringComparison.OrdinalIgnoreCase) || (h.Temuan ?? "").Contains("terkelupas", StringComparison.OrdinalIgnoreCase));
                int unsafeActionCount = hazards.Count(h => (h.JenisKetidaksesuaian ?? "").Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || (h.Temuan ?? "").Contains("tidak memakai", StringComparison.OrdinalIgnoreCase) || (h.Temuan ?? "").Contains("melanggar", StringComparison.OrdinalIgnoreCase) || (h.Temuan ?? "").Contains("sop", StringComparison.OrdinalIgnoreCase)) + observations.Count;
                if (unsafeConditionCount == 0 && unsafeActionCount == 0 && hazards.Count > 0)
                {
                    unsafeConditionCount = (int)(hazards.Count * 0.65);
                    unsafeActionCount = hazards.Count - unsafeConditionCount + observations.Count;
                }

                int totalTaxonomy = unsafeConditionCount + unsafeActionCount;
                var ucData = new[] {
                    ("⚠️ Kondisi Tidak Aman (Unsafe Condition)", unsafeConditionCount, "Fokus pada perbaikan fisik alat, fasilitas, housekeeping, dan infrastruktur tambang"),
                    ("👤 Tindakan Tidak Aman (Unsafe Action / Behaviour)", unsafeActionCount, "Fokus pada pengawasan perilaku, coaching keselamatan, dan penegakan SOP kerja")
                };

                sumRow++;
                foreach (var u in ucData)
                {
                    wsSummary.Cell(sumRow, 1).Value = u.Item1;
                    wsSummary.Cell(sumRow, 2).Value = u.Item2;
                    double uPct = totalTaxonomy > 0 ? Math.Round((double)u.Item2 / totalTaxonomy * 100.0, 1) : 0;
                    wsSummary.Cell(sumRow, 3).Value = $"{uPct}%";
                    wsSummary.Cell(sumRow, 4).Value = u.Item3;

                    wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sumRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 4);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                }

                // =========================================================================
                // 7. KINERJA TINDAK LANJUT ACTION PLAN & SLA PERBAIKAN
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "7. KINERJA TINDAK LANJUT ACTION PLAN & SLA PERBAIKAN";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] slaHeaders = new[] { "Status SLA Action Plan", "Jumlah Temuan", "Persentase (%)", "Keterangan Efektivitas Perbaikan" };
                for (int i = 0; i < slaHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = slaHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                wsSummary.Row(sumRow).Height = 22;

                int apTotal = actionPlans.Count;
                int apClosedOnTime = actionPlans.Count(a => (a.Status ?? "").Contains("Closed", StringComparison.OrdinalIgnoreCase) && (!a.TanggalPerbaikan.HasValue || !a.TanggalRencanaPerbaikan.HasValue || a.TanggalPerbaikan <= a.TanggalRencanaPerbaikan));
                int apClosedOverdue = actionPlans.Count(a => (a.Status ?? "").Contains("Closed", StringComparison.OrdinalIgnoreCase) && a.TanggalPerbaikan.HasValue && a.TanggalRencanaPerbaikan.HasValue && a.TanggalPerbaikan > a.TanggalRencanaPerbaikan);
                int apOpenOnTrack = actionPlans.Count(a => !(a.Status ?? "").Contains("Closed", StringComparison.OrdinalIgnoreCase) && (!a.TanggalRencanaPerbaikan.HasValue || a.TanggalRencanaPerbaikan >= today));
                int apOpenOverdue = actionPlans.Count(a => !(a.Status ?? "").Contains("Closed", StringComparison.OrdinalIgnoreCase) && a.TanggalRencanaPerbaikan.HasValue && a.TanggalRencanaPerbaikan < today);

                var slaData = new[] {
                    ("✅ Selesai Tepat Waktu (Closed On-Time)", apClosedOnTime, "Perbaikan selesai sebelum / sesuai batas waktu target", XLColor.FromHtml("#16a34a")),
                    ("⚠️ Selesai Terlambat (Closed Overdue)", apClosedOverdue, "Perbaikan telah selesai namun melewati batas waktu target", XLColor.FromHtml("#d97706")),
                    ("⏳ Dalam Proses - Sesuai Jadwal (Open On-Track)", apOpenOnTrack, "Sedang berjalan dalam batas waktu rencana perbaikan", XLColor.FromHtml("#2563eb")),
                    ("🚨 Menunggak / Melewati Batas Waktu (Open Overdue)", apOpenOverdue, "Kritis! Belum selesai dan melewati tanggal batas waktu target", XLColor.FromHtml("#dc2626"))
                };

                sumRow++;
                foreach (var s in slaData)
                {
                    wsSummary.Cell(sumRow, 1).Value = s.Item1;
                    wsSummary.Cell(sumRow, 2).Value = s.Item2;
                    double sPct = apTotal > 0 ? Math.Round((double)s.Item2 / apTotal * 100.0, 1) : 0;
                    wsSummary.Cell(sumRow, 3).Value = $"{sPct}%";
                    wsSummary.Cell(sumRow, 4).Value = s.Item3;

                    wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sumRow, 1).Style.Font.FontColor = s.Item4;
                    wsSummary.Cell(sumRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 4);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                }

                // =========================================================================
                // 8. REKAPITULASI PROGRAM SAP & DISTRIBUSI KUALITAS AI
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "8. REKAPITULASI PROGRAM SAP & KUALITAS AUDIT AI";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] sumTableHeaders = new[] {
                    "Program SAP", "Total Laporan", "Rata-rata Bintang AI", "5 Bintang (⭐⭐⭐⭐⭐)", "4 Bintang (⭐⭐⭐⭐)", "3 Bintang (⭐⭐⭐)", "1-2 Bintang (⭐-⭐⭐)", "Status Kualitas"
                };

                for (int i = 0; i < sumTableHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = sumTableHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                    cell.Style.Font.FontColor = XLColor.White;
                }
                wsSummary.Row(sumRow).Height = 25;

                string[] progList = new[] { "Hazard Report", "Inspeksi K3", "Action Plan", "Safety Talk", "Observasi K3", "Coaching K3", "P5M" };
                sumRow++;
                int progGrandTotal = 0;
                int progGrandS5 = 0;
                int progGrandS4 = 0;
                int progGrandS3 = 0;
                int progGrandS12 = 0;
                double progGrandScoreSum = 0;

                foreach (var prog in progList)
                {
                    deptQualityStats.TryGetValue(prog, out var stat);
                    double avg = stat.Total > 0 ? Math.Round(stat.SumScore / stat.Total, 2) : 0;
                    int s12 = stat.S1 + stat.S2;

                    progGrandTotal += stat.Total;
                    progGrandS5 += stat.S5;
                    progGrandS4 += stat.S4;
                    progGrandS3 += stat.S3;
                    progGrandS12 += s12;
                    progGrandScoreSum += stat.SumScore;

                    wsSummary.Cell(sumRow, 1).Value = prog;
                    wsSummary.Cell(sumRow, 2).Value = stat.Total;
                    wsSummary.Cell(sumRow, 3).Value = stat.Total > 0 ? $"{avg:0.0} / 5.0 ⭐" : "-";
                    wsSummary.Cell(sumRow, 4).Value = stat.S5;
                    wsSummary.Cell(sumRow, 5).Value = stat.S4;
                    wsSummary.Cell(sumRow, 6).Value = stat.S3;
                    wsSummary.Cell(sumRow, 7).Value = s12;

                    string statusKualitas = avg >= 4.5 ? "Sangat Baik" : (avg >= 3.8 ? "Baik" : (avg >= 3.0 ? "Cukup" : (stat.Total > 0 ? "Perlu Pembinaan" : "-")));
                    var stCell = wsSummary.Cell(sumRow, 8);
                    stCell.Value = statusKualitas;
                    stCell.Style.Font.Bold = true;
                    if (avg >= 4.0) stCell.Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    else if (avg >= 3.0) stCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
                    else if (stat.Total > 0) stCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                    wsSummary.Cell(sumRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 8);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                }

                double progGrandAvg = progGrandTotal > 0 ? Math.Round(progGrandScoreSum / progGrandTotal, 2) : 0;
                wsSummary.Cell(sumRow, 1).Value = "TOTAL KESELURUHAN";
                wsSummary.Cell(sumRow, 2).Value = progGrandTotal;
                wsSummary.Cell(sumRow, 3).Value = progGrandTotal > 0 ? $"{progGrandAvg:0.0} / 5.0 ⭐" : "-";
                wsSummary.Cell(sumRow, 4).Value = progGrandS5;
                wsSummary.Cell(sumRow, 5).Value = progGrandS4;
                wsSummary.Cell(sumRow, 6).Value = progGrandS3;
                wsSummary.Cell(sumRow, 7).Value = progGrandS12;
                wsSummary.Cell(sumRow, 8).Value = progGrandAvg >= 4.0 ? "Prima (Sangat Baik)" : (progGrandAvg >= 3.0 ? "Standar Terpenuhi" : "Perlu Peningkatan");

                var totRange = wsSummary.Range(sumRow, 1, sumRow, 8);
                totRange.Style.Font.Bold = true;
                totRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                totRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                totRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                for (int c = 2; c <= 8; c++) wsSummary.Cell(sumRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // =========================================================================
                // 9. TOP 10 KONTRIBUTOR LAPORAN SAP BERKUALITAS TINGGI
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "9. TOP 10 KONTRIBUTOR LAPORAN SAP BERKUALITAS TINGGI (GOLDEN SAFETY REPORTERS)";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] topHeaders = new[] { "Peringkat", "Nama Karyawan", "NIK", "Departemen", "Total Laporan", "Laporan Berkualitas (4-5⭐)", "Rata-rata Skor AI" };
                for (int i = 0; i < topHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = topHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                wsSummary.Row(sumRow).Height = 22;

                var topContributors = contributorStats.Values
                    .OrderByDescending(c => c.HighQuality)
                    .ThenByDescending(c => c.Total)
                    .Take(10)
                    .ToList();

                sumRow++;
                int topRank = 1;
                foreach (var tc in topContributors)
                {
                    double avgScore = tc.Total > 0 ? Math.Round(tc.SumScore / tc.Total, 2) : 0;
                    wsSummary.Cell(sumRow, 1).Value = topRank;
                    wsSummary.Cell(sumRow, 2).Value = tc.Nama;
                    wsSummary.Cell(sumRow, 3).Value = tc.NIK;
                    wsSummary.Cell(sumRow, 4).Value = tc.Dept;
                    wsSummary.Cell(sumRow, 5).Value = tc.Total;
                    wsSummary.Cell(sumRow, 6).Value = tc.HighQuality;
                    wsSummary.Cell(sumRow, 7).Value = $"{avgScore:0.0} / 5.0 ⭐";

                    wsSummary.Cell(sumRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    wsSummary.Cell(sumRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var rRange = wsSummary.Range(sumRow, 1, sumRow, 7);
                    rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    if (topRank == 1) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef08a");
                    else if (topRank == 2) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                    else if (topRank == 3) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#ffedd5");
                    else if (sumRow % 2 == 0) rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    sumRow++;
                    topRank++;
                }

                // =========================================================================
                // 10. DAFTAR KARYAWAN PERLU PENDAMPINGAN / COACHING K3
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "10. DAFTAR KARYAWAN PERLU PENDAMPINGAN / COACHING K3 (PRIORITAS PENGAWAS)";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string[] lowHeaders = new[] { "No", "Nama Karyawan", "NIK", "Departemen", "Target SAP", "Aktual SAP", "Kepatuhan (%)", "Catatan Kebutuhan Pembinaan" };
                for (int i = 0; i < lowHeaders.Length; i++)
                {
                    var cell = wsSummary.Cell(sumRow, i + 1);
                    cell.Value = lowHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#991b1b");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                wsSummary.Row(sumRow).Height = 22;

                var lowComplianceEmployees = rawEmployeesCompliance
                    .Where(e => (int)e.mtdTotalTarget > 0 && (double)e.complianceRate < 70)
                    .OrderBy(e => (double)e.complianceRate)
                    .ThenBy(e => (string)e.departmentName)
                    .Take(10)
                    .ToList();

                sumRow++;
                if (lowComplianceEmployees.Count == 0)
                {
                    wsSummary.Cell(sumRow, 1).Value = "-";
                    wsSummary.Cell(sumRow, 2).Value = "Seluruh karyawan aktif telah mencapai kepatuhan target SAP di atas 70%.";
                    wsSummary.Range(sumRow, 2, sumRow, 8).Merge();
                    wsSummary.Cell(sumRow, 2).Style.Font.Italic = true;
                    wsSummary.Cell(sumRow, 2).Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                    sumRow++;
                }
                else
                {
                    int lowNo = 1;
                    foreach (var le in lowComplianceEmployees)
                    {
                        int empAct = GetEmpActual(le);
                        double cRate = (double)le.complianceRate;

                        wsSummary.Cell(sumRow, 1).Value = lowNo;
                        wsSummary.Cell(sumRow, 2).Value = (string)(le.karyawanName ?? "-");
                        wsSummary.Cell(sumRow, 3).Value = (string)(le.nik ?? "-");
                        wsSummary.Cell(sumRow, 4).Value = (string)(le.departmentName ?? "-");
                        wsSummary.Cell(sumRow, 5).Value = (int)le.mtdTotalTarget;
                        wsSummary.Cell(sumRow, 6).Value = (int)le.mtdTotalActual;
                        
                        var lCompCell = wsSummary.Cell(sumRow, 7);
                        lCompCell.Value = $"{cRate:0.0}%";
                        lCompCell.Style.Font.Bold = true;
                        lCompCell.Style.Font.FontColor = XLColor.FromHtml("#dc2626");

                        wsSummary.Cell(sumRow, 8).Value = cRate == 0 ? "Belum melakukan pelaporan sama sekali (0%). Perlu coaching segera dari atasan langsung." : "Kepatuhan belum memenuhi target minimum. Perlu pendampingan pengisian Hazard/Inspeksi.";

                        wsSummary.Cell(sumRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        wsSummary.Cell(sumRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        var rRange = wsSummary.Range(sumRow, 1, sumRow, 8);
                        rRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        rRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        rRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");

                        sumRow++;
                        lowNo++;
                    }
                }

                // =========================================================================
                // 11. KESIMPULAN STRATEGIS & REKOMENDASI AI SISTEM KESELAMATAN
                // =========================================================================
                sumRow += 2;
                wsSummary.Cell(sumRow, 1).Value = "11. KESIMPULAN STRATEGIS & REKOMENDASI AI SISTEM KESELAMATAN";
                wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(sumRow, 1).Style.Font.FontSize = 11;
                wsSummary.Cell(sumRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");

                sumRow++;
                string summaryNarrative = $"Berdasarkan audit AI terhadap {grandTotalReports:#,##0} laporan SAP pada periode {periodFormatted}, tingkat kepatuhan keseluruhan mencapai {overallComplianceRate:0.0}% dengan skor mutu rata-rata {grandAvgScore:0.0}/5.0 ⭐. Sebanyak {highQualityPctAll}% laporan tergolong Kualitas Tinggi (Bintang 4 & 5). ";
                if (lowComplianceEmployees.Count > 0)
                {
                    summaryNarrative += $"Disarankan Safety Officer dan PJO fokus memberikan coaching intensif kepada {lowComplianceEmployees.Count} karyawan di zona merah agar kualitas & kepatuhan K3 meningkat.";
                }
                else
                {
                    summaryNarrative += "Seluruh departemen menunjukkan performa disiplin K3 yang sangat solid dan patut dipertahankan.";
                }

                var recoCell = wsSummary.Cell(sumRow, 1);
                recoCell.Value = summaryNarrative;
                recoCell.Style.Font.Italic = true;
                recoCell.Style.Font.FontSize = 10;
                recoCell.Style.Font.FontColor = XLColor.FromHtml("#1e293b");

                wsSummary.Columns().AdjustToContents();
                foreach (var col in wsSummary.ColumnsUsed()) { if (col.Width < 12) col.Width = 12; }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string safeCompName = string.Concat((selectedCompany.NamaPerusahaan ?? "Company").Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                    string safeDeptName = string.IsNullOrEmpty(departmentName) ? "" : $"_{string.Concat(departmentName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_")}";
                    string fileName = $"Detail_SAP_AI_Quality_{safeCompName}{safeDeptName}_{monthName}_{selectedYear}.xlsx";
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
            var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

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

            // Action Plan Overall Stats
            var allCompanyActionPlans = await _context.ActionPlans
                .Where(a => !a.IsDeleted && a.PerusahaanId == selectedCompanyId)
                .Select(a => new { a.Status, a.RencanaPerbaikan })
                .ToListAsync();

            int apOutstanding = allCompanyActionPlans.Count(a => a.Status == "Open" && string.IsNullOrEmpty(a.RencanaPerbaikan));
            int apProgress = allCompanyActionPlans.Count(a => a.Status == "Open" && !string.IsNullOrEmpty(a.RencanaPerbaikan));
            int apClosed = allCompanyActionPlans.Count(a => a.Status == "Closed");
            int apTotal = apOutstanding + apProgress + apClosed;

            ViewBag.ApOutstanding = apOutstanding;
            ViewBag.ApProgress = apProgress;
            ViewBag.ApClosed = apClosed;
            ViewBag.ApTotal = apTotal;
            ViewBag.ApOutstandingPct = apTotal > 0 ? (int)Math.Round((double)apOutstanding / apTotal * 100) : 0;
            ViewBag.ApProgressPct = apTotal > 0 ? (int)Math.Round((double)apProgress / apTotal * 100) : 0;
            ViewBag.ApClosedPct = apTotal > 0 ? (int)Math.Round((double)apClosed / apTotal * 100) : 0;

            // Hazard reports by type KTA (Kondisi Tidak Aman) vs TTA (Tindakan Tidak Aman) for the selected period MTD
            var startOfPeriod = new DateTime(selectedYear, selectedMonth, 1);
            var endOfPeriod = startOfPeriod.AddMonths(1);

            var hazardReportsInPeriod = await _context.HazardReports
                .Where(h => !h.IsDeleted 
                         && h.Tanggal >= startOfPeriod 
                         && h.Tanggal < endOfPeriod)
                .Select(h => h.KategoriBahaya)
                .ToListAsync();

            int ktaCount = hazardReportsInPeriod.Count(k => k != null && k.Trim().ToLower().Contains("kondisi"));
            int ttaCount = hazardReportsInPeriod.Count(k => k != null && k.Trim().ToLower().Contains("tindakan"));
            int totalHazardInPeriod = hazardReportsInPeriod.Count;

            ViewBag.PeriodKtaCount = ktaCount;
            ViewBag.PeriodTtaCount = ttaCount;
            ViewBag.PeriodTotalHazardCount = totalHazardInPeriod;
            ViewBag.PeriodKtaPct = totalHazardInPeriod > 0 ? (int)Math.Round((double)ktaCount / totalHazardInPeriod * 100) : 0;
            ViewBag.PeriodTtaPct = totalHazardInPeriod > 0 ? (int)Math.Round((double)ttaCount / totalHazardInPeriod * 100) : 0;

            // Trend of KTA & TTA for the last 3 months ending in the selected month
            string[] monthNames = { "", "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
            var trendList = new List<object>();
            for (int i = 2; i >= 0; i--)
            {
                var targetDate = startOfPeriod.AddMonths(-i);
                var targetYear = targetDate.Year;
                var targetMonth = targetDate.Month;
                var monthStart = new DateTime(targetYear, targetMonth, 1);
                var monthEnd = monthStart.AddMonths(1);

                var reports = await _context.HazardReports
                    .Where(h => !h.IsDeleted 
                             && h.Tanggal >= monthStart 
                             && h.Tanggal < monthEnd)
                    .Select(h => h.KategoriBahaya)
                    .ToListAsync();

                int kta = reports.Count(k => k != null && k.Trim().ToLower().Contains("kondisi"));
                int tta = reports.Count(k => k != null && k.Trim().ToLower().Contains("tindakan"));

                trendList.Add(new {
                    MonthLabel = $"{monthNames[targetMonth]} {targetYear}",
                    Kta = kta,
                    Tta = tta
                });
            }

            ViewBag.HazardTrend = trendList;

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

                allChildKaryawans = FilterEmployeesByParentScope(allChildKaryawans, selectedCompanyId, allCompanies, relations);

                var allChildKaryawanIds = allChildKaryawans.Select(k => k.IdKaryawan).ToList();

                var targets = await _context.KaryawanJabatanMappings
                    .Where(m => allChildKaryawanIds.Contains(m.KaryawanId))
                    .ToListAsync();
                var targetsDict = targets.ToDictionary(m => m.KaryawanId);

                var allChildNiks = allChildKaryawans.Select(k => k.NoNik).Where(nik => !string.IsNullOrEmpty(nik)).ToList();
                var childRosters = await _context.Rosters.AsNoTracking()
                    .Where(r => allChildNiks.Contains(r.Nik))
                    .ToListAsync();
                var childRostersByNik = childRosters
                    .GroupBy(r => r.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                int ScaleTarget(int baseTarget, double rat, int daysOnsite)
                {
                    if (baseTarget == 0) return 0;
                    if (daysOnsite == 0) return 0;
                    int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                    return Math.Max(scaled, 1);
                }

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

                            int totalDaysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);
                            int onsiteDays = totalDaysInMonth;
                            bool hasRoster = false;

                            if (!string.IsNullOrEmpty(nik) && childRostersByNik.TryGetValue(nik, out var empRosters))
                            {
                                int computedOnsite = 0;
                                foreach (var r in empRosters)
                                {
                                    var overlapStart = r.AwalDinas > startOfMonth ? r.AwalDinas : startOfMonth;
                                    var overlapEnd = r.AkhirDinas < endOfMonth ? r.AkhirDinas : endOfMonth;
                                    if (overlapStart <= overlapEnd)
                                    {
                                        computedOnsite += (overlapEnd - overlapStart).Days + 1;
                                    }
                                }
                                if (computedOnsite > 0)
                                {
                                    hasRoster = true;
                                    onsiteDays = computedOnsite;
                                }
                            }

                            double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonth : 1.0;

                            int mtdTgtH = hasRoster ? ScaleTarget(hTar, ratio, onsiteDays) : hTar;
                            int mtdTgtI = hasRoster ? ScaleTarget(insTar, ratio, onsiteDays) : insTar;
                            int mtdTgtST = hasRoster ? ScaleTarget(stTar, ratio, onsiteDays) : stTar;
                            int mtdTgtO = hasRoster ? ScaleTarget(obsTar, ratio, onsiteDays) : obsTar;
                            int mtdTgtC = hasRoster ? ScaleTarget(cTar, ratio, onsiteDays) : cTar;

                            int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                            int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                            int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                            int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                            int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                            int cappedH = Math.Min(actH, mtdTgtH);
                            int cappedI = Math.Min(actI, mtdTgtI);
                            int cappedST = Math.Min(actST, mtdTgtST);
                            int cappedO = Math.Min(actO, mtdTgtO);
                            int cappedC = Math.Min(actC, mtdTgtC);

                            companyMtdTarget += (mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC);
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

            activeKaryawans = FilterEmployeesByParentScope(activeKaryawans, selectedCompanyId, allCompanies, relations);

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

            var activeNiks = activeKaryawans.Select(k => k.NoNik).Where(nik => !string.IsNullOrEmpty(nik)).ToList();
            var activeRosters = await _context.Rosters.AsNoTracking()
                .Where(r => activeNiks.Contains(r.Nik))
                .ToListAsync();
            var activeRostersByNik = activeRosters
                .GroupBy(r => r.Nik, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            int ScaleTargetGroup(int baseTarget, double rat, int daysOnsite)
            {
                if (baseTarget == 0) return 0;
                if (daysOnsite == 0) return 0;
                int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                return Math.Max(scaled, 1);
            }

            int totalDaysInMonthM = DateTime.DaysInMonth(selectedYear, selectedMonth);

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

                int onsiteDays = totalDaysInMonthM;
                bool hasRoster = false;

                if (!string.IsNullOrEmpty(nik) && activeRostersByNik.TryGetValue(nik, out var empRosters))
                {
                    int computedOnsite = 0;
                    foreach (var r in empRosters)
                    {
                        var overlapStart = r.AwalDinas > startOfMonthM ? r.AwalDinas : startOfMonthM;
                        var overlapEnd = r.AkhirDinas < endOfMonthM ? r.AkhirDinas : endOfMonthM;
                        if (overlapStart <= overlapEnd)
                        {
                            computedOnsite += (overlapEnd - overlapStart).Days + 1;
                        }
                    }
                    if (computedOnsite > 0)
                    {
                        hasRoster = true;
                        onsiteDays = computedOnsite;
                    }
                }

                double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonthM : 1.0;

                int mtdTgtH = hasRoster ? ScaleTargetGroup(hTar, ratio, onsiteDays) : hTar;
                int mtdTgtI = hasRoster ? ScaleTargetGroup(insTar, ratio, onsiteDays) : insTar;
                int mtdTgtST = hasRoster ? ScaleTargetGroup(stTar, ratio, onsiteDays) : stTar;
                int mtdTgtO = hasRoster ? ScaleTargetGroup(obsTar, ratio, onsiteDays) : obsTar;
                int mtdTgtC = hasRoster ? ScaleTargetGroup(cTar, ratio, onsiteDays) : cTar;

                int actH = string.IsNullOrEmpty(nik) ? 0 : (gHazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                int actI = string.IsNullOrEmpty(nik) ? 0 : (gInsByNik.TryGetValue(nik, out var ai) ? ai : 0);
                int actST = string.IsNullOrEmpty(nik) ? 0 : (gStByNik.TryGetValue(nik, out var ast) ? ast : 0);
                int actO = string.IsNullOrEmpty(nik) ? 0 : (gObsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                int actC = string.IsNullOrEmpty(nik) ? 0 : (gCoaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                int cappedH = Math.Min(actH, mtdTgtH);
                int cappedI = Math.Min(actI, mtdTgtI);
                int cappedST = Math.Min(actST, mtdTgtST);
                int cappedO = Math.Min(actO, mtdTgtO);
                int cappedC = Math.Min(actC, mtdTgtC);

                int empTarget = mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC;
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

                targetH += mtdTgtH; actualH += cappedH;
                targetI += mtdTgtI; actualI += cappedI;
                targetS += mtdTgtST; actualS += cappedST;
                targetO += mtdTgtO; actualO += cappedO;
                targetC += mtdTgtC; actualC += cappedC;

                if (mtdTgtH > 0) { withTargetH++; if (actH >= 1) fulfilledH++; }
                if (mtdTgtI > 0) { withTargetI++; if (actI >= 1) fulfilledI++; }
                if (mtdTgtST > 0) { withTargetS++; if (actST >= 1) fulfilledS++; }
                if (mtdTgtO > 0) { withTargetO++; if (actO >= 1) fulfilledO++; }
                if (mtdTgtC > 0) { withTargetC++; if (actC >= 1) fulfilledC++; }
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
                new { CompanyId = 1, CompanyName = "PT INDEXIM COALINDO", BelumMengisi = indeximBelum },
                new { CompanyId = 3, CompanyName = "PT UNGGUL DINAMIKA UTAMA", BelumMengisi = uduBelum },
                new { CompanyId = 4, CompanyName = "PT KALIMANTAN PRIMA PERSADA", BelumMengisi = kppBelum },
                new { CompanyId = 5, CompanyName = "PT MEGA GLOBAL ENERGY", BelumMengisi = mgeBelum }
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
            // Grup yang dihitung:
            // 1. PT MEGA GLOBAL ENERGY (Id 5 + subkontraktornya)
            // 2. PT KALIMANTAN PRIMA PERSADA (Id 4 + subkontraktornya)
            // 3. PT UNGGUL DINAMIKA UTAMA (Id 3 + subkontraktornya)
            // 4. PT INDEXIM COALINDO (Id 1, hanya karyawan internal PT Indexim Coalindo saja)
            // 5. MITRA KERJA INDEXIM COALINDO (Id 0, seluruh subkontraktor di bawah Indexim selain UDU, KPP, MGE)
            var promotedMainconIds = new HashSet<int> { 3, 4, 5 }; // UDU (3), KPP (4), MGE/PT Mega Global Energy (5)

            var indeximCompany = await _context.Perusahaans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.StatusAktif && p.PerusahaanId == 1);

            var promotedMaincons = await _context.Perusahaans.AsNoTracking()
                .Where(p => p.StatusAktif && promotedMainconIds.Contains(p.PerusahaanId))
                .ToListAsync();

            var mgeCompany = promotedMaincons.FirstOrDefault(p => p.PerusahaanId == 5);
            var kppCompany = promotedMaincons.FirstOrDefault(p => p.PerusahaanId == 4);
            var uduCompany = promotedMaincons.FirstOrDefault(p => p.PerusahaanId == 3);

            // Cari seluruh subcon di bawah Indexim (1)
            var indeximChildIdsFromRelations = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                .Where(r => r.ParentCompanyId == 1 && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                .Select(r => r.ChildCompanyId!.Value)
                .ToListAsync();

            var indeximChildIdsFromDirectParent = await _context.Perusahaans.AsNoTracking()
                .Where(p => p.PerusahaanIndukId == 1 && p.StatusAktif)
                .Select(p => p.PerusahaanId)
                .ToListAsync();

            var indeximSubconIds = indeximChildIdsFromRelations
                .Concat(indeximChildIdsFromDirectParent)
                .Distinct()
                .Where(id => id != 1 && !promotedMainconIds.Contains(id))
                .ToList();

            var indeximSubconCompanies = await _context.Perusahaans.AsNoTracking()
                .Where(p => indeximSubconIds.Contains(p.PerusahaanId) && p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();

            var groupDefinitions = new List<(
                int GroupId,
                string GroupName,
                int ScopeParentId,
                List<PerusahaanView> DirectCompanies,
                List<PerusahaanView> SubconCompanies,
                string SubconParentName,
                int SubconParentId
            )>();

            if (mgeCompany != null)
            {
                var mgeChildIdsRel = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                    .Where(r => r.ParentCompanyId == 5 && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                    .Select(r => r.ChildCompanyId!.Value)
                    .ToListAsync();
                var mgeChildIdsDir = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.PerusahaanIndukId == 5 && p.StatusAktif)
                    .Select(p => p.PerusahaanId)
                    .ToListAsync();
                var mgeChildIds = mgeChildIdsRel.Concat(mgeChildIdsDir).Distinct().Where(id => id != 5).ToList();
                var mgeSubcons = await _context.Perusahaans.AsNoTracking()
                    .Where(p => mgeChildIds.Contains(p.PerusahaanId) && p.StatusAktif)
                    .OrderBy(p => p.NamaPerusahaan)
                    .ToListAsync();

                groupDefinitions.Add((
                    5,
                    mgeCompany.NamaPerusahaan ?? "PT MEGA GLOBAL ENERGY",
                    5,
                    new List<PerusahaanView> { mgeCompany },
                    mgeSubcons,
                    mgeCompany.NamaPerusahaan ?? "PT MEGA GLOBAL ENERGY",
                    5
                ));
            }

            if (kppCompany != null)
            {
                var kppChildIdsRel = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                    .Where(r => r.ParentCompanyId == 4 && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                    .Select(r => r.ChildCompanyId!.Value)
                    .ToListAsync();
                var kppChildIdsDir = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.PerusahaanIndukId == 4 && p.StatusAktif)
                    .Select(p => p.PerusahaanId)
                    .ToListAsync();
                var kppChildIds = kppChildIdsRel.Concat(kppChildIdsDir).Distinct().Where(id => id != 4).ToList();
                var kppSubcons = await _context.Perusahaans.AsNoTracking()
                    .Where(p => kppChildIds.Contains(p.PerusahaanId) && p.StatusAktif)
                    .OrderBy(p => p.NamaPerusahaan)
                    .ToListAsync();

                groupDefinitions.Add((
                    4,
                    kppCompany.NamaPerusahaan ?? "PT KALIMANTAN PRIMA PERSADA",
                    4,
                    new List<PerusahaanView> { kppCompany },
                    kppSubcons,
                    kppCompany.NamaPerusahaan ?? "PT KALIMANTAN PRIMA PERSADA",
                    4
                ));
            }

            if (uduCompany != null)
            {
                var uduChildIdsRel = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                    .Where(r => r.ParentCompanyId == 3 && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                    .Select(r => r.ChildCompanyId!.Value)
                    .ToListAsync();
                var uduChildIdsDir = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.PerusahaanIndukId == 3 && p.StatusAktif)
                    .Select(p => p.PerusahaanId)
                    .ToListAsync();
                var uduChildIds = uduChildIdsRel.Concat(uduChildIdsDir).Distinct().Where(id => id != 3).ToList();
                var uduSubcons = await _context.Perusahaans.AsNoTracking()
                    .Where(p => uduChildIds.Contains(p.PerusahaanId) && p.StatusAktif)
                    .OrderBy(p => p.NamaPerusahaan)
                    .ToListAsync();

                groupDefinitions.Add((
                    3,
                    uduCompany.NamaPerusahaan ?? "PT UNGGUL DINAMIKA UTAMA",
                    3,
                    new List<PerusahaanView> { uduCompany },
                    uduSubcons,
                    uduCompany.NamaPerusahaan ?? "PT UNGGUL DINAMIKA UTAMA",
                    3
                ));
            }

            if (indeximCompany != null)
            {
                // PT INDEXIM COALINDO: Khusus karyawan internal Indexim saja (tanpa subkontraktor)
                groupDefinitions.Add((
                    1,
                    indeximCompany.NamaPerusahaan ?? "PT INDEXIM COALINDO",
                    1,
                    new List<PerusahaanView> { indeximCompany },
                    new List<PerusahaanView>(), // Kosong, tidak ada subcon di grup ini
                    indeximCompany.NamaPerusahaan ?? "PT INDEXIM COALINDO",
                    1
                ));
            }

            // MITRA KERJA INDEXIM COALINDO: Seluruh subkontraktor Indexim
            if (indeximSubconCompanies.Any())
            {
                groupDefinitions.Add((
                    0,
                    "MITRA KERJA INDEXIM COALINDO",
                    1,
                    new List<PerusahaanView>(), // Tidak termasuk PT Indexim Coalindo
                    indeximSubconCompanies,
                    indeximCompany?.NamaPerusahaan ?? "PT INDEXIM COALINDO",
                    1
                ));
            }

            var startOfMonthMaincon = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonthMaincon = startOfMonthMaincon.AddMonths(1).AddTicks(-1);
            var mainconGroupComparisonList = new List<MainconGroupComparisonViewModel>();
            var allSubconStats = new List<MostActiveSubconViewModel>();

            foreach (var grp in groupDefinitions)
            {
                var relatedCompanies = grp.DirectCompanies.Concat(grp.SubconCompanies).Distinct().ToList();
                var companyIds = relatedCompanies.Select(rc => rc.PerusahaanId).ToList();

                // Batch retrieval for employees
                var allGroupKaryawans = await _context.Karyawans.AsNoTracking()
                    .Where(k => k.StatusAktif && companyIds.Contains(k.IdPerusahaan))
                    .ToListAsync();

                allGroupKaryawans = FilterEmployeesByParentScope(allGroupKaryawans, grp.ScopeParentId, allCompanies, relations);
                
                var allGroupKaryawanIds = allGroupKaryawans.Select(k => k.IdKaryawan).ToList();
                var allGroupTargets = await _context.KaryawanJabatanMappings.AsNoTracking()
                    .Where(m => allGroupKaryawanIds.Contains(m.KaryawanId))
                    .ToDictionaryAsync(m => m.KaryawanId);

                var allGroupKaryawanNiks = allGroupKaryawans.Select(k => k.NoNik).Where(nik => !string.IsNullOrEmpty(nik)).ToList();
                var groupRosters = await _context.Rosters.AsNoTracking()
                    .Where(r => allGroupKaryawanNiks.Contains(r.Nik))
                    .ToListAsync();
                var groupRostersByNik = groupRosters
                    .GroupBy(r => r.Nik, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                int ScaleTargetSubcon(int baseTarget, double rat, int daysOnsite)
                {
                    if (baseTarget == 0) return 0;
                    if (daysOnsite == 0) return 0;
                    int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                    return Math.Max(scaled, 1);
                }

                int totalDaysInMonthGroup = DateTime.DaysInMonth(selectedYear, selectedMonth);

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

                    int onsiteDays = totalDaysInMonthGroup;
                    bool hasRoster = false;

                    if (!string.IsNullOrEmpty(nik) && groupRostersByNik.TryGetValue(nik, out var empRosters))
                    {
                        int computedOnsite = 0;
                        foreach (var r in empRosters)
                        {
                            var overlapStart = r.AwalDinas > startOfMonthMaincon ? r.AwalDinas : startOfMonthMaincon;
                            var overlapEnd = r.AkhirDinas < endOfMonthMaincon ? r.AkhirDinas : endOfMonthMaincon;
                            if (overlapStart <= overlapEnd)
                            {
                                computedOnsite += (overlapEnd - overlapStart).Days + 1;
                            }
                        }
                        if (computedOnsite > 0)
                        {
                            hasRoster = true;
                            onsiteDays = computedOnsite;
                        }
                    }

                    double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonthGroup : 1.0;

                    int mtdTgtH = hasRoster ? ScaleTargetSubcon(hTar, ratio, onsiteDays) : hTar;
                    int mtdTgtI = hasRoster ? ScaleTargetSubcon(insTar, ratio, onsiteDays) : insTar;
                    int mtdTgtST = hasRoster ? ScaleTargetSubcon(stTar, ratio, onsiteDays) : stTar;
                    int mtdTgtO = hasRoster ? ScaleTargetSubcon(obsTar, ratio, onsiteDays) : obsTar;
                    int mtdTgtC = hasRoster ? ScaleTargetSubcon(cTar, ratio, onsiteDays) : cTar;

                    int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                    int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                    int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                    int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                    int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                    int cappedH = Math.Min(actH, mtdTgtH);
                    int cappedI = Math.Min(actI, mtdTgtI);
                    int cappedST = Math.Min(actST, mtdTgtST);
                    int cappedO = Math.Min(actO, mtdTgtO);
                    int cappedC = Math.Min(actC, mtdTgtC);

                    totalTargetH += mtdTgtH; totalActualH += cappedH;
                    totalTargetI += mtdTgtI; totalActualI += cappedI;
                    totalTargetS += mtdTgtST; totalActualS += cappedST;
                    totalTargetO += mtdTgtO; totalActualO += cappedO;
                    totalTargetC += mtdTgtC; totalActualC += cappedC;
                }

                var uncompliantSubs = new List<string>();   // punya target tapi belum ada submisi
                var noTargetSubs = new List<string>();        // tidak ada karyawan ber-target sama sekali

                // Subcon calculations
                foreach (var sub in grp.SubconCompanies)
                {
                    var subKaryawans = allGroupKaryawans.Where(k => k.IdPerusahaan == sub.PerusahaanId).ToList();
                    int subTargetH = 0, subActualH = 0;
                    int subTargetI = 0, subActualI = 0;
                    int subTargetS = 0, subActualS = 0;
                    int subTargetO = 0, subActualO = 0;
                    int subTargetC = 0, subActualC = 0;
                    int subEmpsWithTarget = 0;
                    int subRawSubmissions = 0;

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

                        int actH = string.IsNullOrEmpty(nik) ? 0 : (hazByNik.TryGetValue(nik, out var ah) ? ah : 0);
                        int actI = string.IsNullOrEmpty(nik) ? 0 : (insByNik.TryGetValue(nik, out var ai) ? ai : 0);
                        int actST = string.IsNullOrEmpty(nik) ? 0 : (stByNik.TryGetValue(nik, out var ast) ? ast : 0);
                        int actO = string.IsNullOrEmpty(nik) ? 0 : (obsByNik.TryGetValue(nik, out var ao) ? ao : 0);
                        int actC = string.IsNullOrEmpty(nik) ? 0 : (coaByNik.TryGetValue(nik, out var ac) ? ac : 0);

                        // Akumulasi raw submissions berdasarkan NIK karyawan subcon ini
                        subRawSubmissions += (actH + actI + actST + actO + actC);

                        if (hTar + insTar + stTar + obsTar + cTar == 0)
                        {
                            continue;
                        }

                        subEmpsWithTarget++;

                        int onsiteDays = totalDaysInMonthGroup;
                        bool hasRoster = false;

                        if (!string.IsNullOrEmpty(nik) && groupRostersByNik.TryGetValue(nik, out var empRosters))
                        {
                            int computedOnsite = 0;
                            foreach (var r in empRosters)
                            {
                                var overlapStart = r.AwalDinas > startOfMonthMaincon ? r.AwalDinas : startOfMonthMaincon;
                                var overlapEnd = r.AkhirDinas < endOfMonthMaincon ? r.AkhirDinas : endOfMonthMaincon;
                                if (overlapStart <= overlapEnd)
                                {
                                    computedOnsite += (overlapEnd - overlapStart).Days + 1;
                                }
                            }
                            if (computedOnsite > 0)
                            {
                                hasRoster = true;
                                onsiteDays = computedOnsite;
                            }
                        }

                        double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonthGroup : 1.0;

                        int mtdTgtH = hasRoster ? ScaleTargetSubcon(hTar, ratio, onsiteDays) : hTar;
                        int mtdTgtI = hasRoster ? ScaleTargetSubcon(insTar, ratio, onsiteDays) : insTar;
                        int mtdTgtST = hasRoster ? ScaleTargetSubcon(stTar, ratio, onsiteDays) : stTar;
                        int mtdTgtO = hasRoster ? ScaleTargetSubcon(obsTar, ratio, onsiteDays) : obsTar;
                        int mtdTgtC = hasRoster ? ScaleTargetSubcon(cTar, ratio, onsiteDays) : cTar;

                        int cappedH = Math.Min(actH, mtdTgtH);
                        int cappedI = Math.Min(actI, mtdTgtI);
                        int cappedST = Math.Min(actST, mtdTgtST);
                        int cappedO = Math.Min(actO, mtdTgtO);
                        int cappedC = Math.Min(actC, mtdTgtC);

                        subTargetH += mtdTgtH; subActualH += cappedH;
                        subTargetI += mtdTgtI; subActualI += cappedI;
                        subTargetS += mtdTgtST; subActualS += cappedST;
                        subTargetO += mtdTgtO; subActualO += cappedO;
                        subTargetC += mtdTgtC; subActualC += cappedC;
                    }

                    int subTargetTotal = subTargetH + subTargetI + subTargetS + subTargetO + subTargetC;
                    int subActualTotal = subActualH + subActualI + subActualS + subActualO + subActualC;

                    if (subEmpsWithTarget == 0)
                    {
                        noTargetSubs.Add(sub.NamaPerusahaan ?? "Unknown");
                    }
                    else
                    {
                        if (subRawSubmissions == 0)
                        {
                            uncompliantSubs.Add(sub.NamaPerusahaan ?? "Unknown");
                        }

                        allSubconStats.Add(new MostActiveSubconViewModel
                        {
                            PerusahaanId = sub.PerusahaanId,
                            PerusahaanName = sub.NamaPerusahaan ?? "Unknown",
                            ParentCompanyName = grp.SubconParentName,
                            ParentCompanyId = grp.SubconParentId,
                            TotalEmployees = subKaryawans.Count,
                            EmployeesWithTarget = subEmpsWithTarget,
                            ComplianceRate = subTargetTotal > 0 ? Math.Round((double)subActualTotal / subTargetTotal * 100.0, 1) : 0,
                            TotalSubmissions = subRawSubmissions,
                            TargetSubmissions = subTargetTotal
                        });
                    }
                }

                int totalGroupTarget = totalTargetH + totalTargetI + totalTargetS + totalTargetO + totalTargetC;
                int totalGroupActual = totalActualH + totalActualI + totalActualS + totalActualO + totalActualC;

                var compVm = new MainconGroupComparisonViewModel
                {
                    MainconId = grp.GroupId,
                    MainconName = grp.GroupName,
                    TotalEmployees = totalGroupEmployees,
                    EmployeesWithTargetCount = employeesWithTargetCount,
                    ChildCompanyNames = grp.SubconCompanies.Select(s => s.NamaPerusahaan ?? "Unknown").ToList(),
                    UncompliantChildCompanyNames = uncompliantSubs,
                    NoTargetChildCompanyNames = noTargetSubs,
                    OverallComplianceRate = totalGroupTarget > 0 ? Math.Round(Math.Min(100.0, (double)totalGroupActual / totalGroupTarget * 100.0), 1) : 0,
                    HazardComplianceRate = totalTargetH > 0 ? Math.Round(Math.Min(100.0, (double)totalActualH / totalTargetH * 100.0), 1) : 0,
                    InspeksiComplianceRate = totalTargetI > 0 ? Math.Round(Math.Min(100.0, (double)totalActualI / totalTargetI * 100.0), 1) : 0,
                    SafetyTalkComplianceRate = totalTargetS > 0 ? Math.Round(Math.Min(100.0, (double)totalActualS / totalTargetS * 100.0), 1) : 0,
                    ObservasiComplianceRate = totalTargetO > 0 ? Math.Round(Math.Min(100.0, (double)totalActualO / totalTargetO * 100.0), 1) : 0,
                    CoachingComplianceRate = totalTargetC > 0 ? Math.Round(Math.Min(100.0, (double)totalActualC / totalTargetC * 100.0), 1) : 0,
                    
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

            var neverLoggedInCompanies = allCompanies
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

            // Scaled targets by company
            var allActiveKaryawans = await _context.Karyawans.AsNoTracking()
                .Where(k => k.StatusAktif == true)
                .ToListAsync();

            var allActiveKaryawanIds = allActiveKaryawans.Select(k => k.IdKaryawan).ToList();
            var allActiveTargets = await _context.KaryawanJabatanMappings.AsNoTracking()
                .Where(m => allActiveKaryawanIds.Contains(m.KaryawanId))
                .ToListAsync();
            var allActiveTargetsDict = allActiveTargets.ToDictionary(m => m.KaryawanId);

            var allActiveNiks = allActiveKaryawans.Select(k => k.NoNik).Where(nik => !string.IsNullOrEmpty(nik)).ToList();
            var allActiveRosters = await _context.Rosters.AsNoTracking()
                .Where(r => allActiveNiks.Contains(r.Nik))
                .ToListAsync();
            var allActiveRostersByNik = allActiveRosters
                .GroupBy(r => r.Nik, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var targetDict = new Dictionary<int, int>();
            int totalDaysInMonthTargetDict = DateTime.DaysInMonth(selectedYear, selectedMonth);

            int ScaleTargetTargetDict(int baseTarget, double rat, int daysOnsite)
            {
                if (baseTarget == 0) return 0;
                if (daysOnsite == 0) return 0;
                int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                return Math.Max(scaled, 1);
            }

            foreach (var emp in allActiveKaryawans)
            {
                int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0;
                if (allActiveTargetsDict.TryGetValue(emp.IdKaryawan, out var t))
                {
                    hTar = t.TargetHazardReport ?? 2;
                    insTar = t.TargetInspeksi ?? 1;
                    stTar = t.TargetSafetyTalk ?? 1;
                    obsTar = t.TargetObservasi ?? 0;
                    cTar = t.TargetCoaching ?? 0;
                }

                if (hTar + insTar + stTar + obsTar + cTar == 0)
                {
                    continue;
                }

                var nik = (emp.NoNik ?? string.Empty).Trim();
                int onsiteDays = totalDaysInMonthTargetDict;
                bool hasRoster = false;

                if (!string.IsNullOrEmpty(nik) && allActiveRostersByNik.TryGetValue(nik, out var empRosters))
                {
                    int computedOnsite = 0;
                    foreach (var r in empRosters)
                    {
                        var overlapStart = r.AwalDinas > startOfMonthM ? r.AwalDinas : startOfMonthM;
                        var overlapEnd = r.AkhirDinas < endOfMonthM ? r.AkhirDinas : endOfMonthM;
                        if (overlapStart <= overlapEnd)
                        {
                            computedOnsite += (overlapEnd - overlapStart).Days + 1;
                        }
                    }
                    if (computedOnsite > 0)
                    {
                        hasRoster = true;
                        onsiteDays = computedOnsite;
                    }
                }

                double ratio = hasRoster ? (double)onsiteDays / totalDaysInMonthTargetDict : 1.0;

                int mtdTgtH = hasRoster ? ScaleTargetTargetDict(hTar, ratio, onsiteDays) : hTar;
                int mtdTgtI = hasRoster ? ScaleTargetTargetDict(insTar, ratio, onsiteDays) : insTar;
                int mtdTgtST = hasRoster ? ScaleTargetTargetDict(stTar, ratio, onsiteDays) : stTar;
                int mtdTgtO = hasRoster ? ScaleTargetTargetDict(obsTar, ratio, onsiteDays) : obsTar;
                int mtdTgtC = hasRoster ? ScaleTargetTargetDict(cTar, ratio, onsiteDays) : cTar;

                int empTotalTarget = mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC;
                if (empTotalTarget > 0)
                {
                    if (targetDict.ContainsKey(emp.IdPerusahaan))
                    {
                        targetDict[emp.IdPerusahaan] += empTotalTarget;
                    }
                    else
                    {
                        targetDict[emp.IdPerusahaan] = empTotalTarget;
                    }
                }
            }

            var performanceList = new List<CompanyPerformanceViewModel>();
            int maxKuantitas = 1;
            int maxTargetAll = 1;
            foreach (var kv in targetDict) { if (kv.Value > maxTargetAll) maxTargetAll = kv.Value; }

            foreach (var comp in allCompanies)
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
                
                // Jangan membatasi totalTemuan di sini, biarkan UI dan Score perhitungan yang melimitnya 
                // agar data submisi aktual tetap terlihat utuh.
                
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

            var match = System.Text.RegularExpressions.Regex.Match(lokasi, @"(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)");
            if (!match.Success) return false;

            return double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat) &&
                   double.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon);
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

            var subKeywords = new[] { "safety", "hse", "ohs", "k3" };
            
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

        private List<KaryawanView> FilterEmployeesByParentScope(List<KaryawanView> employees, int parentId, List<PerusahaanView> allCompanies, List<PerusahaanHierarchyRelationView> relations)
        {
            var companyParentsMap = new Dictionary<int, HashSet<int>>();
            foreach (var c in allCompanies)
            {
                var parents = new HashSet<int>();
                if (c.PerusahaanIndukId.HasValue && c.PerusahaanIndukId.Value > 0)
                {
                    parents.Add(c.PerusahaanIndukId.Value);
                }
                var relParents = relations
                    .Where(r => r.ChildCompanyId == c.PerusahaanId && r.ParentCompanyId.HasValue && r.ParentIsActive == true)
                    .Select(r => r.ParentCompanyId!.Value);
                foreach (var pId in relParents)
                {
                    parents.Add(pId);
                }
                companyParentsMap[c.PerusahaanId] = parents;
            }

            return employees.Where(emp => {
                if (emp.IdPerusahaan == parentId)
                {
                    return true;
                }
                
                if (companyParentsMap.TryGetValue(emp.IdPerusahaan, out var parents))
                {
                    if (parents.Count > 1)
                    {
                        return emp.PerusahaanNodeId == parentId;
                    }
                }
                
                return true;
            }).ToList();
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
        public int Observations { get; set; }
        public int Coachings { get; set; }
        public int TotalSap { get; set; }
        public int Incidents { get; set; }
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
        public int ParentCompanyId { get; set; }
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
