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
            double? panjangSegment,
            double? defisitSurfacing,
            double? defisitUndulation,
            double? defisitSpoil,
            double? defisitSafetyBerm,
            double? defisitCrossfall,
            double? defisitDust,
            double? defisitDrainage,
            double? defisitRoadAttachment,
            double skorSurfacing = 4.0,
            double skorUndulation = 4.0,
            double skorSpoil = 4.0,
            double skorSafetyBerm = 4.0,
            double skorCrossfall = 4.0,
            double skorDust = 4.0,
            double skorDrainage = 4.0,
            double skorRoadAttachment = 4.0,
            int? skorLebarJalan = null,
            int? skorGradeJalan = null,
            int? skorPermukaanJalan = null,
            int? skorDrainaseParit = null,
            int? skorSuperelevasiTikungan = null,
            int? skorBebasSpillage = null,
            int? skorRambuDebu = null,
            string? catatan = null,
            string? tindakanPerbaikan = null,
            string? pic = null,
            IFormFile? foto = null)
        {
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

                // Rumus Aktual Excel SP3:
                // Pilar 1: Permukaan Jalan (Bobot 60%) -> Surfacing 20%, Undulation 35%, Spoil 5%
                double skorPermukaanGroup = ((skorSurfacing * 0.20) + (skorUndulation * 0.35) + (skorSpoil * 0.05)) / 0.60;
                
                // Pilar 2: Safety Berm (Bobot 20%) -> Safety Berm 10%, Road Attachment 5%, Dust 5%
                double skorSafetyBermGroup = ((skorSafetyBerm * 0.10) + (skorRoadAttachment * 0.05) + (skorDust * 0.05)) / 0.20;

                // Pilar 3: Drainage (Bobot 20%) -> Crossfall 10%, Drainage 10%
                double skorDrainageGroup = ((skorCrossfall * 0.10) + (skorDrainage * 0.10)) / 0.20;

                // Total Road Score (Skala 1.00 - 4.00, Target 4.00):
                double actualRoadScore = (skorPermukaanGroup * 0.60) + (skorSafetyBermGroup * 0.20) + (skorDrainageGroup * 0.20);
                actualRoadScore = Math.Round(actualRoadScore, 2);

                double targetScore = 4.00;
                double achievement = Math.Round((actualRoadScore / targetScore) * 100.0, 2);

                // Kategori: Baik (>= 85%), Sedang (70% - 84.9%), Kurang (< 70%)
                string kategoriIndex = achievement >= 85 ? "Baik" : (achievement >= 70 ? "Sedang" : "Kurang");

                string? fotoUrl = null;
                if (foto != null && foto.Length > 0)
                {
                    fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "rci");
                }

                if (id.HasValue && id.Value > 0)
                {
                    var existing = await _context.RciReports.FindAsync(id.Value);
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
                    existing.NamaJalan = namaJalan;
                    existing.SegmentJalan = segmentJalan;
                    existing.Shift = shift;
                    existing.Latitude = latitude;
                    existing.Longitude = longitude;

                    existing.PanjangSegment = panjangSegment ?? 100;
                    existing.DefisitSurfacing = defisitSurfacing ?? 0;
                    existing.DefisitUndulation = defisitUndulation ?? 0;
                    existing.DefisitSpoil = defisitSpoil ?? 0;
                    existing.DefisitSafetyBerm = defisitSafetyBerm ?? 0;
                    existing.DefisitCrossfall = defisitCrossfall ?? 0;
                    existing.DefisitDust = defisitDust ?? 0;
                    existing.DefisitDrainage = defisitDrainage ?? 0;
                    existing.DefisitRoadAttachment = defisitRoadAttachment ?? 0;

                    existing.SkorSurfacing = skorSurfacing;
                    existing.SkorUndulation = skorUndulation;
                    existing.SkorSpoil = skorSpoil;
                    existing.SkorSafetyBerm = skorSafetyBerm;
                    existing.SkorCrossfall = skorCrossfall;
                    existing.SkorDust = skorDust;
                    existing.SkorDrainage = skorDrainage;
                    existing.SkorRoadAttachment = skorRoadAttachment;

                    existing.SkorPermukaanGroup = Math.Round(skorPermukaanGroup, 2);
                    existing.SkorSafetyBermGroup = Math.Round(skorSafetyBermGroup, 2);
                    existing.SkorDrainageGroup = Math.Round(skorDrainageGroup, 2);

                    existing.ActualRoadScore = actualRoadScore;
                    existing.TargetScore = targetScore;
                    existing.Achievement = achievement;
                    existing.TotalScore = achievement; // stored as % for compatibility
                    existing.KategoriIndex = kategoriIndex;

                    // Legacy mappings
                    existing.SkorPermukaanJalan = (int)Math.Round(skorSurfacing * 25);
                    existing.SkorGradeJalan = (int)Math.Round(skorUndulation * 25);
                    existing.SkorLebarJalan = (int)Math.Round(skorSpoil * 25);
                    existing.SkorDrainaseParit = (int)Math.Round(skorDrainage * 25);
                    existing.SkorSuperelevasiTikungan = (int)Math.Round(skorCrossfall * 25);
                    existing.SkorBebasSpillage = (int)Math.Round(skorDust * 25);
                    existing.SkorRambuDebu = (int)Math.Round(skorRoadAttachment * 25);

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
                    TempData["SuccessMessage"] = $"Laporan RCI di {namaJalan ?? lokasi} berhasil diperbarui! Skor: {actualRoadScore}/4.00 ({achievement}%) [{kategoriIndex}]";
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

                        PanjangSegment = panjangSegment ?? 100,
                        DefisitSurfacing = defisitSurfacing ?? 0,
                        DefisitUndulation = defisitUndulation ?? 0,
                        DefisitSpoil = defisitSpoil ?? 0,
                        DefisitSafetyBerm = defisitSafetyBerm ?? 0,
                        DefisitCrossfall = defisitCrossfall ?? 0,
                        DefisitDust = defisitDust ?? 0,
                        DefisitDrainage = defisitDrainage ?? 0,
                        DefisitRoadAttachment = defisitRoadAttachment ?? 0,

                        SkorSurfacing = skorSurfacing,
                        SkorUndulation = skorUndulation,
                        SkorSpoil = skorSpoil,
                        SkorSafetyBerm = skorSafetyBerm,
                        SkorCrossfall = skorCrossfall,
                        SkorDust = skorDust,
                        SkorDrainage = skorDrainage,
                        SkorRoadAttachment = skorRoadAttachment,

                        SkorPermukaanGroup = Math.Round(skorPermukaanGroup, 2),
                        SkorSafetyBermGroup = Math.Round(skorSafetyBermGroup, 2),
                        SkorDrainageGroup = Math.Round(skorDrainageGroup, 2),

                        ActualRoadScore = actualRoadScore,
                        TargetScore = targetScore,
                        Achievement = achievement,
                        TotalScore = achievement,
                        KategoriIndex = kategoriIndex,

                        // Legacy mappings
                        SkorPermukaanJalan = (int)Math.Round(skorSurfacing * 25),
                        SkorGradeJalan = (int)Math.Round(skorUndulation * 25),
                        SkorLebarJalan = (int)Math.Round(skorSpoil * 25),
                        SkorDrainaseParit = (int)Math.Round(skorDrainage * 25),
                        SkorSuperelevasiTikungan = (int)Math.Round(skorCrossfall * 25),
                        SkorBebasSpillage = (int)Math.Round(skorDust * 25),
                        SkorRambuDebu = (int)Math.Round(skorRoadAttachment * 25),

                        Catatan = catatan,
                        TindakanPerbaikan = tindakanPerbaikan,
                        Pic = pic,
                        FotoUrl = fotoUrl,
                        CreatedAt = DateTime.Now
                    };

                    _context.RciReports.Add(report);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Inspeksi RCI berhasil disimpan! Skor: {actualRoadScore}/4.00 ({achievement}%) [{kategoriIndex}]";
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
                panjangSegment = item.PanjangSegment ?? 100,
                defisitSurfacing = item.DefisitSurfacing ?? 0,
                defisitUndulation = item.DefisitUndulation ?? 0,
                defisitSpoil = item.DefisitSpoil ?? 0,
                defisitSafetyBerm = item.DefisitSafetyBerm ?? 0,
                defisitCrossfall = item.DefisitCrossfall ?? 0,
                defisitDust = item.DefisitDust ?? 0,
                defisitDrainage = item.DefisitDrainage ?? 0,
                defisitRoadAttachment = item.DefisitRoadAttachment ?? 0,
                skorSurfacing = item.SkorSurfacing,
                skorUndulation = item.SkorUndulation,
                skorSpoil = item.SkorSpoil,
                skorSafetyBerm = item.SkorSafetyBerm,
                skorCrossfall = item.SkorCrossfall,
                skorDust = item.SkorDust,
                skorDrainage = item.SkorDrainage,
                skorRoadAttachment = item.SkorRoadAttachment,
                skorPermukaanGroup = item.SkorPermukaanGroup > 0 ? item.SkorPermukaanGroup : Math.Round(((item.SkorSurfacing * 0.2) + (item.SkorUndulation * 0.35) + (item.SkorSpoil * 0.05)) / 0.6, 2),
                skorSafetyBermGroup = item.SkorSafetyBermGroup > 0 ? item.SkorSafetyBermGroup : Math.Round(((item.SkorSafetyBerm * 0.1) + (item.SkorRoadAttachment * 0.05) + (item.SkorDust * 0.05)) / 0.2, 2),
                skorDrainageGroup = item.SkorDrainageGroup > 0 ? item.SkorDrainageGroup : Math.Round(((item.SkorCrossfall * 0.1) + (item.SkorDrainage * 0.1)) / 0.2, 2),
                actualRoadScore = item.ActualRoadScore > 0 ? item.ActualRoadScore : Math.Round(item.TotalScore / 25.0, 2),
                targetScore = item.TargetScore > 0 ? item.TargetScore : 4.0,
                achievement = item.Achievement > 0 ? item.Achievement : item.TotalScore,
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
            if (item.Nik != userNik && !CompanyHierarchyService.IsAdminUser(User))
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
                "Nama Jalan", "Segment", "Panjang Segmen (m)", "Shift",
                "Surfacing (30%)", "Defisit Surfacing (m)",
                "Undulation (25%)", "Defisit Undulation (m)",
                "Spoil (20%)", "Defisit Spoil (m)",
                "Safety Berm (5%)", "Defisit Berm (m)",
                "Crossfall (5%)", "Defisit Crossfall (m)",
                "Dust (5%)", "Defisit Dust (m)",
                "Drainage (5%)", "Defisit Drainage (m)",
                "Road Attachment (5%)", "Defisit Attachment (m)",
                "Actual Score (1-4)", "Target", "Achievement (%)", "Kategori",
                "Catatan", "Tindakan Perbaikan", "PIC"
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
                var actual = r.ActualRoadScore > 0 ? r.ActualRoadScore : (r.TotalScore > 4 ? Math.Round(r.TotalScore / 25.0, 2) : r.TotalScore);
                var ach = r.Achievement > 0 ? r.Achievement : (actual / 4.0 * 100.0);

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
                ws.Cell(row, 13).Value = r.PanjangSegment ?? 100;
                ws.Cell(row, 14).Value = r.Shift ?? "-";
                ws.Cell(row, 15).Value = r.SkorSurfacing;
                ws.Cell(row, 16).Value = r.DefisitSurfacing ?? 0;
                ws.Cell(row, 17).Value = r.SkorUndulation;
                ws.Cell(row, 18).Value = r.DefisitUndulation ?? 0;
                ws.Cell(row, 19).Value = r.SkorSpoil;
                ws.Cell(row, 20).Value = r.DefisitSpoil ?? 0;
                ws.Cell(row, 21).Value = r.SkorSafetyBerm;
                ws.Cell(row, 22).Value = r.DefisitSafetyBerm ?? 0;
                ws.Cell(row, 23).Value = r.SkorCrossfall;
                ws.Cell(row, 24).Value = r.DefisitCrossfall ?? 0;
                ws.Cell(row, 25).Value = r.SkorDust;
                ws.Cell(row, 26).Value = r.DefisitDust ?? 0;
                ws.Cell(row, 27).Value = r.SkorDrainage;
                ws.Cell(row, 28).Value = r.DefisitDrainage ?? 0;
                ws.Cell(row, 29).Value = r.SkorRoadAttachment;
                ws.Cell(row, 30).Value = r.DefisitRoadAttachment ?? 0;
                ws.Cell(row, 31).Value = actual;
                ws.Cell(row, 32).Value = 4.0;
                ws.Cell(row, 33).Value = ach;
                ws.Cell(row, 34).Value = r.KategoriIndex;
                ws.Cell(row, 35).Value = r.Catatan ?? "-";
                ws.Cell(row, 36).Value = r.TindakanPerbaikan ?? "-";
                ws.Cell(row, 37).Value = r.Pic ?? "-";

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
