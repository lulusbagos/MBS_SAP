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

            string? kategoriPengawas = null;
            int targetHazardReport = 2;
            int targetInspeksi = 1;
            int targetSafetyTalk = 1;
            int targetObservasi = 0;
            int targetCoaching = 0;
            int targetP5m = 1;

            if (hasUserNik)
            {
                var currentKaryawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik != null && k.NoNik.Trim() == userNik && k.StatusAktif);
                if (currentKaryawan != null)
                {
                    var targetMapping = await _context.KaryawanJabatanMappings.FirstOrDefaultAsync(m => m.KaryawanId == currentKaryawan.IdKaryawan);
                    if (targetMapping != null)
                    {
                        kategoriPengawas = targetMapping.KategoriPengawas;
                        targetHazardReport = targetMapping.TargetHazardReport ?? 2;
                        targetInspeksi = targetMapping.TargetInspeksi ?? 1;
                        targetSafetyTalk = targetMapping.TargetSafetyTalk ?? 1;
                        targetObservasi = targetMapping.TargetObservasi ?? 0;
                        targetCoaching = targetMapping.TargetCoaching ?? 0;
                        // p5m tidak ada di view, gunakan default 1
                        targetP5m = 1;
                    }
                }
            }
            ViewData["KategoriPengawas"] = kategoriPengawas;

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
                .Where(a => !a.IsDeleted && hasUserNik && (a.Nik == userNik || a.NikPja == userNik || a.NikPic == userNik));
            var safetyTalkQuery = _context.SafetyTalks
                .Where(s => !s.IsDeleted && hasUserNik && s.Nik == userNik);
            var p5mQuery = _context.P5ms
                .Where(p => !p.IsDeleted && hasUserNik && p.Nik == userNik);
            var coachingQuery = _context.Coachings
                .Where(c => !c.IsDeleted && hasUserNik && (c.Nik == userNik || _context.CoachingParticipants.Any(p => p.CoachingId == c.Id && p.Nik == userNik)));
 
            var startOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var observationQuery = _context.Observations.Where(o => !o.IsDeleted && hasUserNik && o.Nik == userNik);

            var hazardStats = await hazardQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count(),
                Open = g.Sum(h => h.StatusTemuan == "Open" ? 1 : 0),
                Closed = g.Sum(h => h.StatusTemuan == "Closed" ? 1 : 0),
                ThisMonth = g.Sum(h => h.Tanggal >= startOfMonth ? 1 : 0)
            }).FirstOrDefaultAsync();

            var inspectionStats = await inspectionQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count(),
                ThisMonth = g.Sum(i => i.Tanggal >= startOfMonth ? 1 : 0)
            }).FirstOrDefaultAsync();

            var actionPlanStats = await actionPlanQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count()
            }).FirstOrDefaultAsync();

            var safetyTalkStats = await safetyTalkQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count(),
                ThisMonth = g.Sum(s => s.Tanggal >= startOfMonth ? 1 : 0)
            }).FirstOrDefaultAsync();

            var p5mStats = await p5mQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count(),
                ThisMonth = g.Sum(p => p.Tanggal >= startOfMonth ? 1 : 0)
            }).FirstOrDefaultAsync();

            var coachingStats = await coachingQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count(),
                ThisMonth = g.Sum(c => c.Tanggal >= startOfMonth ? 1 : 0)
            }).FirstOrDefaultAsync();

            var observationStats = await observationQuery.GroupBy(x => 1).Select(g => new {
                Total = g.Count(),
                ThisMonth = g.Sum(o => o.Date >= startOfMonth ? 1 : 0)
            }).FirstOrDefaultAsync();

            var closedAssignedHazardCredits = await actionPlanQuery
                .Where(a => a.Status == "Closed"
                    && a.TanggalPerbaikan != null
                    && a.TanggalPerbaikan >= startOfMonth
                    && a.ItemSap != null
                    && a.ItemSap.StartsWith("hazard:")
                    && a.Nik != userNik)
                .Select(a => a.ItemSap!)
                .Distinct()
                .CountAsync();

            var closedAssignedInspectionCredits = await actionPlanQuery
                .Where(a => a.Status == "Closed"
                    && a.TanggalPerbaikan != null
                    && a.TanggalPerbaikan >= startOfMonth
                    && a.ItemSap != null
                    && a.ItemSap.StartsWith("inspection:")
                    && a.Nik != userNik)
                .Select(a => a.ItemSap!)
                .Distinct()
                .CountAsync();

            var totalHazards = hazardStats?.Total ?? 0;
            var openHazards = hazardStats?.Open ?? 0;
            var closedHazards = hazardStats?.Closed ?? 0;
            var thisMonthHazards = (hazardStats?.ThisMonth ?? 0) + closedAssignedHazardCredits;

            var totalInspections = inspectionStats?.Total ?? 0;
            var thisMonthInspections = (inspectionStats?.ThisMonth ?? 0) + closedAssignedInspectionCredits;

            var totalActionPlans = actionPlanStats?.Total ?? 0;

            var totalSafetyTalks = safetyTalkStats?.Total ?? 0;
            var thisMonthSafetyTalks = safetyTalkStats?.ThisMonth ?? 0;

            var totalP5ms = p5mStats?.Total ?? 0;
            var thisMonthP5ms = p5mStats?.ThisMonth ?? 0;

            var totalCoachings = coachingStats?.Total ?? 0;
            var thisMonthCoachings = coachingStats?.ThisMonth ?? 0;

            var totalObservations = observationStats?.Total ?? 0;
            var thisMonthObservations = observationStats?.ThisMonth ?? 0;
 
            int cappedActH = Math.Min(thisMonthHazards, targetHazardReport);
            int cappedActI = Math.Min(thisMonthInspections, targetInspeksi);
            int cappedActST = Math.Min(thisMonthSafetyTalks, targetSafetyTalk);
            int cappedActO = Math.Min(thisMonthObservations, targetObservasi);
            int cappedActC = Math.Min(thisMonthCoachings, targetCoaching);

            int myTotalMonthTarget = targetHazardReport + targetInspeksi + targetSafetyTalk + targetObservasi + targetCoaching;
            int myTotalThisMonth = cappedActH + cappedActI + cappedActST + cappedActO + cappedActC;

            int complianceScore = 0;
            if (myTotalMonthTarget > 0)
            {
                complianceScore = (int)Math.Round((double)myTotalThisMonth / myTotalMonthTarget * 100.0, MidpointRounding.AwayFromZero);
                if (complianceScore > 100) complianceScore = 100;
            }

            ViewData["TotalHazards"] = totalHazards;
            ViewData["OpenHazards"] = openHazards;
            ViewData["ClosedHazards"] = closedHazards;
            ViewData["TotalInspections"] = totalInspections;
            ViewData["TotalActionPlans"] = totalActionPlans;
            ViewData["TotalSafetyTalks"] = totalSafetyTalks;
            ViewData["TotalP5ms"] = totalP5ms;
            ViewData["TotalObservations"] = totalObservations;
            ViewData["TotalCoachings"] = totalCoachings;
            
            // Send specific targets and achievements for display
            ViewData["ComplianceScore"] = complianceScore;
            ViewData["MyTotalThisMonth"] = myTotalThisMonth;
            ViewData["MyTotalMonthTarget"] = myTotalMonthTarget;
            
            ViewData["ThisMonthHazards"] = thisMonthHazards;
            ViewData["ThisMonthInspections"] = thisMonthInspections;
            ViewData["ThisMonthSafetyTalks"] = thisMonthSafetyTalks;
            ViewData["ThisMonthP5ms"] = thisMonthP5ms;
            ViewData["ThisMonthObservations"] = thisMonthObservations;
            ViewData["ThisMonthCoachings"] = thisMonthCoachings;

            ViewData["TargetHazard"] = targetHazardReport;
            ViewData["TargetInspeksi"] = targetInspeksi;
            ViewData["TargetSafetyTalk"] = targetSafetyTalk;
            ViewData["TargetP5m"] = targetP5m;
            ViewData["TargetObservasi"] = targetObservasi;
            ViewData["TargetCoaching"] = targetCoaching;

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

            var recentCoachings = await coachingQuery
                .OrderByDescending(c => c.CreatedAt)
                .Take(2)
                .Select(c => new RecentActivityViewModel
                {
                    Type = "Coaching",
                    Title = "Coaching: " + (c.Tema ?? "Pembinaan"),
                    Description = c.Feedback ?? "",
                    Date = c.CreatedAt,
                    Status = "Completed",
                    User = c.Nama
                }).ToListAsync();

            // Merge and sort activities
            var recentActivities = recentHazards
                .Concat(recentInspections)
                .Concat(recentActionPlans)
                .Concat(recentSafetyTalks)
                .Concat(recentP5ms)
                .Concat(recentCoachings)
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
