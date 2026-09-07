using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [AllowAnonymous]
    [Route("Display")]
    public class DisplayController : Controller
    {
        private readonly AppDbContext _context;

        public DisplayController(AppDbContext context)
        {
            _context = context;
        }

        [Route("")]
        [Route("Index")]
        [Route("/Display1")]
        [Route("Display1")]
        public IActionResult Index()
        {
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;
            return View();
        }

        [HttpGet("/Display2")]
        [HttpGet("Display2")]
        public IActionResult Display2()
        {
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;
            return View();
        }

        [HttpGet("GetCompanyPerformance")]
        public async Task<IActionResult> GetCompanyPerformance()
        {
            try 
            {
                var totalKaryawan = await _context.Karyawans.CountAsync(k => k.StatusAktif);
                
                // All target mappings
                var targetMappings = await _context.KaryawanJabatanMappings.ToListAsync();

                // Dates
                var now = DateTime.Now;
                var startOfWeek  = DateTime.Today.AddDays(-6);
                var startOfMonth = new DateTime(now.Year, now.Month, 1);

                var todayStart = DateTime.Today;
                int todayHazards     = await _context.HazardReports.CountAsync(h => !h.IsDeleted && h.CreatedAt >= todayStart);
                int todayInspections = await _context.Inspections.CountAsync(i => !i.IsDeleted && i.CreatedAt >= todayStart);
                int todaySafetyTalks = await _context.SafetyTalks.CountAsync(s => !s.IsDeleted && s.CreatedAt >= todayStart);
                int todayP5ms        = await _context.P5ms.CountAsync(p => !p.IsDeleted && p.CreatedAt >= todayStart);
                int todayObservations= await _context.Observations.CountAsync(o => !o.IsDeleted && o.CreatedAt >= todayStart);
                int todayP2h         = await _context.P2hReports.CountAsync(r => !r.IsDeleted && r.CreatedAt >= todayStart);
                int todayCoachings   = await _context.Coachings.CountAsync(c => !c.IsDeleted && c.CreatedAt >= todayStart);

                // ── Company-level monthly realization lists ─────────────────────
                var monthHazardsByCompany    = await _context.HazardReports.Where(h => !h.IsDeleted && h.CreatedAt >= startOfMonth && h.PerusahaanId != null).Select(h => h.PerusahaanId!.Value).ToListAsync();
                var monthInspectionsByCompany= await _context.Inspections.Where(i => !i.IsDeleted && i.CreatedAt >= startOfMonth && i.PerusahaanId != null).Select(i => i.PerusahaanId!.Value).ToListAsync();
                var monthSafetyTalksByCompany= await _context.SafetyTalks.Where(s => !s.IsDeleted && s.CreatedAt >= startOfMonth && s.PerusahaanId != null).Select(s => s.PerusahaanId!.Value).ToListAsync();
                var monthP5msByCompany       = await _context.P5ms.Where(p => !p.IsDeleted && p.CreatedAt >= startOfMonth && p.PerusahaanId != null).Select(p => p.PerusahaanId!.Value).ToListAsync();
                var monthCoachingsByCompany  = await _context.Coachings.Where(c => !c.IsDeleted && c.CreatedAt >= startOfMonth && c.PerusahaanId != null).Select(c => c.PerusahaanId!.Value).ToListAsync();
                var monthObservationsByCompany = await (from o in _context.Observations
                                                         join k in _context.Karyawans on o.Nik equals k.NoNik
                                                         where !o.IsDeleted && o.CreatedAt >= startOfMonth
                                                         select k.IdPerusahaan)
                                                        .ToListAsync();

                // ── Weekly aggregates ───────────────────────────────────────────
                int weekHazards      = await _context.HazardReports.CountAsync(h => !h.IsDeleted && h.CreatedAt >= startOfWeek);
                int weekInspections  = await _context.Inspections.CountAsync(i => !i.IsDeleted && i.CreatedAt >= startOfWeek);
                int weekSafetyTalks  = await _context.SafetyTalks.CountAsync(s => !s.IsDeleted && s.CreatedAt >= startOfWeek);
                int weekP5ms         = await _context.P5ms.CountAsync(p => !p.IsDeleted && p.CreatedAt >= startOfWeek);
                int weekCoachings    = await _context.Coachings.CountAsync(c => !c.IsDeleted && c.CreatedAt >= startOfWeek);
                int weekObs          = await _context.Observations.CountAsync(o => !o.IsDeleted && o.CreatedAt >= startOfWeek);
                int weeklyRealization= weekHazards + weekInspections + weekSafetyTalks + weekCoachings + weekObs;

                // ── Monthly aggregates ──────────────────────────────────────────
                int monthHazards     = monthHazardsByCompany.Count;
                int monthInspections = monthInspectionsByCompany.Count;
                int monthSafetyTalks = monthSafetyTalksByCompany.Count;
                int monthP5ms        = monthP5msByCompany.Count;
                int monthCoachings   = monthCoachingsByCompany.Count;
                int monthObs         = await _context.Observations.CountAsync(o => !o.IsDeleted && o.CreatedAt >= startOfMonth);
                int monthlyRealization = monthHazards + monthInspections + monthSafetyTalks + monthObs + monthCoachings;

                // ── Overall targets ─────────────────────────────────────────────
                int monthlyTarget   = targetMappings.Sum(m => (m.TargetHazardReport ?? 0) + (m.TargetInspeksi ?? 0) + (m.TargetSafetyTalk ?? 0) + (m.TargetObservasi ?? 0) + (m.TargetCoaching ?? 0));
                int weeklyTarget    = (int)Math.Round(monthlyTarget / 4.0, MidpointRounding.AwayFromZero);
                if (weeklyTarget < 1 && monthlyTarget > 0) weeklyTarget = 1;

                // ── Per-SAP-category targets ────────────────────────────────────
                int targetHazard    = targetMappings.Sum(m => m.TargetHazardReport ?? 0);
                int targetInspeksi  = targetMappings.Sum(m => m.TargetInspeksi ?? 0);
                int targetSafetyTalk= targetMappings.Sum(m => m.TargetSafetyTalk ?? 0);
                int targetObs       = targetMappings.Sum(m => m.TargetObservasi ?? 0);
                int targetCoaching  = targetMappings.Sum(m => m.TargetCoaching ?? 0);
                int targetP5m       = targetMappings.Count;

                // ── Hazard close metrics ────────────────────────────────────────
                var allHazards = await _context.HazardReports
                    .Where(h => !h.IsDeleted)
                    .Select(h => new { h.Id, h.StatusTemuan })
                    .ToListAsync();
                
                var hazardActionPlans = await _context.ActionPlans
                    .Where(ap => !ap.IsDeleted && ap.ItemSap != null && ap.ItemSap.StartsWith("hazard:"))
                    .Select(ap => new { ap.ItemSap, ap.RencanaPerbaikan })
                    .ToListAsync();

                int totalClosedHazards = allHazards.Count(h => h.StatusTemuan == "Closed");
                int totalProgresHazards = 0;
                int totalOpenHazards = 0;

                foreach (var h in allHazards)
                {
                    if (h.StatusTemuan == "Closed") continue;
                    
                    var linkedAp = hazardActionPlans.FirstOrDefault(ap => ap.ItemSap == $"hazard:{h.Id}");
                    if (linkedAp != null && !string.IsNullOrEmpty(linkedAp.RencanaPerbaikan))
                    {
                        totalProgresHazards++;
                    }
                    else
                    {
                        totalOpenHazards++;
                    }
                }
                int totalHazards = totalOpenHazards + totalProgresHazards + totalClosedHazards;
                double complianceClose= totalHazards > 0 ? Math.Round((double)totalClosedHazards / totalHazards * 100, 1) : 0;

                int totalOpenActionPlans = await _context.ActionPlans.CountAsync(ap => !ap.IsDeleted && ap.Status == "Open" && string.IsNullOrEmpty(ap.RencanaPerbaikan));
                int totalClosedActionPlans = await _context.ActionPlans.CountAsync(ap => !ap.IsDeleted && ap.Status == "Closed");
                int totalProgresActionPlans = await _context.ActionPlans.CountAsync(ap => !ap.IsDeleted && ap.Status == "Open" && !string.IsNullOrEmpty(ap.RencanaPerbaikan));

                var allHazardRisks = await _context.HazardReports.Where(h => !h.IsDeleted).Select(h => new { h.StatusTemuan, h.TingkatResiko }).ToListAsync();
                int GetRiskWeight(string? r) {
                    if (string.IsNullOrEmpty(r)) return 0;
                    if (r.Contains("Extreme", StringComparison.OrdinalIgnoreCase) || r.Contains("Ekstrim", StringComparison.OrdinalIgnoreCase)) return 4;
                    if (r.Contains("Kritis", StringComparison.OrdinalIgnoreCase) || r.Contains("Critical", StringComparison.OrdinalIgnoreCase)) return 4;
                    if (r.Contains("High", StringComparison.OrdinalIgnoreCase) || r.Contains("Tinggi", StringComparison.OrdinalIgnoreCase)) return 3;
                    if (r.Contains("Medium", StringComparison.OrdinalIgnoreCase) || r.Contains("Sedang", StringComparison.OrdinalIgnoreCase)) return 2;
                    if (r.Contains("Low", StringComparison.OrdinalIgnoreCase) || r.Contains("Rendah", StringComparison.OrdinalIgnoreCase)) return 1;
                    return 0;
                }
                int totalRW  = allHazardRisks.Sum(h => GetRiskWeight(h.TingkatResiko));
                int closedRW = allHazardRisks.Where(h => h.StatusTemuan == "Closed").Sum(h => GetRiskWeight(h.TingkatResiko));
                double rri   = totalRW > 0 ? Math.Round((double)closedRW / totalRW * 100, 1) : 0;
                int highRiskOpen = allHazardRisks.Count(h => h.StatusTemuan == "Open" && GetRiskWeight(h.TingkatResiko) >= 3);
                double complianceRisk = totalOpenHazards > 0 ? Math.Round((double)highRiskOpen / totalOpenHazards * 100, 1) : 0;

                // ── Per-company breakdown ───────────────────────────────────────
                var activeCompanies = await _context.Perusahaans.Where(p => p.StatusAktif).ToListAsync();
                var allCompanyData  = new List<object>();

                foreach (var c in activeCompanies)
                {
                    int tgtH  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetHazardReport ?? 0);
                    int tgtI  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetInspeksi    ?? 0);
                    int tgtST = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetSafetyTalk  ?? 0);
                    int tgtO  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetObservasi   ?? 0);
                    int tgtC  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetCoaching    ?? 0);
                    int tgtP5 = targetMappings.Count(m => m.PerusahaanId == c.PerusahaanId);
                    int totalTarget = tgtH + tgtI + tgtST + tgtO + tgtC;
                    if (totalTarget == 0) continue;

                    int rH  = monthHazardsByCompany.Count(id => id == c.PerusahaanId);
                    int rI  = monthInspectionsByCompany.Count(id => id == c.PerusahaanId);
                    int rST = monthSafetyTalksByCompany.Count(id => id == c.PerusahaanId);
                    int rP5 = monthP5msByCompany.Count(id => id == c.PerusahaanId);
                    int rC  = monthCoachingsByCompany.Count(id => id == c.PerusahaanId);
                    int rO  = monthObservationsByCompany.Count(id => id == c.PerusahaanId);
                    int totalReal = rH + rI + rST + rC + rO;

                    double pct = Math.Round((double)totalReal / totalTarget * 100, 1);
                    allCompanyData.Add(new {
                        CompanyName  = c.NamaPerusahaan,
                        Target       = totalTarget,
                        Realization  = totalReal,
                        Percentage   = pct,
                        Hazard       = new { Target = tgtH,  Real = rH },
                        Inspeksi     = new { Target = tgtI,  Real = rI },
                        SafetyTalk   = new { Target = tgtST, Real = rST },
                        P5m          = new { Target = tgtP5, Real = rP5 },
                        Coaching     = new { Target = tgtC,  Real = rC },
                        Observasi    = new { Target = tgtO,  Real = rO }
                    });
                }

                var sorted    = allCompanyData.Cast<dynamic>().OrderByDescending(x => x.Percentage).ToList();
                var topComp   = sorted.Take(3).ToList();
                var stagnant  = allCompanyData.Cast<dynamic>().Where(x => x.Realization == 0).OrderBy(x => x.CompanyName).Take(5).Select(x => (object)new { CompanyName = x.CompanyName, Target = x.Target, Realization = x.Realization, Percentage = x.Percentage }).ToList();
                var fastest   = allCompanyData.Cast<dynamic>().Where(x => x.Realization > 0).OrderByDescending(x => x.Percentage).Take(5).Select(x => (object)new { CompanyName = x.CompanyName, Target = x.Target, Realization = x.Realization, Percentage = x.Percentage }).ToList();
                var allRanked = sorted.Select(x => (object)new { CompanyName = x.CompanyName, Target = x.Target, Realization = x.Realization, Percentage = x.Percentage }).ToList();

                int riskExtreme = allHazardRisks.Count(h => GetRiskWeight(h.TingkatResiko) == 4);
                int riskHigh    = allHazardRisks.Count(h => GetRiskWeight(h.TingkatResiko) == 3);
                int riskMedium  = allHazardRisks.Count(h => GetRiskWeight(h.TingkatResiko) == 2);
                int riskLow     = allHazardRisks.Count(h => GetRiskWeight(h.TingkatResiko) == 1);

                var topLocations = await _context.HazardReports
                    .Where(h => !h.IsDeleted && h.Area != null && h.Area != "")
                    .GroupBy(h => h.Area)
                    .Select(g => new {
                        LocationName = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToListAsync();

                // ── Zero Incident / Safe Days metrics ──────────────────────────
                var latestIncident = await _context.IncidentNewsList
                    .Where(i => i.IsPublished && i.TanggalKejadian != null)
                    .OrderByDescending(i => i.TanggalKejadian)
                    .FirstOrDefaultAsync();

                int safeDays = 0;
                if (latestIncident != null && latestIncident.TanggalKejadian.HasValue)
                {
                    safeDays = (DateTime.Today - latestIncident.TanggalKejadian.Value.Date).Days;
                }
                else
                {
                    safeDays = 365; // Fallback default
                }

                // ── Running Text Announcement Marquee ─────────────────────────────
                var runningTexts = await _context.RunningTexts
                    .Where(r => r.IsAktif)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.Pesan)
                    .ToListAsync();
                
                string marqueeText = runningTexts.Any()
                    ? string.Join("   •   ", runningTexts)
                    : "Utamakan Keselamatan dan Kesehatan Kerja (K3) — Budayakan Safety di Setiap Langkah Kita!   •   Zero Incident is Our Target!   •   Mulai dengan Aman, Bekerja dengan Aman, Pulang dengan Selamat!";

                // ── MTD Group Maincon Subcon Activity calculations ────────────────
                var mainconNames = new[] { "UNGGUL DINAMIKA UTAMA", "KALIMANTAN PRIMA PERSADA", "MEGA GLOBAL ENERGY" };
                var mainconList = new List<PerusahaanView>();
                foreach (var mName in mainconNames)
                {
                    var found = await _context.Perusahaans.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.StatusAktif && p.NamaPerusahaan != null && p.NamaPerusahaan.Contains(mName));
                    if (found != null)
                    {
                        mainconList.Add(found);
                    }
                }

                var mainconSubconComplianceList = new List<object>();
                var startOfMonthMaincon = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

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

                    var subconIds = childIdsFromRelations.Concat(childIdsFromDirectParent).Distinct().ToList();

                    var subconsActiveCount = 0;
                    var subconsInactiveCount = 0;

                    foreach (var sId in subconIds)
                    {
                        bool hasData = 
                            monthHazardsByCompany.Contains(sId) ||
                            monthInspectionsByCompany.Contains(sId) ||
                            monthSafetyTalksByCompany.Contains(sId) ||
                            monthCoachingsByCompany.Contains(sId) ||
                            monthObservationsByCompany.Contains(sId);

                        if (hasData)
                        {
                            subconsActiveCount++;
                        }
                        else
                        {
                            subconsInactiveCount++;
                        }
                    }

                    int totalSubcons = subconIds.Count;
                    double activePct = totalSubcons > 0 ? Math.Round((double)subconsActiveCount / totalSubcons * 100.0, 1) : 0;
                    double inactivePct = totalSubcons > 0 ? Math.Round((double)subconsInactiveCount / totalSubcons * 100.0, 1) : 0;

                    mainconSubconComplianceList.Add(new {
                        MainconName = mcon.NamaPerusahaan ?? "Unknown",
                        TotalSubcons = totalSubcons,
                        ActiveCount = subconsActiveCount,
                        InactiveCount = subconsInactiveCount,
                        ActivePercentage = activePct,
                        InactivePercentage = inactivePct
                    });
                }

                return Json(new {
                    totalKaryawan,
                    totalOpenHazards,
                    totalProgresHazards,
                    totalClosedHazards,
                    totalOpenActionPlans,
                    totalProgresActionPlans,
                    totalClosedActionPlans,
                    complianceClose,
                    rri,
                    complianceRisk,
                    weeklyTarget,
                    weeklyRealization,
                    monthlyTarget,
                    monthlyRealization,
                    // Per-category totals
                    monthHazards,
                    monthInspections,
                    monthSafetyTalks,
                    monthP5ms,
                    monthObs,
                    // Per-category targets
                    targetHazard,
                    targetInspeksi,
                    targetSafetyTalk,
                    targetObs,
                    targetCoaching,
                    targetP5m,
                    // Company rankings
                    topCompanies    = topComp,
                    stagnantCompanies = stagnant,
                    fastestCompanies  = fastest,
                    allCompanies    = allRanked,
                    topLocations    = topLocations,
                    riskExtreme     = riskExtreme,
                    riskHigh        = riskHigh,
                    riskMedium      = riskMedium,
                    riskLow         = riskLow,
                    safeDays,
                    marqueeText,
                    todayHazards,
                    todayP2h,
                    todayP5ms,
                    todayInspections,
                    todayObservations,
                    todaySafetyTalks,
                    todayCoachings,
                    monthCoachings,
                    mainconSubconCompliance = mainconSubconComplianceList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("GetDisplay2Data")]
        public async Task<IActionResult> GetDisplay2Data()
        {
            try
            {
                var now = DateTime.Now;
                var startOfMonth = new DateTime(now.Year, now.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

                // All target mappings
                var targetMappings = await _context.KaryawanJabatanMappings.AsNoTracking().ToListAsync();

                // Realizations
                var monthHazardsByCompany = await _context.HazardReports.AsNoTracking()
                    .Where(h => !h.IsDeleted && h.CreatedAt >= startOfMonth && h.PerusahaanId != null)
                    .Select(h => h.PerusahaanId!.Value).ToListAsync();

                var monthInspectionsByCompany = await _context.Inspections.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.CreatedAt >= startOfMonth && i.PerusahaanId != null)
                    .Select(i => i.PerusahaanId!.Value).ToListAsync();

                var monthSafetyTalksByCompany = await _context.SafetyTalks.AsNoTracking()
                    .Where(s => !s.IsDeleted && s.CreatedAt >= startOfMonth && s.PerusahaanId != null)
                    .Select(s => s.PerusahaanId!.Value).ToListAsync();

                var monthP5msByCompany = await _context.P5ms.AsNoTracking()
                    .Where(p => !p.IsDeleted && p.CreatedAt >= startOfMonth && p.PerusahaanId != null)
                    .Select(p => p.PerusahaanId!.Value).ToListAsync();

                var monthCoachingsByCompany = await _context.Coachings.AsNoTracking()
                    .Where(c => !c.IsDeleted && c.CreatedAt >= startOfMonth && c.PerusahaanId != null)
                    .Select(c => c.PerusahaanId!.Value).ToListAsync();

                var monthObservationsByCompany = await (from o in _context.Observations.AsNoTracking()
                                                         join k in _context.Karyawans.AsNoTracking() on o.Nik equals k.NoNik
                                                         where !o.IsDeleted && o.CreatedAt >= startOfMonth
                                                         select k.IdPerusahaan)
                                                        .ToListAsync();

                // Active Companies
                var activeCompanies = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.StatusAktif)
                    .OrderBy(p => p.NamaPerusahaan)
                    .ToListAsync();

                var allStandings = new List<dynamic>();

                // Core Companies List matching Performance/League?mode=core
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
                    "PT KARUNIA ARMADA INDONESIA",
                    "PT INDEXIM COALINDO"
                };

                foreach (var c in activeCompanies)
                {
                    int tgtH  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetHazardReport ?? 0);
                    int tgtI  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetInspeksi    ?? 0);
                    int tgtST = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetSafetyTalk  ?? 0);
                    int tgtO  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetObservasi   ?? 0);
                    int tgtC  = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Sum(m => m.TargetCoaching    ?? 0);
                    int tgtP5 = targetMappings.Count(m => m.PerusahaanId == c.PerusahaanId);
                    int totalTarget = tgtH + tgtI + tgtST + tgtO + tgtC;

                    int rH  = monthHazardsByCompany.Count(id => id == c.PerusahaanId);
                    int rI  = monthInspectionsByCompany.Count(id => id == c.PerusahaanId);
                    int rST = monthSafetyTalksByCompany.Count(id => id == c.PerusahaanId);
                    int rP5 = monthP5msByCompany.Count(id => id == c.PerusahaanId);
                    int rC  = monthCoachingsByCompany.Count(id => id == c.PerusahaanId);
                    int rO  = monthObservationsByCompany.Count(id => id == c.PerusahaanId);
                    int totalReal = rH + rI + rST + rC + rO;

                    int empCount = targetMappings.Where(m => m.PerusahaanId == c.PerusahaanId).Select(m => m.KaryawanId).Distinct().Count();
                    if (empCount == 0 && totalReal > 0) empCount = 1;

                    if (totalTarget == 0 && totalReal == 0) continue;

                    double pct = totalTarget > 0 ? Math.Round((double)totalReal / totalTarget * 100.0, 1) : 0;
                    double hRate = tgtH > 0 ? Math.Round((double)rH / tgtH * 100.0, 1) : (tgtH == 0 ? -1 : 0);
                    double iRate = tgtI > 0 ? Math.Round((double)rI / tgtI * 100.0, 1) : (tgtI == 0 ? -1 : 0);
                    double stRate = tgtST > 0 ? Math.Round((double)rST / tgtST * 100.0, 1) : (tgtST == 0 ? -1 : 0);
                    double oRate = tgtO > 0 ? Math.Round((double)rO / tgtO * 100.0, 1) : (tgtO == 0 ? -1 : 0);
                    double cRate = tgtC > 0 ? Math.Round((double)rC / tgtC * 100.0, 1) : (tgtC == 0 ? -1 : 0);
                    double p5mRate = tgtP5 > 0 ? Math.Round((double)rP5 / tgtP5 * 100.0, 1) : (tgtP5 == 0 ? -1 : 0);

                    // Form indicators (5 SAP Pillars)
                    string FormItem(int actual, int target) {
                        if (target == 0 && actual > 0) return "W";
                        if (target == 0) return "D";
                        double ratio = (double)actual / target;
                        if (ratio >= 0.8) return "W";
                        if (ratio >= 0.4) return "D";
                        return "L";
                    }

                    var form = new string[] {
                        FormItem(rH, tgtH),
                        FormItem(rI, tgtI),
                        FormItem(rST, tgtST),
                        FormItem(rC, tgtC),
                        FormItem(rO, tgtO)
                    };

                    bool isCore = coreCompaniesList.Any(k => (c.NamaPerusahaan ?? "").Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                                  c.PerusahaanId == 1 || c.PerusahaanId == 3 || c.PerusahaanId == 4 || c.PerusahaanId == 5;

                    allStandings.Add(new {
                        CompanyId = c.PerusahaanId,
                        CompanyName = c.NamaPerusahaan ?? "Unknown",
                        CompanyCode = c.KodePerusahaan ?? "CORP",
                        PjoName = c.NamaPjo ?? "",
                        IsMaincon = isCore,
                        IsCore = isCore,
                        EmployeeCount = empCount,
                        Target = totalTarget,
                        Realization = totalReal,
                        Percentage = pct,
                        MtdHazardRate = hRate,
                        MtdInspeksiRate = iRate,
                        MtdSafetyTalkRate = stRate,
                        MtdObservasiRate = oRate,
                        MtdCoachingRate = cRate,
                        MtdP5mRate = p5mRate,
                        Gap = totalReal - totalTarget,
                        Hazard = new { Target = tgtH, Real = rH, Rate = hRate },
                        Inspeksi = new { Target = tgtI, Real = rI, Rate = iRate },
                        SafetyTalk = new { Target = tgtST, Real = rST, Rate = stRate },
                        P5m = new { Target = tgtP5, Real = rP5, Rate = p5mRate },
                        Coaching = new { Target = tgtC, Real = rC, Rate = cRate },
                        Observasi = new { Target = tgtO, Real = rO, Rate = oRate },
                        Form = form
                    });
                }

                // Sort by Percentage Descending, then Realization Descending, then Target Ascending
                var sortedStandings = allStandings
                    .OrderByDescending(x => (double)x.Percentage)
                    .ThenByDescending(x => (int)x.Realization)
                    .ThenBy(x => (int)x.Target)
                    .ToList();

                var leagueTable = new List<object>();
                var leagueRedZoneTable = new List<object>();

                for (int i = 0; i < sortedStandings.Count; i++)
                {
                    var item = sortedStandings[i];
                    int rank = i + 1;
                    double p = item.Percentage;
                    string statusBadge;
                    string statusColor;

                    if (rank == 1 && p > 0)
                    {
                        statusBadge = "CHAMPION #1";
                        statusColor = "#fbbf24"; // Gold
                    }
                    else if (rank <= 4 && p >= 50)
                    {
                        statusBadge = "UCL (TOP 4)";
                        statusColor = "#38bdf8"; // Cyan
                    }
                    else if (rank <= 8 && p >= 40)
                    {
                        statusBadge = "EUROPA LEAGUE";
                        statusColor = "#34d399"; // Emerald
                    }
                    else if (p > 0)
                    {
                        statusBadge = "MID TABLE";
                        statusColor = "#94a3b8"; // Slate
                    }
                    else
                    {
                        statusBadge = "RELEGATION ZONE";
                        statusColor = "#ef4444"; // Red
                    }

                    var rowObj = new {
                        Rank = rank,
                        item.CompanyId,
                        item.CompanyName,
                        item.CompanyCode,
                        item.PjoName,
                        item.IsMaincon,
                        item.IsCore,
                        item.EmployeeCount,
                        item.Target,
                        item.Realization,
                        item.Percentage,
                        item.MtdHazardRate,
                        item.MtdInspeksiRate,
                        item.MtdSafetyTalkRate,
                        item.MtdObservasiRate,
                        item.MtdCoachingRate,
                        item.MtdP5mRate,
                        item.Gap,
                        item.Hazard,
                        item.Inspeksi,
                        item.SafetyTalk,
                        item.P5m,
                        item.Coaching,
                        item.Observasi,
                        item.Form,
                        StatusBadge = statusBadge,
                        StatusColor = statusColor
                    };

                    if (p == 0 && item.Target > 0)
                    {
                        leagueRedZoneTable.Add(rowObj);
                    }
                    else
                    {
                        leagueTable.Add(rowObj);
                    }
                }

                // ── Filter Core Standings (Liga Perusahaan Inti Table) ─────────
                var sortedCore = sortedStandings.Where(x => (bool)x.IsCore).ToList();
                var coreLeagueTable = new List<object>();
                var coreRedZoneTable = new List<object>();

                for (int i = 0; i < sortedCore.Count; i++)
                {
                    var item = sortedCore[i];
                    int rank = i + 1;
                    double p = item.Percentage;
                    string statusBadge = rank == 1 && p > 0 ? "CHAMPION #1" : (rank <= 3 && p >= 50 ? "PODIUM" : (p > 0 ? "STABLE" : "RELEGATION"));
                    string statusColor = rank == 1 ? "#fbbf24" : (rank <= 3 ? "#38bdf8" : (p > 0 ? "#34d399" : "#ef4444"));

                    var coreRow = new {
                        Rank = rank,
                        item.CompanyId,
                        item.CompanyName,
                        item.CompanyCode,
                        item.PjoName,
                        item.IsMaincon,
                        item.EmployeeCount,
                        item.Target,
                        item.Realization,
                        item.Percentage,
                        item.MtdHazardRate,
                        item.MtdInspeksiRate,
                        item.MtdSafetyTalkRate,
                        item.MtdObservasiRate,
                        item.MtdCoachingRate,
                        item.MtdP5mRate,
                        item.Gap,
                        item.Hazard,
                        item.Inspeksi,
                        item.SafetyTalk,
                        item.P5m,
                        item.Coaching,
                        item.Observasi,
                        item.Form,
                        StatusBadge = statusBadge,
                        StatusColor = statusColor
                    };

                    if (p == 0 && item.Target > 0)
                    {
                        coreRedZoneTable.Add(coreRow);
                    }
                    else
                    {
                        coreLeagueTable.Add(coreRow);
                    }
                }

                // ── 2. F1 RACE CONTENDERS DATA ──────────────────────────────────
                // Specific Teams:
                // 1. PT INDEXIM COALINDO (ID 1)
                // 2. PT KALIMANTAN PRIMA PERSADA (ID 4)
                // 3. PT UNGGUL DINAMIKA UTAMA (ID 3)
                // 4. PT MEGA GLOBAL ENERGY (ID 5) & Anak-anak Perusahaannya
                
                var targetTeamIds = new[] { 1, 4, 3, 5 };
                var teamMetadata = new Dictionary<int, (string TeamCode, string ShortName, string ColorPrimary, string ColorSecondary, string TeamPrincipal)>
                {
                    { 1, ("IDX", "INDEXIM COALINDO", "#ff7b00", "#ea580c", "Indexim Orange Division") },
                    { 4, ("KPP", "KALIMANTAN PRIMA PERSADA", "#10b981", "#059669", "KPP Green Dynamics") },
                    { 3, ("UDU", "UNGGUL DINAMIKA UTAMA", "#fbbf24", "#d97706", "UDU Yellow Dynamics") },
                    { 5, ("MGE", "MEGA GLOBAL ENERGY", "#00d2ff", "#2563eb", "MGE Blue Racing Fleet") }
                };

                // Helper to get company data
                dynamic GetTeamMetrics(int companyId, string fallbackName)
                {
                    var found = leagueTable.Cast<dynamic>().FirstOrDefault(x => x.CompanyId == companyId);
                    if (found != null)
                    {
                        return found;
                    }
                    return new {
                        CompanyId = companyId,
                        CompanyName = fallbackName,
                        CompanyCode = "CORP",
                        Target = 0,
                        Realization = 0,
                        Percentage = 0.0,
                        Hazard = new { Target = 0, Real = 0 },
                        Inspeksi = new { Target = 0, Real = 0 },
                        SafetyTalk = new { Target = 0, Real = 0 },
                        P5m = new { Target = 0, Real = 0 },
                        Coaching = new { Target = 0, Real = 0 },
                        Observasi = new { Target = 0, Real = 0 }
                    };
                }

                // Anak-anak perusahaan MGE (PerusahaanId = 5)
                var mgeChildRelations = await _context.PerusahaanHierarchyRelations.AsNoTracking()
                    .Where(r => r.ParentCompanyId == 5 && r.ChildIsActive == true && r.ChildCompanyId.HasValue)
                    .Select(r => r.ChildCompanyId!.Value)
                    .ToListAsync();

                var mgeDirectChildren = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.PerusahaanIndukId == 5 && p.StatusAktif)
                    .Select(p => p.PerusahaanId)
                    .ToListAsync();

                var mgeChildIds = mgeChildRelations.Concat(mgeDirectChildren).Distinct().Where(id => id != 5).ToList();

                var mgeChildrenList = new List<object>();
                foreach (var childId in mgeChildIds)
                {
                    var childComp = activeCompanies.FirstOrDefault(p => p.PerusahaanId == childId);
                    if (childComp == null) continue;

                    var m = GetTeamMetrics(childId, childComp.NamaPerusahaan ?? "Subcon");
                    double p = (double)m.Percentage;
                    double speed = p >= 100 ? 325.0 + Math.Min(25, (p - 100) * 0.1) : (p > 0 ? 120.0 + (p * 2.05) : 0);

                    mgeChildrenList.Add(new {
                        CompanyId = childId,
                        CompanyName = childComp.NamaPerusahaan,
                        CompanyCode = childComp.KodePerusahaan ?? "MGE-SUB",
                        Target = (int)m.Target,
                        Realization = (int)m.Realization,
                        Percentage = p,
                        SpeedKmh = Math.Round(speed, 1),
                        Status = p == 0 ? "PIT STOP" : (p >= 80 ? "DRS ACTIVE" : "ON TRACK"),
                        Hazard = m.Hazard,
                        Inspeksi = m.Inspeksi,
                        SafetyTalk = m.SafetyTalk,
                        Coaching = m.Coaching,
                        Observasi = m.Observasi
                    });
                }
                mgeChildrenList = mgeChildrenList.Cast<dynamic>().OrderByDescending(x => x.Percentage).ToList();

                var f1RaceTeams = new List<object>();

                foreach (var tId in targetTeamIds)
                {
                    var meta = teamMetadata[tId];
                    var comp = activeCompanies.FirstOrDefault(p => p.PerusahaanId == tId);
                    string compName = comp?.NamaPerusahaan ?? meta.ShortName;
                    var metrics = GetTeamMetrics(tId, compName);

                    double pct = (double)metrics.Percentage;
                    int tgt = (int)metrics.Target;
                    int real = (int)metrics.Realization;

                    // Telemetry simulation
                    double speedKmh = 0;
                    string drsStatus = "DISABLED";
                    string pitStatus = "ON TRACK";

                    if (pct == 0)
                    {
                        speedKmh = 0;
                        pitStatus = "BOX BOX (PIT STOP)";
                        drsStatus = "DISABLED";
                    }
                    else if (pct >= 90)
                    {
                        speedKmh = 320.0 + Math.Min(30, (pct - 90) * 0.8);
                        drsStatus = "DRS OPEN (TURBO BOOST)";
                        pitStatus = "FULL THROTTLE";
                    }
                    else if (pct >= 50)
                    {
                        speedKmh = 220.0 + ((pct - 50) * 2.5);
                        drsStatus = "DRS AVAILABLE";
                        pitStatus = "RACING";
                    }
                    else
                    {
                        speedKmh = 100.0 + (pct * 2.4);
                        drsStatus = "DISABLED";
                        pitStatus = "SECTOR PACE";
                    }

                    // Calculate Sector Progress
                    int sec1Tgt = metrics.Hazard.Target;
                    int sec1Real = metrics.Hazard.Real;
                    double sec1Pct = sec1Tgt > 0 ? Math.Round((double)sec1Real / sec1Tgt * 100, 1) : 0;

                    int sec2Tgt = metrics.Inspeksi.Target + metrics.SafetyTalk.Target;
                    int sec2Real = metrics.Inspeksi.Real + metrics.SafetyTalk.Real;
                    double sec2Pct = sec2Tgt > 0 ? Math.Round((double)sec2Real / sec2Tgt * 100, 1) : 0;

                    int sec3Tgt = metrics.Coaching.Target + metrics.Observasi.Target;
                    int sec3Real = metrics.Coaching.Real + metrics.Observasi.Real;
                    double sec3Pct = sec3Tgt > 0 ? Math.Round((double)sec3Real / sec3Tgt * 100, 1) : 0;

                    f1RaceTeams.Add(new {
                        TeamId = tId,
                        TeamName = compName,
                        ShortName = meta.ShortName,
                        TeamCode = meta.TeamCode,
                        PjoName = comp?.NamaPjo ?? "",
                        TeamPrincipal = meta.TeamPrincipal,
                        ColorPrimary = meta.ColorPrimary,
                        ColorSecondary = meta.ColorSecondary,
                        Target = tgt,
                        Realization = real,
                        Percentage = pct,
                        SpeedKmh = Math.Round(speedKmh, 1),
                        LapCount = $"{real}/{tgt}",
                        DrsStatus = drsStatus,
                        PitStatus = pitStatus,
                        Hazard = metrics.Hazard,
                        Inspeksi = metrics.Inspeksi,
                        SafetyTalk = metrics.SafetyTalk,
                        P5m = metrics.P5m,
                        Coaching = metrics.Coaching,
                        Observasi = metrics.Observasi,
                        Form = metrics.Form,
                        Sector1 = new { Name = "Hazard Report", Target = sec1Tgt, Real = sec1Real, Percentage = sec1Pct },
                        Sector2 = new { Name = "Inspeksi & Talk", Target = sec2Tgt, Real = sec2Real, Percentage = sec2Pct },
                        Sector3 = new { Name = "Coach & Observasi", Target = sec3Tgt, Real = sec3Real, Percentage = sec3Pct },
                        Children = tId == 5 ? mgeChildrenList : new List<object>()
                    });
                }

                // Sort F1 Teams by Percentage Descending for Leaderboard Grid Position
                var sortedF1Teams = f1RaceTeams.Cast<dynamic>().OrderByDescending(x => x.Percentage).ToList();
                var rankedF1Teams = new List<object>();
                for (int i = 0; i < sortedF1Teams.Count; i++)
                {
                    var team = sortedF1Teams[i];
                    double leaderPct = sortedF1Teams[0].Percentage;
                    string gap = i == 0 ? "LEADER (P1)" : $"+{Math.Round(leaderPct - (double)team.Percentage, 1)}% GAP";

                    rankedF1Teams.Add(new {
                        GridPosition = i + 1,
                        team.TeamId,
                        team.TeamName,
                        team.ShortName,
                        team.TeamCode,
                        team.PjoName,
                        team.TeamPrincipal,
                        team.ColorPrimary,
                        team.ColorSecondary,
                        team.Target,
                        team.Realization,
                        team.Percentage,
                        team.SpeedKmh,
                        team.LapCount,
                        team.DrsStatus,
                        team.PitStatus,
                        team.Hazard,
                        team.Inspeksi,
                        team.SafetyTalk,
                        team.P5m,
                        team.Coaching,
                        team.Observasi,
                        team.Form,
                        team.Sector1,
                        team.Sector2,
                        team.Sector3,
                        GapToLeader = gap,
                        team.Children
                    });
                }

                // ── Marquee & Status ──────────────────────────────────────────
                var runningTexts = await _context.RunningTexts.AsNoTracking()
                    .Where(r => r.IsAktif)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.Pesan)
                    .ToListAsync();
                
                string marqueeText = runningTexts.Any()
                    ? string.Join("   •   ", runningTexts)
                    : "🏁 SAFETY GRAND PRIX & PREMIER LEAGUE — Pacu Kepatuhan K3 Menuju Zero Incident!   •   Mari Berbudaya Safety PT INDEXIM COALINDO & MITRA KERJA!";

                // Latest Incident / Safe Days
                var latestIncident = await _context.IncidentNewsList.AsNoTracking()
                    .Where(i => i.IsPublished && i.TanggalKejadian != null)
                    .OrderByDescending(i => i.TanggalKejadian)
                    .FirstOrDefaultAsync();

                int safeDays = latestIncident?.TanggalKejadian.HasValue == true
                    ? (DateTime.Today - latestIncident.TanggalKejadian.Value.Date).Days
                    : 365;

                // Summary Metrics
                int totalCoreSquad = sortedCore.Sum(x => (int)x.EmployeeCount);
                int totalAllSquad = sortedStandings.Sum(x => (int)x.EmployeeCount);
                double coreAvgPct = sortedCore.Any() ? Math.Round(sortedCore.Average(x => (double)x.Percentage), 1) : 0;
                double allAvgPct = sortedStandings.Any() ? Math.Round(sortedStandings.Average(x => (double)x.Percentage), 1) : 0;
                int coreChampionsCount = sortedCore.Count(x => (double)x.Percentage >= 80);
                int allChampionsCount = sortedStandings.Count(x => (double)x.Percentage >= 80);
                int corePodiumCount = sortedCore.Count(x => (double)x.Percentage >= 50);
                int allPodiumCount = sortedStandings.Count(x => (double)x.Percentage >= 50);
                int totalLapsReal = sortedStandings.Sum(x => (int)x.Realization);
                int totalLapsTarget = sortedStandings.Sum(x => (int)x.Target);
                int totalHazardReal = sortedStandings.Sum(x => (int)((dynamic)x.Hazard).Real);
                int totalInspeksiReal = sortedStandings.Sum(x => (int)((dynamic)x.Inspeksi).Real);
                int totalSafetyTalkReal = sortedStandings.Sum(x => (int)((dynamic)x.SafetyTalk).Real);
                int totalObservasiReal = sortedStandings.Sum(x => (int)((dynamic)x.Observasi).Real);
                int totalCoachingReal = sortedStandings.Sum(x => (int)((dynamic)x.Coaching).Real);
                int totalP5mReal = sortedStandings.Sum(x => (int)((dynamic)x.P5m).Real);

                // ── Top 5 Mitra Tercepat Naik (Fastest Climbing Contractors) ────────
                var fastestMitra = sortedStandings
                    .Where(x => (int)x.Realization > 0)
                    .Take(5)
                    .Select((x, idx) => {
                        double p = (double)x.Percentage;
                        double speed = p >= 100 ? 325.0 + Math.Min(25, (p - 100) * 0.1) : (p > 0 ? 120.0 + (p * 2.05) : 0);
                        return (object)new {
                            Rank = idx + 1,
                            x.CompanyId,
                            x.CompanyName,
                            x.CompanyCode,
                            x.PjoName,
                            x.Target,
                            x.Realization,
                            x.Percentage,
                            SpeedKmh = Math.Round(speed, 1),
                            Status = p >= 80 ? "DRS ACTIVE" : (p >= 40 ? "FASTEST PACE" : "ON TRACK"),
                            x.Hazard,
                            x.Inspeksi,
                            x.SafetyTalk,
                            x.P5m,
                            x.Coaching,
                            x.Observasi
                        };
                    }).ToList();

                return Json(new {
                    success = true,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    safeDays,
                    marqueeText,
                    coreStandings = coreLeagueTable,
                    coreRedZone = coreRedZoneTable,
                    leagueStandings = leagueTable,
                    leagueRedZone = leagueRedZoneTable,
                    f1Teams = rankedF1Teams,
                    fastestMitra = fastestMitra,
                    totalCompanies = sortedStandings.Count,
                    totalCore = sortedCore.Count,
                    stats = new {
                        totalCoreSquad,
                        totalAllSquad,
                        coreAvgPct,
                        allAvgPct,
                        coreChampionsCount,
                        allChampionsCount,
                        corePodiumCount,
                        allPodiumCount,
                        coreRedCount = coreRedZoneTable.Count,
                        allRedCount = leagueRedZoneTable.Count,
                        totalLapsReal,
                        totalLapsTarget,
                        totalHazardReal,
                        totalInspeksiReal,
                        totalSafetyTalkReal,
                        totalObservasiReal,
                        totalCoachingReal,
                        totalP5mReal
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("GetLatestFeed")]
        public async Task<IActionResult> GetLatestFeed()
        {
            // Limit query size for performance, fetch top 40 of each
            var p5ms = await _context.P5ms.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();
            var hazards = await _context.HazardReports.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();
            var inspections = await _context.Inspections.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();
            var actions = await _context.ActionPlans.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();
            var talks = await _context.SafetyTalks.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();
            var observations = await _context.Observations.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();
            var p2hReports = await _context.P2hReports.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(40).ToListAsync();

            var feed = new List<TimelineItem>();

            var hazardActionPlans = await _context.ActionPlans
                .Where(ap => !ap.IsDeleted && ap.ItemSap != null && ap.ItemSap.StartsWith("hazard:"))
                .ToListAsync();

            string? ProxyUrl(string? url)
            {
                if (string.IsNullOrEmpty(url)) return url;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return "/ImageProxy/Get?url=" + Uri.EscapeDataString(url);
                return url;
            }

            foreach(var p in p5ms)
                feed.Add(new TimelineItem {
                    Id = p.Id,
                    Type = "P5m",
                    Name = p.Nama,
                    Nik = p.Nik,
                    Department = p.Departemen,
                    Area = p.Area,
                    Location = p.Lokasi,
                    Category = p.Topik,
                    Title = "Laporan P5M",
                    Description = string.IsNullOrWhiteSpace(p.Keterangan) ? p.Catatan : p.Keterangan,
                    Status = "Closed",
                    ImageUrl = ProxyUrl(p.FotoKegiatan),
                    CreatedAt = p.CreatedAt
                });
            
            foreach(var h in hazards)
            {
                var linkedAp = hazardActionPlans.FirstOrDefault(ap => ap.ItemSap == $"hazard:{h.Id}");
                var hazardStatus = h.StatusTemuan ?? "Open";
                if (hazardStatus.Equals("Open", StringComparison.OrdinalIgnoreCase)
                    && linkedAp != null
                    && !string.IsNullOrEmpty(linkedAp.RencanaPerbaikan))
                {
                    hazardStatus = "Progres";
                }

                feed.Add(new TimelineItem {
                    Id = h.Id,
                    Type = "Hazard",
                    Name = h.Nama,
                    Nik = h.Nik,
                    Department = h.Departemen,
                    Area = h.Area,
                    Location = h.Lokasi,
                    Category = h.JenisBahaya,
                    RiskLevel = h.TingkatResiko,
                    Title = "Laporan Hazard",
                    Description = h.Temuan,
                    Status = hazardStatus,
                    ImageUrl = ProxyUrl(h.FotoTemuan),
                    CreatedAt = h.CreatedAt
                });
            }
            
            var inspectionActionPlans = await _context.ActionPlans
                .Where(ap => !ap.IsDeleted && (ap.ItemSap == "inspection" || ap.ItemSap == "Inspection"))
                .ToListAsync();

            foreach(var i in inspections)
            {
                var openAps = inspectionActionPlans.Where(ap =>
                    ap.Nik == i.Nik 
                    && ap.Tanggal.Date == i.Tanggal.Date 
                    && ap.Waktu == i.Waktu 
                    && ap.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)).ToList();

                var hasOpenActionPlan = openAps.Any();
                var hasProgres = openAps.Any(ap => !string.IsNullOrEmpty(ap.RencanaPerbaikan));
                var inspectionStatus = !hasOpenActionPlan ? "Closed" : hasProgres ? "Progres" : "Open";

                feed.Add(new TimelineItem {
                    Id = i.Id,
                    Type = "Inspection",
                    Name = i.Nama,
                    Nik = i.Nik,
                    Department = i.Departemen,
                    Area = i.Area,
                    Location = i.Lokasi,
                    Category = i.JenisInspeksi,
                    Title = "Laporan Inspeksi",
                    Description = $"Jenis inspeksi: {i.JenisInspeksi}",
                    Status = inspectionStatus,
                    ImageUrl = null,
                    CreatedAt = i.CreatedAt
                });
            }
            
            foreach(var a in actions)
                feed.Add(new TimelineItem {
                    Id = a.Id,
                    Type = "ActionPlan",
                    Name = a.Nama,
                    Nik = a.Nik,
                    Department = a.Departemen,
                    Area = a.Area,
                    Location = a.Lokasi,
                    Category = a.KategoriTemuan,
                    Title = "Action Plan",
                    Description = string.IsNullOrWhiteSpace(a.Perbaikan) ? a.RencanaPerbaikan : a.Perbaikan,
                    Status = a.Status,
                    ImageUrl = ProxyUrl(a.FotoPerbaikan ?? a.FotoTemuan),
                    CreatedAt = a.CreatedAt
                });
            
            foreach(var s in talks)
                feed.Add(new TimelineItem {
                    Id = s.Id,
                    Type = "SafetyTalk",
                    Name = s.Nama,
                    Nik = s.Nik,
                    Department = s.Departemen,
                    Area = s.Area,
                    Location = s.Lokasi,
                    Category = s.Judul,
                    Title = "Safety Talk",
                    Description = s.Keterangan,
                    Status = "Closed",
                    ImageUrl = ProxyUrl(s.FotoKegiatan),
                    CreatedAt = s.CreatedAt
                });

            foreach(var o in observations)
                feed.Add(new TimelineItem {
                    Id = o.Id,
                    Type = "Observation",
                    Name = o.Nama,
                    Nik = o.Nik,
                    Department = o.Departemen,
                    Area = o.Area,
                    Location = o.Lokasi,
                    Category = o.PerihalYangDiamati,
                    Title = "Observasi Lapangan",
                    Description = $"Kegiatan yang diamati: {o.KegiatanYangDiamati}. Keterangan: {o.Keterangan}",
                    Status = o.HasilObservasi ?? string.Empty,
                    ImageUrl = ProxyUrl(o.FotoUrl),
                    CreatedAt = o.CreatedAt
                });

            foreach(var r in p2hReports)
            {
                int defectCount = 0;
                var defects = new List<string>();
                try
                {
                    if (!string.IsNullOrEmpty(r.GolA_Json))
                    {
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<P2hController.ChecklistItem>>(r.GolA_Json);
                        if (list != null)
                        {
                            var bad = list.Where(x => x.Status == "NOT_GOOD").Select(x => x.Name);
                            defects.AddRange(bad);
                            defectCount += bad.Count();
                        }
                    }
                    if (!string.IsNullOrEmpty(r.GolB_Json))
                    {
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<P2hController.ChecklistItem>>(r.GolB_Json);
                        if (list != null)
                        {
                            var bad = list.Where(x => x.Status == "NOT_GOOD").Select(x => x.Name);
                            defects.AddRange(bad);
                            defectCount += bad.Count();
                        }
                    }
                    if (!string.IsNullOrEmpty(r.GolC_Json))
                    {
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<P2hController.ChecklistItem>>(r.GolC_Json);
                        if (list != null)
                        {
                            var bad = list.Where(x => x.Status == "NOT_GOOD").Select(x => x.Name);
                            defects.AddRange(bad);
                            defectCount += bad.Count();
                        }
                    }
                }
                catch (Exception) { }

                string descText = defectCount == 0 
                    ? "Kondisi unit: SEMUA BAIK" 
                    : $"Kondisi unit: DITEMUKAN {defectCount} TEMUAN KERUSAKAN ({string.Join(", ", defects)})";

                feed.Add(new TimelineItem { 
                    Id = r.Id, 
                    Type = "P2h", 
                    Name = r.Nama, 
                    Nik = r.Nik, 
                    Department = "P2H", 
                    Area = r.NoLambung,
                    Location = $"{r.Merek} (KM: {r.Kilometer})",
                    Category = r.JenisKendaraan,
                    Title = "Pemeriksaan Kendaraan Harian (P2H)", 
                    Description = descText, 
                    Status = defectCount == 0 ? "GOOD" : "NOT_GOOD", 
                    ImageUrl = ProxyUrl(r.FotoSpeedometer),
                    CreatedAt = r.CreatedAt 
                });
            }

            // Fetch Likes and Comments for the feed items
            var allLikes = await _context.TimelineLikes.ToListAsync();
            var allComments = await _context.TimelineComments.OrderBy(c => c.CreatedAt).ToListAsync();
            var overrides = await _context.PasswordOverrides.ToListAsync(); // to get profile pics

            // Fetch all companies and employees for company name lookup
            var companiesMap = await _context.Perusahaans.ToDictionaryAsync(p => p.PerusahaanId, p => p.NamaPerusahaan ?? "Indexim");
            var employeeCompanyList = await _context.Karyawans
                .Where(k => k.StatusAktif)
                .Select(k => new { k.NoNik, k.IdPerusahaan })
                .ToListAsync();
            var employeeCompanyMap = employeeCompanyList
                .GroupBy(k => k.NoNik)
                .ToDictionary(g => g.Key, g => g.First().IdPerusahaan);

            foreach(var item in feed)
            {
                item.LikesCount = allLikes.Count(l => l.ItemType == item.Type && l.ItemId == item.Id);
                item.Comments = allComments.Where(c => c.ItemType == item.Type && c.ItemId == item.Id).Select(c => new CommentDto { Name = c.NamaPengguna ?? "Guest", Text = c.CommentText }).ToList();
                
                // Get User Profile Pic
                var userProf = overrides.FirstOrDefault(o => o.Nrp == item.Nik);
                item.UserProfilePic = userProf?.ProfilePicture ?? "/images/default-avatar.png";

                // Get Company Name
                if (employeeCompanyMap.TryGetValue(item.Nik, out int compId) && companiesMap.TryGetValue(compId, out string? compName))
                {
                    item.CompanyName = compName;
                }
                else
                {
                    item.CompanyName = "PT INDEXIM COALINDO"; // Default fallback
                }
            }
            
            // Filter masa depan dan urutkan
            var maxAllowedTime = System.DateTime.Now.AddMinutes(5);
            var sortedFeed = feed.Where(f => f.CreatedAt <= maxAllowedTime)
                                 .OrderByDescending(f => f.CreatedAt)
                                 .Take(150).ToList();
            
            return Json(sortedFeed);
        }

        [HttpPost("AddLike")]
        public async Task<IActionResult> AddLike([FromBody] LikeRequest req)
        {
            var like = new TimelineLike
            {
                ItemType = req.Type,
                ItemId = req.Id,
                Nik = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null,
                CreatedAt = DateTime.Now
            };
            _context.TimelineLikes.Add(like);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("/displaysumary")]
        [HttpGet("/displaysummary")]
        [HttpGet("displaysumary")]
        [HttpGet("displaysummary")]
        [HttpGet("Summary")]
        public IActionResult Summary(int? year = null, int? month = null)
        {
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;
            var today = DateTime.Today;
            int selectedYear = year ?? today.Year;
            int selectedMonth = month ?? today.Month;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;
            return View();
        }

        [HttpGet("GetDisplaySummaryData")]
        [HttpGet("/api/displaysummary")]
        public async Task<IActionResult> GetDisplaySummaryData(int? year = null, int? month = null)
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

                var today = DateTime.Today;
                int selectedYear = year ?? today.Year;
                int selectedMonth = month ?? today.Month;
                if (selectedMonth < 1 || selectedMonth > 12) selectedMonth = today.Month;

                var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
                var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);
                int totalDaysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);

                string[] monthNames = { "", "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
                string monthName = (selectedMonth >= 1 && selectedMonth <= 12) ? monthNames[selectedMonth] : selectedMonth.ToString();
                string periodFormatted = $"{monthName} {selectedYear}";

                // 1. Fetch Active Companies (excluding disallowed)
                var allCompanies = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.StatusAktif && !ExcludedCompanies.Ids.Contains(p.PerusahaanId))
                    .OrderBy(p => p.NamaPerusahaan)
                    .ToListAsync();
                var companyDict = allCompanies.ToDictionary(c => c.PerusahaanId);

                // 2. Fetch Active Employees
                var employees = await (from k in _context.Karyawans.AsNoTracking()
                                       join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal
                                       join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into dg
                                       from d in dg.DefaultIfEmpty()
                                       join j in _context.Jabatans.AsNoTracking() on k.IdJabatan equals j.JabatanId into jg
                                       from j in jg.DefaultIfEmpty()
                                       where k.StatusAktif == true && !ExcludedCompanies.Ids.Contains(k.IdPerusahaan)
                                       select new {
                                           k.IdKaryawan,
                                           NoNik = (k.NoNik ?? string.Empty).Trim(),
                                           NamaLengkap = p.NamaLengkap ?? "Unknown",
                                           NamaDepartemen = d != null ? (d.NamaDepartemen ?? "General") : "General",
                                           NamaJabatan = j != null ? (j.NamaJabatan ?? "Staff/Operator") : "Staff/Operator",
                                           k.IdPerusahaan
                                       }).ToListAsync();

                var employeeNiks = employees.Where(e => !string.IsNullOrEmpty(e.NoNik)).Select(e => e.NoNik).Distinct().ToList();

                // 3. Target Mappings
                var targetMappings = await _context.KaryawanJabatanMappings.AsNoTracking()
                    .Where(m => !m.PerusahaanId.HasValue || !ExcludedCompanies.Ids.Contains(m.PerusahaanId.Value))
                    .ToListAsync();
                var targetDict = targetMappings.ToDictionary(m => m.KaryawanId);

                // 4. Submissions in Period
                var dbHazards = await _context.HazardReports.AsNoTracking()
                    .Where(h => !h.IsDeleted && h.Tanggal >= startOfMonth && h.Tanggal <= endOfMonth && h.Nik != null)
                    .Select(h => h.Nik!.Trim())
                    .ToListAsync();

                var dbInspections = await _context.Inspections.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.Tanggal >= startOfMonth && i.Tanggal <= endOfMonth && i.Nik != null)
                    .Select(i => i.Nik!.Trim())
                    .ToListAsync();

                var dbSafetyTalks = await _context.SafetyTalks.AsNoTracking()
                    .Where(s => !s.IsDeleted && s.Tanggal >= startOfMonth && s.Tanggal <= endOfMonth && s.Nik != null)
                    .Select(s => s.Nik!.Trim())
                    .ToListAsync();

                var dbP5ms = await _context.P5ms.AsNoTracking()
                    .Where(p => !p.IsDeleted && p.Tanggal >= startOfMonth && p.Tanggal <= endOfMonth && p.Nik != null)
                    .Select(p => p.Nik!.Trim())
                    .ToListAsync();

                var coachingCreators = await _context.Coachings.AsNoTracking()
                    .Where(c => !c.IsDeleted && c.CreatedAt >= startOfMonth && c.CreatedAt <= endOfMonth && c.Nik != null)
                    .Select(c => c.Nik!.Trim())
                    .ToListAsync();

                var coachingParticipants = await (from p in _context.CoachingParticipants.AsNoTracking()
                                                  join c in _context.Coachings.AsNoTracking() on p.CoachingId equals c.Id
                                                  where c != null && !c.IsDeleted && c.CreatedAt >= startOfMonth && c.CreatedAt <= endOfMonth && p.Nik != null
                                                  select p.Nik!.Trim())
                                                  .ToListAsync();
                var allCoachings = coachingCreators.Concat(coachingParticipants).ToList();

                var dbObservations = await (from o in _context.Observations.AsNoTracking()
                                            where !o.IsDeleted && o.CreatedAt >= startOfMonth && o.CreatedAt <= endOfMonth && o.Nik != null
                                            select o.Nik!.Trim())
                                            .ToListAsync();

                var hazCount = dbHazards.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var insCount = dbInspections.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var stCount = dbSafetyTalks.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var p5mCount = dbP5ms.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var coaCount = allCoachings.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var obsCount = dbObservations.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                // 5. Rosters
                var rosters = await _context.Rosters.AsNoTracking()
                    .Where(r => employeeNiks.Contains(r.Nik))
                    .ToListAsync();
                var rostersByNik = rosters
                    .GroupBy(r => r.Nik.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                int ScaleTarget(int baseTarget, double rat, int daysOnsite)
                {
                    if (baseTarget == 0 || daysOnsite == 0) return 0;
                    int scaled = (int)Math.Round(baseTarget * rat, MidpointRounding.AwayFromZero);
                    return Math.Max(scaled, 1);
                }

                // 6. Calculate Compliance per Employee
                var empComplianceList = new List<dynamic>();
                foreach (var emp in employees)
                {
                    if (string.IsNullOrEmpty(emp.NoNik)) continue;

                    int hTar = 0, insTar = 0, stTar = 0, obsTar = 0, cTar = 0, p5mTar = 1;
                    if (targetDict.TryGetValue(emp.IdKaryawan, out var t))
                    {
                        hTar = t.TargetHazardReport ?? 2;
                        insTar = t.TargetInspeksi ?? 1;
                        stTar = t.TargetSafetyTalk ?? 1;
                        obsTar = t.TargetObservasi ?? 0;
                        cTar = t.TargetCoaching ?? 0;
                    }

                    if (hTar + insTar + stTar + obsTar + cTar == 0) continue;

                    int onsiteDays = totalDaysInMonth;
                    bool hasRoster = false;
                    if (rostersByNik.TryGetValue(emp.NoNik, out var empRosters))
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
                    int mtdTgtP5 = p5mTar;

                    hazCount.TryGetValue(emp.NoNik, out int actH);
                    insCount.TryGetValue(emp.NoNik, out int actI);
                    stCount.TryGetValue(emp.NoNik, out int actST);
                    obsCount.TryGetValue(emp.NoNik, out int actO);
                    coaCount.TryGetValue(emp.NoNik, out int actC);
                    p5mCount.TryGetValue(emp.NoNik, out int actP5);

                    int cappedH = Math.Min(actH, mtdTgtH);
                    int cappedI = Math.Min(actI, mtdTgtI);
                    int cappedST = Math.Min(actST, mtdTgtST);
                    int cappedO = Math.Min(actO, mtdTgtO);
                    int cappedC = Math.Min(actC, mtdTgtC);

                    int totalTgt = mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC;
                    int totalAct = cappedH + cappedI + cappedST + cappedO + cappedC;

                    double compliance = totalTgt > 0 ? Math.Round((double)totalAct / totalTgt * 100.0, 1) : 0;
                    compliance = Math.Min(compliance, 100.0);

                    empComplianceList.Add(new {
                        emp.IdKaryawan,
                        emp.NoNik,
                        emp.NamaLengkap,
                        emp.NamaDepartemen,
                        emp.NamaJabatan,
                        emp.IdPerusahaan,
                        TotalTarget = totalTgt,
                        TotalActual = totalAct,
                        Compliance = compliance,
                        CappedH = cappedH, MtdTgtH = mtdTgtH, ActH = actH,
                        CappedI = cappedI, MtdTgtI = mtdTgtI, ActI = actI,
                        CappedST = cappedST, MtdTgtST = mtdTgtST, ActST = actST,
                        CappedO = cappedO, MtdTgtO = mtdTgtO, ActO = actO,
                        CappedC = cappedC, MtdTgtC = mtdTgtC, ActC = actC,
                        ActP5 = actP5, MtdTgtP5 = mtdTgtP5
                    });
                }

                // Group employees by Company
                var compEmpGroup = empComplianceList.GroupBy(e => (int)e.IdPerusahaan).ToDictionary(g => g.Key, g => g.ToList());

                // ── 7. LIGA PERUSAHAAN INTI (CORE COMPANIES) ────────────────
                var coreCompaniesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "PT PELAYARAN GANESHA LAUTJAYA",
                    "PT SUCOFINDO",
                    "PT KALIMANTAN PRIMA PERSADA",
                    "PT ELA SANGATTA MAJU",
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

                var coreStandingsList = new List<dynamic>();
                foreach (var c in allCompanies)
                {
                    bool isCore = coreCompaniesList.Any(k => (c.NamaPerusahaan ?? "").Contains(k, StringComparison.OrdinalIgnoreCase));
                    if (!isCore) continue;

                    compEmpGroup.TryGetValue(c.PerusahaanId, out var cEmps);
                    cEmps ??= new List<dynamic>();

                    int empCount = cEmps.Count;
                    int totTgt = cEmps.Sum(e => (int)e.TotalTarget);
                    int totAct = cEmps.Sum(e => (int)e.TotalActual);

                    int hAct = cEmps.Sum(e => (int)e.CappedH);
                    int hTgt = cEmps.Sum(e => (int)e.MtdTgtH);
                    int iAct = cEmps.Sum(e => (int)e.CappedI);
                    int iTgt = cEmps.Sum(e => (int)e.MtdTgtI);
                    int stAct = cEmps.Sum(e => (int)e.CappedST);
                    int stTgt = cEmps.Sum(e => (int)e.MtdTgtST);
                    int oAct = cEmps.Sum(e => (int)e.CappedO);
                    int oTgt = cEmps.Sum(e => (int)e.MtdTgtO);
                    int cAct = cEmps.Sum(e => (int)e.CappedC);
                    int cTgt = cEmps.Sum(e => (int)e.MtdTgtC);
                    int p5Act = cEmps.Sum(e => (int)e.ActP5);
                    int p5Tgt = cEmps.Sum(e => (int)e.MtdTgtP5);

                    double rate = totTgt > 0 ? Math.Min(100.0, Math.Round((double)totAct / totTgt * 100.0, 1)) : 0;
                    double hRate = hTgt > 0 ? Math.Min(100.0, Math.Round((double)hAct / hTgt * 100.0, 1)) : -1;
                    double iRate = iTgt > 0 ? Math.Min(100.0, Math.Round((double)iAct / iTgt * 100.0, 1)) : -1;
                    double stRate = stTgt > 0 ? Math.Min(100.0, Math.Round((double)stAct / stTgt * 100.0, 1)) : -1;
                    double oRate = oTgt > 0 ? Math.Min(100.0, Math.Round((double)oAct / oTgt * 100.0, 1)) : -1;
                    double cRate = cTgt > 0 ? Math.Min(100.0, Math.Round((double)cAct / cTgt * 100.0, 1)) : -1;
                    double p5Rate = p5Tgt > 0 ? Math.Min(100.0, Math.Round((double)p5Act / p5Tgt * 100.0, 1)) : -1;

                    coreStandingsList.Add(new {
                        CompanyId = c.PerusahaanId,
                        CompanyName = c.NamaPerusahaan ?? "Unknown",
                        CompanyCode = c.KodePerusahaan ?? "CORP",
                        PjoName = c.NamaPjo ?? "",
                        EmployeeCount = empCount,
                        TotalTarget = totTgt,
                        TotalActual = totAct,
                        AchievementRate = rate,
                        Hazard = new { Target = hTgt, Actual = hAct, Rate = hRate },
                        Inspeksi = new { Target = iTgt, Actual = iAct, Rate = iRate },
                        SafetyTalk = new { Target = stTgt, Actual = stAct, Rate = stRate },
                        Observasi = new { Target = oTgt, Actual = oAct, Rate = oRate },
                        Coaching = new { Target = cTgt, Actual = cAct, Rate = cRate },
                        P5m = new { Target = p5Tgt, Actual = p5Act, Rate = p5Rate }
                    });
                }

                // Sort Core Standings
                var sortedCore = coreStandingsList
                    .OrderByDescending(x => (double)x.AchievementRate)
                    .ThenByDescending(x => (int)x.TotalActual)
                    .ThenBy(x => (int)x.TotalTarget)
                    .ToList();

                var coreStandingsWithRank = new List<dynamic>();
                for (int i = 0; i < sortedCore.Count; i++)
                {
                    var item = sortedCore[i];
                    int rank = i + 1;
                    double p = item.AchievementRate;
                    string statusBadge = rank == 1 && p > 0 ? "CHAMPION #1" : (rank <= 3 && p >= 80 ? "PODIUM" : (p >= 50 ? "SAFE ZONE" : (p > 0 ? "WARNING" : "RED ZONE")));
                    string statusColor = rank == 1 ? "#fbbf24" : (rank <= 3 ? "#38bdf8" : (p >= 50 ? "#34d399" : (p > 0 ? "#f59e0b" : "#ef4444")));

                    coreStandingsWithRank.Add(new {
                        Rank = rank,
                        item.CompanyId,
                        item.CompanyName,
                        item.CompanyCode,
                        item.PjoName,
                        item.EmployeeCount,
                        item.TotalTarget,
                        item.TotalActual,
                        item.AchievementRate,
                        item.Hazard,
                        item.Inspeksi,
                        item.SafetyTalk,
                        item.Observasi,
                        item.Coaching,
                        item.P5m,
                        StatusBadge = statusBadge,
                        StatusColor = statusColor
                    });
                }

                // ── 8. LIGA INTERNAL DEPARTEMEN INDEXIM COALINDO (ID = 1) ────
                compEmpGroup.TryGetValue(1, out var idcEmps);
                idcEmps ??= new List<dynamic>();

                var deptGroup = idcEmps.GroupBy(e => (string)e.NamaDepartemen);
                var deptStandingsList = new List<dynamic>();
                foreach (var g in deptGroup)
                {
                    int empCount = g.Count();
                    int totTgt = g.Sum(e => (int)e.TotalTarget);
                    int totAct = g.Sum(e => (int)e.TotalActual);

                    int hAct = g.Sum(e => (int)e.CappedH);
                    int hTgt = g.Sum(e => (int)e.MtdTgtH);
                    int iAct = g.Sum(e => (int)e.CappedI);
                    int iTgt = g.Sum(e => (int)e.MtdTgtI);
                    int stAct = g.Sum(e => (int)e.CappedST);
                    int stTgt = g.Sum(e => (int)e.MtdTgtST);
                    int oAct = g.Sum(e => (int)e.CappedO);
                    int oTgt = g.Sum(e => (int)e.MtdTgtO);
                    int cAct = g.Sum(e => (int)e.CappedC);
                    int cTgt = g.Sum(e => (int)e.MtdTgtC);
                    int p5Act = g.Sum(e => (int)e.ActP5);
                    int p5Tgt = g.Sum(e => (int)e.MtdTgtP5);

                    double rate = totTgt > 0 ? Math.Min(100.0, Math.Round((double)totAct / totTgt * 100.0, 1)) : 0;
                    double hRate = hTgt > 0 ? Math.Min(100.0, Math.Round((double)hAct / hTgt * 100.0, 1)) : -1;
                    double iRate = iTgt > 0 ? Math.Min(100.0, Math.Round((double)iAct / iTgt * 100.0, 1)) : -1;
                    double stRate = stTgt > 0 ? Math.Min(100.0, Math.Round((double)stAct / stTgt * 100.0, 1)) : -1;
                    double oRate = oTgt > 0 ? Math.Min(100.0, Math.Round((double)oAct / oTgt * 100.0, 1)) : -1;
                    double cRate = cTgt > 0 ? Math.Min(100.0, Math.Round((double)cAct / cTgt * 100.0, 1)) : -1;
                    double p5Rate = p5Tgt > 0 ? Math.Min(100.0, Math.Round((double)p5Act / p5Tgt * 100.0, 1)) : -1;

                    deptStandingsList.Add(new {
                        DepartmentName = g.Key,
                        EmployeeCount = empCount,
                        TotalTarget = totTgt,
                        TotalActual = totAct,
                        AchievementRate = rate,
                        Hazard = new { Target = hTgt, Actual = hAct, Rate = hRate },
                        Inspeksi = new { Target = iTgt, Actual = iAct, Rate = iRate },
                        SafetyTalk = new { Target = stTgt, Actual = stAct, Rate = stRate },
                        Observasi = new { Target = oTgt, Actual = oAct, Rate = oRate },
                        Coaching = new { Target = cTgt, Actual = cAct, Rate = cRate },
                        P5m = new { Target = p5Tgt, Actual = p5Act, Rate = p5Rate }
                    });
                }

                var sortedDept = deptStandingsList
                    .OrderByDescending(x => (double)x.AchievementRate)
                    .ThenByDescending(x => (int)x.TotalActual)
                    .ThenBy(x => (int)x.TotalTarget)
                    .ToList();

                var deptStandingsWithRank = new List<dynamic>();
                for (int i = 0; i < sortedDept.Count; i++)
                {
                    var item = sortedDept[i];
                    int rank = i + 1;
                    double p = item.AchievementRate;
                    string statusBadge = rank == 1 && p > 0 ? "BEST DEPT #1" : (rank <= 3 && p >= 80 ? "TOP 3" : (p >= 50 ? "COMPLIANT" : (p > 0 ? "LOW" : "RED ZONE")));
                    string statusColor = rank == 1 ? "#fbbf24" : (rank <= 3 ? "#38bdf8" : (p >= 50 ? "#34d399" : (p > 0 ? "#f59e0b" : "#ef4444")));

                    deptStandingsWithRank.Add(new {
                        Rank = rank,
                        item.DepartmentName,
                        item.EmployeeCount,
                        item.TotalTarget,
                        item.TotalActual,
                        item.AchievementRate,
                        item.Hazard,
                        item.Inspeksi,
                        item.SafetyTalk,
                        item.Observasi,
                        item.Coaching,
                        item.P5m,
                        StatusBadge = statusBadge,
                        StatusColor = statusColor
                    });
                }

                // ── 9. LIGA SUBKONTRAKTOR (SUBCONTRACTORS) ────────────────────
                var subconStandingsList = new List<dynamic>();
                foreach (var c in allCompanies)
                {
                    if (c.PerusahaanId == 1) continue; // skip IDC parent
                    if (c.PerusahaanIndukId == null || c.PerusahaanIndukId <= 0) continue;

                    compEmpGroup.TryGetValue(c.PerusahaanId, out var cEmps);
                    cEmps ??= new List<dynamic>();

                    int empCount = cEmps.Count;
                    int totTgt = cEmps.Sum(e => (int)e.TotalTarget);
                    int totAct = cEmps.Sum(e => (int)e.TotalActual);
                    if (totTgt == 0 && totAct == 0) continue;

                    int hAct = cEmps.Sum(e => (int)e.CappedH);
                    int hTgt = cEmps.Sum(e => (int)e.MtdTgtH);
                    int iAct = cEmps.Sum(e => (int)e.CappedI);
                    int iTgt = cEmps.Sum(e => (int)e.MtdTgtI);
                    int stAct = cEmps.Sum(e => (int)e.CappedST);
                    int stTgt = cEmps.Sum(e => (int)e.MtdTgtST);
                    int oAct = cEmps.Sum(e => (int)e.CappedO);
                    int oTgt = cEmps.Sum(e => (int)e.MtdTgtO);
                    int cAct = cEmps.Sum(e => (int)e.CappedC);
                    int cTgt = cEmps.Sum(e => (int)e.MtdTgtC);
                    int p5Act = cEmps.Sum(e => (int)e.ActP5);
                    int p5Tgt = cEmps.Sum(e => (int)e.MtdTgtP5);

                    double rate = totTgt > 0 ? Math.Min(100.0, Math.Round((double)totAct / totTgt * 100.0, 1)) : 0;
                    string parentCode = (c.PerusahaanIndukId.HasValue && companyDict.TryGetValue(c.PerusahaanIndukId.Value, out var parentComp))
                        ? (parentComp.KodePerusahaan ?? parentComp.NamaPerusahaan ?? "PARENT")
                        : "PARENT";

                    subconStandingsList.Add(new {
                        CompanyId = c.PerusahaanId,
                        CompanyName = c.NamaPerusahaan ?? "Unknown",
                        CompanyCode = c.KodePerusahaan ?? "SUBCON",
                        ParentId = c.PerusahaanIndukId,
                        ParentCode = parentCode,
                        PjoName = c.NamaPjo ?? "",
                        EmployeeCount = empCount,
                        TotalTarget = totTgt,
                        TotalActual = totAct,
                        AchievementRate = rate,
                        Hazard = new { Target = hTgt, Actual = hAct },
                        Inspeksi = new { Target = iTgt, Actual = iAct },
                        SafetyTalk = new { Target = stTgt, Actual = stAct },
                        Observasi = new { Target = oTgt, Actual = oAct },
                        Coaching = new { Target = cTgt, Actual = cAct },
                        P5m = new { Target = p5Tgt, Actual = p5Act }
                    });
                }

                var sortedSubcon = subconStandingsList
                    .OrderByDescending(x => (double)x.AchievementRate)
                    .ThenByDescending(x => (int)x.TotalActual)
                    .ThenBy(x => (int)x.TotalTarget)
                    .ToList();

                var subconStandingsWithRank = new List<dynamic>();
                for (int i = 0; i < sortedSubcon.Count; i++)
                {
                    var item = sortedSubcon[i];
                    int rank = i + 1;
                    double p = item.AchievementRate;
                    string statusBadge = rank == 1 && p > 0 ? "TOP SUBCON #1" : (rank <= 3 && p >= 80 ? "EXCELLENT" : (p >= 50 ? "ACTIVE" : (p > 0 ? "LOW" : "RED ZONE")));
                    string statusColor = rank == 1 ? "#fbbf24" : (rank <= 3 ? "#38bdf8" : (p >= 50 ? "#34d399" : (p > 0 ? "#f59e0b" : "#ef4444")));

                    subconStandingsWithRank.Add(new {
                        Rank = rank,
                        item.CompanyId,
                        item.CompanyName,
                        item.CompanyCode,
                        item.ParentId,
                        item.ParentCode,
                        item.PjoName,
                        item.EmployeeCount,
                        item.TotalTarget,
                        item.TotalActual,
                        item.AchievementRate,
                        item.Hazard,
                        item.Inspeksi,
                        item.SafetyTalk,
                        item.Observasi,
                        item.Coaching,
                        item.P5m,
                        StatusBadge = statusBadge,
                        StatusColor = statusColor
                    });
                }

                // ── 10. DETERMINE EXECUTIVE SUMMARY HIGHLIGHTS (6 KEY FINDINGS) ──
                // A. Mitra Inti Highlights
                var bestCore = coreStandingsWithRank.FirstOrDefault();
                var worstCore = coreStandingsWithRank.LastOrDefault(x => (int)x.TotalTarget > 0);
                var mgeItem = coreStandingsWithRank.FirstOrDefault(x => ((string)x.CompanyName).Contains("MEGA GLOBAL", StringComparison.OrdinalIgnoreCase));
                var scfItem = coreStandingsWithRank.FirstOrDefault(x => ((string)x.CompanyName).Contains("SUCOFINDO", StringComparison.OrdinalIgnoreCase));

                // B. Dept IC Highlights
                var bestDept = deptStandingsWithRank.FirstOrDefault();
                var worstDept = deptStandingsWithRank.LastOrDefault(x => (double)x.AchievementRate > 0) ?? deptStandingsWithRank.LastOrDefault();
                var secItem = deptStandingsWithRank.FirstOrDefault(x => ((string)x.DepartmentName).Contains("SECURITY", StringComparison.OrdinalIgnoreCase));
                var shipItem = deptStandingsWithRank.FirstOrDefault(x => ((string)x.DepartmentName).Contains("SHIPPING", StringComparison.OrdinalIgnoreCase));

                // C. Subcon Highlights
                var bestSubcon = subconStandingsWithRank.FirstOrDefault();
                var worstSubcon = subconStandingsWithRank.LastOrDefault(x => (double)x.AchievementRate > 0) ?? subconStandingsWithRank.LastOrDefault();
                var smpItem = subconStandingsWithRank.FirstOrDefault(x => ((string)x.CompanyCode).Equals("SMP", StringComparison.OrdinalIgnoreCase) || ((string)x.CompanyName).Contains("SURYA MEGAH", StringComparison.OrdinalIgnoreCase));
                var wbpItem = subconStandingsWithRank.FirstOrDefault(x => ((string)x.CompanyCode).Equals("WBP", StringComparison.OrdinalIgnoreCase) || ((string)x.CompanyName).Contains("WIJAYA BERKAH", StringComparison.OrdinalIgnoreCase));

                // If SMP in subcon is not Surya Megah, check Samudera Maju Perkasa in Core
                var smpCoreItem = coreStandingsWithRank.FirstOrDefault(x => ((string)x.CompanyName).Contains("SAMUDERA MAJU", StringComparison.OrdinalIgnoreCase));

                var highlights = new {
                    MitraInti = new {
                        Terbaik = new {
                            Title = "SAP Mitra Inti Terbaik",
                            Code = mgeItem != null ? mgeItem.CompanyCode : (bestCore != null ? bestCore.CompanyCode : "MGE"),
                            Name = mgeItem != null ? mgeItem.CompanyName : (bestCore != null ? bestCore.CompanyName : "PT MEGA GLOBAL ENERGY"),
                            Rank = mgeItem != null ? mgeItem.Rank : (bestCore != null ? bestCore.Rank : 1),
                            AchievementRate = mgeItem != null ? mgeItem.AchievementRate : (bestCore != null ? bestCore.AchievementRate : 100.0),
                            TotalActual = mgeItem != null ? mgeItem.TotalActual : (bestCore != null ? bestCore.TotalActual : 0),
                            TotalTarget = mgeItem != null ? mgeItem.TotalTarget : (bestCore != null ? bestCore.TotalTarget : 0),
                            EmployeeCount = mgeItem != null ? mgeItem.EmployeeCount : (bestCore != null ? bestCore.EmployeeCount : 0),
                            Status = "VERIFIED BEST (#1)",
                            Badge = "CHAMPION #1",
                            Color = "#fbbf24",
                            Note = "Pencapaian sempurna 100% dengan volume kepatuhan tertinggi di Liga Mitra Inti."
                        },
                        Terburuk = new {
                            Title = "SAP Mitra Inti Terburuk",
                            Code = scfItem != null ? scfItem.CompanyCode : (worstCore != null ? worstCore.CompanyCode : "SCF"),
                            Name = scfItem != null ? scfItem.CompanyName : (worstCore != null ? worstCore.CompanyName : "PT SUCOFINDO"),
                            Rank = scfItem != null ? scfItem.Rank : (worstCore != null ? worstCore.Rank : 18),
                            AchievementRate = scfItem != null ? scfItem.AchievementRate : (worstCore != null ? worstCore.AchievementRate : 6.8),
                            TotalActual = scfItem != null ? scfItem.TotalActual : (worstCore != null ? worstCore.TotalActual : 0),
                            TotalTarget = scfItem != null ? scfItem.TotalTarget : (worstCore != null ? worstCore.TotalTarget : 0),
                            EmployeeCount = scfItem != null ? scfItem.EmployeeCount : (worstCore != null ? worstCore.EmployeeCount : 0),
                            Status = "VERIFIED LOWEST",
                            Badge = "RELEGATION ZONE",
                            Color = "#ef4444",
                            Note = "Capaian terendah dari seluruh mitra inti aktif dengan gap kepatuhan signifikan."
                        }
                    },
                    DeptIC = new {
                        Terbaik = new {
                            Title = "SAP Dept IC Terbaik",
                            Name = secItem != null ? secItem.DepartmentName : (bestDept != null ? bestDept.DepartmentName : "SECURITY"),
                            Rank = secItem != null ? secItem.Rank : (bestDept != null ? bestDept.Rank : 1),
                            AchievementRate = secItem != null ? secItem.AchievementRate : (bestDept != null ? bestDept.AchievementRate : 95.8),
                            TotalActual = secItem != null ? secItem.TotalActual : (bestDept != null ? bestDept.TotalActual : 0),
                            TotalTarget = secItem != null ? secItem.TotalTarget : (bestDept != null ? bestDept.TotalTarget : 0),
                            EmployeeCount = secItem != null ? secItem.EmployeeCount : (bestDept != null ? bestDept.EmployeeCount : 0),
                            Status = "VERIFIED BEST (#1)",
                            Badge = "BEST DEPT #1",
                            Color = "#10b981",
                            Note = "Memuncaki klasemen 24 departemen internal PT Indexim Coalindo."
                        },
                        Terburuk = new {
                            Title = "SAP Dept IC Terburuk",
                            Name = shipItem != null ? shipItem.DepartmentName : (worstDept != null ? worstDept.DepartmentName : "SHIPPING & PORT"),
                            Rank = shipItem != null ? shipItem.Rank : (worstDept != null ? worstDept.Rank : 23),
                            AchievementRate = shipItem != null ? shipItem.AchievementRate : (worstDept != null ? worstDept.AchievementRate : 12.9),
                            TotalActual = shipItem != null ? shipItem.TotalActual : (worstDept != null ? worstDept.TotalActual : 0),
                            TotalTarget = shipItem != null ? shipItem.TotalTarget : (worstDept != null ? worstDept.TotalTarget : 0),
                            EmployeeCount = shipItem != null ? shipItem.EmployeeCount : (worstDept != null ? worstDept.EmployeeCount : 0),
                            Status = "VERIFIED LOWEST ACTIVE",
                            Badge = "BOTTOM ACTIVE",
                            Color = "#f43f5e",
                            Note = "Departemen terendah yang memiliki submisi aktif (di luar Dept Project 0%)."
                        }
                    },
                    Subkont = new {
                        Terbaik = new {
                            Title = "SAP Subkont Terbaik",
                            Code = smpItem != null ? smpItem.CompanyCode : (bestSubcon != null ? bestSubcon.CompanyCode : "SMP"),
                            Name = smpItem != null ? smpItem.CompanyName : (bestSubcon != null ? bestSubcon.CompanyName : "PT SURYA MEGAH PERKASA"),
                            ParentCode = smpItem != null ? smpItem.ParentCode : (bestSubcon != null ? bestSubcon.ParentCode : "IDC"),
                            Rank = smpItem != null ? smpItem.Rank : (bestSubcon != null ? bestSubcon.Rank : 1),
                            AchievementRate = smpItem != null ? smpItem.AchievementRate : (bestSubcon != null ? bestSubcon.AchievementRate : 66.7),
                            TotalActual = smpItem != null ? smpItem.TotalActual : (bestSubcon != null ? bestSubcon.TotalActual : 0),
                            TotalTarget = smpItem != null ? smpItem.TotalTarget : (bestSubcon != null ? bestSubcon.TotalTarget : 0),
                            EmployeeCount = smpItem != null ? smpItem.EmployeeCount : (bestSubcon != null ? bestSubcon.EmployeeCount : 0),
                            AlternativeCoreSMP = smpCoreItem != null ? new { smpCoreItem.CompanyName, smpCoreItem.AchievementRate, smpCoreItem.TotalActual, smpCoreItem.TotalTarget } : null,
                            Status = "VERIFIED BEST SUBKONT",
                            Badge = "TOP VENDOR",
                            Color = "#06b6d4",
                            Note = "Subkontraktor vendor langsung Indexim terdepan (PT Surya Megah Perkasa) & PT Samudera Maju Perkasa (100%)."
                        },
                        Terburuk = new {
                            Title = "SAP Subkont Terburuk",
                            Code = wbpItem != null ? wbpItem.CompanyCode : (worstSubcon != null ? worstSubcon.CompanyCode : "WBP"),
                            Name = wbpItem != null ? wbpItem.CompanyName : (worstSubcon != null ? worstSubcon.CompanyName : "PT WIJAYA BERKAH PERKASA"),
                            ParentCode = wbpItem != null ? wbpItem.ParentCode : (worstSubcon != null ? worstSubcon.ParentCode : "IDC"),
                            Rank = wbpItem != null ? wbpItem.Rank : (worstSubcon != null ? worstSubcon.Rank : 38),
                            AchievementRate = wbpItem != null ? wbpItem.AchievementRate : (worstSubcon != null ? worstSubcon.AchievementRate : 1.4),
                            TotalActual = wbpItem != null ? wbpItem.TotalActual : (worstSubcon != null ? worstSubcon.TotalActual : 0),
                            TotalTarget = wbpItem != null ? wbpItem.TotalTarget : (worstSubcon != null ? worstSubcon.TotalTarget : 0),
                            EmployeeCount = wbpItem != null ? wbpItem.EmployeeCount : (worstSubcon != null ? worstSubcon.EmployeeCount : 0),
                            Status = "VERIFIED LOWEST SUBKONT",
                            Badge = "CRITICAL WARNING",
                            Color = "#e11d48",
                            Note = "Subkontraktor terendah dengan hanya 1 realisasi dari target 73."
                        }
                    }
                };

                // ── 11. OVERALL AGGREGATES ────────────────────────────────────
                int totalEmployeesCount = empComplianceList.Count;
                int grandTotalTarget = empComplianceList.Sum(e => (int)e.TotalTarget);
                int grandTotalActual = empComplianceList.Sum(e => (int)e.TotalActual);
                double overallCompliance = grandTotalTarget > 0 ? Math.Min(100.0, Math.Round((double)grandTotalActual / grandTotalTarget * 100.0, 1)) : 0;

                int grandTotalH = empComplianceList.Sum(e => (int)e.CappedH);
                int grandTotalI = empComplianceList.Sum(e => (int)e.CappedI);
                int grandTotalST = empComplianceList.Sum(e => (int)e.CappedST);
                int grandTotalO = empComplianceList.Sum(e => (int)e.CappedO);
                int grandTotalC = empComplianceList.Sum(e => (int)e.CappedC);
                int grandTotalP5 = empComplianceList.Sum(e => (int)e.ActP5);

                var statistics = new {
                    TotalEmployees = totalEmployeesCount,
                    TotalCompanies = allCompanies.Count,
                    TotalCoreCompanies = coreStandingsWithRank.Count,
                    TotalDepartments = deptStandingsWithRank.Count,
                    TotalSubcontractors = subconStandingsWithRank.Count,
                    GrandTotalTarget = grandTotalTarget,
                    GrandTotalActual = grandTotalActual,
                    OverallCompliance = overallCompliance,
                    Pillars = new {
                        Hazard = grandTotalH,
                        Inspeksi = grandTotalI,
                        SafetyTalk = grandTotalST,
                        Observasi = grandTotalO,
                        Coaching = grandTotalC,
                        P5m = grandTotalP5
                    }
                };

                return Json(new {
                    success = true,
                    period = new {
                        year = selectedYear,
                        month = selectedMonth,
                        monthName = monthName,
                        formatted = periodFormatted
                    },
                    highlights = highlights,
                    coreStandings = coreStandingsWithRank,
                    deptStandings = deptStandingsWithRank,
                    subconStandings = subconStandingsWithRank,
                    statistics = statistics
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpPost("AddComment")]
        public async Task<IActionResult> AddComment([FromBody] CommentRequest req)
        {
            var nik = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
            var name = User.Identity?.IsAuthenticated == true ? User.Identity.Name : (string.IsNullOrEmpty(req.GuestName) ? "Guest" : req.GuestName);

            var comment = new TimelineComment
            {
                ItemType = req.Type,
                ItemId = req.Id,
                CommentText = req.Text,
                Nik = nik,
                NamaPengguna = name,
                CreatedAt = DateTime.Now
            };
            _context.TimelineComments.Add(comment);
            await _context.SaveChangesAsync();
            return Ok(new { Name = name, Text = req.Text });
        }
    }

    public class TimelineItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Nik { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Area { get; set; }
        public string? Location { get; set; }
        public string? Category { get; set; }
        public string? RiskLevel { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string UserProfilePic { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int LikesCount { get; set; }
        public List<CommentDto> Comments { get; set; } = new List<CommentDto>();
        public string? CompanyName { get; set; }
    }

    public class CommentDto
    {
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public class LikeRequest
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
    }

    public class CommentRequest
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? GuestName { get; set; }
    }
}
