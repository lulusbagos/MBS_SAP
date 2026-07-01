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
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class P5mController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ExcelService _excelService;
        private readonly CompanyHierarchyService _companyHierarchyService;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;

        public P5mController(AppDbContext context, IWebHostEnvironment webHostEnvironment, ExcelService excelService, CompanyHierarchyService companyHierarchyService, MBS_SAP.Services.ImageUploadService imageUploadService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _excelService = excelService;
            _companyHierarchyService = companyHierarchyService;
            _imageUploadService = imageUploadService;
        }

        // GET: P5m
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "P5M & Kelayakan Kerja";
            ViewData["ActiveTab"] = "P5m";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(companyIdStr, out var cid) && cid > 0 ? cid : null;

            IQueryable<P5m> query = _context.P5ms.Where(p => !p.IsDeleted);

            // Filter berdasarkan perusahaan (berlaku untuk Admin maupun non-Admin)
            if (companyId.HasValue)
            {
                if (isAdmin)
                {
                    // Admin melihat semua P5M milik perusahaannya
                    query = query.Where(p => p.PerusahaanId.HasValue && p.PerusahaanId.Value == companyId.Value);
                }
                else
                {
                    // Non-Admin hanya melihat miliknya sendiri
                    query = query.Where(p => p.Nik == userNik);
                }
            }
            else
            {
                // Jika user tidak punya CompanyId claim, tampilkan kosong
                query = query.Where(p => false);
            }

            var reports = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        // POST: P5m/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            int? id,
            DateTime tanggal,
            string waktuStr,
            string? area,
            string? lokasi,
            string? detilLokasi,
            string? topik,
            string? judul,
            string? keterangan,
            List<string> questions,
            List<string> answers,
            string? catatan,
            IFormFile? fotoKegiatan)
        {
            if (!id.HasValue || id.Value == 0)
            {
                if (questions == null || questions.Count == 0)
                {
                    TempData["ErrorMessage"] = "Kolom checklist P5M wajib diisi!";
                    return RedirectToAction(nameof(Index));
                }
            }

            TimeSpan waktu = DateTime.Now.TimeOfDay;
            if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
            {
                waktu = parsedWaktu;
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            var userName = User.Identity?.Name ?? "Anonymous";
            var userDept = User.FindFirst("Department")?.Value ?? "General";

            string? photoPath = null;

            // Handle Foto Kegiatan Upload once
            if (fotoKegiatan != null && fotoKegiatan.Length > 0)
            {
                try
                {
                    photoPath = await _imageUploadService.UploadAndCompressImageAsync(fotoKegiatan, "p5ms");
                }
                catch (Exception)
                {
                    photoPath = null;
                }
            }

            if (id.HasValue && id.Value > 0)
            {
                var originalRecord = await _context.P5ms.FindAsync(id.Value);
                if (originalRecord == null || originalRecord.IsDeleted) return NotFound();

                if (originalRecord.Nik != userNik && !User.IsInRole("Admin"))
                {
                    TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan ini.";
                    return RedirectToAction(nameof(Index));
                }

                // Update all records in the group
                var groupRecords = await _context.P5ms
                    .Where(p => p.Tanggal == originalRecord.Tanggal && p.Waktu == originalRecord.Waktu && p.Nik == originalRecord.Nik)
                    .ToListAsync();

                foreach (var rec in groupRecords)
                {
                    rec.Tanggal = tanggal == default ? DateTime.Today : tanggal;
                    rec.Waktu = waktu;
                    rec.Area = area;
                    rec.Lokasi = lokasi;
                    rec.DetilLokasi = detilLokasi;
                    rec.Topik = topik ?? "Pekerjaan Umum";
                    rec.Judul = judul ?? "Siap Bekerja";
                    rec.Keterangan = keterangan ?? ".";
                    rec.Catatan = catatan;
                    
                    if (photoPath != null)
                    {
                        rec.FotoKegiatan = photoPath;
                    }

                    _context.P5ms.Update(rec);
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Laporan P5M berhasil diperbarui!";
            }
            else
            {
                // Loop and save each question response
                for (int i = 0; i < questions.Count; i++)
                {
                    var q = questions[i];
                    var a = i < answers.Count ? answers[i] : "No";

                    var companyIdStr = User.FindFirst("CompanyId")?.Value;
                    int? companyId = int.TryParse(companyIdStr, out var cid) && cid > 0 ? cid : null;

                    var p5mRecord = new P5m
                    {
                        FotoKegiatan = photoPath,
                        Tanggal = tanggal == default ? DateTime.Today : tanggal,
                        Waktu = waktu,
                        Nama = userName,
                        Nik = userNik,
                        Departemen = userDept,
                        Area = area,
                        Lokasi = lokasi,
                        DetilLokasi = detilLokasi,
                        Topik = topik ?? "Pekerjaan Umum",
                        Judul = judul ?? "Siap Bekerja",
                        Keterangan = keterangan ?? ".",
                        ListPertanyaan = q,
                        Jawaban = a,
                        Catatan = catatan,
                        CreatedAt = DateTime.Now,
                        PerusahaanId = companyId
                    };

                    _context.P5ms.Add(p5mRecord);
                    await _context.SaveChangesAsync();

                    // Append to Excel Sheet D:\SAP.xlsx
                    try
                    {
                        _excelService.AppendP5m(p5mRecord);
                    }
                    catch (Exception ex)
                    {
                        TempData["WarningMessage"] = "Data disimpan di database, tetapi gagal ditulis ke Excel: " + ex.Message;
                    }
                }
                TempData["SuccessMessage"] = "Laporan P5M berhasil disimpan!";
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var p5m = await _context.P5ms.FindAsync(id);
            if (p5m == null || p5m.IsDeleted) return NotFound();

            // Validasi akses company
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(companyIdStr, out var cid) && cid > 0 ? cid : null;

            if (!userCompanyId.HasValue && !User.IsInRole("Admin"))
                return Unauthorized();

            // Jika user punya CompanyId, validasi akses perusahaan
            if (userCompanyId.HasValue)
            {
                if (!p5m.PerusahaanId.HasValue)
                    return Unauthorized();

                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                bool isUserInvolved = p5m.Nik == userNik;

                if (!isUserInvolved && p5m.PerusahaanId.Value != userCompanyId.Value)
                    return Unauthorized();
            }

            return Json(p5m);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var p5m = await _context.P5ms.FindAsync(id);
            if (p5m == null || p5m.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (p5m.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            // Soft Delete all records in the group
            var groupRecords = await _context.P5ms
                .Where(p => p.Tanggal == p5m.Tanggal && p.Waktu == p5m.Waktu && p.Nik == p5m.Nik)
                .ToListAsync();

            foreach (var rec in groupRecords)
            {
                rec.IsDeleted = true;
                _context.P5ms.Update(rec);
            }
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "P5M berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }
    }
}
