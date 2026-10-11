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
    public class SpiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public SpiController(AppDbContext context, ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        public async Task<IActionResult> Index(string? search, string? kolam, string? kategori, DateTime? startDate, DateTime? endDate)
        {
            ViewData["HeaderTitle"] = "Sediment Pond Index (SPI)";
            ViewData["ActiveTab"] = "Spi";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            var isAdmin = CompanyHierarchyService.IsAdminUser(User);

            var query = _context.SpiReports.Where(r => !r.IsDeleted);

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
                                         (r.NamaKolam != null && r.NamaKolam.ToLower().Contains(s)) ||
                                         (r.TitikPenaatan != null && r.TitikPenaatan.ToLower().Contains(s)));
            }

            if (!string.IsNullOrEmpty(kolam))
            {
                query = query.Where(r => r.NamaKolam == kolam);
            }

            if (!string.IsNullOrEmpty(kategori))
            {
                query = query.Where(r => r.KategoriIndex == kategori);
            }

            var reports = await query.OrderByDescending(r => r.Tanggal).ThenByDescending(r => r.CreatedAt).ToListAsync();

            // Statistics (MTD - 30 days)
            var allMtdReports = await _context.SpiReports
                .Where(r => !r.IsDeleted && r.Tanggal >= DateTime.Today.AddDays(-30))
                .ToListAsync();

            ViewBag.TotalReports = allMtdReports.Count;
            ViewBag.AvgScore = allMtdReports.Any() ? Math.Round(allMtdReports.Average(r => r.TotalScore), 1) : 100.0;
            ViewBag.GoodCount = allMtdReports.Count(r => r.KategoriIndex == "Baik");
            ViewBag.FairCount = allMtdReports.Count(r => r.KategoriIndex == "Sedang");
            ViewBag.PoorCount = allMtdReports.Count(r => r.KategoriIndex == "Kurang");

            // Distinct ponds for filter
            ViewBag.KolamList = await _context.SpiReports
                .Where(r => !r.IsDeleted && !string.IsNullOrEmpty(r.NamaKolam))
                .Select(r => r.NamaKolam!)
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
            ViewBag.CurrentKolam = kolam ?? "";
            ViewBag.CurrentKategori = kategori ?? "";
            ViewBag.UserNik = userNik;
            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "Environment";
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
            string? namaKolam,
            string? titikPenaatan,
            string? shift,
            string? latitude,
            string? longitude,
            int skorKapasitasEndapan,
            int skorKondisiTanggul,
            int skorSekatBaffle,
            int skorPelimpahSpillway,
            int skorFasilitasDosing,
            int skorKualitasAirFisik,
            int skorTitikPenaatanDebit,
            int skorRambuPengaman,
            string? catatan,
            string? tindakanPerbaikan,
            string? pic,
            IFormFile? foto)
        {
            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "Environment";
                var userComp = User.FindFirst("Company")?.Value ?? "PT INDEXIM COALINDO";
                int.TryParse(User.FindFirst("CompanyId")?.Value, out int compId);

                TimeSpan waktu = DateTime.Now.TimeOfDay;
                if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
                {
                    waktu = parsedWaktu;
                }

                // Weighted calculation:
                // Kapasitas Endapan (15%), Kondisi Tanggul (15%), Sekat Baffle (15%), Pelimpah Spillway (15%),
                // Fasilitas Dosing (10%), Kualitas Air Fisik (10%), Titik Penaatan Debit (10%), Rambu & Pengaman (10%)
                double totalScore = (skorKapasitasEndapan * 0.15) +
                                    (skorKondisiTanggul * 0.15) +
                                    (skorSekatBaffle * 0.15) +
                                    (skorPelimpahSpillway * 0.15) +
                                    (skorFasilitasDosing * 0.10) +
                                    (skorKualitasAirFisik * 0.10) +
                                    (skorTitikPenaatanDebit * 0.10) +
                                    (skorRambuPengaman * 0.10);
                totalScore = Math.Round(totalScore, 1);

                string kategoriIndex = totalScore >= 85 ? "Baik" : (totalScore >= 70 ? "Sedang" : "Kurang");

                string? fotoUrl = null;
                if (foto != null && foto.Length > 0)
                {
                    fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "spi");
                }

                if (id.HasValue && id.Value > 0)
                {
                    var existing = await _context.SpiReports.FindAsync(id.Value);
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
                    existing.NamaKolam = namaKolam;
                    existing.TitikPenaatan = titikPenaatan;
                    existing.Shift = shift;
                    existing.Latitude = latitude;
                    existing.Longitude = longitude;
                    existing.SkorKapasitasEndapan = skorKapasitasEndapan;
                    existing.SkorKondisiTanggul = skorKondisiTanggul;
                    existing.SkorSekatBaffle = skorSekatBaffle;
                    existing.SkorPelimpahSpillway = skorPelimpahSpillway;
                    existing.SkorFasilitasDosing = skorFasilitasDosing;
                    existing.SkorKualitasAirFisik = skorKualitasAirFisik;
                    existing.SkorTitikPenaatanDebit = skorTitikPenaatanDebit;
                    existing.SkorRambuPengaman = skorRambuPengaman;
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

                    _context.SpiReports.Update(existing);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Laporan SPI di {namaKolam ?? lokasi} berhasil diperbarui! Skor Index: {totalScore}% ({kategoriIndex})";
                }
                else
                {
                    var report = new SpiReport
                    {
                        Tanggal = tanggal.Date,
                        Waktu = waktu,
                        Nik = userNik,
                        Nama = userName,
                        Departemen = userDept,
                        Perusahaan = userComp,
                        PerusahaanId = compId > 0 ? compId : null,
                        Area = area,
                        Lokasi = lokasi,
                        DetilLokasi = detilLokasi,
                        NamaKolam = namaKolam,
                        TitikPenaatan = titikPenaatan,
                        Shift = shift,
                        Latitude = latitude,
                        Longitude = longitude,
                        SkorKapasitasEndapan = skorKapasitasEndapan,
                        SkorKondisiTanggul = skorKondisiTanggul,
                        SkorSekatBaffle = skorSekatBaffle,
                        SkorPelimpahSpillway = skorPelimpahSpillway,
                        SkorFasilitasDosing = skorFasilitasDosing,
                        SkorKualitasAirFisik = skorKualitasAirFisik,
                        SkorTitikPenaatanDebit = skorTitikPenaatanDebit,
                        SkorRambuPengaman = skorRambuPengaman,
                        TotalScore = totalScore,
                        KategoriIndex = kategoriIndex,
                        Catatan = catatan,
                        TindakanPerbaikan = tindakanPerbaikan,
                        Pic = pic,
                        FotoUrl = fotoUrl,
                        CreatedAt = DateTime.Now
                    };

                    _context.SpiReports.Add(report);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Inspeksi Sediment Pond Index (SPI) di {namaKolam ?? lokasi} berhasil disimpan! Skor: {totalScore}% ({kategoriIndex})";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Terjadi kesalahan saat menyimpan data SPI: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetail(int id)
        {
            var item = await _context.SpiReports.FindAsync(id);
            if (item == null || item.IsDeleted) return NotFound();

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
                namaKolam = item.NamaKolam,
                titikPenaatan = item.TitikPenaatan,
                shift = item.Shift,
                latitude = item.Latitude,
                longitude = item.Longitude,
                skorKapasitasEndapan = item.SkorKapasitasEndapan,
                skorKondisiTanggul = item.SkorKondisiTanggul,
                skorSekatBaffle = item.SkorSekatBaffle,
                skorPelimpahSpillway = item.SkorPelimpahSpillway,
                skorFasilitasDosing = item.SkorFasilitasDosing,
                skorKualitasAirFisik = item.SkorKualitasAirFisik,
                skorTitikPenaatanDebit = item.SkorTitikPenaatanDebit,
                skorRambuPengaman = item.SkorRambuPengaman,
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
            var item = await _context.SpiReports.FindAsync(id);
            if (item == null || item.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "";
            if (item.Nik != userNik && !CompanyHierarchyService.IsAdminUser(User))
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk menghapus laporan ini.";
                return RedirectToAction(nameof(Index));
            }

            item.IsDeleted = true;
            item.UpdatedAt = DateTime.Now;
            _context.SpiReports.Update(item);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan SPI berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = (endDate ?? DateTime.Today).AddDays(1).AddTicks(-1);

            var list = await _context.SpiReports
                .Where(r => !r.IsDeleted && r.Tanggal >= start && r.Tanggal <= end)
                .OrderByDescending(r => r.Tanggal)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan SPI");

            // Header Title
            ws.Cell(1, 1).Value = "LAPORAN SEDIMENT POND INDEX (SPI)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"Periode: {start:dd/MM/yyyy} s/d {end:dd/MM/yyyy} | Dicetak: {DateTime.Now:dd/MM/yyyy HH:mm}";

            // Columns
            string[] headers = new[]
            {
                "No", "Tanggal", "Waktu", "NIK", "Nama", "Departemen", "Perusahaan", "Area", "Lokasi", "Detil Lokasi",
                "Nama Kolam / KPL", "Titik Penaatan", "Shift", "Kapasitas Endapan (15%)", "Kondisi Tanggul (15%)", "Sekat Baffle (15%)", "Pelimpah Spillway (15%)",
                "Fasilitas Dosing (10%)", "Kualitas Air Fisik (10%)", "Titik Pantau Debit (10%)", "Rambu Pengaman (10%)",
                "Total Skor", "Kategori", "PIC", "Catatan", "Tindakan Perbaikan", "Foto URL"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = ws.Cell(4, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(5, 150, 105); // Emerald green theme
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
                ws.Cell(row, 11).Value = r.NamaKolam;
                ws.Cell(row, 12).Value = r.TitikPenaatan;
                ws.Cell(row, 13).Value = r.Shift;
                ws.Cell(row, 14).Value = r.SkorKapasitasEndapan;
                ws.Cell(row, 15).Value = r.SkorKondisiTanggul;
                ws.Cell(row, 16).Value = r.SkorSekatBaffle;
                ws.Cell(row, 17).Value = r.SkorPelimpahSpillway;
                ws.Cell(row, 18).Value = r.SkorFasilitasDosing;
                ws.Cell(row, 19).Value = r.SkorKualitasAirFisik;
                ws.Cell(row, 20).Value = r.SkorTitikPenaatanDebit;
                ws.Cell(row, 21).Value = r.SkorRambuPengaman;
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

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_SPI_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
