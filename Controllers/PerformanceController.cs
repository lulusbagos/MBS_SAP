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

        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Pencapaian SAP";
            ViewData["ActiveTab"] = "Performance";

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;
            var isAdmin = User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value;

            // 1. Total Karyawan Aktif
            var totalKaryawan = await _context.Karyawans
                .CountAsync(k => k.StatusAktif && (companyId == null || k.IdPerusahaan == companyId.Value));

            // Target 1 minggu 1 per karyawan
            int weeklyTarget = totalKaryawan * 1;
            int monthlyTarget = totalKaryawan * 4;

            // Date ranges
            var now = DateTime.Now;
            var startOfWeek = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7); // Monday
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            // Submissions query
            var hazards = _context.HazardReports.Where(h => !h.IsDeleted && (companyId == null || h.PerusahaanId == companyId));
            var inspections = _context.Inspections.Where(i => !i.IsDeleted && (companyId == null || i.PerusahaanId == companyId));
            var safetyTalks = _context.SafetyTalks.Where(s => !s.IsDeleted && (companyId == null || s.PerusahaanId == companyId));
            var p5ms = _context.P5ms.Where(p => !p.IsDeleted && (companyId == null || p.PerusahaanId == companyId));

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
            int openLow = openHazardsList.Count(r => string.Equals(r, "Low", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Ringan", StringComparison.OrdinalIgnoreCase));
            int openMedium = openHazardsList.Count(r => string.Equals(r, "Medium", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sedang", StringComparison.OrdinalIgnoreCase));
            int openHigh = openHazardsList.Count(r => string.Equals(r, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Berat", StringComparison.OrdinalIgnoreCase));
            int openExtreme = openHazardsList.Count(r => string.Equals(r, "Extreme", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Sangat Berat", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "Critical", StringComparison.OrdinalIgnoreCase));

            // 5. Total Open vs Closed Hazards
            int totalOpenHazards = await hazards.CountAsync(h => h.StatusTemuan == "Open");
            int totalClosedHazards = await hazards.CountAsync(h => h.StatusTemuan == "Closed");

            // 6. Leaderboard Perusahaan
            var allCompanies = await _context.Perusahaans.Where(p => p.StatusAktif).ToListAsync();
            var allKaryawans = await _context.Karyawans.Where(k => k.StatusAktif).ToListAsync();

            var compHazards = await _context.HazardReports.Where(h => !h.IsDeleted && h.CreatedAt >= startOfMonth).GroupBy(h => h.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compInspections = await _context.Inspections.Where(i => !i.IsDeleted && i.CreatedAt >= startOfMonth).GroupBy(i => i.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compSafetyTalks = await _context.SafetyTalks.Where(s => !s.IsDeleted && s.CreatedAt >= startOfMonth).GroupBy(s => s.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();
            var compP5ms = await _context.P5ms.Where(p => !p.IsDeleted && p.CreatedAt >= startOfMonth).GroupBy(p => p.PerusahaanId).Select(g => new { CompId = g.Key, Count = g.Count() }).ToListAsync();

            var leaderboard = new List<CompanyLeaderboardViewModel>();
            foreach (var c in allCompanies)
            {
                if (!isAdmin && companyId.HasValue && c.PerusahaanId != companyId.Value)
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

            leaderboard = leaderboard.OrderByDescending(l => l.AchievementRate).ToList();
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
                myHazardsWeek = await _context.HazardReports.CountAsync(h => !h.IsDeleted && h.Nik == userNik && h.CreatedAt >= startOfWeek);
                myInspectionsWeek = await _context.Inspections.CountAsync(i => !i.IsDeleted && i.Nik == userNik && i.CreatedAt >= startOfWeek);
                mySafetyTalksWeek = await _context.SafetyTalks.CountAsync(s => !s.IsDeleted && s.Nik == userNik && s.CreatedAt >= startOfWeek);
                myP5msWeek = await _context.P5ms.CountAsync(p => !p.IsDeleted && p.Nik == userNik && p.CreatedAt >= startOfWeek);

                myHazardsMonth = await _context.HazardReports.CountAsync(h => !h.IsDeleted && h.Nik == userNik && h.CreatedAt >= startOfMonth);
                myInspectionsMonth = await _context.Inspections.CountAsync(i => !i.IsDeleted && i.Nik == userNik && i.CreatedAt >= startOfMonth);
                mySafetyTalksMonth = await _context.SafetyTalks.CountAsync(s => !s.IsDeleted && s.Nik == userNik && s.CreatedAt >= startOfMonth);
                myP5msMonth = await _context.P5ms.CountAsync(p => !p.IsDeleted && p.Nik == userNik && p.CreatedAt >= startOfMonth);
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

            ViewBag.OpenLow = openLow;
            ViewBag.OpenMedium = openMedium;
            ViewBag.OpenHigh = openHigh;
            ViewBag.OpenExtreme = openExtreme;

            ViewBag.TotalOpenHazards = totalOpenHazards;
            ViewBag.TotalClosedHazards = totalClosedHazards;

            ViewBag.Leaderboard = leaderboard;
            ViewBag.MonthlyTrend = monthlyTrend;

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
            bool isSafetyRole = (jobTitle != null && jobTitle.Contains("safety", StringComparison.OrdinalIgnoreCase)) || isAdmin;
            ViewBag.IsSafetyRole = isSafetyRole;

            if (isSafetyRole)
            {
                // Query active employees of this company
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
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> DownloadUncompliantReport(string range = "week")
        {
            var jobTitle = User.FindFirst("JobTitle")?.Value;
            var isAdmin = User.IsInRole("Admin");
            bool isSafetyRole = (jobTitle != null && jobTitle.Contains("safety", StringComparison.OrdinalIgnoreCase)) || isAdmin;

            if (!isSafetyRole)
            {
                return Forbid();
            }

            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;

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
}
