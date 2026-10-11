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
    public class DciController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public DciController(AppDbContext context, ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        public async Task<IActionResult> Index(string? search, string? disposal, string? kategori, DateTime? startDate, DateTime? endDate)
        {
            ViewData["HeaderTitle"] = "Dump Condition Index (DCI)";
            ViewData["ActiveTab"] = "Dci";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            var isAdmin = CompanyHierarchyService.IsAdminUser(User);

            var query = _context.DciReports.Where(r => !r.IsDeleted);

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
                                         (r.NamaDump != null && r.NamaDump.ToLower().Contains(s)) ||
                                         (r.Disposal != null && r.Disposal.ToLower().Contains(s)));
            }

            if (!string.IsNullOrEmpty(disposal))
            {
                query = query.Where(r => r.Disposal == disposal);
            }

            if (!string.IsNullOrEmpty(kategori))
            {
                query = query.Where(r => r.KategoriIndex == kategori);
            }

            var reports = await query.OrderByDescending(r => r.Tanggal).ThenByDescending(r => r.CreatedAt).ToListAsync();

            // Statistics (MTD - 30 days)
            var allMtdReports = await _context.DciReports
                .Where(r => !r.IsDeleted && r.Tanggal >= DateTime.Today.AddDays(-30))
                .ToListAsync();

            ViewBag.TotalReports = allMtdReports.Count;
            ViewBag.AvgScore = allMtdReports.Any() ? Math.Round(allMtdReports.Average(r => r.TotalScore), 1) : 100.0;
            ViewBag.GoodCount = allMtdReports.Count(r => r.KategoriIndex == "Baik");
            ViewBag.FairCount = allMtdReports.Count(r => r.KategoriIndex == "Sedang");
            ViewBag.PoorCount = allMtdReports.Count(r => r.KategoriIndex == "Kurang");

            // Distinct disposals for filter
            ViewBag.DisposalList = await _context.DciReports
                .Where(r => !r.IsDeleted && !string.IsNullOrEmpty(r.Disposal))
                .Select(r => r.Disposal!)
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
            ViewBag.CurrentDisposal = disposal ?? "";
            ViewBag.CurrentKategori = kategori ?? "";
            ViewBag.UserNik = userNik;
            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "Operations";
            ViewBag.UserCompany = User.FindFirst("Company")?.Value ?? "PT INDEXIM COALINDO";

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
            string? namaDump,
            string? disposal,
            string? shift,
            string? latitude,
            string? longitude,
            int skorLantaiDump,
            int skorSafetyBerm,
            int skorKondisiCrest,
            int skorGradeDumping,
            int skorDrainaseDump,
            int skorPenataanSpillage,
            int skorRambuPenerangan,
            int skorSpotterManuver,
            string? catatan,
            string? tindakanPerbaikan,
            string? pic,
            IFormFile? foto)
        {
            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "Operations";
                var userComp = User.FindFirst("Company")?.Value ?? "PT INDEXIM COALINDO";
                int.TryParse(User.FindFirst("CompanyId")?.Value, out int compId);

                TimeSpan waktu = DateTime.Now.TimeOfDay;
                if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
                {
                    waktu = parsedWaktu;
                }

                // Weighted calculation:
                // Lantai Dump (15%), Safety Berm (15%), Kondisi Crest (15%), Grade Dumping (15%),
                // Drainase Dump (10%), Penataan Spillage (10%), Rambu & Penerangan (10%), Spotter & Manuver (10%)
                double totalScore = (skorLantaiDump * 0.15) +
                                    (skorSafetyBerm * 0.15) +
                                    (skorKondisiCrest * 0.15) +
                                    (skorGradeDumping * 0.15) +
                                    (skorDrainaseDump * 0.10) +
                                    (skorPenataanSpillage * 0.10) +
                                    (skorRambuPenerangan * 0.10) +
                                    (skorSpotterManuver * 0.10);
                totalScore = Math.Round(totalScore, 1);

                string kategoriIndex = totalScore >= 85 ? "Baik" : (totalScore >= 70 ? "Sedang" : "Kurang");

                string? fotoUrl = null;
                if (foto != null && foto.Length > 0)
                {
                    fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "dci");
                }

                if (id.HasValue && id.Value > 0)
                {
                    var existing = await _context.DciReports.FindAsync(id.Value);
                    if (existing == null || existing.IsDeleted) return NotFound();

                    if (existing.Nik != userNik && !CompanyHierarchyService.IsAdminUser(User))
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki hak untuk mengedit data ini.";
                        return RedirectToAction(nameof(Index));
                    }

                    existing.Tanggal = tanggal.Date;
                    existing.Waktu = waktu;
                    existing.Area = area;
                    existing.Lokasi = lokasi;
                    existing.DetilLokasi = detilLokasi;
                    existing.NamaDump = namaDump;
                    existing.Disposal = disposal;
                    existing.Shift = shift;
                    existing.Latitude = latitude;
                    existing.Longitude = longitude;
                    existing.SkorLantaiDump = skorLantaiDump;
                    existing.SkorSafetyBerm = skorSafetyBerm;
                    existing.SkorKondisiCrest = skorKondisiCrest;
                    existing.SkorGradeDumping = skorGradeDumping;
                    existing.SkorDrainaseDump = skorDrainaseDump;
                    existing.SkorPenataanSpillage = skorPenataanSpillage;
                    existing.SkorRambuPenerangan = skorRambuPenerangan;
                    existing.SkorSpotterManuver = skorSpotterManuver;
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

                    _context.DciReports.Update(existing);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Laporan DCI di {namaDump ?? lokasi} berhasil diperbarui! Skor Index: {totalScore}% ({kategoriIndex})";
                }
                else
                {
                    var report = new DciReport
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
                        NamaDump = namaDump,
                        Disposal = disposal,
                        Shift = shift,
                        Latitude = latitude,
                        Longitude = longitude,
                        SkorLantaiDump = skorLantaiDump,
                        SkorSafetyBerm = skorSafetyBerm,
                        SkorKondisiCrest = skorKondisiCrest,
                        SkorGradeDumping = skorGradeDumping,
                        SkorDrainaseDump = skorDrainaseDump,
                        SkorPenataanSpillage = skorPenataanSpillage,
                        SkorRambuPenerangan = skorRambuPenerangan,
                        SkorSpotterManuver = skorSpotterManuver,
                        TotalScore = totalScore,
                        KategoriIndex = kategoriIndex,
                        Catatan = catatan,
                        TindakanPerbaikan = tindakanPerbaikan,
                        Pic = pic,
                        FotoUrl = fotoUrl,
                        CreatedAt = DateTime.Now
                    };

                    _context.DciReports.Add(report);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Inspeksi DCI berhasil disimpan! Skor Index: {totalScore}% ({kategoriIndex})";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Gagal menyimpan inspeksi DCI: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetail(int id)
        {
            var item = await _context.DciReports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
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
                namaDump = item.NamaDump,
                disposal = item.Disposal,
                shift = item.Shift,
                latitude = item.Latitude,
                longitude = item.Longitude,
                skorLantaiDump = item.SkorLantaiDump,
                skorSafetyBerm = item.SkorSafetyBerm,
                skorKondisiCrest = item.SkorKondisiCrest,
                skorGradeDumping = item.SkorGradeDumping,
                skorDrainaseDump = item.SkorDrainaseDump,
                skorPenataanSpillage = item.SkorPenataanSpillage,
                skorRambuPenerangan = item.SkorRambuPenerangan,
                skorSpotterManuver = item.SkorSpotterManuver,
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
            var item = await _context.DciReports.FindAsync(id);
            if (item == null || item.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "";
            if (item.Nik != userNik && !CompanyHierarchyService.IsAdminUser(User))
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk menghapus laporan ini.";
                return RedirectToAction(nameof(Index));
            }

            item.IsDeleted = true;
            item.UpdatedAt = DateTime.Now;
            _context.DciReports.Update(item);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan DCI berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = (endDate ?? DateTime.Today).AddDays(1).AddTicks(-1);

            var list = await _context.DciReports
                .Where(r => !r.IsDeleted && r.Tanggal >= start && r.Tanggal <= end)
                .OrderByDescending(r => r.Tanggal)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan DCI");

            // Header Title
            ws.Cell(1, 1).Value = "LAPORAN DUMP CONDITION INDEX (DCI)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode: {start:dd/MM/yyyy} s/d {end:dd/MM/yyyy} | Dicetak: {DateTime.Now:dd/MM/yyyy HH:mm}";

            // Columns
            string[] headers = new[]
            {
                "No", "Tanggal", "Waktu", "NIK", "Nama", "Departemen", "Perusahaan", "Area", "Lokasi", "Detil Lokasi",
                "Disposal", "Nama Dump", "Shift", "Lantai Dump", "Safety Berm", "Kondisi Crest", "Grade Dumping", "Drainase Dump",
                "Penataan Spillage", "Rambu Penerangan", "Spotter Manuver", "Total Skor", "Kategori", "PIC", "Catatan", "Tindakan Perbaikan", "Foto URL"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = ws.Cell(4, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(234, 88, 12); // Orange theme
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 5;
            int no = 1;
            foreach (var r in list)
            {
                ws.Cell(row, 1).Value = no++;
                ws.Cell(row, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                ws.Cell(row, 3).Value = r.Waktu.ToString(@"hh\:mm");
                ws.Cell(row, 4).Value = r.Nik;
                ws.Cell(row, 5).Value = r.Nama;
                ws.Cell(row, 6).Value = r.Departemen;
                ws.Cell(row, 7).Value = r.Perusahaan;
                ws.Cell(row, 8).Value = r.Area;
                ws.Cell(row, 9).Value = r.Lokasi;
                ws.Cell(row, 10).Value = r.DetilLokasi;
                ws.Cell(row, 11).Value = r.Disposal;
                ws.Cell(row, 12).Value = r.NamaDump;
                ws.Cell(row, 13).Value = r.Shift;
                ws.Cell(row, 14).Value = r.SkorLantaiDump;
                ws.Cell(row, 15).Value = r.SkorSafetyBerm;
                ws.Cell(row, 16).Value = r.SkorKondisiCrest;
                ws.Cell(row, 17).Value = r.SkorGradeDumping;
                ws.Cell(row, 18).Value = r.SkorDrainaseDump;
                ws.Cell(row, 19).Value = r.SkorPenataanSpillage;
                ws.Cell(row, 20).Value = r.SkorRambuPenerangan;
                ws.Cell(row, 21).Value = r.SkorSpotterManuver;
                ws.Cell(row, 22).Value = r.TotalScore;
                ws.Cell(row, 23).Value = r.KategoriIndex;
                ws.Cell(row, 24).Value = r.Pic;
                ws.Cell(row, 25).Value = r.Catatan;
                ws.Cell(row, 26).Value = r.TindakanPerbaikan;
                ws.Cell(row, 27).Value = r.FotoUrl;

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_DCI_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
