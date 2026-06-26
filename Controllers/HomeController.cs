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

            // Query dynamic dashboard statistics
            var totalHazards = await _context.HazardReports.CountAsync();
            var openHazards = await _context.HazardReports.CountAsync(h => h.StatusTemuan == "Open");
            var closedHazards = await _context.HazardReports.CountAsync(h => h.StatusTemuan == "Closed");
            
            var totalInspections = await _context.Inspections.CountAsync();
            var totalActionPlans = await _context.ActionPlans.CountAsync();
            var totalSafetyTalks = await _context.SafetyTalks.CountAsync();
            var totalP5ms = await _context.P5ms.CountAsync();
            
            // Dynamic Compliance score calculation based on submissions
            int complianceScore = 80; // Baseline
            int totalActivities = totalHazards + totalInspections + totalActionPlans + totalSafetyTalks + totalP5ms;
            if (totalActivities > 0)
            {
                complianceScore = Math.Min(100, 70 + (totalActivities * 2));
            }

            ViewData["TotalHazards"] = totalHazards;
            ViewData["OpenHazards"] = openHazards;
            ViewData["ClosedHazards"] = closedHazards;
            ViewData["TotalInspections"] = totalInspections;
            ViewData["TotalActionPlans"] = totalActionPlans;
            ViewData["TotalSafetyTalks"] = totalSafetyTalks;
            ViewData["TotalP5ms"] = totalP5ms;
            ViewData["ComplianceScore"] = complianceScore;

            // Load recent history items
            var recentHazards = await _context.HazardReports
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

            var recentInspections = await _context.Inspections
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

            var recentActionPlans = await _context.ActionPlans
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

            var recentSafetyTalks = await _context.SafetyTalks
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

            var recentP5ms = await _context.P5ms
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
