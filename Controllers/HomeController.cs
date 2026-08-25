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
using Microsoft.Extensions.Caching.Memory;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public HomeController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<IActionResult> Index()
        {
            var nrp = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userNik = nrp?.Trim();
            bool hasUserNik = !string.IsNullOrWhiteSpace(userNik);
            var userDept = User.FindFirst("Department")?.Value ?? string.Empty;
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

            if (!hasUserNik)
            {
                return View(new List<RecentActivityViewModel>());
            }

            bool forceRefresh = HttpContext.Request.Query.ContainsKey("refresh") && 
                               string.Equals(HttpContext.Request.Query["refresh"], "true", StringComparison.OrdinalIgnoreCase);

            var cacheKey = $"UserDashboardStats_{userNik}";
            if (forceRefresh || !_cache.TryGetValue(cacheKey, out DashboardStatsCache? stats) || stats == null)
            {
                stats = new DashboardStatsCache();

                var latestRoster = await _context.Rosters
                    .Where(r => r.Nik == userNik)
                    .OrderByDescending(r => r.AkhirCuti)
                    .FirstOrDefaultAsync();

                // Trigger popup if roster is unset/empty OR if today is after the active cycle (expired)
                stats.ShowRosterPopup = (latestRoster == null) || (DateTime.Today > latestRoster.AkhirCuti);

                stats.RosterHistory = await _context.Rosters
                    .Where(r => r.Nik == userNik)
                    .OrderByDescending(r => r.AkhirCuti)
                    .ToListAsync();

                var mitraRoster = await _context.MitraRosters
                    .FirstOrDefaultAsync(r => r.NoNik == userNik);
                if (mitraRoster != null)
                {
                    stats.MitraHariOnsite = mitraRoster.HariOnsite;
                    stats.MitraHariOffsite = mitraRoster.HariOffsite;
                }

                // Smart prefilling of roster dates in the form
                if (latestRoster != null)
                {
                    if (latestRoster.AkhirCuti >= DateTime.Today)
                    {
                        // Currently running: edit mode (can edit running roster)
                        stats.RosterAwalDinas = latestRoster.AwalDinas.ToString("yyyy-MM-dd");
                        stats.RosterAkhirDinas = latestRoster.AkhirDinas.ToString("yyyy-MM-dd");
                        stats.RosterAwalCuti = latestRoster.AwalCuti.ToString("yyyy-MM-dd");
                        stats.RosterAkhirCuti = latestRoster.AkhirCuti.ToString("yyyy-MM-dd");
                        stats.IsEditMode = true;
                    }
                    else
                    {
                        // Finished / past roster: cannot be edited. Prefill with new cycle dates.
                        var nextAwalDinas = latestRoster.AkhirCuti.AddDays(1);
                        int onsiteDays = (mitraRoster != null && mitraRoster.HariOnsite.HasValue) ? mitraRoster.HariOnsite.Value : 42;
                        int offsiteDays = (mitraRoster != null && mitraRoster.HariOffsite.HasValue) ? mitraRoster.HariOffsite.Value : 14;

                        var nextAkhirDinas = nextAwalDinas.AddDays(onsiteDays - 1);
                        var nextAwalCuti = nextAkhirDinas.AddDays(1);
                        var nextAkhirCuti = nextAwalCuti.AddDays(offsiteDays - 1);

                        stats.RosterAwalDinas = nextAwalDinas.ToString("yyyy-MM-dd");
                        stats.RosterAkhirDinas = nextAkhirDinas.ToString("yyyy-MM-dd");
                        stats.RosterAwalCuti = nextAwalCuti.ToString("yyyy-MM-dd");
                        stats.RosterAkhirCuti = nextAkhirCuti.ToString("yyyy-MM-dd");
                        stats.IsEditMode = false;
                    }
                }
                else
                {
                    stats.IsEditMode = false;
                }

                int targetHazardReport = 2;
                int targetInspeksi = 1;
                int targetSafetyTalk = 1;
                int targetObservasi = 0;
                int targetCoaching = 0;
                int targetP5m = 1;

                var currentKaryawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik != null && k.NoNik.Trim() == userNik && k.StatusAktif);
                if (currentKaryawan != null)
                {
                    var targetMapping = await _context.KaryawanJabatanMappings.FirstOrDefaultAsync(m => m.KaryawanId == currentKaryawan.IdKaryawan);
                    if (targetMapping != null)
                    {
                        stats.KategoriPengawas = targetMapping.KategoriPengawas;
                        stats.AlasanTargetZero = targetMapping.AlasanTargetZero;
                        targetHazardReport = targetMapping.TargetHazardReport ?? 2;
                        targetInspeksi = targetMapping.TargetInspeksi ?? 1;
                        targetSafetyTalk = targetMapping.TargetSafetyTalk ?? 1;
                        targetObservasi = targetMapping.TargetObservasi ?? 0;
                        targetCoaching = targetMapping.TargetCoaching ?? 0;
                        targetP5m = 1;
                    }
                }
                var startOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
                var lookbackDate = startOfMonth.AddMonths(-6);

                // Scale targets by roster to match League calculations
                int totalDaysInMonth = DateTime.DaysInMonth(startOfMonth.Year, startOfMonth.Month);
                int computedOnsiteDays = totalDaysInMonth;
                bool hasRoster = false;

                if (stats.RosterHistory != null && stats.RosterHistory.Any())
                {
                    int computedOnsite = 0;
                    foreach (var r in stats.RosterHistory)
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
                        computedOnsiteDays = computedOnsite;
                    }
                }

                double ratio = hasRoster ? (double)computedOnsiteDays / totalDaysInMonth : 1.0;

                int ScaleTarget(int baseTarget, double rat, int daysOnsite)
                {
                    if (baseTarget == 0) return 0;
                    if (daysOnsite == 0) return 0;
                    int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                    return Math.Max(scaled, 1);
                }

                if (hasRoster)
                {
                    targetHazardReport = ScaleTarget(targetHazardReport, ratio, computedOnsiteDays);
                    targetInspeksi = ScaleTarget(targetInspeksi, ratio, computedOnsiteDays);
                    targetSafetyTalk = ScaleTarget(targetSafetyTalk, ratio, computedOnsiteDays);
                    targetObservasi = ScaleTarget(targetObservasi, ratio, computedOnsiteDays);
                    targetCoaching = ScaleTarget(targetCoaching, ratio, computedOnsiteDays);
                }

                stats.TargetHazardReport = targetHazardReport;
                stats.TargetInspeksi = targetInspeksi;
                stats.TargetSafetyTalk = targetSafetyTalk;
                stats.TargetObservasi = targetObservasi;
                stats.TargetCoaching = targetCoaching;
                stats.TargetP5m = targetP5m;

                stats.RunningTexts = await _context.RunningTexts
                    .Where(r => r.IsAktif)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.Pesan)
                    .ToListAsync();

                var hazardQuery = _context.HazardReports
                    .Where(h => !h.IsDeleted && h.Nik == userNik && h.CreatedAt >= lookbackDate);
                var inspectionQuery = _context.Inspections
                    .Where(i => !i.IsDeleted && i.Nik == userNik && i.CreatedAt >= lookbackDate);
                var actionPlanQuery = _context.ActionPlans
                    .Where(a => !a.IsDeleted && (a.Nik == userNik || a.NikPja == userNik || a.NikPic == userNik) && a.CreatedAt >= lookbackDate);
                var safetyTalkQuery = _context.SafetyTalks
                    .Where(s => !s.IsDeleted && s.Nik == userNik && s.CreatedAt >= lookbackDate);
                var p5mQuery = _context.P5ms
                    .Where(p => !p.IsDeleted && p.Nik == userNik && p.CreatedAt >= lookbackDate);
                var coachingQuery = _context.Coachings
                    .Where(c => !c.IsDeleted && (c.Nik == userNik || _context.CoachingParticipants.Any(p => p.CoachingId == c.Id && p.Nik == userNik)) && c.CreatedAt >= lookbackDate);
                var observationQuery = _context.Observations
                    .Where(o => !o.IsDeleted && o.Nik == userNik && o.CreatedAt >= lookbackDate);

                // Performa tinggi: Menggunakan CountAsync secara langsung daripada GroupBy(1) yang menghasilkan subquery SQL kompleks dan lambat.
                Console.WriteLine($"[DEBUG-HOME] userNik: {userNik}");
                Console.WriteLine($"[DEBUG-HOME] startOfMonth: {startOfMonth:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"[DEBUG-HOME] endOfMonth: {endOfMonth:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"[DEBUG-HOME] lookbackDate: {lookbackDate:yyyy-MM-dd HH:mm:ss}");
                
                int thisMonthHazardsCount = await hazardQuery.CountAsync(h => h.CreatedAt >= startOfMonth && h.CreatedAt <= endOfMonth);
                Console.WriteLine($"[DEBUG-HOME] thisMonthHazardsCount: {thisMonthHazardsCount}");
                
                int openHazardsCount = await hazardQuery.CountAsync(h => h.StatusTemuan == "Open");
                int closedHazardsCount = await hazardQuery.CountAsync(h => h.StatusTemuan == "Closed");
                int totalHazardsCount = openHazardsCount + closedHazardsCount;

                int thisMonthInspectionsCount = await inspectionQuery.CountAsync(i => i.CreatedAt >= startOfMonth && i.CreatedAt <= endOfMonth);
                int totalInspectionsCount = await inspectionQuery.CountAsync();

                int totalActionPlansCount = await actionPlanQuery.CountAsync();

                int thisMonthSafetyTalksCount = await safetyTalkQuery.CountAsync(s => s.CreatedAt >= startOfMonth && s.CreatedAt <= endOfMonth);
                int totalSafetyTalksCount = await safetyTalkQuery.CountAsync();

                int thisMonthP5msCount = await p5mQuery.CountAsync(p => p.CreatedAt >= startOfMonth && p.CreatedAt <= endOfMonth);
                int totalP5msCount = await p5mQuery.CountAsync();

                int coachingAsCreator = await _context.Coachings
                    .Where(c => !c.IsDeleted && c.Nik == userNik && c.CreatedAt >= startOfMonth && c.CreatedAt <= endOfMonth)
                    .CountAsync();
                int coachingAsParticipant = await _context.CoachingParticipants
                    .Where(p => p.Nik == userNik && p.Coaching != null && !p.Coaching.IsDeleted && p.Coaching.CreatedAt >= startOfMonth && p.Coaching.CreatedAt <= endOfMonth)
                    .CountAsync();
                int thisMonthCoachingsCount = coachingAsCreator + coachingAsParticipant;

                int totalCoachingAsCreator = await _context.Coachings
                    .Where(c => !c.IsDeleted && c.Nik == userNik)
                    .CountAsync();
                int totalCoachingAsParticipant = await _context.CoachingParticipants
                    .Where(p => p.Nik == userNik && p.Coaching != null && !p.Coaching.IsDeleted)
                    .CountAsync();
                int totalCoachingsCount = totalCoachingAsCreator + totalCoachingAsParticipant;

                int thisMonthObservationsCount = await observationQuery.CountAsync(o => o.CreatedAt >= startOfMonth && o.CreatedAt <= endOfMonth);
                int totalObservationsCount = await observationQuery.CountAsync();

                // [FIX] Hapus kredit action plan agar konsisten dengan perhitungan Liga
                // Sebelumnya ThisMonthHazards dan ThisMonthInspections ditambah closedAssignedCredits
                // yang menyebabkan Home menampilkan angka berbeda dari Liga.

                stats.TotalHazards = totalHazardsCount;
                stats.OpenHazards = openHazardsCount;
                stats.ClosedHazards = closedHazardsCount;
                stats.ThisMonthHazards = thisMonthHazardsCount; // tanpa kredit action plan

                stats.TotalInspections = totalInspectionsCount;
                stats.ThisMonthInspections = thisMonthInspectionsCount; // tanpa kredit action plan

                stats.TotalActionPlans = totalActionPlansCount;

                // Query Action Plan Open: individu (saya sebagai pembuat/PJA/PIC)
                var currentKaryawanForAP = await _context.Karyawans
                    .Where(k => k.NoNik != null && k.NoNik.Trim() == userNik && k.StatusAktif)
                    .Select(k => new { k.IdPerusahaan })
                    .FirstOrDefaultAsync();
                int? userCompanyId = currentKaryawanForAP?.IdPerusahaan;

                // Action plan Open milik saya (saya sebagai pembuat atau PJA)
                stats.MyOpenActionPlans = await _context.ActionPlans
                    .Where(a => !a.IsDeleted && a.Status == "Open"
                        && (a.Nik == userNik || a.NikPja == userNik || a.NikPic == userNik))
                    .CountAsync();

                // Action plan Open di departemen saya (berdasarkan field Departemen pada action plan)
                stats.DeptOpenActionPlans = 0;
                if (!string.IsNullOrEmpty(userDept) && userCompanyId.HasValue)
                {
                    stats.DeptOpenActionPlans = await _context.ActionPlans
                        .Where(a => !a.IsDeleted && a.Status == "Open"
                            && a.PerusahaanId == userCompanyId.Value
                            && (a.Departemen == userDept || a.DepartemenPja == userDept || a.DepartemenPic == userDept))
                        .CountAsync();
                }

                stats.TotalSafetyTalks = totalSafetyTalksCount;
                stats.ThisMonthSafetyTalks = thisMonthSafetyTalksCount;

                stats.TotalP5ms = totalP5msCount;
                stats.ThisMonthP5ms = thisMonthP5msCount;

                stats.TotalCoachings = totalCoachingsCount;
                stats.ThisMonthCoachings = thisMonthCoachingsCount;

                stats.TotalObservations = totalObservationsCount;
                stats.ThisMonthObservations = thisMonthObservationsCount;
     
                int cappedActH = Math.Min(stats.ThisMonthHazards, targetHazardReport);
                int cappedActI = Math.Min(stats.ThisMonthInspections, targetInspeksi);
                int cappedActST = Math.Min(stats.ThisMonthSafetyTalks, targetSafetyTalk);
                int cappedActO = Math.Min(stats.ThisMonthObservations, targetObservasi);
                int cappedActC = Math.Min(stats.ThisMonthCoachings, targetCoaching);

                int myTotalMonthTarget = targetHazardReport + targetInspeksi + targetSafetyTalk + targetObservasi + targetCoaching;
                int myTotalThisMonth = cappedActH + cappedActI + cappedActST + cappedActO + cappedActC;

                int complianceScore = 100;
                if (myTotalMonthTarget > 0)
                {
                    complianceScore = (int)Math.Round((double)myTotalThisMonth / myTotalMonthTarget * 100.0, MidpointRounding.AwayFromZero);
                    if (complianceScore > 100) complianceScore = 100;
                }
                
                int compliantWeeks = 0;
                int targetWeeks = 4;
                if (myTotalMonthTarget == 0)
                {
                    compliantWeeks = 4;
                }
                else
                {
                    for (int w = 0; w < 4; w++)
                    {
                        var startOfWeek = DateTime.Today.AddDays(-7 * (w + 1) + 1);
                        var endOfWeek = DateTime.Today.AddDays(-7 * w).AddDays(1).AddTicks(-1);

                        bool submittedInWeek = await hazardQuery.AnyAsync(h => h.CreatedAt >= startOfWeek && h.CreatedAt <= endOfWeek)
                            || await inspectionQuery.AnyAsync(i => i.CreatedAt >= startOfWeek && i.CreatedAt <= endOfWeek)
                            || await safetyTalkQuery.AnyAsync(s => s.CreatedAt >= startOfWeek && s.CreatedAt <= endOfWeek)
                            || await p5mQuery.AnyAsync(p => p.CreatedAt >= startOfWeek && p.CreatedAt <= endOfWeek)
                            || await coachingQuery.AnyAsync(c => c.CreatedAt >= startOfWeek && c.CreatedAt <= endOfWeek)
                            || await observationQuery.AnyAsync(o => o.CreatedAt >= startOfWeek && o.CreatedAt <= endOfWeek);

                        if (submittedInWeek)
                        {
                            compliantWeeks++;
                        }
                    }
                }
                
                stats.ComplianceScore = complianceScore;
                stats.CompliantWeeks = compliantWeeks;
                stats.TargetWeeks = targetWeeks;
                stats.MyTotalThisMonth = myTotalThisMonth;
                stats.MyTotalMonthTarget = myTotalMonthTarget;

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
                stats.RecentActivities = recentHazards
                    .Concat(recentInspections)
                    .Concat(recentActionPlans)
                    .Concat(recentSafetyTalks)
                    .Concat(recentP5ms)
                    .Concat(recentCoachings)
                    .OrderByDescending(a => a.Date)
                    .Take(6)
                    .ToList();

                _cache.Set(cacheKey, stats, TimeSpan.FromMinutes(3));
            }

            ViewData["ShowRosterPopup"] = stats.ShowRosterPopup;
            ViewData["RosterHistory"] = stats.RosterHistory;
            ViewData["MitraHariOnsite"] = stats.MitraHariOnsite;
            ViewData["MitraHariOffsite"] = stats.MitraHariOffsite;
            ViewData["RosterAwalDinas"] = stats.RosterAwalDinas;
            ViewData["RosterAkhirDinas"] = stats.RosterAkhirDinas;
            ViewData["RosterAwalCuti"] = stats.RosterAwalCuti;
            ViewData["RosterAkhirCuti"] = stats.RosterAkhirCuti;
            ViewData["IsEditMode"] = stats.IsEditMode;

            ViewData["KategoriPengawas"] = stats.KategoriPengawas;
            ViewData["RunningTexts"] = stats.RunningTexts;

            ViewData["TotalHazards"] = stats.TotalHazards;
            ViewData["OpenHazards"] = stats.OpenHazards;
            ViewData["ClosedHazards"] = stats.ClosedHazards;
            ViewData["TotalInspections"] = stats.TotalInspections;
            ViewData["TotalActionPlans"] = stats.TotalActionPlans;
            ViewData["TotalSafetyTalks"] = stats.TotalSafetyTalks;
            ViewData["TotalP5ms"] = stats.TotalP5ms;
            ViewData["TotalObservations"] = stats.TotalObservations;
            ViewData["TotalCoachings"] = stats.TotalCoachings;
            
            ViewData["ComplianceScore"] = stats.ComplianceScore;
            ViewData["CompliantWeeks"] = stats.CompliantWeeks;
            ViewData["TargetWeeks"] = stats.TargetWeeks;
            ViewData["AlasanTargetZero"] = stats.AlasanTargetZero;
            ViewData["MyTotalThisMonth"] = stats.MyTotalThisMonth;
            ViewData["MyTotalMonthTarget"] = stats.MyTotalMonthTarget;
            
            ViewData["ThisMonthHazards"] = stats.ThisMonthHazards;
            ViewData["ThisMonthInspections"] = stats.ThisMonthInspections;
            ViewData["ThisMonthSafetyTalks"] = stats.ThisMonthSafetyTalks;
            ViewData["ThisMonthP5ms"] = stats.ThisMonthP5ms;
            ViewData["ThisMonthObservations"] = stats.ThisMonthObservations;
            ViewData["ThisMonthCoachings"] = stats.ThisMonthCoachings;

            ViewData["TargetHazard"] = stats.TargetHazardReport;
            ViewData["TargetInspeksi"] = stats.TargetInspeksi;
            ViewData["TargetSafetyTalk"] = stats.TargetSafetyTalk;
            ViewData["TargetP5m"] = stats.TargetP5m;
            ViewData["TargetObservasi"] = stats.TargetObservasi;
            ViewData["TargetCoaching"] = stats.TargetCoaching;

            ViewData["MyOpenActionPlans"] = stats.MyOpenActionPlans;
            ViewData["DeptOpenActionPlans"] = stats.DeptOpenActionPlans;
            ViewData["UserDept"] = userDept;

            return View(stats.RecentActivities);
        }

        public class DashboardStatsCache
        {
            public bool ShowRosterPopup { get; set; }
            public List<Roster> RosterHistory { get; set; } = new();
            public int? MitraHariOnsite { get; set; }
            public int? MitraHariOffsite { get; set; }
            public string RosterAwalDinas { get; set; } = string.Empty;
            public string RosterAkhirDinas { get; set; } = string.Empty;
            public string RosterAwalCuti { get; set; } = string.Empty;
            public string RosterAkhirCuti { get; set; } = string.Empty;
            public bool IsEditMode { get; set; }
            
            public string? KategoriPengawas { get; set; }
            public int TargetHazardReport { get; set; }
            public int TargetInspeksi { get; set; }
            public int TargetSafetyTalk { get; set; }
            public int TargetObservasi { get; set; }
            public int TargetCoaching { get; set; }
            public int TargetP5m { get; set; }
            
            public List<string> RunningTexts { get; set; } = new();
            
            public int TotalHazards { get; set; }
            public int OpenHazards { get; set; }
            public int ClosedHazards { get; set; }
            public int TotalInspections { get; set; }
            public int TotalActionPlans { get; set; }
            public int TotalSafetyTalks { get; set; }
            public int TotalP5ms { get; set; }
            public int TotalObservations { get; set; }
            public int TotalCoachings { get; set; }
            
            public int ComplianceScore { get; set; }
            public int CompliantWeeks { get; set; }
            public int TargetWeeks { get; set; }
            public string? AlasanTargetZero { get; set; }
            public int MyTotalThisMonth { get; set; }
            public int MyTotalMonthTarget { get; set; }
            public int MyOpenActionPlans { get; set; }
            public int DeptOpenActionPlans { get; set; }
            
            public int ThisMonthHazards { get; set; }
            public int ThisMonthInspections { get; set; }
            public int ThisMonthSafetyTalks { get; set; }
            public int ThisMonthP5ms { get; set; }
            public int ThisMonthObservations { get; set; }
            public int ThisMonthCoachings { get; set; }
            
            public List<RecentActivityViewModel> RecentActivities { get; set; } = new();
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
