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
                .Select(p => new { p.Id, p.Tanggal, p.Nama, p.Area, p.Lokasi, p.Topik, p.Judul, p.Keterangan, p.FotoKegiatan })
                .ToListAsync();

            foreach (var p in dbP5ms)
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

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var (companyId, allowedCompanyIds) = await ResolveCompanyScopeAsync();
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var department = User.FindFirst("Department")?.Value;
            bool isSafetyRole = CheckIsSafetyRole(jobTitle, department, isAdmin);
            var allCompanies = await _context.Perusahaans.Where(p => p.StatusAktif).ToListAsync();

            // 1. Total Karyawan Aktif
            var totalKaryawan = await _context.Karyawans
                .CountAsync(k => k.StatusAktif && (companyId == null || allowedCompanyIds.Contains(k.IdPerusahaan)));

            // Target 1 minggu 1 per karyawan
            int weeklyTarget = totalKaryawan * 1;
            int monthlyTarget = totalKaryawan * 4;

            // Date ranges
            var now = DateTime.Now;
            var startOfWeek = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7); // Monday
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            // Submissions query
            var hazards = _context.HazardReports.Where(h => !h.IsDeleted && (companyId == null || (h.PerusahaanId.HasValue && allowedCompanyIds.Contains(h.PerusahaanId.Value))));
            var inspections = _context.Inspections.Where(i => !i.IsDeleted && (companyId == null || (i.PerusahaanId.HasValue && allowedCompanyIds.Contains(i.PerusahaanId.Value))));
            var safetyTalks = _context.SafetyTalks.Where(s => !s.IsDeleted && (companyId == null || (s.PerusahaanId.HasValue && allowedCompanyIds.Contains(s.PerusahaanId.Value))));
            var p5ms = _context.P5ms.Where(p => !p.IsDeleted && (companyId == null || (p.PerusahaanId.HasValue && allowedCompanyIds.Contains(p.PerusahaanId.Value))));

            // 2. Realisasi Minggu Ini
            int weekHazards = await hazards.CountAsync(h => h.CreatedAt >= startOfWeek);
            int weekInspections = await inspections.CountAsync(i => i.CreatedAt >= startOfWeek);
            int weekSafetyTalks = await safetyTalks.CountAsync(s => s.CreatedAt >= startOfWeek);
            int weekP5ms = await p5ms.CountAsync(p => p.CreatedAt >= startOfWeek);
            int weekTotal = weekHazards + weekInspections + weekSafetyTalks + weekP5ms;

            // 3. Realisasi Bulan Ini
            int monthHazards = await hazards.CountAsync(h => h.CreatedAt >= startOfMonth);
            int monthInspections = await inspections.CountAsync(i => i.CreatedAt >= startOfMonth);
            int monthSafetyTalks = await safetyTalks.CountAsync(s => s.CreatedAt >= startOfMonth);
            int monthP5ms = await p5ms.CountAsync(p => p.CreatedAt >= startOfMonth);
            int monthTotal = monthHazards + monthInspections + monthSafetyTalks + monthP5ms;

            // 4. Open Hazards breakdown by Risk Level (Low/Medium/High/Extreme)
            var openHazardsList = await hazards.Where(h => h.StatusTemuan == "Open" && h.TingkatResiko != null).Select(h => h.TingkatResiko).ToListAsync();
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

            int highRiskOpen = openKritis + openExtreme + openHigh;
            double complianceRisk = totalOpenHazards > 0 ? (double)highRiskOpen / totalOpenHazards * 100 : 0;

            var allHazardRisks = await hazards.Select(h => new { h.StatusTemuan, h.TingkatResiko }).ToListAsync();
            int GetRiskWeight(string r) {
                if (string.IsNullOrEmpty(r)) return 0;
                if (r.Contains("Insiden", StringComparison.OrdinalIgnoreCase)) return 6;
                if (r.Contains("Kritis", StringComparison.OrdinalIgnoreCase) || r.Contains("Critical", StringComparison.OrdinalIgnoreCase)) return 5;
                if (r.Contains("Extreme", StringComparison.OrdinalIgnoreCase) || r.Contains("Ekstrim", StringComparison.OrdinalIgnoreCase) || r.Contains("Sangat Berat", StringComparison.OrdinalIgnoreCase)) return 4;
                if (r.Contains("High", StringComparison.OrdinalIgnoreCase) || r.Contains("Tinggi", StringComparison.OrdinalIgnoreCase) || r.Contains("Berat", StringComparison.OrdinalIgnoreCase)) return 3;
                if (r.Contains("Medium", StringComparison.OrdinalIgnoreCase) || r.Contains("Sedang", StringComparison.OrdinalIgnoreCase)) return 2;
                if (r.Contains("Low", StringComparison.OrdinalIgnoreCase) || r.Contains("Rendah", StringComparison.OrdinalIgnoreCase) || r.Contains("Ringan", StringComparison.OrdinalIgnoreCase)) return 1;
                return 0;
            }
            int totalRiskWeight = allHazardRisks.Sum(h => GetRiskWeight(h.TingkatResiko));
            int closedRiskWeight = allHazardRisks.Where(h => h.StatusTemuan == "Closed").Sum(h => GetRiskWeight(h.TingkatResiko));
            double rri = totalRiskWeight > 0 ? (double)closedRiskWeight / totalRiskWeight * 100 : 0;

            var hazardSigs = await hazards.Where(h => h.JenisBahaya != null && h.Area != null)
                .Select(h => new { h.JenisBahaya, h.Area, h.Lokasi, h.Pja })
                .ToListAsync();
            var groupedSigs = hazardSigs.GroupBy(h => $"{h.JenisBahaya}|{h.Area}|{h.Lokasi}|{h.Pja}").ToList();
            int repeatSignatures = groupedSigs.Count(g => g.Count() > 1);
            int totalSignatures = groupedSigs.Count;
            double rhr = totalSignatures > 0 ? (double)repeatSignatures / totalSignatures * 100 : 0;

            var topRepeated = groupedSigs
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new {
                    Label = $"{g.First().JenisBahaya} - {g.First().Area}",
                    Count = g.Count()
                })
                .ToList();
            
            ViewBag.TopRepeatedLabels = topRepeated.Select(x => x.Label).ToList();
            ViewBag.TopRepeatedData = topRepeated.Select(x => x.Count).ToList();


            var closedHazardsList = await hazards.Where(h => h.StatusTemuan == "Closed" && h.TingkatResiko != null).Select(h => h.TingkatResiko).ToListAsync();
            int closedKritis = closedHazardsList.Count(r => string.Equals(r, "Kritis", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Critical", StringComparison.OrdinalIgnoreCase));
            int closedExtreme = closedHazardsList.Count(r => string.Equals(r, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sangat Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ekstrim", StringComparison.OrdinalIgnoreCase));
            int closedHigh = closedHazardsList.Count(r => string.Equals(r, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Tinggi", StringComparison.OrdinalIgnoreCase));
            int highRiskClosed = closedKritis + closedExtreme + closedHigh;
            int totalHighRisk = highRiskOpen + highRiskClosed;
            double highRiskResolution = totalHighRisk > 0 ? (double)highRiskClosed / totalHighRisk * 100 : 0;

            // 5b. Extra Professional Graphs Data
            var allKategori = await hazards.Where(h => h.StatusTemuan == "Open" && h.KategoriBahaya != null).Select(h => h.KategoriBahaya).ToListAsync();
            int unsafeActCount = allKategori.Count(k => k.Contains("Tindakan", StringComparison.OrdinalIgnoreCase) || k.Contains("Act", StringComparison.OrdinalIgnoreCase) || k.Contains("KTA", StringComparison.OrdinalIgnoreCase));
            int unsafeConditionCount = allKategori.Count(k => k.Contains("Kondisi", StringComparison.OrdinalIgnoreCase) || k.Contains("Condition", StringComparison.OrdinalIgnoreCase) || k.Contains("KTC", StringComparison.OrdinalIgnoreCase));
            
            var topAreas = await hazards.Where(h => h.StatusTemuan == "Open" && !string.IsNullOrEmpty(h.Area))
                                        .GroupBy(h => h.Area)
                                        .Select(g => new { Area = g.Key, Count = g.Count() })
                                        .OrderByDescending(x => x.Count)
                                        .Take(5)
                                        .ToListAsync();

            // 6. Leaderboard Perusahaan
            var allKaryawans = await _context.Karyawans.Where(k => k.StatusAktif).ToListAsync();

            var compHazards = await _context.HazardReports.Where(h => !h.IsDeleted && h.CreatedAt >= startOfMonth).GroupBy(h => h.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compInspections = await _context.Inspections.Where(i => !i.IsDeleted && i.CreatedAt >= startOfMonth).GroupBy(i => i.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compSafetyTalks = await _context.SafetyTalks.Where(s => !s.IsDeleted && s.CreatedAt >= startOfMonth).GroupBy(s => s.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compP5ms = await _context.P5ms.Where(p => !p.IsDeleted && p.CreatedAt >= startOfMonth).GroupBy(p => p.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();

            var leaderboard = new List<CompanyLeaderboardViewModel>();
            foreach (var c in allCompanies)
            {
                if (!isAdmin && companyId.HasValue && !allowedCompanyIds.Contains(c.PerusahaanId))
                {
                    continue;
                }

                int empCount = allKaryawans.Count(k => k.IdPerusahaan == c.PerusahaanId);
                if (empCount == 0) continue;

                int subCount = (compHazards.FirstOrDefault(h => h.CompId == c.PerusahaanId)?.Count ?? 0)
                             + (compInspections.FirstOrDefault(i => i.CompId == c.PerusahaanId)?.Count ?? 0)
                             + (compSafetyTalks.FirstOrDefault(s => s.CompId == c.PerusahaanId)?.Count ?? 0)
                             + (compP5ms.FirstOrDefault(p => p.CompId == c.PerusahaanId)?.Count ?? 0);

                int target = empCount * 4;
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

            leaderboard = leaderboard.OrderByDescending(l => l.AchievementRate).Take(10).ToList();
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

            int myHazardsMonth = 0;
            int myInspectionsMonth = 0;
            int mySafetyTalksMonth = 0;
            int myP5msMonth = 0;

            if (!string.IsNullOrEmpty(userNik))
            {
                // Personal metrics must always follow logged-in user identity (NIK), not company aggregation.
                var myHazardsQuery = _context.HazardReports.Where(h => !h.IsDeleted && h.Nik == userNik);
                var myInspectionsQuery = _context.Inspections.Where(i => !i.IsDeleted && i.Nik == userNik);
                var mySafetyTalksQuery = _context.SafetyTalks.Where(s => !s.IsDeleted && s.Nik == userNik);
                var myP5msQuery = _context.P5ms.Where(p => !p.IsDeleted && p.Nik == userNik);

                myHazardsWeek = await myHazardsQuery.CountAsync(h => h.CreatedAt >= startOfWeek);
                myInspectionsWeek = await myInspectionsQuery.CountAsync(i => i.CreatedAt >= startOfWeek);
                mySafetyTalksWeek = await mySafetyTalksQuery.CountAsync(s => s.CreatedAt >= startOfWeek);
                myP5msWeek = await myP5msQuery.CountAsync(p => p.CreatedAt >= startOfWeek);

                myHazardsMonth = await myHazardsQuery.CountAsync(h => h.CreatedAt >= startOfMonth);
                myInspectionsMonth = await myInspectionsQuery.CountAsync(i => i.CreatedAt >= startOfMonth);
                mySafetyTalksMonth = await mySafetyTalksQuery.CountAsync(s => s.CreatedAt >= startOfMonth);
                myP5msMonth = await myP5msQuery.CountAsync(p => p.CreatedAt >= startOfMonth);
            }

            int myTotalWeek = myHazardsWeek + myInspectionsWeek + mySafetyTalksWeek + myP5msWeek;
            int myTotalMonth = myHazardsMonth + myInspectionsMonth + mySafetyTalksMonth + myP5msMonth;

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

            ViewBag.OpenInsiden = openInsiden;
            ViewBag.OpenKritis = openKritis;
            ViewBag.OpenExtreme = openExtreme;
            ViewBag.OpenHigh = openHigh;
            ViewBag.OpenMedium = openMedium;
            ViewBag.OpenLow = openLow;

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
            ViewBag.RepeatHazards = repeatSignatures;
            ViewBag.TotalSignatures = totalSignatures;
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
            ViewBag.MyTotalWeek = myTotalWeek;

            ViewBag.MyHazardsMonth = myHazardsMonth;
            ViewBag.MyInspectionsMonth = myInspectionsMonth;
            ViewBag.MySafetyTalksMonth = mySafetyTalksMonth;
            ViewBag.MyP5msMonth = myP5msMonth;
            ViewBag.MyTotalMonth = myTotalMonth;

            // Individual Gamification Rank
            string myBadgeName = "Safety Novice";
            string myBadgeIcon = "bi-shield-slash";
            string myBadgeColor = "#9ca3af";
            
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
            var weekSubmitters = new HashSet<string>();
            var weekHazNiks = await hazards.Where(h => h.CreatedAt >= startOfWeek).Select(h => h.Nik).Distinct().ToListAsync();
            var weekInsNiks = await inspections.Where(i => i.CreatedAt >= startOfWeek).Select(i => i.Nik).Distinct().ToListAsync();
            var weekSafNiks = await safetyTalks.Where(s => s.CreatedAt >= startOfWeek).Select(s => s.Nik).Distinct().ToListAsync();
            var weekP5mNiks = await p5ms.Where(p => p.CreatedAt >= startOfWeek).Select(p => p.Nik).Distinct().ToListAsync();
            
            foreach (var n in weekHazNiks.Concat(weekInsNiks).Concat(weekSafNiks).Concat(weekP5mNiks))
            {
                if (!string.IsNullOrEmpty(n)) weekSubmitters.Add(n.Trim());
            }

            // Get submitters for current month
            var monthSubmitters = new Dictionary<string, int>();
            var monthHazNiks = await hazards.Where(h => h.CreatedAt >= startOfMonth).Select(h => new { h.Nik }).ToListAsync();
            var monthInsNiks = await inspections.Where(i => i.CreatedAt >= startOfMonth).Select(i => new { i.Nik }).ToListAsync();
            var monthSafNiks = await safetyTalks.Where(s => s.CreatedAt >= startOfMonth).Select(s => new { s.Nik }).ToListAsync();
            var monthP5mNiks = await p5ms.Where(p => p.CreatedAt >= startOfMonth).Select(p => new { p.Nik }).ToListAsync();

            foreach (var item in monthHazNiks.Concat(monthInsNiks).Concat(monthSafNiks).Concat(monthP5mNiks))
            {
                if (string.IsNullOrEmpty(item.Nik)) continue;
                var cleanNik = item.Nik.Trim();
                if (monthSubmitters.ContainsKey(cleanNik))
                    monthSubmitters[cleanNik]++;
                else
                    monthSubmitters[cleanNik] = 1;
            }

            var uncompliantWeekList = new List<UncompliantEmployeeViewModel>();
            var uncompliantMonthList = new List<UncompliantEmployeeViewModel>();

            foreach (var k in activeKaryawans)
            {
                var cleanNik = k.NoNik.Trim();
                int monthCount = monthSubmitters.ContainsKey(cleanNik) ? monthSubmitters[cleanNik] : 0;
                bool hasWeekSub = weekSubmitters.Contains(cleanNik);

                if (!hasWeekSub)
                {
                    uncompliantWeekList.Add(new UncompliantEmployeeViewModel
                    {
                        Nik = k.NoNik,
                        Nama = k.NamaLengkap,
                        Departemen = k.NamaDepartemen,
                        Perusahaan = k.NamaPerusahaan,
                        SubmissionCount = 0
                    });
                }

                if (monthCount < 4)
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
            var nodeMap = new Dictionary<int, CompanyHierarchyNode>();
            foreach (var c in allCompanies)
            {
                if (companyId.HasValue && !allowedCompanyIds.Contains(c.PerusahaanId))
                {
                    continue;
                }

                int empCount = allKaryawans.Count(k => k.IdPerusahaan == c.PerusahaanId);
                
                int subCount = (compHazards.FirstOrDefault(h => h.CompId == c.PerusahaanId)?.Count ?? 0)
                             + (compInspections.FirstOrDefault(i => i.CompId == c.PerusahaanId)?.Count ?? 0)
                             + (compSafetyTalks.FirstOrDefault(s => s.CompId == c.PerusahaanId)?.Count ?? 0)
                             + (compP5ms.FirstOrDefault(p => p.CompId == c.PerusahaanId)?.Count ?? 0);

                int target = empCount * 4;
                double rate = target > 0 ? (double)subCount / target * 100.0 : 0.0;

                var node = new CompanyHierarchyNode
                {
                    CompanyId = c.PerusahaanId,
                    CompanyName = c.NamaPerusahaan ?? "Unknown",
                    ParentCompanyId = c.PerusahaanIndukId,
                    OwnEmployees = empCount,
                    OwnSubmissions = subCount,
                    OwnTarget = target,
                    OwnAchievementRate = Math.Round(rate, 1)
                };
                nodeMap[c.PerusahaanId] = node;
            }

            var rootNodes = new List<CompanyHierarchyNode>();
            foreach (var kvp in nodeMap)
            {
                var node = kvp.Value;
                if (companyId.HasValue)
                {
                    // Untuk user biasa, root-nya adalah perusahaannya sendiri
                    if (node.CompanyId == companyId.Value)
                    {
                        rootNodes.Add(node);
                    }
                    else if (node.ParentCompanyId.HasValue && node.ParentCompanyId.Value != 0 && nodeMap.ContainsKey(node.ParentCompanyId.Value))
                    {
                        var parentNode = nodeMap[node.ParentCompanyId.Value];
                        parentNode.Children.Add(node);
                    }
                }
                else
                {
                    // Untuk admin/safety, root adalah yang tidak punya parent di nodeMap
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
            }

            // Recursive cumulative logic local function
            void CalculateCumulative(CompanyHierarchyNode node)
            {
                node.CumulativeEmployees = node.OwnEmployees;
                node.CumulativeSubmissions = node.OwnSubmissions;
                node.CumulativeTarget = node.OwnTarget;

                foreach (var child in node.Children)
                {
                    CalculateCumulative(child);
                    node.CumulativeEmployees += child.CumulativeEmployees;
                    node.CumulativeSubmissions += child.CumulativeSubmissions;
                    node.CumulativeTarget += child.CumulativeTarget;
                }

                node.CumulativeAchievementRate = node.CumulativeTarget > 0 
                    ? Math.Round((double)node.CumulativeSubmissions / node.CumulativeTarget * 100.0, 1) 
                    : 0.0;

                node.Children = node.Children.OrderBy(c => c.CompanyName).ToList();
            }

            foreach (var root in rootNodes)
            {
                CalculateCumulative(root);
            }

            rootNodes = rootNodes.OrderBy(r => r.CompanyName).ToList();
            ViewBag.CompanyHierarchy = rootNodes;

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
            var startOfWeek = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7); // Monday
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

            var submitters = new Dictionary<string, int>();
            var hazNiks = await hazards.Select(h => h.Nik).ToListAsync();
            var insNiks = await inspections.Select(i => i.Nik).ToListAsync();
            var safNiks = await safetyTalks.Select(s => s.Nik).ToListAsync();
            var p5mNiks = await p5ms.Select(p => p.Nik).ToListAsync();

            foreach (var nik in hazNiks.Concat(insNiks).Concat(safNiks).Concat(p5mNiks))
            {
                if (string.IsNullOrEmpty(nik)) continue;
                var cleanNik = nik.Trim();
                if (submitters.ContainsKey(cleanNik))
                    submitters[cleanNik]++;
                else
                    submitters[cleanNik] = 1;
            }

            var uncompliantList = new List<UncompliantEmployeeViewModel>();
            foreach (var k in activeKaryawans)
            {
                var cleanNik = k.NoNik.Trim();
                int count = submitters.ContainsKey(cleanNik) ? submitters[cleanNik] : 0;

                if (range == "week" && count == 0)
                {
                    uncompliantList.Add(new UncompliantEmployeeViewModel
                    {
                        Nik = k.NoNik,
                        Nama = k.NamaLengkap,
                        Departemen = k.NamaDepartemen,
                        Perusahaan = k.NamaPerusahaan,
                        SubmissionCount = 0
                    });
                }
                else if (range == "month" && count < 4)
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
                wsSummary.Cell(7, 2).Value = hazNiks.Count + insNiks.Count + safNiks.Count + p5mNiks.Count;

                wsSummary.Cell(8, 1).Value = "Rata-rata Durasi Perbaikan Hazard (Hari)";
                wsSummary.Cell(8, 2).Value = avgClosureDays;

                wsSummary.Cell(9, 1).Value = "Kepatuhan Target SAP Perusahaan (%)";
                double totalReal = hazNiks.Count + insNiks.Count + safNiks.Count + p5mNiks.Count;
                double totalTar = range == "month" ? activeKaryawans.Count * 4 : activeKaryawans.Count * 1;
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
                
                worksheet.Cell(2, 1).Value = $"Periode: {(range == "week" ? "Minggu Ini (Target: 1 Laporan)" : "Bulan Ini (Target: 4 Laporan)")}";
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
                    worksheet.Cell(row, 6).Value = range == "week" ? "Belum Membuat SAP (0/1)" : $"Kurang Laporan ({emp.SubmissionCount}/4)";

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

        // Cumulative (Group) stats
        public int CumulativeEmployees { get; set; }
        public int CumulativeSubmissions { get; set; }
        public int CumulativeTarget { get; set; }
        public double CumulativeAchievementRate { get; set; }

        public List<CompanyHierarchyNode> Children { get; set; } = new List<CompanyHierarchyNode>();
    }
}
