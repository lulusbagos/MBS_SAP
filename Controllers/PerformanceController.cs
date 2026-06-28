using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
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

            return View();
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
}
