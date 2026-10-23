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
    public class GciController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public GciController(AppDbContext context, ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        public async Task<IActionResult> Index(string? search, string? pit, string? areaType, string? kategori, string? tarpLevel, DateTime? startDate, DateTime? endDate)
        {
            ViewData["HeaderTitle"] = "Geotechnical Condition Index (GCI)";
            ViewData["ActiveTab"] = "Gci";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            var isAdmin = CompanyHierarchyService.IsAdminUser(User);

            var query = _context.GciReports.Where(r => !r.IsDeleted);

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
                                         (r.NamaLereng != null && r.NamaLereng.ToLower().Contains(s)) ||
                                         (r.AreaType != null && r.AreaType.ToLower().Contains(s)) ||
                                         (r.Pit != null && r.Pit.ToLower().Contains(s)));
            }

            if (!string.IsNullOrEmpty(pit))
            {
                query = query.Where(r => r.Pit == pit);
            }

            if (!string.IsNullOrEmpty(areaType))
            {
                query = query.Where(r => r.AreaType == areaType);
            }

            if (!string.IsNullOrEmpty(kategori))
            {
                query = query.Where(r => r.KategoriIndex == kategori);
            }

            if (!string.IsNullOrEmpty(tarpLevel))
            {
                query = query.Where(r => r.TarpLevel == tarpLevel);
            }

            var reports = await query.OrderByDescending(r => r.Tanggal).ThenByDescending(r => r.CreatedAt).ToListAsync();

            // Statistics (MTD - 30 days)
            var allMtdReports = await _context.GciReports
                .Where(r => !r.IsDeleted && r.Tanggal >= DateTime.Today.AddDays(-30))
                .ToListAsync();

            ViewBag.TotalReports = allMtdReports.Count;
            ViewBag.AvgScore = allMtdReports.Any() ? Math.Round(allMtdReports.Average(r => r.TotalScore), 1) : 100.0;
            ViewBag.GoodCount = allMtdReports.Count(r => r.KategoriIndex == "Baik");
            ViewBag.FairCount = allMtdReports.Count(r => r.KategoriIndex == "Sedang");
            ViewBag.PoorCount = allMtdReports.Count(r => r.KategoriIndex == "Kurang");

            // TARP Level Statistics
            ViewBag.TarpGreenCount = allMtdReports.Count(r => r.TarpLevel == "GREEN");
            ViewBag.TarpYellowCount = allMtdReports.Count(r => r.TarpLevel == "YELLOW");
            ViewBag.TarpOrangeCount = allMtdReports.Count(r => r.TarpLevel == "ORANGE");
            ViewBag.TarpRedCount = allMtdReports.Count(r => r.TarpLevel == "RED");

            // Distinct pits for filter
            ViewBag.PitList = await _context.GciReports
                .Where(r => !r.IsDeleted && !string.IsNullOrEmpty(r.Pit))
                .Select(r => r.Pit!)
                .Distinct()
                .ToListAsync();

            // Area Types based on Master Specification
            ViewBag.AreaTypeList = new List<string>
            {
                "Highwall", "Lowwall", "Sidewall", "Bench / Jenjang",
                "Disposal Slope", "In-Pit Dump", "Stockpile Embankment", "Sump Slope"
            };

            // Master Areas
            ViewBag.AreaList = await _context.MasterAreas
                .OrderBy(a => a.NamaArea)
                .Select(a => a.NamaArea)
                .Distinct()
                .ToListAsync();

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = (endDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            ViewBag.CurrentSearch = search ?? "";
            ViewBag.CurrentPit = pit ?? "";
            ViewBag.CurrentAreaType = areaType ?? "";
            ViewBag.CurrentKategori = kategori ?? "";
            ViewBag.CurrentTarpLevel = tarpLevel ?? "";
            ViewBag.UserNik = userNik;
            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "Geotechnical";
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
            string? pit,
            string? namaLereng,
            string? areaType,
            string? shift,
            string? latitude,
            string? longitude,
            int skorGeometriLereng,
            int skorKondisiCrestToe,
            int skorTensionCrack,
            int skorKestabilanBatuan,
            int skorRembesanDrainase,
            int skorPemantauanSensor,
            int skorDemarkasiZona,
            int skorKepatuhanRekomendasi,
            string? manualTarpLevel,
            string? catatan,
            string? tindakanPerbaikan,
            string? pic,
            string? picNik,
            string? judulTemuan,
            DateTime? dueDate,
            string? jenisInspeksi,
            string? pjaNama,
            string? pjaNik,
            double? elevasiRl,
            double? tinggiJenjang,
            double? lebarBerm,
            double? actualFaceAngle,
            double? designFaceAngle,
            double? overallSlopeAngle,
            string? kondisiDesain,
            string? kondisiRetakan,
            string? kondisiRockfall,
            string? kondisiBulging,
            string? kondisiRembesan,
            double? debitRembesan,
            string? kondisiErosi,
            string? kondisiSettlement,
            double? lebarRetakanMm,
            double? panjangRetakanM,
            double? kedalamanRetakanM,
            string? arahRetakan,
            string? pertumbuhanRetakan,
            string? kecepatanRetakan,
            string? statusWorkflow,
            IFormFile? foto,
            IFormFile? fotoTemuan,
            IFormFile? fotoRekomendasi,
            IFormFile? petaHazard,
            IFormFile? petaMonitoring,
            IFormFile? petaDesain)
        {
            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "Geotechnical";
                var userComp = User.FindFirst("Company")?.Value ?? "PT INDEXIM COALINDO";
                int.TryParse(User.FindFirst("CompanyId")?.Value, out int compId);

                TimeSpan waktu = DateTime.Now.TimeOfDay;
                if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
                {
                    waktu = parsedWaktu;
                }

                // Weighted calculation (Total 100%):
                // 1. Geometri & Kemiringan Lereng (15%)
                // 2. Kondisi Crest, Toe & Catch Berm (15%)
                // 3. Bebas Retakan Tarik & Amblesan (15%)
                // 4. Kestabilan Batuan, Bulging & Rockfall (15%)
                // 5. Kontrol Rembesan Air & Drainase (10%)
                // 6. Pemantauan Sensor & Deformasi (10%)
                // 7. Demarkasi Exclusion Zone & Tanggap Darurat (10%)
                // 8. Kepatuhan Rekomendasi Geoteknik (10%)
                double totalScore = (skorGeometriLereng * 0.15) +
                                    (skorKondisiCrestToe * 0.15) +
                                    (skorTensionCrack * 0.15) +
                                    (skorKestabilanBatuan * 0.15) +
                                    (skorRembesanDrainase * 0.10) +
                                    (skorPemantauanSensor * 0.10) +
                                    (skorDemarkasiZona * 0.10) +
                                    (skorKepatuhanRekomendasi * 0.10);
                totalScore = Math.Round(totalScore, 1);

                string kategoriIndex = totalScore >= 80 ? "Baik" : (totalScore >= 65 ? "Sedang" : "Kurang");

                // Determine TARP Level:
                string autoTarpLevel = "GREEN";
                int minParam = Math.Min(skorGeometriLereng,
                               Math.Min(skorKondisiCrestToe,
                               Math.Min(skorTensionCrack,
                               Math.Min(skorKestabilanBatuan,
                               Math.Min(skorRembesanDrainase,
                               Math.Min(skorPemantauanSensor,
                               Math.Min(skorDemarkasiZona, skorKepatuhanRekomendasi)))))));

                if (minParam <= 40 || totalScore < 50)
                {
                    autoTarpLevel = "RED";
                }
                else if (minParam <= 60 || totalScore < 70)
                {
                    autoTarpLevel = "ORANGE";
                }
                else if (minParam <= 75 || totalScore < 85)
                {
                    autoTarpLevel = "YELLOW";
                }

                string finalTarp = autoTarpLevel;
                if (!string.IsNullOrEmpty(manualTarpLevel))
                {
                    finalTarp = manualTarpLevel.ToUpper().Trim();
                }

                string? fotoUrl = null;
                if (foto != null && foto.Length > 0)
                {
                    fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "gci");
                }

                string? petaHazardUrl = null;
                if (petaHazard != null && petaHazard.Length > 0)
                {
                    petaHazardUrl = await _imageUploadService.UploadAndCompressImageAsync(petaHazard, "gci_map");
                }

                string? petaMonitoringUrl = null;
                if (petaMonitoring != null && petaMonitoring.Length > 0)
                {
                    petaMonitoringUrl = await _imageUploadService.UploadAndCompressImageAsync(petaMonitoring, "gci_map");
                }

                string? petaDesainUrl = null;
                if (petaDesain != null && petaDesain.Length > 0)
                {
                    petaDesainUrl = await _imageUploadService.UploadAndCompressImageAsync(petaDesain, "gci_map");
                }

                string? fotoTemuanUrl = null;
                if (fotoTemuan != null && fotoTemuan.Length > 0)
                {
                    fotoTemuanUrl = await _imageUploadService.UploadAndCompressImageAsync(fotoTemuan, "gci");
                }

                string? fotoRekomendasiUrl = null;
                if (fotoRekomendasi != null && fotoRekomendasi.Length > 0)
                {
                    fotoRekomendasiUrl = await _imageUploadService.UploadAndCompressImageAsync(fotoRekomendasi, "gci");
                }

                if (id.HasValue && id.Value > 0)
                {
                    var existing = await _context.GciReports.FindAsync(id.Value);
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
                    existing.Pit = pit;
                    existing.NamaLereng = namaLereng;
                    existing.AreaType = areaType ?? "Highwall";
                    existing.Shift = shift;
                    existing.Latitude = latitude;
                    existing.Longitude = longitude;
                    existing.JenisInspeksi = jenisInspeksi ?? "Inspeksi Rutin";
                    existing.PjaNama = pjaNama;
                    existing.PjaNik = pjaNik;
                    existing.ElevasiRl = elevasiRl ?? 245.0;
                    existing.TinggiJenjang = tinggiJenjang ?? 15.5;
                    existing.LebarBerm = lebarBerm ?? 8.0;
                    existing.ActualFaceAngle = actualFaceAngle ?? 68.0;
                    existing.DesignFaceAngle = designFaceAngle ?? 70.0;
                    existing.OverallSlopeAngle = overallSlopeAngle ?? 42.0;
                    existing.KondisiDesain = kondisiDesain ?? "Sesuai";
                    existing.KondisiRetakan = kondisiRetakan ?? "Tidak Ada";
                    existing.KondisiRockfall = kondisiRockfall ?? "Tidak Ada";
                    existing.KondisiBulging = kondisiBulging ?? "Tidak Ada";
                    existing.KondisiRembesan = kondisiRembesan ?? "Tidak Ada";
                    existing.DebitRembesan = debitRembesan;
                    existing.KondisiErosi = kondisiErosi ?? "Tidak Ada";
                    existing.KondisiSettlement = kondisiSettlement ?? "Tidak Ada";
                    existing.LebarRetakanMm = lebarRetakanMm;
                    existing.PanjangRetakanM = panjangRetakanM;
                    existing.KedalamanRetakanM = kedalamanRetakanM;
                    existing.ArahRetakan = arahRetakan;
                    existing.PertumbuhanRetakan = pertumbuhanRetakan;
                    existing.KecepatanRetakan = kecepatanRetakan;
                    existing.StatusWorkflow = statusWorkflow ?? "Menunggu Review PJA";
                    existing.SkorGeometriLereng = skorGeometriLereng;
                    existing.SkorKondisiCrestToe = skorKondisiCrestToe;
                    existing.SkorTensionCrack = skorTensionCrack;
                    existing.SkorKestabilanBatuan = skorKestabilanBatuan;
                    existing.SkorRembesanDrainase = skorRembesanDrainase;
                    existing.SkorPemantauanSensor = skorPemantauanSensor;
                    existing.SkorDemarkasiZona = skorDemarkasiZona;
                    existing.SkorKepatuhanRekomendasi = skorKepatuhanRekomendasi;
                    existing.TotalScore = totalScore;
                    existing.KategoriIndex = kategoriIndex;
                    existing.TarpLevel = finalTarp;
                    existing.Catatan = catatan;
                    existing.TindakanPerbaikan = tindakanPerbaikan;
                    existing.Pic = pic;
                    existing.PicNik = picNik;
                    existing.JudulTemuan = judulTemuan;
                    existing.DueDate = dueDate;
                    existing.UpdatedAt = DateTime.Now;

                    if (!string.IsNullOrEmpty(fotoUrl)) existing.FotoUrl = fotoUrl;
                    if (!string.IsNullOrEmpty(fotoTemuanUrl)) existing.FotoTemuanUrl = fotoTemuanUrl;
                    if (!string.IsNullOrEmpty(fotoRekomendasiUrl)) existing.FotoRekomendasiUrl = fotoRekomendasiUrl;
                    if (!string.IsNullOrEmpty(petaHazardUrl)) existing.PetaHazardUrl = petaHazardUrl;
                    if (!string.IsNullOrEmpty(petaMonitoringUrl)) existing.PetaMonitoringUrl = petaMonitoringUrl;
                    if (!string.IsNullOrEmpty(petaDesainUrl)) existing.PetaDesainUrl = petaDesainUrl;

                    _context.GciReports.Update(existing);
                    await _context.SaveChangesAsync();

                    // Notifikasi jika PIC ditugaskan/diperbarui
                    if (!string.IsNullOrEmpty(picNik))
                    {
                        var notif = new Notification
                        {
                            RecipientNik = picNik.Trim(),
                            Title = $"Tugas Tindakan Geoteknik: {namaLereng ?? lokasi}",
                            Message = $"Anda ditugaskan sebagai PIC perbaikan geoteknik di {namaLereng ?? lokasi}. Tindakan: {(string.IsNullOrEmpty(tindakanPerbaikan) ? (judulTemuan ?? "Inspeksi Geoteknik") : tindakanPerbaikan)}. Target: {(dueDate.HasValue ? dueDate.Value.ToString("dd MMM yyyy") : "ASAP")}.",
                            Url = "/Gci/Index",
                            IsRead = false,
                            NotifType = "actionplan_new",
                            CreatedAt = DateTime.Now
                        };
                        _context.Notifications.Add(notif);
                        await _context.SaveChangesAsync();
                    }

                    TempData["SuccessMessage"] = $"Laporan GCI di {namaLereng ?? lokasi} berhasil diperbarui! Skor Index: {totalScore}% ({kategoriIndex}) &bull; TARP {finalTarp}";
                }
                else
                {
                    var report = new GciReport
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
                        Pit = pit,
                        NamaLereng = namaLereng,
                        AreaType = areaType ?? "Highwall",
                        Shift = shift,
                        Latitude = latitude,
                        Longitude = longitude,
                        JenisInspeksi = jenisInspeksi ?? "Inspeksi Rutin",
                        PjaNama = pjaNama,
                        PjaNik = pjaNik,
                        ElevasiRl = elevasiRl ?? 245.0,
                        TinggiJenjang = tinggiJenjang ?? 15.5,
                        LebarBerm = lebarBerm ?? 8.0,
                        ActualFaceAngle = actualFaceAngle ?? 68.0,
                        DesignFaceAngle = designFaceAngle ?? 70.0,
                        OverallSlopeAngle = overallSlopeAngle ?? 42.0,
                        KondisiDesain = kondisiDesain ?? "Sesuai",
                        KondisiRetakan = kondisiRetakan ?? "Tidak Ada",
                        KondisiRockfall = kondisiRockfall ?? "Tidak Ada",
                        KondisiBulging = kondisiBulging ?? "Tidak Ada",
                        KondisiRembesan = kondisiRembesan ?? "Tidak Ada",
                        DebitRembesan = debitRembesan,
                        KondisiErosi = kondisiErosi ?? "Tidak Ada",
                        KondisiSettlement = kondisiSettlement ?? "Tidak Ada",
                        LebarRetakanMm = lebarRetakanMm,
                        PanjangRetakanM = panjangRetakanM,
                        KedalamanRetakanM = kedalamanRetakanM,
                        ArahRetakan = arahRetakan,
                        PertumbuhanRetakan = pertumbuhanRetakan,
                        KecepatanRetakan = kecepatanRetakan,
                        StatusWorkflow = statusWorkflow ?? "Menunggu Review PJA",
                        SkorGeometriLereng = skorGeometriLereng,
                        SkorKondisiCrestToe = skorKondisiCrestToe,
                        SkorTensionCrack = skorTensionCrack,
                        SkorKestabilanBatuan = skorKestabilanBatuan,
                        SkorRembesanDrainase = skorRembesanDrainase,
                        SkorPemantauanSensor = skorPemantauanSensor,
                        SkorDemarkasiZona = skorDemarkasiZona,
                        SkorKepatuhanRekomendasi = skorKepatuhanRekomendasi,
                        TotalScore = totalScore,
                        KategoriIndex = kategoriIndex,
                        TarpLevel = finalTarp,
                        Catatan = catatan,
                        TindakanPerbaikan = tindakanPerbaikan,
                        Pic = pic,
                        PicNik = picNik,
                        JudulTemuan = judulTemuan,
                        DueDate = dueDate,
                        FotoUrl = fotoUrl,
                        FotoTemuanUrl = fotoTemuanUrl,
                        FotoRekomendasiUrl = fotoRekomendasiUrl,
                        PetaHazardUrl = petaHazardUrl,
                        PetaMonitoringUrl = petaMonitoringUrl,
                        PetaDesainUrl = petaDesainUrl,
                        IsDeleted = false,
                        CreatedAt = DateTime.Now
                    };

                    _context.GciReports.Add(report);
                    await _context.SaveChangesAsync();

                    // Notifikasi ke PIC yang ditugaskan
                    if (!string.IsNullOrEmpty(picNik))
                    {
                        var notifPic = new Notification
                        {
                            RecipientNik = picNik.Trim(),
                            Title = $"Tugas Tindakan Geoteknik: {namaLereng ?? lokasi}",
                            Message = $"Anda ditugaskan sebagai PIC tindakan geoteknik di {namaLereng ?? lokasi}. Tindakan: {(string.IsNullOrEmpty(tindakanPerbaikan) ? (judulTemuan ?? "Inspeksi Geoteknik") : tindakanPerbaikan)}. Target: {(dueDate.HasValue ? dueDate.Value.ToString("dd MMM yyyy") : "ASAP")}.",
                            Url = "/Gci/Index",
                            IsRead = false,
                            NotifType = "actionplan_new",
                            CreatedAt = DateTime.Now
                        };
                        _context.Notifications.Add(notifPic);
                    }

                    // Notifikasi ke PJA untuk review
                    if (!string.IsNullOrEmpty(pjaNik) && pjaNik.Trim() != picNik?.Trim())
                    {
                        var notifPja = new Notification
                        {
                            RecipientNik = pjaNik.Trim(),
                            Title = $"Review Laporan Geoteknik: {namaLereng ?? lokasi}",
                            Message = $"Laporan inspeksi geoteknik baru di {namaLereng ?? lokasi} (TARP {finalTarp}) memerlukan review PJA.",
                            Url = "/Gci/Index",
                            IsRead = false,
                            NotifType = "inspection_new",
                            CreatedAt = DateTime.Now
                        };
                        _context.Notifications.Add(notifPja);
                    }

                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = $"Laporan Geotechnical Condition Index (GCI) berhasil disimpan! Skor Index: {totalScore}% ({kategoriIndex}) &bull; TARP {finalTarp}";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Terjadi kesalahan sistem saat menyimpan laporan: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchEmployees(string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
                return Json(new List<object>());

            var query = q.Trim().ToLower();
            var employees = await (from k in _context.Karyawans
                                   join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                   join j in _context.Jabatans on k.IdJabatan equals j.JabatanId into jg
                                   from j in jg.DefaultIfEmpty()
                                   join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                   from d in dg.DefaultIfEmpty()
                                   join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                                   from c in cg.DefaultIfEmpty()
                                   where k.StatusAktif == true &&
                                         (p.NamaLengkap.ToLower().Contains(query) ||
                                          k.NoNik.ToLower().Contains(query) ||
                                          (c != null && c.NamaPerusahaan != null && c.NamaPerusahaan.ToLower().Contains(query)) ||
                                          (c != null && c.KodePerusahaan != null && c.KodePerusahaan.ToLower().Contains(query)))
                                   select new
                                   {
                                       nik = k.NoNik,
                                       nama = p.NamaLengkap,
                                       jabatan = j != null ? j.NamaJabatan : "",
                                       departemen = d != null ? d.NamaDepartemen : "",
                                       perusahaan = c != null ? c.NamaPerusahaan : ""
                                   })
                                   .Take(25)
                                   .ToListAsync();

            return Json(employees);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? string.Empty;
            var report = await _context.GciReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            if (report.Nik != userNik && !CompanyHierarchyService.IsAdminUser(User))
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki hak untuk menghapus data ini.";
                return RedirectToAction(nameof(Index));
            }

            report.IsDeleted = true;
            report.UpdatedAt = DateTime.Now;
            _context.GciReports.Update(report);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan GCI berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = (endDate ?? DateTime.Today).AddDays(1).AddTicks(-1);

            var reports = await _context.GciReports
                .Where(r => !r.IsDeleted && r.Tanggal >= start && r.Tanggal <= end)
                .OrderByDescending(r => r.Tanggal)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Laporan GCI");

            // Header Style
            ws.Cell(1, 1).Value = "LAPORAN GEOTECHNICAL CONDITION INDEX (GCI)";
            ws.Range(1, 1, 1, 23).Merge().Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.Purple);
            ws.Cell(2, 1).Value = $"Periode: {start:dd MMM yyyy} s/d {end:dd MMM yyyy} | PT INDEXIM COALINDO";
            ws.Range(2, 1, 2, 23).Merge().Style.Font.SetItalic().Font.SetFontSize(10);

            string[] headers = new string[]
            {
                "No", "Tanggal", "Waktu", "NIK", "Nama Pelapor", "Perusahaan", "Pit", "Nama Lereng", "Tipe Area", "Area", "Lokasi",
                "Geometri Lereng (15%)", "Crest & Catch Berm (15%)", "Tension Crack (15%)", "Kestabilan Batuan (15%)",
                "Rembesan & Drainase (10%)", "Sensor Monitoring (10%)", "Exclusion Zone (10%)", "Kepatuhan Rekomendasi (10%)",
                "Skor Total GCI", "Kategori Index", "TARP Status", "PIC"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = ws.Cell(4, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.DarkSlateBlue);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }

            int rowIdx = 5;
            int counter = 1;
            foreach (var r in reports)
            {
                ws.Cell(rowIdx, 1).Value = counter++;
                ws.Cell(rowIdx, 2).Value = r.Tanggal.ToString("yyyy-MM-dd");
                ws.Cell(rowIdx, 3).Value = r.Waktu.ToString(@"hh\:mm");
                ws.Cell(rowIdx, 4).Value = r.Nik;
                ws.Cell(rowIdx, 5).Value = r.Nama;
                ws.Cell(rowIdx, 6).Value = r.Perusahaan ?? "-";
                ws.Cell(rowIdx, 7).Value = r.Pit ?? "-";
                ws.Cell(rowIdx, 8).Value = r.NamaLereng ?? "-";
                ws.Cell(rowIdx, 9).Value = r.AreaType ?? "-";
                ws.Cell(rowIdx, 10).Value = r.Area;
                ws.Cell(rowIdx, 11).Value = r.Lokasi;
                ws.Cell(rowIdx, 12).Value = r.SkorGeometriLereng;
                ws.Cell(rowIdx, 13).Value = r.SkorKondisiCrestToe;
                ws.Cell(rowIdx, 14).Value = r.SkorTensionCrack;
                ws.Cell(rowIdx, 15).Value = r.SkorKestabilanBatuan;
                ws.Cell(rowIdx, 16).Value = r.SkorRembesanDrainase;
                ws.Cell(rowIdx, 17).Value = r.SkorPemantauanSensor;
                ws.Cell(rowIdx, 18).Value = r.SkorDemarkasiZona;
                ws.Cell(rowIdx, 19).Value = r.SkorKepatuhanRekomendasi;
                ws.Cell(rowIdx, 20).Value = r.TotalScore;
                ws.Cell(rowIdx, 21).Value = r.KategoriIndex;
                ws.Cell(rowIdx, 22).Value = r.TarpLevel;
                ws.Cell(rowIdx, 23).Value = r.Pic ?? "-";

                // Color coding for TARP
                var tarpCell = ws.Cell(rowIdx, 22);
                if (r.TarpLevel == "RED") tarpCell.Style.Font.SetBold().Font.SetFontColor(XLColor.Red);
                else if (r.TarpLevel == "ORANGE") tarpCell.Style.Font.SetBold().Font.SetFontColor(XLColor.Orange);
                else if (r.TarpLevel == "YELLOW") tarpCell.Style.Font.SetBold().Font.SetFontColor(XLColor.Amber);
                else tarpCell.Style.Font.SetBold().Font.SetFontColor(XLColor.Green);

                rowIdx++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Laporan_GCI_{DateTime.Today:yyyyMMdd}.xlsx");
        }

        private static int GetTarpRank(string level)
        {
            return level.ToUpper() switch
            {
                "RED" => 4,
                "ORANGE" => 3,
                "YELLOW" => 2,
                _ => 1 // GREEN
            };
        }
    }
}
