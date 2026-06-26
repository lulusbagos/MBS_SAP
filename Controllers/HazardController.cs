using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class HazardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ExcelService _excelService;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;

        public HazardController(AppDbContext context, IWebHostEnvironment webHostEnvironment, ExcelService excelService, MBS_SAP.Services.ImageUploadService imageUploadService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _excelService = excelService;
            _imageUploadService = imageUploadService;
        }

        // GET: Hazard
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Temuan Hazard";
            ViewData["ActiveTab"] = "Hazard";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");

            var query = _context.HazardReports.Where(r => !r.IsDeleted);

            if (!isAdmin && !string.IsNullOrEmpty(userNik))
            {
                // Only show hazards created by this user or assigned to this user (PJA)
                query = query.Where(r => r.Nik == userNik || r.NikPja == userNik);
            }

            var reports = await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        // POST: Hazard/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            int? id,
            DateTime tanggal,
            string waktuStr,
            string? area,
            string? lokasi,
            string? detilLokasi,
            string temuan,
            string? kategoriBahaya,
            string? jenisBahaya,
            string? jenisKetidaksesuaian,
            string? tingkatResiko,
            string? perbaikan,
            string? tindakanPerbaikan,
            string? pja,
            string? nikPja,
            string? departemenPja,
            IFormFile? fotoTemuan)
        {
            if (string.IsNullOrEmpty(temuan))
            {
                TempData["ErrorMessage"] = "Kolom Detil Temuan wajib diisi!";
                return RedirectToAction(nameof(Index));
            }

            // Parse time
            TimeSpan waktu = DateTime.Now.TimeOfDay;
            if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
            {
                waktu = parsedWaktu;
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            var userName = User.Identity?.Name ?? "Anonymous";
            var userDept = User.FindFirst("Department")?.Value ?? "General";

            HazardReport? report;
            bool isNew = true;

            if (id.HasValue && id.Value > 0)
            {
                report = await _context.HazardReports.FindAsync(id.Value);
                if (report == null || report.IsDeleted) return NotFound();
                
                // Optional: Check if user is Admin or the creator
                if (report.Nik != userNik && !User.IsInRole("Admin"))
                {
                    TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan ini.";
                    return RedirectToAction(nameof(Index));
                }
                isNew = false;
            }
            else
            {
                report = new HazardReport
                {
                    Nama = userName,
                    Nik = userNik,
                    Departemen = userDept,
                    StatusTemuan = "Open",
                    CreatedAt = DateTime.Now
                };
            }

            report.Tanggal = tanggal == default ? DateTime.Today : tanggal;
            report.Waktu = waktu;
            report.Area = area;
            report.Lokasi = lokasi;
            report.DetilLokasi = detilLokasi;
            report.Temuan = temuan;
            report.KategoriBahaya = kategoriBahaya;
            report.JenisBahaya = jenisBahaya;
            report.JenisKetidaksesuaian = jenisKetidaksesuaian;
            report.TingkatResiko = tingkatResiko;
            report.Perbaikan = perbaikan;
            report.TindakanPerbaikan = tindakanPerbaikan;
            report.Pja = pja;
            report.NikPja = nikPja;
            report.DepartemenPja = departemenPja;

            // Handle Photo Upload
            if (fotoTemuan != null && fotoTemuan.Length > 0)
            {
                try
                {
                    report.FotoTemuan = await _imageUploadService.UploadAndCompressImageAsync(fotoTemuan, "hazards");
                }
                catch (Exception)
                {
                    report.FotoTemuan = null;
                }
            }

            // Save to SQL Database
            if (isNew)
            {
                _context.HazardReports.Add(report);
            }
            else
            {
                _context.HazardReports.Update(report);
            }
            await _context.SaveChangesAsync();

            // Sync with ActionPlan
            if (!string.IsNullOrEmpty(report.NikPja))
            {
                var actionPlanItemSap = $"hazard:{report.Id}";
                var actionPlan = await _context.ActionPlans.FirstOrDefaultAsync(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted);

                if (actionPlan == null)
                {
                    actionPlan = new ActionPlan
                    {
                        Tanggal = report.Tanggal,
                        Waktu = report.Waktu,
                        Nama = report.Nama,
                        Nik = report.Nik,
                        Departemen = report.Departemen,
                        Area = report.Area,
                        Lokasi = report.Lokasi,
                        DetilLokasi = report.DetilLokasi,
                        ItemSap = actionPlanItemSap,
                        KategoriTemuan = report.KategoriBahaya,
                        DetilTemuan = report.Temuan,
                        Status = report.StatusTemuan,
                        Pja = report.Pja,
                        NikPja = report.NikPja,
                        DepartemenPja = report.DepartemenPja,
                        RencanaPerbaikan = report.TindakanPerbaikan,
                        FotoTemuan = report.FotoTemuan,
                        CreatedAt = DateTime.Now
                    };
                    _context.ActionPlans.Add(actionPlan);
                }
                else
                {
                    actionPlan.Tanggal = report.Tanggal;
                    actionPlan.Waktu = report.Waktu;
                    actionPlan.Area = report.Area;
                    actionPlan.Lokasi = report.Lokasi;
                    actionPlan.DetilLokasi = report.DetilLokasi;
                    actionPlan.KategoriTemuan = report.KategoriBahaya;
                    actionPlan.DetilTemuan = report.Temuan;
                    actionPlan.Pja = report.Pja;
                    actionPlan.NikPja = report.NikPja;
                    actionPlan.DepartemenPja = report.DepartemenPja;
                    actionPlan.RencanaPerbaikan = report.TindakanPerbaikan;
                    actionPlan.FotoTemuan = report.FotoTemuan;
                    actionPlan.Status = report.StatusTemuan;
                    _context.ActionPlans.Update(actionPlan);
                }
                await _context.SaveChangesAsync();
            }
            else
            {
                var actionPlanItemSap = $"hazard:{report.Id}";
                var actionPlan = await _context.ActionPlans.FirstOrDefaultAsync(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted);
                if (actionPlan != null)
                {
                    actionPlan.IsDeleted = true;
                    _context.ActionPlans.Update(actionPlan);
                    await _context.SaveChangesAsync();
                }
            }

            // Notify PJA if new or if PJA changed/assigned
            bool pjaAssignedOrChanged = false;
            if (isNew && !string.IsNullOrEmpty(report.NikPja))
            {
                pjaAssignedOrChanged = true;
            }
            else if (!isNew && !string.IsNullOrEmpty(report.NikPja))
            {
                var originalReport = await _context.HazardReports.AsNoTracking().FirstOrDefaultAsync(h => h.Id == report.Id);
                if (originalReport != null && originalReport.NikPja != report.NikPja)
                {
                    pjaAssignedOrChanged = true;
                }
            }

            if (pjaAssignedOrChanged)
            {
                var notif = new Notification
                {
                    RecipientNik = report.NikPja!,
                    Title = "Temuan Hazard Baru",
                    Message = $"Anda ditunjuk sebagai PJA untuk temuan Hazard di {report.Lokasi ?? report.Area} oleh {report.Nama}.",
                    Url = "/ActionPlan/Index"
                };
                _context.Notifications.Add(notif);
                await _context.SaveChangesAsync();
            }

            // Append to Excel D:\SAP.xlsx
            try
            {
                _excelService.AppendHazardReport(report);
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "Data disimpan di database, tetapi gagal ditulis ke Excel: " + ex.Message;
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var report = await _context.HazardReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();
            return Json(report);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var report = await _context.HazardReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (report.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            report.IsDeleted = true;
            _context.HazardReports.Update(report);

            var actionPlanItemSap = $"hazard:{report.Id}";
            var actionPlan = await _context.ActionPlans.FirstOrDefaultAsync(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted);
            if (actionPlan != null)
            {
                actionPlan.IsDeleted = true;
                _context.ActionPlans.Update(actionPlan);
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan hazard berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id, string closeMode, string? closeNote)
        {
            var report = await _context.HazardReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            var userName = User.Identity?.Name ?? "Anonymous";
            var userDept = User.FindFirst("Department")?.Value ?? "General";

            if (report.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            if (string.Equals(report.StatusTemuan, "Closed", StringComparison.OrdinalIgnoreCase))
            {
                TempData["WarningMessage"] = "Laporan hazard sudah berstatus Closed.";
                return RedirectToAction(nameof(Index));
            }

            closeMode = (closeMode ?? string.Empty).Trim().ToLowerInvariant();
            if (closeMode != "self" && closeMode != "pja")
            {
                TempData["ErrorMessage"] = "Mode close hazard tidak valid.";
                return RedirectToAction(nameof(Index));
            }

            if (closeMode == "pja")
            {
                if (string.IsNullOrWhiteSpace(report.NikPja) || string.IsNullOrWhiteSpace(report.Pja))
                {
                    TempData["ErrorMessage"] = "PJA belum ditentukan. Silakan edit laporan dan pilih PJA terlebih dahulu.";
                    return RedirectToAction(nameof(Index));
                }

                var actionPlanItemSap = $"hazard:{report.Id}";
                var actionPlan = await _context.ActionPlans.FirstOrDefaultAsync(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted);

                if (actionPlan == null)
                {
                    actionPlan = new ActionPlan
                    {
                        Tanggal = DateTime.Today,
                        Waktu = DateTime.Now.TimeOfDay,
                        Nama = userName,
                        Nik = userNik,
                        Departemen = userDept,
                        Area = report.Area,
                        Lokasi = report.Lokasi,
                        DetilLokasi = report.DetilLokasi,
                        ItemSap = actionPlanItemSap,
                        KategoriTemuan = report.KategoriBahaya,
                        DetilTemuan = report.Temuan,
                        Status = "Open",
                        Pja = report.Pja,
                        NikPja = report.NikPja,
                        DepartemenPja = report.DepartemenPja,
                        RencanaPerbaikan = string.IsNullOrWhiteSpace(closeNote) ? report.TindakanPerbaikan : closeNote.Trim(),
                        CreatedAt = DateTime.Now
                    };
                    _context.ActionPlans.Add(actionPlan);
                }
                else
                {
                    actionPlan.Status = "Open";
                    actionPlan.RencanaPerbaikan = string.IsNullOrWhiteSpace(closeNote) ? report.TindakanPerbaikan : closeNote.Trim();
                    _context.ActionPlans.Update(actionPlan);
                }

                var notif = new Notification
                {
                    RecipientNik = report.NikPja,
                    Title = "Action Plan Hazard Baru",
                    Message = $"Anda menerima tindak lanjut hazard dari {report.Nama} di {report.Lokasi ?? report.Area}.",
                    Url = "/ActionPlan/Index"
                };
                _context.Notifications.Add(notif);

                try
                {
                    _excelService.AppendActionPlan(actionPlan);
                }
                catch (Exception)
                {
                    // Fail silently for secondary Excel write.
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(closeNote))
                {
                    report.Perbaikan = string.IsNullOrWhiteSpace(report.Perbaikan)
                        ? closeNote.Trim()
                        : $"{report.Perbaikan}{Environment.NewLine}{closeNote.Trim()}";
                }

                var actionPlanItemSap = $"hazard:{report.Id}";
                var actionPlan = await _context.ActionPlans.FirstOrDefaultAsync(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted);
                if (actionPlan != null)
                {
                    actionPlan.Status = "Closed";
                    actionPlan.Perbaikan = string.IsNullOrWhiteSpace(closeNote) ? "Diselesaikan sendiri oleh pelapor." : closeNote.Trim();
                    actionPlan.TanggalPerbaikan = DateTime.Today;
                    _context.ActionPlans.Update(actionPlan);
                }
            }

            report.StatusTemuan = "Closed";
            _context.HazardReports.Update(report);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = closeMode == "pja"
                ? "Hazard berhasil di-close dan dikirim ke PJA sebagai Action Plan."
                : "Hazard berhasil di-close dengan penyelesaian sendiri.";

            return RedirectToAction(nameof(Index));
        }
    }
}
