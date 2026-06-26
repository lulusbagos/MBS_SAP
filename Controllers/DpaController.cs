using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MBS_SAP.Data;
using MBS_SAP.Models;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class DpaController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DpaController> _logger;

        public DpaController(AppDbContext context, ILogger<DpaController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public static readonly string[] SafetyDriving = new[]
        {
            "Bagaimana kemampuan driver menjaga kecepatan yang aman selama perjalanan?",
            "Bagaimana kemampuan driver mengantisipasi kondisi jalan dan potensi bahaya?",
            "Bagaimana kemampuan driver melakukan pengereman dan akselerasi secara halus?"
        };

        public static readonly string[] DrivingSkill = new[]
        {
            "Bagaimana kemampuan driver menjaga fokus dan konsentrasi selama perjalanan?",
            "Bagaimana kemampuan driver mengendalikan kendaraan dalam berbagai kondisi jalan?"
        };

        public static readonly string[] Behavior = new[]
        {
            "Bagaimana sikap dan keramahan driver terhadap penumpang?",
            "Bagaimana kemampuan komunikasi driver selama perjalanan?"
        };

        public static readonly string[] ServiceQuality = new[]
        {
            "Bagaimana tingkat kebersihan dan kerapihan kendaraan?",
            "Bagaimana ketepatan waktu driver dalam penjemputan dan pengantaran?",
            "Bagaimana penilaian Anda terhadap performa driver secara keseluruhan?"
        };

        public class AssessmentItem
        {
            public int Id { get; set; }
            public string Question { get; set; } = string.Empty;
            public int Score { get; set; } = 5; // 1-5
            public string Label { get; set; } = "Sangat Baik";
        }

        private int? GetCompanyId()
        {
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(compIdStr, out int cid) && cid > 0) return cid;
            return null;
        }

        private static string NormalizeDriverName(string input)
        {
            return string.Join(" ", input.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        public async Task<IActionResult> Index()
        {
            // Access restriction: only company ID 1 (PT Indexim Coalindo)
            var companyId = GetCompanyId();
            if (companyId == null || companyId != 1)
            {
                TempData["ErrorMessage"] = "Fitur DPA hanya tersedia untuk PT Indexim Coalindo.";
                return RedirectToAction("Index", "Home");
            }

            ViewData["HeaderTitle"] = "Driver Performance Assessment";
            ViewData["ActiveTab"] = "Dpa";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");

            var query = _context.DpaReports.Where(r => !r.IsDeleted && r.PerusahaanId == 1);

            if (!isAdmin && !string.IsNullOrEmpty(userNik))
            {
                query = query.Where(r => r.AssessorNik == userNik);
            }

            var reports = await query
                .OrderByDescending(r => r.TanggalPenilaian)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserNik = userNik ?? "00000";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "General";
            ViewBag.CompanyId = companyId;

            return View(reports);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int? id,
            string driverNik, string driverNama, string? driverDepartemen,
            DateTime tanggalPenilaian, string jenisPerjalanan, string? rute, string? noLambung,
            string? keterangan)
        {
            try
            {
                var companyId = GetCompanyId();
                if (companyId == null || companyId != 1)
                {
                    return Forbid();
                }

                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "General";

                if (string.IsNullOrEmpty(driverNik) || string.IsNullOrEmpty(driverNama))
                {
                    TempData["ErrorMessage"] = "Data driver wajib diisi.";
                    return RedirectToAction(nameof(Index));
                }

                DpaReport? report;
                bool isNew = true;

                if (id.HasValue && id.Value > 0)
                {
                    report = await _context.DpaReports.FindAsync(id.Value);
                    if (report == null || report.IsDeleted) return NotFound();

                    if (report.AssessorNik != userNik && !User.IsInRole("Admin"))
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah penilaian ini.";
                        return RedirectToAction(nameof(Index));
                    }
                    isNew = false;
                }
                else
                {
                    report = new DpaReport
                    {
                        AssessorNik = userNik,
                        AssessorNama = userName,
                        AssessorDepartemen = userDept,
                        PerusahaanId = companyId,
                        CreatedAt = DateTime.Now
                    };
                }

                report.DriverNik = driverNik.Trim();
                report.DriverNama = NormalizeDriverName(driverNama);
                report.DriverDepartemen = driverDepartemen?.Trim();
                report.TanggalPenilaian = tanggalPenilaian.Date;
                report.JenisPerjalanan = jenisPerjalanan;
                report.Rute = rute?.Trim();
                report.NoLambung = noLambung?.Trim();
                report.Keterangan = keterangan?.Trim();

                if (report.DriverNik.Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedName = NormalizeDriverName(report.DriverNama);
                    var manualExists = await _context.DpaDrivers.AnyAsync(d =>
                        d.DriverNamaNormalized == normalizedName &&
                        d.PerusahaanId == 1);

                    if (!manualExists)
                    {
                        _context.DpaDrivers.Add(new DpaDriver
                        {
                            DriverNama = normalizedName,
                            DriverNamaNormalized = normalizedName,
                            PerusahaanId = 1,
                            CreatedByNik = userNik,
                            CreatedAt = DateTime.Now
                        });
                    }
                }

                // Parse assessment scores from form
                var safetyList = ParseCategory("safety", SafetyDriving);
                var skillList = ParseCategory("skill", DrivingSkill);
                var behaviorList = ParseCategory("behavior", Behavior);
                var serviceList = ParseCategory("service", ServiceQuality);

                report.SafetyDrivingJson = JsonSerializer.Serialize(safetyList);
                report.DrivingSkillJson = JsonSerializer.Serialize(skillList);
                report.BehaviorJson = JsonSerializer.Serialize(behaviorList);
                report.ServiceQualityJson = JsonSerializer.Serialize(serviceList);

                // Calculate passenger/user score (average of all items, scaled to 100)
                var allScores = safetyList.Concat(skillList).Concat(behaviorList).Concat(serviceList).ToList();
                double avgScore = allScores.Count > 0 ? allScores.Average(x => x.Score) : 3.0;
                report.ScorePenumpang = Math.Round((avgScore / 5.0) * 100, 1);

                // GPS and LenzGuard scores default to 0 until external integration
                // For now, final score = passenger score only (weighted at 40%, but since others are 0, we show raw)
                // When GPS/LenzGuard are integrated:
                // ScoreFinal = (ScorePenumpang * 0.4) + (ScoreGps * 0.3) + (ScoreLenzguard * 0.3)
                if (report.ScoreGps > 0 || report.ScoreLenzguard > 0)
                {
                    report.ScoreFinal = Math.Round(
                        (report.ScorePenumpang * 0.4) +
                        (report.ScoreGps * 0.3) +
                        (report.ScoreLenzguard * 0.3), 1);
                }
                else
                {
                    // When external data not available, use passenger score as final
                    report.ScoreFinal = report.ScorePenumpang;
                }

                // Determine category
                report.Kategori = report.ScoreFinal switch
                {
                    >= 90 => "Excellent Driver",
                    >= 80 => "Good Driver",
                    >= 70 => "Satisfactory Driver",
                    >= 60 => "Need Improvement",
                    _ => "Corrective Action Required"
                };

                if (isNew)
                    _context.DpaReports.Add(report);
                else
                    _context.DpaReports.Update(report);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = isNew
                    ? "Penilaian Driver berhasil disimpan!"
                    : "Penilaian Driver berhasil diperbarui!";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving DPA report");
                TempData["ErrorMessage"] = "Terjadi kesalahan saat menyimpan data penilaian driver.";
            }

            return RedirectToAction(nameof(Index));
        }

        private List<AssessmentItem> ParseCategory(string prefix, string[] questions)
        {
            var list = new List<AssessmentItem>();
            for (int i = 0; i < questions.Length; i++)
            {
                var scoreStr = Request.Form[$"{prefix}_score_{i}"].ToString();
                int score = 5;
                if (int.TryParse(scoreStr, out int parsed) && parsed >= 1 && parsed <= 5)
                    score = parsed;

                string label = score switch
                {
                    5 => "Sangat Baik",
                    4 => "Baik",
                    3 => "Cukup",
                    2 => "Kurang",
                    1 => "Sangat Kurang",
                    _ => "Sangat Baik"
                };

                list.Add(new AssessmentItem
                {
                    Id = i + 1,
                    Question = questions[i],
                    Score = score,
                    Label = label
                });
            }
            return list;
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var report = await _context.DpaReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            return Json(new
            {
                id = report.Id,
                assessorNik = report.AssessorNik,
                assessorNama = report.AssessorNama,
                driverNik = report.DriverNik,
                driverNama = report.DriverNama,
                driverDepartemen = report.DriverDepartemen,
                tanggalPenilaian = report.TanggalPenilaian.ToString("yyyy-MM-dd"),
                jenisPerjalanan = report.JenisPerjalanan,
                rute = report.Rute,
                noLambung = report.NoLambung,
                keterangan = report.Keterangan,
                scorePenumpang = report.ScorePenumpang,
                scoreGps = report.ScoreGps,
                scoreLenzguard = report.ScoreLenzguard,
                scoreFinal = report.ScoreFinal,
                kategori = report.Kategori,
                safety = string.IsNullOrEmpty(report.SafetyDrivingJson) ? new List<AssessmentItem>() : JsonSerializer.Deserialize<List<AssessmentItem>>(report.SafetyDrivingJson),
                skill = string.IsNullOrEmpty(report.DrivingSkillJson) ? new List<AssessmentItem>() : JsonSerializer.Deserialize<List<AssessmentItem>>(report.DrivingSkillJson),
                behavior = string.IsNullOrEmpty(report.BehaviorJson) ? new List<AssessmentItem>() : JsonSerializer.Deserialize<List<AssessmentItem>>(report.BehaviorJson),
                service = string.IsNullOrEmpty(report.ServiceQualityJson) ? new List<AssessmentItem>() : JsonSerializer.Deserialize<List<AssessmentItem>>(report.ServiceQualityJson)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var report = await _context.DpaReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (report.AssessorNik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            report.IsDeleted = true;
            _context.DpaReports.Update(report);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Penilaian Driver berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SearchDriver(string query)
        {
            if (string.IsNullOrEmpty(query) || query.Length < 2)
                return Json(new List<object>());

            var q = query.Trim().ToLower();

            var sapResults = await (from k in _context.Karyawans
                                    join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                    join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                    from dept in dg.DefaultIfEmpty()
                                    where k.IdPerusahaan == 1 &&
                                          k.StatusAktif == true &&
                                          (k.NoNik.ToLower().Contains(q) ||
                                           p.NamaLengkap.ToLower().Contains(q))
                                    select new
                                    {
                                        nik = k.NoNik,
                                        nama = p.NamaLengkap,
                                        departemen = dept != null ? dept.NamaDepartemen : "General"
                                    })
                                   .Take(15)
                                   .ToListAsync();

            var manualResults = await _context.DpaDrivers
                .Where(d => d.PerusahaanId == 1 && d.DriverNamaNormalized.ToLower().Contains(q))
                .Select(d => new
                {
                    nik = "MANUAL",
                    nama = d.DriverNama,
                    departemen = "MASTER DRIVER"
                })
                .Take(15)
                .ToListAsync();

            var combined = sapResults
                .Concat(manualResults)
                .GroupBy(x => NormalizeDriverName(x.nama))
                .Select(g => g.OrderBy(x => x.nik == "MANUAL" ? 1 : 0).First())
                .Take(15)
                .ToList();

            return Json(combined);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDriver(string driverNama)
        {
            var companyId = GetCompanyId();
            if (companyId == null || companyId != 1)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(driverNama))
            {
                return Json(new { success = false, message = "Nama driver wajib diisi." });
            }

            var normalizedName = NormalizeDriverName(driverNama);
            if (normalizedName.Length < 2)
            {
                return Json(new { success = false, message = "Nama driver minimal 2 karakter." });
            }

            var exists = await _context.DpaDrivers.AnyAsync(d =>
                d.DriverNamaNormalized == normalizedName &&
                d.PerusahaanId == 1);

            if (exists)
            {
                return Json(new { success = false, message = "Nama driver sudah terdaftar." });
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";

            var newDriver = new DpaDriver
            {
                DriverNama = normalizedName,
                DriverNamaNormalized = normalizedName,
                PerusahaanId = 1,
                CreatedByNik = userNik,
                CreatedAt = DateTime.Now
            };

            _context.DpaDrivers.Add(newDriver);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Driver berhasil ditambahkan.",
                data = new
                {
                    nik = "MANUAL",
                    nama = newDriver.DriverNama,
                    departemen = "MASTER DRIVER"
                }
            });
        }
    }
}
