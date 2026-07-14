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
        public IActionResult Index()
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
                    .Select(ap => new { ap.ItemSap, ap.ReassignedFrom })
                    .ToListAsync();

                int totalClosedHazards = allHazards.Count(h => h.StatusTemuan == "Closed");
                int totalProgresHazards = 0;
                int totalOpenHazards = 0;

                foreach (var h in allHazards)
                {
                    if (h.StatusTemuan == "Closed") continue;
                    
                    var linkedAp = hazardActionPlans.FirstOrDefault(ap => ap.ItemSap == $"hazard:{h.Id}");
                    if (linkedAp != null && !string.IsNullOrEmpty(linkedAp.ReassignedFrom))
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

                int totalOpenActionPlans = await _context.ActionPlans.CountAsync(ap => !ap.IsDeleted && ap.Status == "Open");
                int totalClosedActionPlans = await _context.ActionPlans.CountAsync(ap => !ap.IsDeleted && ap.Status == "Closed");
                int totalProgresActionPlans = await _context.ActionPlans.CountAsync(ap => !ap.IsDeleted && (ap.Status == "Progres" || ap.Status == "Progress" || !string.IsNullOrEmpty(ap.ReassignedFrom)));

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
                            await _context.HazardReports.AnyAsync(x => !x.IsDeleted && x.PerusahaanId == sId && x.CreatedAt >= startOfMonthMaincon) ||
                            await _context.Inspections.AnyAsync(x => !x.IsDeleted && x.PerusahaanId == sId && x.CreatedAt >= startOfMonthMaincon) ||
                            await _context.SafetyTalks.AnyAsync(x => !x.IsDeleted && x.PerusahaanId == sId && x.CreatedAt >= startOfMonthMaincon) ||
                            await _context.Coachings.AnyAsync(x => !x.IsDeleted && x.PerusahaanId == sId && x.CreatedAt >= startOfMonthMaincon) ||
                            await _context.Observations.AnyAsync(o => !o.IsDeleted && o.CreatedAt >= startOfMonthMaincon && _context.Karyawans.Any(k => k.NoNik == o.Nik && k.IdPerusahaan == sId));

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
                    && !string.IsNullOrEmpty(linkedAp.ReassignedFrom))
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
                var hasReassigned = openAps.Any(ap => !string.IsNullOrEmpty(ap.ReassignedFrom));
                var inspectionStatus = !hasOpenActionPlan ? "Closed" : hasReassigned ? "Progres" : "Open";

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
