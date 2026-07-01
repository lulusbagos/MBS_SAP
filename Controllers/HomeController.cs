using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var nrp = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userNik = nrp?.Trim();
            bool hasUserNik = !string.IsNullOrWhiteSpace(userNik);
            if (!string.IsNullOrEmpty(nrp))
            {
                var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
                if (overridePwd == null || !overridePwd.HasAgreedToTerms)
                {
                    return RedirectToAction("UserAgreement", "Account");
                }
            }

            ViewData["HeaderTitle"] = "Portal K3 MBS";
            ViewData["ActiveTab"] = "Home";

            var runningTexts = await _context.RunningTexts
                .Where(r => r.IsAktif)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.Pesan)
                .ToListAsync();

            ViewData["RunningTexts"] = runningTexts;

            // Query dashboard berbasis akun login (NIK), bukan agregasi perusahaan.
            var hazardQuery = _context.HazardReports
                .Where(h => !h.IsDeleted && hasUserNik && h.Nik == userNik);
            var inspectionQuery = _context.Inspections
                .Where(i => !i.IsDeleted && hasUserNik && i.Nik == userNik);
            var actionPlanQuery = _context.ActionPlans
                .Where(a => !a.IsDeleted && hasUserNik && (a.Nik == userNik || a.NikPja == userNik));
            var safetyTalkQuery = _context.SafetyTalks
                .Where(s => !s.IsDeleted && hasUserNik && s.Nik == userNik);
            var p5mQuery = _context.P5ms
                .Where(p => !p.IsDeleted && hasUserNik && p.Nik == userNik);

            var totalHazards    = await hazardQuery.CountAsync();
            var openHazards     = await hazardQuery.CountAsync(h => h.StatusTemuan == "Open");
            var closedHazards   = await hazardQuery.CountAsync(h => h.StatusTemuan == "Closed");
            var totalInspections  = await inspectionQuery.CountAsync();
            var totalActionPlans  = await actionPlanQuery.CountAsync();
            var totalSafetyTalks  = await safetyTalkQuery.CountAsync();
            var totalP5ms         = await p5mQuery.CountAsync();

            // Kepatuhan berbasis target mingguan:
            // Minimal 1 aktivitas gabungan per minggu (rolling 4 minggu).
            var startOfToday = DateTime.Today;
            var startOfCurrentWeek = startOfToday.AddDays(-((int)startOfToday.DayOfWeek + 6) % 7); // Monday-based
            var startOfWindow = startOfCurrentWeek.AddDays(-21); // 4 weeks window
            var endOfWindowExclusive = startOfCurrentWeek.AddDays(7);

            var hazardDates = await hazardQuery
                .Where(h => h.CreatedAt >= startOfWindow && h.CreatedAt < endOfWindowExclusive)
                .Select(h => h.CreatedAt)
                .ToListAsync();

            var inspectionDates = await inspectionQuery
                .Where(i => i.CreatedAt >= startOfWindow && i.CreatedAt < endOfWindowExclusive)
                .Select(i => i.CreatedAt)
                .ToListAsync();

            var actionPlanDates = await actionPlanQuery
                .Where(a => a.CreatedAt >= startOfWindow && a.CreatedAt < endOfWindowExclusive)
                .Select(a => a.CreatedAt)
                .ToListAsync();

            var safetyTalkDates = await safetyTalkQuery
                .Where(s => s.CreatedAt >= startOfWindow && s.CreatedAt < endOfWindowExclusive)
                .Select(s => s.CreatedAt)
                .ToListAsync();

            var p5mDates = await p5mQuery
                .Where(p => p.CreatedAt >= startOfWindow && p.CreatedAt < endOfWindowExclusive)
                .Select(p => p.CreatedAt)
                .ToListAsync();

            var allActivityDates = hazardDates
                .Concat(inspectionDates)
                .Concat(actionPlanDates)
                .Concat(safetyTalkDates)
                .Concat(p5mDates)
                .ToList();

            var weeklyCounts = allActivityDates
                .GroupBy(d => $"{ISOWeek.GetYear(d)}-{ISOWeek.GetWeekOfYear(d)}")
                .ToDictionary(g => g.Key, g => g.Count());

            int targetWeeks = 4;
            int compliantWeeks = 0;
            for (int i = 0; i < targetWeeks; i++)
            {
                var weekDate = startOfCurrentWeek.AddDays(-7 * i);
                var weekKey = $"{ISOWeek.GetYear(weekDate)}-{ISOWeek.GetWeekOfYear(weekDate)}";
                if (weeklyCounts.TryGetValue(weekKey, out var countInWeek) && countInWeek >= 1)
                {
                    compliantWeeks++;
                }
            }

            int complianceScore = (int)Math.Round((double)compliantWeeks / targetWeeks * 100.0, MidpointRounding.AwayFromZero);

            ViewData["TotalHazards"] = totalHazards;
            ViewData["OpenHazards"] = openHazards;
            ViewData["ClosedHazards"] = closedHazards;
            ViewData["TotalInspections"] = totalInspections;
            ViewData["TotalActionPlans"] = totalActionPlans;
            ViewData["TotalSafetyTalks"] = totalSafetyTalks;
            ViewData["TotalP5ms"] = totalP5ms;
            ViewData["ComplianceScore"] = complianceScore;
            ViewData["CompliantWeeks"] = compliantWeeks;
            ViewData["TargetWeeks"] = targetWeeks;

            // Load recent history items — difilter akun login
            var recentHazards = await hazardQuery
                .OrderByDescending(h => h.CreatedAt)
                .Take(2)
                .Select(h => new RecentActivityViewModel
                {
                    Type = "Hazard",
                    Title = "Hazard: " + (h.Lokasi ?? h.Area ?? "Unknown"),
                    Description = h.Temuan ?? "",
                    Date = h.CreatedAt,
                    Status = h.StatusTemuan,
                    User = h.Nama
                }).ToListAsync();

            var recentInspections = await inspectionQuery
                .OrderByDescending(i => i.CreatedAt)
                .Take(2)
                .Select(i => new RecentActivityViewModel
                {
                    Type = "Inspection",
                    Title = "Inspeksi: " + (i.JenisInspeksi ?? "Umum"),
                    Description = "Inspeksi di area " + (i.Area ?? "umum"),
                    Date = i.CreatedAt,
                    Status = "Completed",
                    User = i.Nama
                }).ToListAsync();

            var recentActionPlans = await actionPlanQuery
                .OrderByDescending(a => a.CreatedAt)
                .Take(2)
                .Select(a => new RecentActivityViewModel
                {
                    Type = "ActionPlan",
                    Title = "Action Plan: " + (a.KategoriTemuan ?? "Temuan"),
                    Description = a.DetilTemuan ?? "",
                    Date = a.CreatedAt,
                    Status = a.Status ?? "Open",
                    User = a.Nama
                }).ToListAsync();

            var recentSafetyTalks = await safetyTalkQuery
                .OrderByDescending(s => s.CreatedAt)
                .Take(2)
                .Select(s => new RecentActivityViewModel
                {
                    Type = "SafetyTalk",
                    Title = "Safety Talk: " + (s.Judul ?? "Talk"),
                    Description = s.Keterangan ?? "",
                    Date = s.CreatedAt,
                    Status = "Completed",
                    User = s.Nama
                }).ToListAsync();

            var recentP5ms = await p5mQuery
                .OrderByDescending(p => p.CreatedAt)
                .Take(2)
                .Select(p => new RecentActivityViewModel
                {
                    Type = "P5m",
                    Title = "P5M: " + (p.Judul ?? "Pre-Start"),
                    Description = p.Keterangan ?? "",
                    Date = p.CreatedAt,
                    Status = "Completed",
                    User = p.Nama
                }).ToListAsync();

            // Merge and sort activities
            var recentActivities = recentHazards
                .Concat(recentInspections)
                .Concat(recentActionPlans)
                .Concat(recentSafetyTalks)
                .Concat(recentP5ms)
                .OrderByDescending(a => a.Date)
                .Take(6)
                .ToList();

            return View(recentActivities);
        }

        public IActionResult SafetyQuiz()
        {
            ViewData["HeaderTitle"] = "Safety Quiz";
            ViewData["ActiveTab"] = "Home";
            return View();
        }
    }

    public class RecentActivityViewModel
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
    }
}
