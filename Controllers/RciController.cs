using ClosedXML.Excel;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class RciController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public RciController(AppDbContext context, ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        public async Task<IActionResult> Index(string? search, string? namaJalan, string? kategori, DateTime? startDate, DateTime? endDate)
        {
            // Access control: only PT Mega Global Energy and subsidiaries (or Admin)
            if (!CompanyHierarchyService.IsMgeGroupUser(User))
            {
                TempData["ErrorMessage"] = "Fitur Road Condition Index (RCI) hanya dapat diakses oleh PT Mega Global Energy dan anak perusahaannya.";
                return RedirectToAction("Index", "Home");
            }

            ViewData["HeaderTitle"] = "Road Condition Index (RCI)";
            ViewData["ActiveTab"] = "Rci";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;

            var query = _context.RciReports.Where(r => !r.IsDeleted);

            // Filter date range (default: 30 days)
            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = (endDate ?? DateTime.Today).AddDays(1).AddTicks(-1);

            query = query.Where(r => r.Tanggal >= start && r.Tanggal <= end);

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(r => r.Nama.ToLower().Contains(s) ||
                                         r.Nik.ToLower().Contains(s) ||
                                         r.Lokasi.ToLower().Contains(s) ||
                                         (r.NamaJalan != null && r.NamaJalan.ToLower().Contains(s)) ||
                                         (r.SegmentJalan != null && r.SegmentJalan.ToLower().Contains(s)));
            }

            if (!string.IsNullOrEmpty(namaJalan))
            {
                query = query.Where(r => r.NamaJalan == namaJalan);
            }

            if (!string.IsNullOrEmpty(kategori))
            {
                query = query.Where(r => r.KategoriIndex == kategori);
            }

            var reports = await query.OrderByDescending(r => r.Tanggal).ThenByDescending(r => r.CreatedAt).ToListAsync();

            // Statistics
            var allMtdReports = await _context.RciReports
                .Where(r => !r.IsDeleted && r.Tanggal >= DateTime.Today.AddDays(-30))
                .ToListAsync();

            ViewBag.TotalReports = allMtdReports.Count;
            ViewBag.AvgScore = allMtdReports.Any() ? Math.Round(allMtdReports.Average(r => r.TotalScore), 1) : 100.0;
            ViewBag.GoodCount = allMtdReports.Count(r => r.KategoriIndex == "Baik");
            ViewBag.FairCount = allMtdReports.Count(r => r.KategoriIndex == "Sedang");
            ViewBag.PoorCount = allMtdReports.Count(r => r.KategoriIndex == "Kurang");

            // Distinct roads for filter
            ViewBag.JalanList = await _context.RciReports
                .Where(r => !r.IsDeleted && !string.IsNullOrEmpty(r.NamaJalan))
                .Select(r => r.NamaJalan!)
                .Distinct()
                .ToListAsync();

            // Areas
            ViewBag.AreaList = await _context.MasterAreas
                .OrderBy(a => a.NamaArea)
                .Select(a => a.NamaArea)
                .Distinct()
                .ToListAsync();

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = (endDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            ViewBag.CurrentSearch = search ?? "";
            ViewBag.CurrentJalan = namaJalan ?? "";
            ViewBag.CurrentKategori = kategori ?? "";
            ViewBag.UserNik = userNik;
            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "Operations";
            ViewBag.UserCompany = User.FindFirst("Company")?.Value ?? "PT MEGA GLOBAL ENERGY";

            return View(reports);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            int? id,
            DateTime tanggal,
            string waktuStr,
            string area,
            string lokasi,
            string? detilLokasi,
            string? namaJalan,
            string? segmentJalan,
            string? shift,
            string? latitude,
            string? longitude,
            int skorLebarJalan,
            int skorGradeJalan,
            int skorPermukaanJalan,
            int skorSafetyBerm,
            int skorDrainaseParit,
            int skorSuperelevasiTikungan,
            int skorBebasSpillage,
            int skorRambuDebu,
            string? catatan,
            string? tindakanPerbaikan,
            string? pic,
            IFormFile? foto)
        {
            if (!CompanyHierarchyService.IsMgeGroupUser(User))
            {
                TempData["ErrorMessage"] = "Akses ditolak.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "Operations";
                var userComp = User.FindFirst("Company")?.Value ?? "PT MEGA GLOBAL ENERGY";
                int.TryParse(User.FindFirst("CompanyId")?.Value, out int compId);

                TimeSpan waktu = DateTime.Now.TimeOfDay;
                if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
                {
                    waktu = parsedWaktu;
                }

                // Weighted calculation:
                // Lebar Jalan (15%), Grade Jalan (10%), Permukaan Jalan (15%), Safety Berm (15%),
                // Drainase Parit (15%), Superelevasi Tikungan (10%), Bebas Spillage (10%), Rambu & Debu (10%)
                double totalScore = (skorLebarJalan * 0.15) +
                                    (skorGradeJalan * 0.10) +
                                    (skorPermukaanJalan * 0.15) +
                                    (skorSafetyBerm * 0.15) +
                                    (skorDrainaseParit * 0.15) +
                                    (skorSuperelevasiTikungan * 0.10) +
                                    (skorBebasSpillage * 0.10) +
                                    (skorRambuDebu * 0.10);
                totalScore = Math.Round(totalScore, 1);

                string kategoriIndex = totalScore >= 85 ? "Baik" : (totalScore >= 70 ? "Sedang" : "Kurang");

                string? fotoUrl = null;
                if (foto != null && foto.Length > 0)
                {
                    fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "rci");
                }

                if (id.HasValue && id.Value > 0)
                {
                    var existing = await _context.RciReports.FindAsync(id.Value);
                    if (existing == null || existing.IsDeleted) return NotFound();

                    if (existing.Nik != userNik && !User.IsInRole("Admin"))
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki hak untuk mengedit data ini.";
                        return RedirectToAction(nameof(Index));
                    }

                    existing.Tanggal = tanggal.Date;
                    existing.Waktu = waktu;
                    existing.Area = area;
                    existing.Lokasi = lokasi;
                    existing.DetilLokasi = detilLokasi;
                    existing.NamaJalan = namaJalan;
                    existing.SegmentJalan = segmentJalan;
                    existing.Shift = shift;
                    existing.Latitude = latitude;
                    existing.Longitude = longitude;
                    existing.SkorLebarJalan = skorLebarJalan;
                    existing.SkorGradeJalan = skorGradeJalan;
                    existing.SkorPermukaanJalan = skorPermukaanJalan;
                    existing.SkorSafetyBerm = skorSafetyBerm;
                    existing.SkorDrainaseParit = skorDrainaseParit;
                    existing.SkorSuperelevasiTikungan = skorSuperelevasiTikungan;
                    existing.SkorBebasSpillage = skorBebasSpillage;
                    existing.SkorRambuDebu = skorRambuDebu;
                    existing.TotalScore = totalScore;
                    existing.KategoriIndex = kategoriIndex;
                    existing.Catatan = catatan;
                    existing.TindakanPerbaikan = tindakanPerbaikan;
                    existing.Pic = pic;
                    existing.UpdatedAt = DateTime.Now;

                    if (!string.IsNullOrEmpty(fotoUrl))
                    {
                        existing.FotoUrl = fotoUrl;
                    }

                    _context.RciReports.Update(existing);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Laporan RCI di {namaJalan ?? lokasi} berhasil diperbarui! Skor Index: {totalScore}% ({kategoriIndex})";
                }
                else
                {
                    var report = new RciReport
                    {
                        Tanggal = tanggal.Date,
                        Waktu = waktu,
                        Nik = userNik,
                        Nama = userName,
                        Departemen = userDept,
                        Perusahaan = userComp,
                        PerusahaanId = compId > 0 ? compId : (int?)null,
                        Area = area,
                        Lokasi = lokasi,
                        DetilLokasi = detilLokasi,
                        NamaJalan = namaJalan,
                        SegmentJalan = segmentJalan,
                        Shift = shift,
                        Latitude = latitude,
                        Longitude = longitude,
                        SkorLebarJalan = skorLebarJalan,
                        SkorGradeJalan = skorGradeJalan,
                        SkorPermukaanJalan = skorPermukaanJalan,
                        SkorSafetyBerm = skorSafetyBerm,
                        SkorDrainaseParit = skorDrainaseParit,
                        SkorSuperelevasiTikungan = skorSuperelevasiTikungan,
                        SkorBebasSpillage = skorBebasSpillage,
                        SkorRambuDebu = skorRambuDebu,
                        TotalScore = totalScore,
                        KategoriIndex = kategoriIndex,
                        Catatan = catatan,
                        TindakanPerbaikan = tindakanPerbaikan,
                        Pic = pic,
                        FotoUrl = fotoUrl,
                        CreatedAt = DateTime.Now
                    };

                    _context.RciReports.Add(report);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Inspeksi RCI berhasil disimpan! Skor Index: {totalScore}% ({kategoriIndex})";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Gagal menyimpan inspeksi RCI: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetail(int id)
        {
            var item = await _context.RciReports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
            if (item == null) return NotFound();

            return Json(new
            {
                id = item.Id,
                tanggal = item.Tanggal.ToString("yyyy-MM-dd"),
                waktu = item.Waktu.ToString(@"hh\:mm"),
                nik = item.Nik,
                nama = item.Nama,
                departemen = item.Departemen,
                perusahaan = item.Perusahaan,
                area = item.Area,
                lokasi = item.Lokasi,
                detilLokasi = item.DetilLokasi,
                namaJalan = item.NamaJalan,
                segmentJalan = item.SegmentJalan,
                shift = item.Shift,
                latitude = item.Latitude,
                longitude = item.Longitude,
                skorLebarJalan = item.SkorLebarJalan,
                skorGradeJalan = item.SkorGradeJalan,
                skorPermukaanJalan = item.SkorPermukaanJalan,
                skorSafetyBerm = item.SkorSafetyBerm,
                skorDrainaseParit = item.SkorDrainaseParit,
                skorSuperelevasiTikungan = item.SkorSuperelevasiTikungan,
                skorBebasSpillage = item.SkorBebasSpillage,
                skorRambuDebu = item.SkorRambuDebu,
                totalScore = item.TotalScore,
                kategoriIndex = item.KategoriIndex,
                catatan = item.Catatan,
                tindakanPerbaikan = item.TindakanPerbaikan,
                pic = item.Pic,
                fotoUrl = item.FotoUrl
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.RciReports.FindAsync(id);
            if (item == null || item.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "";
            if (item.Nik != userNik && !User.IsInRole("Admin"))
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk menghapus laporan ini.";
                return RedirectToAction(nameof(Index));
            }

            item.IsDeleted = true;
            item.UpdatedAt = DateTime.Now;
            _context.RciReports.Update(item);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan RCI berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate)
        {
            if (!CompanyHierarchyService.IsMgeGroupUser(User))
            {
                return Forbid();
            }

            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = (endDate ?? DateTime.Today).AddDays(1).AddTicks(-1);

            var list = await _context.RciReports
                .Where(r => !r.IsDeleted && r.Tanggal >= start && r.Tanggal <= end)
                .OrderByDescending(r => r.Tanggal)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan RCI");

            // Header info
            ws.Cell(1, 1).Value = "LAPORAN ROAD CONDITION INDEX (RCI)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode: {start:dd/MM/yyyy} s/d {end:dd/MM/yyyy} | PT Mega Global Energy Group";
            ws.Cell(2, 1).Style.Font.Italic = true;

            // Table headers
            string[] headers = new[]
            {
                "No", "Tanggal", "Waktu", "NIK", "Inspector", "Departemen", "Perusahaan", "Area", "Lokasi", "Detil Lokasi",
                "Nama Jalan", "Segment", "Shift", "Lebar Jalan", "Grade Jalan", "Permukaan Jalan", "Safety Berm", "Drainase/Parit",
                "Superelevasi/Tikungan", "Bebas Spillage", "Rambu & Debu", "Skor Index (%)", "Kategori", "Catatan", "Tindakan Perbaikan", "PIC"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 5;
            for (int idx = 0; idx < list.Count; idx++)
            {
                var r = list[idx];
                ws.Cell(row, 1).Value = idx + 1;
                ws.Cell(row, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                ws.Cell(row, 3).Value = r.Waktu.ToString(@"hh\:mm");
                ws.Cell(row, 4).Value = r.Nik;
                ws.Cell(row, 5).Value = r.Nama;
                ws.Cell(row, 6).Value = r.Departemen ?? "-";
                ws.Cell(row, 7).Value = r.Perusahaan ?? "-";
                ws.Cell(row, 8).Value = r.Area;
                ws.Cell(row, 9).Value = r.Lokasi;
                ws.Cell(row, 10).Value = r.DetilLokasi ?? "-";
                ws.Cell(row, 11).Value = r.NamaJalan ?? "-";
                ws.Cell(row, 12).Value = r.SegmentJalan ?? "-";
                ws.Cell(row, 13).Value = r.Shift ?? "-";
                ws.Cell(row, 14).Value = r.SkorLebarJalan;
                ws.Cell(row, 15).Value = r.SkorGradeJalan;
                ws.Cell(row, 16).Value = r.SkorPermukaanJalan;
                ws.Cell(row, 17).Value = r.SkorSafetyBerm;
                ws.Cell(row, 18).Value = r.SkorDrainaseParit;
                ws.Cell(row, 19).Value = r.SkorSuperelevasiTikungan;
                ws.Cell(row, 20).Value = r.SkorBebasSpillage;
                ws.Cell(row, 21).Value = r.SkorRambuDebu;
                ws.Cell(row, 22).Value = r.TotalScore;
                ws.Cell(row, 23).Value = r.KategoriIndex;
                ws.Cell(row, 24).Value = r.Catatan ?? "-";
                ws.Cell(row, 25).Value = r.TindakanPerbaikan ?? "-";
                ws.Cell(row, 26).Value = r.Pic ?? "-";

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_RCI_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
