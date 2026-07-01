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
    public class SafetyTalkController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ExcelService _excelService;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public SafetyTalkController(AppDbContext context, IWebHostEnvironment webHostEnvironment, ExcelService excelService, MBS_SAP.Services.ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _excelService = excelService;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        // GET: SafetyTalk
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Safety Talk & Briefing";
            ViewData["ActiveTab"] = "SafetyTalk";

            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(companyIdStr, out var cid) && cid > 0 ? cid : null;

            var query = _context.SafetyTalks.Where(s => !s.IsDeleted);

            // Filter berdasarkan hierarki perusahaan (berlaku untuk Admin maupun non-Admin)
            if (companyId.HasValue)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(companyId.Value);
                query = query.Where(s => s.PerusahaanId.HasValue && allowedIds.Contains(s.PerusahaanId.Value));
            }

            var reports = await query
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        // POST: SafetyTalk/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            int? id,
            DateTime tanggal,
            string waktuStr,
            string? area,
            string? lokasi,
            string? detilLokasi,
            string? judul,
            string? keterangan,
            IFormFile? fotoDiri,
            IFormFile? fotoKegiatan)
        {
            if (string.IsNullOrEmpty(judul))
            {
                TempData["ErrorMessage"] = "Judul Safety Talk wajib diisi!";
                return RedirectToAction(nameof(Index));
            }

            TimeSpan waktu = DateTime.Now.TimeOfDay;
            if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
            {
                waktu = parsedWaktu;
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            var userName = User.Identity?.Name ?? "Anonymous";
            var userDept = User.FindFirst("Department")?.Value ?? "General";

            SafetyTalk? talk;
            bool isNew = true;

            if (id.HasValue && id.Value > 0)
            {
                talk = await _context.SafetyTalks.FindAsync(id.Value);
                if (talk == null || talk.IsDeleted) return NotFound();

                if (talk.Nik != userNik && !User.IsInRole("Admin"))
                {
                    TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan ini.";
                    return RedirectToAction(nameof(Index));
                }
                isNew = false;
            }
            else
            {
                talk = new SafetyTalk
                {
                    Nama = userName,
                    Nik = userNik,
                    Departemen = userDept,
                    CreatedAt = DateTime.Now
                };
            }

            talk.Tanggal = tanggal == default ? DateTime.Today : tanggal;
            talk.Waktu = waktu;
            talk.Area = area;
            talk.Lokasi = lokasi;
            talk.DetilLokasi = detilLokasi;
            talk.Judul = judul;
            talk.Keterangan = keterangan;

            // Guard backend against near-duplicate submit (double-click / retry) for new records.
            if (isNew)
            {
                var duplicateWindowStart = DateTime.Now.AddSeconds(-20);
                var normalizedArea = (talk.Area ?? string.Empty).Trim();
                var normalizedLokasi = (talk.Lokasi ?? string.Empty).Trim();
                var normalizedJudul = (talk.Judul ?? string.Empty).Trim();

                var duplicatedTalk = await _context.SafetyTalks
                    .AsNoTracking()
                    .Where(s => !s.IsDeleted
                                && s.Nik == userNik
                                && s.CreatedAt >= duplicateWindowStart)
                    .FirstOrDefaultAsync(s => (s.Area ?? string.Empty).Trim() == normalizedArea
                                           && (s.Lokasi ?? string.Empty).Trim() == normalizedLokasi
                                           && (s.Judul ?? string.Empty).Trim() == normalizedJudul);

                if (duplicatedTalk != null)
                {
                    TempData["WarningMessage"] = "Data Safety Talk yang sama terdeteksi terkirim dua kali. Sistem hanya menyimpan satu data.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Handle Foto Diri Upload
            if (fotoDiri != null && fotoDiri.Length > 0)
            {
                try
                {
                    talk.FotoDiri = await _imageUploadService.UploadAndCompressImageAsync(fotoDiri, "safetytalks", "diri");
                }
                catch (Exception)
                {
                    talk.FotoDiri = null;
                }
            }

            // Handle Foto Kegiatan Upload
            if (fotoKegiatan != null && fotoKegiatan.Length > 0)
            {
                try
                {
                    talk.FotoKegiatan = await _imageUploadService.UploadAndCompressImageAsync(fotoKegiatan, "safetytalks", "keg");
                }
                catch (Exception)
                {
                    talk.FotoKegiatan = null;
                }
            }

            if (isNew)
            {
                _context.SafetyTalks.Add(talk);
            }
            else
            {
                _context.SafetyTalks.Update(talk);
            }
            await _context.SaveChangesAsync();

            // Append to Excel Sheet D:\SAP.xlsx
            try
            {
                if (isNew)
                {
                    _excelService.AppendSafetyTalk(talk);
                }
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "Data disimpan di database, tetapi gagal ditulis ke Excel: " + ex.Message;
            }

            TempData["SuccessMessage"] = isNew ? "Laporan Safety Talk berhasil disimpan!" : "Laporan Safety Talk berhasil diperbarui!";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var talk = await _context.SafetyTalks.FindAsync(id);
            if (talk == null || talk.IsDeleted) return NotFound();
            return Json(talk);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var talk = await _context.SafetyTalks.FindAsync(id);
            if (talk == null || talk.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (talk.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            talk.IsDeleted = true;
            _context.SafetyTalks.Update(talk);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Safety Talk berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }
    }
}
