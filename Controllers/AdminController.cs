using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CompanyHierarchyService _companyHierarchyService;
        private static readonly string[] AllowedWorksheetNames =
        {
            "Hazard",
            "Inspection",
            "Observation",
            "Coaching",
            "Safety Talk",
            "P5M"
        };
        private const long MaxExcelUploadBytes = 10 * 1024 * 1024;
        private const int MaxRowsPerSheet = 5000;

        private static bool _isBulkAuditing = false;
        private static int _bulkTotal = 0;
        private static int _bulkProcessed = 0;
        private static string _bulkStatusMsg = "";

        private class TempAuditItem
        {
            public int Id { get; set; }
            public string? Desc { get; set; }
        }

        public AdminController(AppDbContext context, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _companyHierarchyService = companyHierarchyService;
        }

        private bool IsAuthorizedUser()
        {
            if (User.IsInRole("Admin")) return true;
            var company = User.FindFirst("Company")?.Value;
            return string.Equals(company?.Trim(), "PT KALIMANTAN PRIMA PERSADA", StringComparison.OrdinalIgnoreCase);
        }

        public IActionResult Index()
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }
            ViewData["HeaderTitle"] = "Admin Area";
            ViewData["ActiveTab"] = "Admin";
            return View();
        }

        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }
            using var wb = new XLWorkbook();
            
            void StyleHeader(IXLWorksheet ws, int rowNum)
            {
                var row = ws.Row(rowNum);
                row.Style.Font.Bold = true;
                row.Style.Font.FontColor = XLColor.White;
                row.Style.Fill.BackgroundColor = XLColor.FromHtml("#0284c7");
                row.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.SheetView.FreezeRows(rowNum);
                ws.Columns().AdjustToContents();
            }

            var wsHazard = wb.Worksheets.Add("Hazard");
            wsHazard.Cell(1, 1).Value = "Filter diterapkan:\n(Contoh Filter)";
            wsHazard.Cell(2, 1).InsertData(new object[][] {
                new object[] { "Foto Temuan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Kategori Temuan", "Temuan", "Kategori Bahaya", "Jenis Bahaya", "Jenis Ketidaksesuaian", "Tingkat Resiko", "Perbaikan", "Tindakan Perbaikan", "PJA", "NIK PJA", "Departemen PJA", "Status Temuan", "PIC", "NIK PIC", "Departemen PIC", "Rencana Perbaikan", "Tanggal Rencana Perbaikan", "Perbaikan", "Tanggal Perbaikan", "Overdue", "Alasan Overdue", "Foto Perbaikan" },
                new object[] { "http://apiis.idcapps.net/uploads/example_hazard.jpg", new DateTime(2026, 7, 15), new TimeSpan(8, 30, 0), "Budi Santoso (CONTOH)", "CONTOH01", "Safety", "Pit Area", "Ramp A", "Samping Pos Pantau 2", "KTA", "Ditemukan ceceran oli di jalan ramp", "Fisik", "Kimia", "KTA", "Medium", "Diberi pasir/tanah penyerap", "Melakukan pembersihan berkala", "Andi Pratama", "PJA01", "Operation", "Open", "Joko Susilo", "PIC01", "Maintenance", "Pembersihan area ceceran", new DateTime(2026, 7, 16), "", "", "No", "", "" }
            });
            StyleHeader(wsHazard, 2);

            var wsInsp = wb.Worksheets.Add("Inspection");
            wsInsp.Cell(1, 1).Value = "Filter diterapkan:\n(Contoh Filter)";
            wsInsp.Cell(3, 1).InsertData(new object[][] {
                new object[] { "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Jenis Inspeksi", "PJA", "NIK PJA", "Departemen PJA", "Kategori Temuan", "Detil Temuan", "Status", "PIC", "NIK PIC", "Departemen PIC", "Rencana Perbaikan", "Tanggal Rencana Perbaikan", "Perbaikan", "Tanggal Perbaikan", "Overdue", "Alasan Overdue", "Foto Temuan", "Foto Perbaikan" },
                new object[] { new DateTime(2026, 7, 15), new TimeSpan(9, 0, 0), "Budi Santoso (CONTOH)", "CONTOH01", "Safety", "Pit Area", "Ramp A", "Samping Pos Pantau 2", "Inspeksi Bersama", "Andi Pratama", "PJA01", "Operation", "KTA", "Ceceran oli di area workshop", "Open", "Joko Susilo", "PIC01", "Maintenance", "Pembersihan ceceran oli", new DateTime(2026, 7, 16), "", "", "No", "", "http://apiis.idcapps.net/uploads/example_inspection.jpg", "" }
            });
            StyleHeader(wsInsp, 3);

            var wsObs = wb.Worksheets.Add("Observation");
            wsObs.Cell(3, 1).InsertData(new object[][] {
                new object[] { "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Kegiatan Yang Diamati", "Departemen Yang Diamati", "Dokumen Pendukung", "Resiko Kritis", "Tingkat Resiko", "Perihal Yang Diamati", "Hasil Observasi" },
                new object[] { new DateTime(2026, 7, 15), new TimeSpan(10, 0, 0), "Budi Santoso (CONTOH)", "CONTOH01", "Safety", "Pit Area", "Ramp A", "Samping Pos Pantau 2", "Pengelasan tanpa kacamata pelindung", "Maintenance", "JSA Pengelasan", "Cidera Mata", "High", "Alat Pelindung Diri (APD)", "Pekerja langsung menggunakan kacamata pelindung" }
            });
            StyleHeader(wsObs, 3);

            var wsCoach = wb.Worksheets.Add("Coaching");
            wsCoach.Cell(3, 1).InsertData(new object[][] {
                new object[] { "Foto Kegiatan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Tema", "Judul", "Feedback" },
                new object[] { "http://apiis.idcapps.net/uploads/example_coaching.jpg", new DateTime(2026, 7, 15), new TimeSpan(11, 0, 0), "Budi Santoso (CONTOH)", "CONTOH01", "Safety", "Pit Area", "Ramp A", "Samping Pos Pantau 2", "Pentingnya APD", "Coaching APD Las", "Pekerja menyadari bahaya bekerja tanpa APD" }
            });
            StyleHeader(wsCoach, 3);

            var wsSt = wb.Worksheets.Add("Safety Talk");
            wsSt.Cell(3, 1).InsertData(new object[][] {
                new object[] { "Foto Diri", "Foto Kegiatan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Judul", "Keterangan" },
                new object[] { "http://apiis.idcapps.net/uploads/example_self.jpg", "http://apiis.idcapps.net/uploads/example_safetytalk.jpg", new DateTime(2026, 7, 15), new TimeSpan(7, 30, 0), "Budi Santoso (CONTOH)", "CONTOH01", "Safety", "Pit Area", "Ramp A", "Samping Pos Pantau 2", "Sosialisasi JSA & APD", "Penjelasan JSA sebelum mulai bekerja" }
            });
            StyleHeader(wsSt, 3);

            var wsP5 = wb.Worksheets.Add("P5M");
            wsP5.Cell(3, 1).InsertData(new object[][] {
                new object[] { "Foto Kegiatan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Topik", "Judul", "Keterangan", "List Pertanyaan", "Jawaban", "Catatan" },
                new object[] { "http://apiis.idcapps.net/uploads/example_p5m.jpg", new DateTime(2026, 7, 15), new TimeSpan(7, 0, 0), "Budi Santoso (CONTOH)", "CONTOH01", "Safety", "Pit Area", "Ramp A", "Samping Pos Pantau 2", "Safety First", "P5M Pagi", "Briefing pagi aspek keselamatan kerja", "Apakah APD dalam kondisi layak pakai?", "Ya, semua layak pakai", "Seluruh kru siap bekerja" }
            });
            StyleHeader(wsP5, 3);

            using var stream = new System.IO.MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Upload_SAP.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadExcel(IFormFile excelFile)
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["ErrorMessage"] = "Silakan pilih file Excel (.xlsx)";
                return RedirectToAction(nameof(Index));
            }

            if (!excelFile.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx";
                return RedirectToAction(nameof(Index));
            }

            if (excelFile.Length > MaxExcelUploadBytes)
            {
                TempData["ErrorMessage"] = "Ukuran file Excel maksimal 10 MB.";
                return RedirectToAction(nameof(Index));
            }

            int addedHazards = 0, addedInspections = 0, addedActionPlans = 0, addedSafetyTalks = 0, addedP5m = 0;
            int skippedHazards = 0, skippedInspections = 0, skippedActionPlans = 0, skippedSafetyTalks = 0, skippedP5m = 0;
            int failedHazards = 0, failedInspections = 0, failedSafetyTalks = 0, failedP5m = 0;
            var validationErrors = new List<string>();
            var duplicateLogs = new List<string>();

            try
            {
                using var stream = excelFile.OpenReadStream();
                if (!IsZipBasedExcel(stream))
                {
                    TempData["ErrorMessage"] = "File tidak valid. Pastikan file benar-benar Excel .xlsx.";
                    return RedirectToAction(nameof(Index));
                }

                stream.Position = 0;
                using var wb = new XLWorkbook(stream);

                var worksheetCount = wb.Worksheets.Count;
                if (worksheetCount == 0 || worksheetCount > AllowedWorksheetNames.Length)
                {
                    TempData["ErrorMessage"] = "Workbook tidak valid atau jumlah sheet tidak sesuai template.";
                    return RedirectToAction(nameof(Index));
                }

                if (wb.Worksheets.Any(w => !AllowedWorksheetNames.Contains(w.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    TempData["ErrorMessage"] = "Ditemukan sheet yang tidak diizinkan. Gunakan template resmi upload.";
                    return RedirectToAction(nameof(Index));
                }

                // 1. Hazard
                var wsHazard = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Hazard", StringComparison.OrdinalIgnoreCase));
                if (wsHazard != null)
                {
                    EnsureWorksheetRowLimit(wsHazard);
                    foreach (var row in wsHazard.RowsUsed().Skip(2)) // Data starts at row 3 (if header at row 2)
                    {
                        var nik = SafeTruncate(GetString(row, 5), 50);
                        var tanggal = GetDate(row, 2);
                        
                        if (string.IsNullOrEmpty(nik) && !tanggal.HasValue) continue;
                        if (nik != null && nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        if (string.IsNullOrEmpty(nik))
                        {
                            validationErrors.Add($"[Hazard] Baris {row.RowNumber()}: NIK tidak boleh kosong.");
                            failedHazards++;
                            continue;
                        }
                        if (!tanggal.HasValue)
                        {
                            validationErrors.Add($"[Hazard] Baris {row.RowNumber()}: Kolom Tanggal kosong atau format salah.");
                            failedHazards++;
                            continue;
                        }

                        string temuan = GetString(row, 11) ?? "-";
                        
                        var existingTemuans = _context.HazardReports
                            .Where(h => h.Nik == nik && h.Tanggal == tanggal.Value.Date)
                            .Select(h => h.Temuan)
                            .ToList();
                        bool isDuplicateHazard = existingTemuans.Any(existing => CalculateSimilarity(temuan, existing) >= 0.80);

                        if (!isDuplicateHazard)
                        {
                            _context.HazardReports.Add(new HazardReport
                            {
                                FotoTemuan = SafeTruncate(GetString(row, 1), 500),
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 3) ?? TimeSpan.Zero,
                                Nama = SafeTruncate(GetString(row, 4) ?? "", 150),
                                Nik = nik,
                                Departemen = SafeTruncate(GetString(row, 6), 150),
                                Area = SafeTruncate(GetString(row, 7), 150),
                                Lokasi = SafeTruncate(GetString(row, 8), 150),
                                DetilLokasi = SafeTruncate(GetString(row, 9), 250),
                                Temuan = temuan,
                                KategoriBahaya = SafeTruncate(GetString(row, 12), 100),
                                JenisBahaya = SafeTruncate(GetString(row, 13), 100),
                                JenisKetidaksesuaian = SafeTruncate(GetString(row, 14), 150),
                                TingkatResiko = SafeTruncate(GetString(row, 15), 50),
                                Perbaikan = GetString(row, 16),
                                TindakanPerbaikan = GetString(row, 17),
                                Pja = SafeTruncate(GetString(row, 18), 150),
                                NikPja = SafeTruncate(GetString(row, 19), 50),
                                DepartemenPja = SafeTruncate(GetString(row, 20), 100), // Max DB column is 100
                                StatusTemuan = SafeTruncate(GetString(row, 21) ?? "Open", 50),
                                CreatedAt = DateTime.Now
                            });
                            addedHazards++;
                        }
                        else 
                        { 
                            skippedHazards++; 
                            duplicateLogs.Add($"[Hazard] Baris {row.RowNumber()}: Temuan serupa dengan data yang sudah ada (NIK: {nik}, Tanggal: {tanggal.Value.ToString("dd/MM/yyyy")}, Temuan: \"{temuan}\").");
                        }

                        // Parse Action Plan from Hazard
                        var pic = SafeTruncate(GetString(row, 22), 150);
                        var status = SafeTruncate(GetString(row, 21), 50);
                        if (!string.IsNullOrEmpty(pic) || status != null)
                        {
                            string apDetil = temuan;
                            var existingApDetails = _context.ActionPlans
                                .Where(a => a.Nik == nik && a.Tanggal == tanggal.Value.Date)
                                .Select(a => a.DetilTemuan)
                                .ToList();
                            bool isDuplicateAp = existingApDetails.Any(existing => CalculateSimilarity(apDetil, existing) >= 0.80);

                            if (!isDuplicateAp)
                            {
                                _context.ActionPlans.Add(new ActionPlan
                                {
                                    FotoTemuan = SafeTruncate(GetString(row, 1), 500),
                                    FotoPerbaikan = SafeTruncate(GetString(row, 31), 500),
                                    Tanggal = tanggal.Value.Date,
                                    Waktu = GetTime(row, 3) ?? TimeSpan.Zero,
                                    Nama = SafeTruncate(GetString(row, 4) ?? "", 150),
                                    Nik = nik,
                                    Departemen = SafeTruncate(GetString(row, 6), 150),
                                    Area = SafeTruncate(GetString(row, 7), 150),
                                    Lokasi = SafeTruncate(GetString(row, 8), 150),
                                    DetilLokasi = SafeTruncate(GetString(row, 9), 250),
                                    ItemSap = "Hazard",
                                    KategoriTemuan = SafeTruncate(GetString(row, 10), 150),
                                    DetilTemuan = apDetil,
                                    Status = status ?? "Open",
                                    Pja = SafeTruncate(GetString(row, 18), 150),
                                    NikPja = SafeTruncate(GetString(row, 19), 50),
                                    DepartemenPja = SafeTruncate(GetString(row, 20), 100), // Max DB column is 100
                                    Pic = pic,
                                    NikPic = SafeTruncate(GetString(row, 23), 50),
                                    DepartemenPic = SafeTruncate(GetString(row, 24), 150),
                                    RencanaPerbaikan = GetString(row, 25),
                                    TanggalRencanaPerbaikan = GetDate(row, 26),
                                    Perbaikan = GetString(row, 27),
                                    TanggalPerbaikan = GetDate(row, 28),
                                    Overdue = SafeTruncate(GetString(row, 29), 50),
                                    AlasanOverdue = GetString(row, 30),
                                    CreatedAt = DateTime.Now
                                });
                                addedActionPlans++;
                            }
                            else 
                            { 
                                skippedActionPlans++; 
                                duplicateLogs.Add($"[Action Plan] Baris {row.RowNumber()}: Rencana perbaikan serupa dengan data yang sudah ada (NIK: {nik}, Tanggal: {tanggal.Value.ToString("dd/MM/yyyy")}, Detail: \"{apDetil}\").");
                            }
                        }
                    }
                }

                // 2. Inspection
                var wsInsp = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Inspection", StringComparison.OrdinalIgnoreCase));
                if (wsInsp != null)
                {
                    EnsureWorksheetRowLimit(wsInsp);
                    foreach (var row in wsInsp.RowsUsed().Skip(2)) // Data starts at row 3
                    {
                        var nik = SafeTruncate(GetString(row, 4), 50);
                        var tanggal = GetDate(row, 1);
                        
                        if (string.IsNullOrEmpty(nik) && !tanggal.HasValue) continue;
                        if (nik != null && nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        if (string.IsNullOrEmpty(nik))
                        {
                            validationErrors.Add($"[Inspection] Baris {row.RowNumber()}: NIK tidak boleh kosong.");
                            failedInspections++;
                            continue;
                        }
                        if (!tanggal.HasValue)
                        {
                            validationErrors.Add($"[Inspection] Baris {row.RowNumber()}: Kolom Tanggal kosong atau format salah.");
                            failedInspections++;
                            continue;
                        }

                        string jenis = SafeTruncate(GetString(row, 9) ?? "-", 150);
                        var existingJenis = _context.Inspections
                            .Where(i => i.Nik == nik && i.Tanggal == tanggal.Value.Date)
                            .Select(i => i.JenisInspeksi)
                            .ToList();
                        bool isDuplicateInspection = existingJenis.Any(existing => CalculateSimilarity(jenis, existing) >= 0.80);

                        if (!isDuplicateInspection)
                        {
                            _context.Inspections.Add(new Inspection
                            {
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 2) ?? TimeSpan.Zero,
                                Nama = SafeTruncate(GetString(row, 3) ?? "", 150),
                                Nik = nik,
                                Departemen = SafeTruncate(GetString(row, 5), 150),
                                Area = SafeTruncate(GetString(row, 6), 150),
                                Lokasi = SafeTruncate(GetString(row, 7), 150),
                                DetilLokasi = SafeTruncate(GetString(row, 8), 250),
                                JenisInspeksi = jenis,
                                Pja = SafeTruncate(GetString(row, 10), 150),
                                NikPja = SafeTruncate(GetString(row, 11), 50),
                                DepartemenPja = SafeTruncate(GetString(row, 12), 100),
                                CreatedAt = DateTime.Now
                            });
                            addedInspections++;
                        }
                        else 
                        { 
                            skippedInspections++; 
                            duplicateLogs.Add($"[Inspection] Baris {row.RowNumber()}: Inspeksi serupa dengan data yang sudah ada (NIK: {nik}, Tanggal: {tanggal.Value.ToString("dd/MM/yyyy")}, Jenis: \"{jenis}\").");
                        }

                        // Parse Action Plan from Inspection
                        var apDetil = GetString(row, 14);
                        if (!string.IsNullOrEmpty(apDetil))
                        {
                            var existingApDetails = _context.ActionPlans
                                .Where(a => a.Nik == nik && a.Tanggal == tanggal.Value.Date)
                                .Select(a => a.DetilTemuan)
                                .ToList();
                            bool isDuplicateAp = existingApDetails.Any(existing => CalculateSimilarity(apDetil, existing) >= 0.80);

                            if (!isDuplicateAp)
                            {
                                _context.ActionPlans.Add(new ActionPlan
                                {
                                    FotoTemuan = SafeTruncate(GetString(row, 25), 500),
                                    FotoPerbaikan = SafeTruncate(GetString(row, 26), 500),
                                    Tanggal = tanggal.Value.Date,
                                    Waktu = GetTime(row, 2) ?? TimeSpan.Zero,
                                    Nama = SafeTruncate(GetString(row, 3) ?? "", 150),
                                    Nik = nik,
                                    Departemen = SafeTruncate(GetString(row, 5), 150),
                                    Area = SafeTruncate(GetString(row, 6), 150),
                                    Lokasi = SafeTruncate(GetString(row, 7), 150),
                                    DetilLokasi = SafeTruncate(GetString(row, 8), 250),
                                    ItemSap = "Inspection",
                                    KategoriTemuan = SafeTruncate(GetString(row, 13), 150),
                                    DetilTemuan = apDetil,
                                    Status = SafeTruncate(GetString(row, 15) ?? "Open", 50),
                                    Pja = SafeTruncate(GetString(row, 10), 150),
                                    NikPja = SafeTruncate(GetString(row, 11), 50),
                                    DepartemenPja = SafeTruncate(GetString(row, 12), 100), // Max DB column is 100
                                    Pic = SafeTruncate(GetString(row, 16), 150),
                                    NikPic = SafeTruncate(GetString(row, 17), 50),
                                    DepartemenPic = SafeTruncate(GetString(row, 18), 150),
                                    RencanaPerbaikan = GetString(row, 19),
                                    TanggalRencanaPerbaikan = GetDate(row, 20),
                                    Perbaikan = GetString(row, 21),
                                    TanggalPerbaikan = GetDate(row, 22),
                                    Overdue = SafeTruncate(GetString(row, 23), 50),
                                    AlasanOverdue = GetString(row, 24),
                                    CreatedAt = DateTime.Now
                                });
                                addedActionPlans++;
                            }
                            else
                            {
                                skippedActionPlans++;
                                duplicateLogs.Add($"[Action Plan] Baris {row.RowNumber()}: Rencana perbaikan serupa dengan data yang sudah ada (NIK: {nik}, Tanggal: {tanggal.Value.ToString("dd/MM/yyyy")}, Detail: \"{apDetil}\").");
                            }
                        }
                    }
                }

                // 3. Safety Talk
                var wsSt = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Safety Talk", StringComparison.OrdinalIgnoreCase));
                if (wsSt != null)
                {
                    EnsureWorksheetRowLimit(wsSt);
                    foreach (var row in wsSt.RowsUsed().Skip(2))
                    {
                        var nik = SafeTruncate(GetString(row, 6), 50);
                        var tanggal = GetDate(row, 3);
                        if (string.IsNullOrEmpty(nik) && !tanggal.HasValue) continue;
                        if (nik != null && nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        if (string.IsNullOrEmpty(nik))
                        {
                            validationErrors.Add($"[Safety Talk] Baris {row.RowNumber()}: NIK tidak boleh kosong.");
                            failedSafetyTalks++;
                            continue;
                        }
                        if (!tanggal.HasValue)
                        {
                            validationErrors.Add($"[Safety Talk] Baris {row.RowNumber()}: Kolom Tanggal kosong atau format salah.");
                            failedSafetyTalks++;
                            continue;
                        }

                        string judul = SafeTruncate(GetString(row, 11) ?? "-", 150);
                        var existingJuduls = _context.SafetyTalks
                            .Where(s => s.Nik == nik && s.Tanggal == tanggal.Value.Date)
                            .Select(s => s.Judul)
                            .ToList();
                        bool isDuplicateSt = existingJuduls.Any(existing => CalculateSimilarity(judul, existing) >= 0.80);

                        if (!isDuplicateSt)
                        {
                            _context.SafetyTalks.Add(new SafetyTalk
                            {
                                FotoDiri = SafeTruncate(GetString(row, 1), 500),
                                FotoKegiatan = SafeTruncate(GetString(row, 2), 500),
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 4) ?? TimeSpan.Zero,
                                Nama = SafeTruncate(GetString(row, 5) ?? "", 150),
                                Nik = nik,
                                Departemen = SafeTruncate(GetString(row, 7), 150),
                                Area = SafeTruncate(GetString(row, 8), 150),
                                Lokasi = SafeTruncate(GetString(row, 9), 150),
                                DetilLokasi = SafeTruncate(GetString(row, 10), 250),
                                Judul = judul,
                                Keterangan = GetString(row, 12),
                                CreatedAt = DateTime.Now
                            });
                            addedSafetyTalks++;
                        }
                        else 
                        { 
                            skippedSafetyTalks++; 
                            duplicateLogs.Add($"[Safety Talk] Baris {row.RowNumber()}: Judul Safety Talk serupa dengan data yang sudah ada (NIK: {nik}, Tanggal: {tanggal.Value.ToString("dd/MM/yyyy")}, Judul: \"{judul}\").");
                        }
                    }
                }

                // 4. P5M
                var wsP5 = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("P5M", StringComparison.OrdinalIgnoreCase));
                if (wsP5 != null)
                {
                    EnsureWorksheetRowLimit(wsP5);
                    foreach (var row in wsP5.RowsUsed().Skip(2))
                    {
                        var nik = SafeTruncate(GetString(row, 5), 50);
                        var tanggal = GetDate(row, 2);
                        if (string.IsNullOrEmpty(nik) && !tanggal.HasValue) continue;
                        if (nik != null && nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        if (string.IsNullOrEmpty(nik))
                        {
                            validationErrors.Add($"[P5M] Baris {row.RowNumber()}: NIK tidak boleh kosong.");
                            failedP5m++;
                            continue;
                        }
                        if (!tanggal.HasValue)
                        {
                            validationErrors.Add($"[P5M] Baris {row.RowNumber()}: Kolom Tanggal kosong atau format salah.");
                            failedP5m++;
                            continue;
                        }

                        string judul = SafeTruncate(GetString(row, 11) ?? "-", 150);
                        string pertanyaan = GetString(row, 13) ?? "";
                        var existingP5ms = _context.P5ms
                            .Where(p => p.Nik == nik && p.Tanggal == tanggal.Value.Date)
                            .Select(p => new { p.Judul, p.ListPertanyaan })
                            .ToList();
                        bool isDuplicateP5m = existingP5ms.Any(existing => 
                            CalculateSimilarity(judul, existing.Judul) >= 0.80 &&
                            CalculateSimilarity(pertanyaan, existing.ListPertanyaan) >= 0.80
                        );

                        if (!isDuplicateP5m)
                        {
                            _context.P5ms.Add(new P5m
                            {
                                FotoKegiatan = SafeTruncate(GetString(row, 1), 500),
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 3) ?? TimeSpan.Zero,
                                Nama = SafeTruncate(GetString(row, 4) ?? "", 150),
                                Nik = nik,
                                Departemen = SafeTruncate(GetString(row, 6), 150),
                                Area = SafeTruncate(GetString(row, 7), 150),
                                Lokasi = SafeTruncate(GetString(row, 8), 150),
                                DetilLokasi = SafeTruncate(GetString(row, 9), 250),
                                Topik = SafeTruncate(GetString(row, 10), 150),
                                Judul = judul,
                                Keterangan = GetString(row, 12),
                                ListPertanyaan = pertanyaan,
                                Jawaban = GetString(row, 14),
                                Catatan = GetString(row, 15),
                                CreatedAt = DateTime.Now
                            });
                            addedP5m++;
                        }
                        else 
                        { 
                            skippedP5m++; 
                            duplicateLogs.Add($"[P5M] Baris {row.RowNumber()}: P5M serupa dengan data yang sudah ada (NIK: {nik}, Tanggal: {tanggal.Value.ToString("dd/MM/yyyy")}, Judul: \"{judul}\").");
                        }
                    }
                }

                await _context.SaveChangesAsync();
                
                var result = new
                {
                    isSuccess = true,
                    message = "File Excel berhasil diimpor ke sistem.",
                    addedHazards, skippedHazards, failedHazards,
                    addedInspections, skippedInspections, failedInspections,
                    addedActionPlans, skippedActionPlans,
                    addedSafetyTalks, skippedSafetyTalks, failedSafetyTalks,
                    addedP5m, skippedP5m, failedP5m,
                    validationErrors,
                    duplicateLogs
                };
                TempData["UploadResultJson"] = JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                var result = new
                {
                    isSuccess = false,
                    message = "Gagal memproses berkas Excel: " + ex.Message,
                    addedHazards = 0, skippedHazards = 0, failedHazards = 0,
                    addedInspections = 0, skippedInspections = 0, failedInspections = 0,
                    addedActionPlans = 0, skippedActionPlans = 0,
                    addedSafetyTalks = 0, skippedSafetyTalks = 0, failedSafetyTalks = 0,
                    addedP5m = 0, skippedP5m = 0, failedP5m = 0,
                    validationErrors = new List<string> { ex.Message }
                };
                TempData["UploadResultJson"] = JsonSerializer.Serialize(result);
            }

            return RedirectToAction(nameof(Index));
        }

        private string? GetString(IXLRow row, int col)
        {
            var cell = row.Cell(col);
            if (cell.IsEmpty()) return null;
            return SanitizeExcelString(cell.Value.ToString());
        }

        private string SafeTruncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        private double CalculateSimilarity(string? s, string? t)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t))
                return 0;

            s = s.ToLowerInvariant().Trim();
            t = t.ToLowerInvariant().Trim();

            if (s == t) return 1.0;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            int maxLength = Math.Max(s.Length, t.Length);
            return 1.0 - ((double)d[n, m] / maxLength);
        }
        
        private DateTime? GetDate(IXLRow row, int col)
        {
            var cell = row.Cell(col);
            if (cell.IsEmpty()) return null;
            if (cell.TryGetValue<DateTime>(out var date)) return date;
            return null;
        }

        private TimeSpan? GetTime(IXLRow row, int col)
        {
            var cell = row.Cell(col);
            if (cell.IsEmpty()) return null;
            if (cell.TryGetValue<TimeSpan>(out var time)) return time;
            if (cell.TryGetValue<DateTime>(out var dt)) return dt.TimeOfDay;
            
            string? s = cell.Value.ToString();
            if (!string.IsNullOrEmpty(s) && TimeSpan.TryParse(s, out var ts)) return ts;
            return null;
        }

        private static bool IsZipBasedExcel(System.IO.Stream stream)
        {
            if (!stream.CanRead || !stream.CanSeek || stream.Length < 4)
            {
                return false;
            }

            long originalPosition = stream.Position;
            try
            {
                Span<byte> signature = stackalloc byte[4];
                stream.Position = 0;
                int read = stream.Read(signature);
                return read == 4
                    && signature[0] == 0x50
                    && signature[1] == 0x4B
                    && signature[2] == 0x03
                    && signature[3] == 0x04;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static void EnsureWorksheetRowLimit(IXLWorksheet worksheet)
        {
            int usedRows = worksheet.RowsUsed().Count();
            if (usedRows > MaxRowsPerSheet)
            {
                throw new InvalidOperationException($"Sheet '{worksheet.Name}' melebihi batas {MaxRowsPerSheet} baris.");
            }
        }

        private static string? SanitizeExcelString(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var sanitized = new string(input
                .Trim()
                .Where(ch => !char.IsControl(ch) || ch == '\r' || ch == '\n' || ch == '\t')
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return null;
            }

            if (sanitized[0] == '=' || sanitized[0] == '+' || sanitized[0] == '-' || sanitized[0] == '@')
            {
                sanitized = "'" + sanitized;
            }

            return sanitized;
        }

        // ============================================================
        // Download Report with Embedded Photos
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> DownloadReport(string sapType, DateTime? startDate, DateTime? endDate)
        {
            if (!User.IsInRole("Admin"))
            {
                return Forbid();
            }
            if (string.IsNullOrEmpty(sapType))
            {
                TempData["ErrorMessage"] = "Pilih jenis SAP terlebih dahulu.";
                return RedirectToAction(nameof(Index));
            }

            var start = startDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate ?? DateTime.Now.Date;

            // Company hierarchy filter
            List<int>? allowedIds = null;
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(companyIdStr, out var cid) && cid > 0)
            {
                allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(cid);
            }

            try
            {
                using var wb = new XLWorkbook();
                string fileName;

                switch (sapType.ToLower())
                {
                    case "hazard":
                        await BuildHazardSheet(wb, start, end, allowedIds);
                        fileName = $"Report_Hazard_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "inspection":
                        await BuildInspectionSheet(wb, start, end, allowedIds);
                        fileName = $"Report_Inspection_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "actionplan":
                        await BuildActionPlanSheet(wb, start, end, allowedIds);
                        fileName = $"Report_ActionPlan_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "safetytalk":
                        await BuildSafetyTalkSheet(wb, start, end, allowedIds);
                        fileName = $"Report_SafetyTalk_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "p5m":
                        await BuildP5mSheet(wb, start, end, allowedIds);
                        fileName = $"Report_P5M_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "observation":
                        await BuildObservationSheet(wb, start, end, allowedIds);
                        fileName = $"Report_Observation_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "coaching":
                        await BuildCoachingSheet(wb, start, end, allowedIds);
                        fileName = $"Report_Coaching_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    case "p2h":
                        await BuildP2hSheet(wb, start, end, allowedIds);
                        fileName = $"Report_P2H_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
                        break;
                    default:
                        TempData["ErrorMessage"] = "Jenis SAP tidak valid.";
                        return RedirectToAction(nameof(Index));
                }

                using var stream = new MemoryStream();
                wb.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal membuat report: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // --- Image download helper ---
        private static async Task<byte[]?> DownloadImageBytes(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            // Only allow known image servers
            if (!url.StartsWith("https://apiis.idcapps.net/", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://apiis.idcapps.net/", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://172.16.1.96/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch
            {
                return null;
            }
        }

        private static void EmbedImage(IXLWorksheet ws, int row, int col, byte[]? imageBytes, string name)
        {
            if (imageBytes == null || imageBytes.Length == 0) return;

            try
            {
                using var ms = new MemoryStream(imageBytes);
                var pic = ws.AddPicture(ms, $"{name}_{row}_{col}")
                    .MoveTo(ws.Cell(row, col))
                    .WithSize(120, 90);
                ws.Row(row).Height = 70;
                ws.Column(col).Width = 18;
            }
            catch
            {
                // If image embedding fails, just put URL text
            }
        }

        private static void StyleReportHeader(IXLWorksheet ws, int headerRow, int colCount)
        {
            var range = ws.Range(headerRow, 1, headerRow, colCount);
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#0284c7");
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.SheetView.FreezeRows(headerRow);
        }

        // --- Sheet builders ---

        private async Task BuildHazardSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && h.Tanggal >= start && h.Tanggal <= end);
            if (allowedIds != null)
                query = query.Where(h => h.PerusahaanId.HasValue && allowedIds.Contains(h.PerusahaanId.Value));

            var data = await query.OrderByDescending(h => h.Tanggal).ThenByDescending(h => h.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("Hazard Report");
            var headers = new[] { "No", "Foto Temuan", "Tanggal", "Waktu", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Temuan", "Kategori Bahaya", "Jenis Bahaya", "Jenis Ketidaksesuaian", "Tingkat Resiko", "Perbaikan", "Tindakan Perbaikan", "PJA", "NIK PJA", "Departemen PJA", "Status Temuan" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                // Col 2: Foto - embedded below
                ws.Cell(row, 3).Value = r.Tanggal;
                ws.Cell(row, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 4).Value = r.Waktu;
                ws.Cell(row, 4).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 5).Value = r.Nama;
                ws.Cell(row, 6).Value = r.Nik;
                ws.Cell(row, 7).Value = r.Departemen ?? "";
                ws.Cell(row, 8).Value = r.Area ?? "";
                ws.Cell(row, 9).Value = r.Lokasi ?? "";
                ws.Cell(row, 10).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 11).Value = r.Temuan;
                ws.Cell(row, 12).Value = r.KategoriBahaya ?? "";
                ws.Cell(row, 13).Value = r.JenisBahaya ?? "";
                ws.Cell(row, 14).Value = r.JenisKetidaksesuaian ?? "";
                ws.Cell(row, 15).Value = r.TingkatResiko ?? "";
                ws.Cell(row, 16).Value = r.Perbaikan ?? "";
                ws.Cell(row, 17).Value = r.TindakanPerbaikan ?? "";
                ws.Cell(row, 18).Value = r.Pja ?? "";
                ws.Cell(row, 19).Value = r.NikPja ?? "";
                ws.Cell(row, 20).Value = r.DepartemenPja ?? "";
                ws.Cell(row, 21).Value = r.StatusTemuan;

                var imgBytes = await DownloadImageBytes(r.FotoTemuan);
                if (imgBytes != null)
                    EmbedImage(ws, row, 2, imgBytes, "hazard");
                else
                    ws.Cell(row, 2).Value = r.FotoTemuan ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
        }

        private async Task BuildInspectionSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.Inspections.AsNoTracking()
                .Where(i => !i.IsDeleted && i.Tanggal >= start && i.Tanggal <= end);
            if (allowedIds != null)
                query = query.Where(i => i.PerusahaanId.HasValue && allowedIds.Contains(i.PerusahaanId.Value));

            var data = await query.OrderByDescending(i => i.Tanggal).ThenByDescending(i => i.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("Inspection");
            var headers = new[] { "No", "Tanggal", "Waktu", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Jenis Inspeksi", "PJA", "NIK PJA", "Departemen PJA", "Catatan", "Lampiran Foto" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 2).Value = r.Tanggal;
                ws.Cell(row, 2).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 3).Value = r.Waktu;
                ws.Cell(row, 3).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 4).Value = r.Nama;
                ws.Cell(row, 5).Value = r.Nik;
                ws.Cell(row, 6).Value = r.Departemen ?? "";
                ws.Cell(row, 7).Value = r.Area ?? "";
                ws.Cell(row, 8).Value = r.Lokasi ?? "";
                ws.Cell(row, 9).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 10).Value = r.JenisInspeksi;
                ws.Cell(row, 11).Value = r.Pja ?? "";
                ws.Cell(row, 12).Value = r.NikPja ?? "";
                ws.Cell(row, 13).Value = r.DepartemenPja ?? "";
                ws.Cell(row, 14).Value = r.Catatan ?? "";

                // Parse LampiranJson for photo URLs
                if (!string.IsNullOrEmpty(r.LampiranJson))
                {
                    try
                    {
                        var urls = JsonSerializer.Deserialize<List<string>>(r.LampiranJson);
                        if (urls != null && urls.Count > 0)
                        {
                            var imgBytes = await DownloadImageBytes(urls[0]);
                            if (imgBytes != null)
                                EmbedImage(ws, row, 15, imgBytes, "inspection");
                            else
                                ws.Cell(row, 15).Value = string.Join("; ", urls);
                        }
                    }
                    catch
                    {
                        ws.Cell(row, 15).Value = r.LampiranJson;
                    }
                }
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(15).Width < 18) ws.Column(15).Width = 18;
        }

        private async Task BuildActionPlanSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.ActionPlans.AsNoTracking()
                .Where(a => !a.IsDeleted && a.Tanggal >= start && a.Tanggal <= end);
            if (allowedIds != null)
                query = query.Where(a => a.PerusahaanId.HasValue && allowedIds.Contains(a.PerusahaanId.Value));

            var data = await query.OrderByDescending(a => a.Tanggal).ThenByDescending(a => a.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("Action Plan");
            var headers = new[] { "No", "Foto Temuan", "Foto Perbaikan", "Tanggal", "Waktu", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Item SAP", "Kategori Temuan", "Detil Temuan", "Status", "PJA", "NIK PJA", "Departemen PJA", "PIC", "NIK PIC", "Departemen PIC", "Rencana Perbaikan", "Tgl Rencana Perbaikan", "Perbaikan", "Tgl Perbaikan", "Overdue", "Alasan Overdue" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                // Col 2,3: Foto Temuan / Perbaikan
                ws.Cell(row, 4).Value = r.Tanggal;
                ws.Cell(row, 4).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 5).Value = r.Waktu;
                ws.Cell(row, 5).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 6).Value = r.Nama;
                ws.Cell(row, 7).Value = r.Nik;
                ws.Cell(row, 8).Value = r.Departemen ?? "";
                ws.Cell(row, 9).Value = r.Area ?? "";
                ws.Cell(row, 10).Value = r.Lokasi ?? "";
                ws.Cell(row, 11).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 12).Value = r.ItemSap ?? "";
                ws.Cell(row, 13).Value = r.KategoriTemuan ?? "";
                ws.Cell(row, 14).Value = r.DetilTemuan ?? "";
                ws.Cell(row, 15).Value = r.Status;
                ws.Cell(row, 16).Value = r.Pja ?? "";
                ws.Cell(row, 17).Value = r.NikPja ?? "";
                ws.Cell(row, 18).Value = r.DepartemenPja ?? "";
                ws.Cell(row, 19).Value = r.Pic ?? "";
                ws.Cell(row, 20).Value = r.NikPic ?? "";
                ws.Cell(row, 21).Value = r.DepartemenPic ?? "";
                ws.Cell(row, 22).Value = r.RencanaPerbaikan ?? "";
                if (r.TanggalRencanaPerbaikan.HasValue)
                {
                    ws.Cell(row, 23).Value = r.TanggalRencanaPerbaikan.Value;
                    ws.Cell(row, 23).Style.DateFormat.Format = "yyyy-MM-dd";
                }
                ws.Cell(row, 24).Value = r.Perbaikan ?? "";
                if (r.TanggalPerbaikan.HasValue)
                {
                    ws.Cell(row, 25).Value = r.TanggalPerbaikan.Value;
                    ws.Cell(row, 25).Style.DateFormat.Format = "yyyy-MM-dd";
                }
                ws.Cell(row, 26).Value = r.Overdue ?? "";
                ws.Cell(row, 27).Value = r.AlasanOverdue ?? "";

                var imgTemuan = await DownloadImageBytes(r.FotoTemuan);
                if (imgTemuan != null)
                    EmbedImage(ws, row, 2, imgTemuan, "ap_temuan");
                else
                    ws.Cell(row, 2).Value = r.FotoTemuan ?? "";

                var imgPerbaikan = await DownloadImageBytes(r.FotoPerbaikan);
                if (imgPerbaikan != null)
                    EmbedImage(ws, row, 3, imgPerbaikan, "ap_perbaikan");
                else
                    ws.Cell(row, 3).Value = r.FotoPerbaikan ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
            if (ws.Column(3).Width < 18) ws.Column(3).Width = 18;
        }

        private async Task BuildSafetyTalkSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.SafetyTalks.AsNoTracking()
                .Where(s => !s.IsDeleted && s.Tanggal >= start && s.Tanggal <= end);
            if (allowedIds != null)
                query = query.Where(s => s.PerusahaanId.HasValue && allowedIds.Contains(s.PerusahaanId.Value));

            var data = await query.OrderByDescending(s => s.Tanggal).ThenByDescending(s => s.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("Safety Talk");
            var headers = new[] { "No", "Foto Diri", "Foto Kegiatan", "Tanggal", "Waktu", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Judul", "Keterangan" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 4).Value = r.Tanggal;
                ws.Cell(row, 4).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 5).Value = r.Waktu;
                ws.Cell(row, 5).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 6).Value = r.Nama;
                ws.Cell(row, 7).Value = r.Nik;
                ws.Cell(row, 8).Value = r.Departemen ?? "";
                ws.Cell(row, 9).Value = r.Area ?? "";
                ws.Cell(row, 10).Value = r.Lokasi ?? "";
                ws.Cell(row, 11).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 12).Value = r.Judul ?? "";
                ws.Cell(row, 13).Value = r.Keterangan ?? "";

                var imgDiri = await DownloadImageBytes(r.FotoDiri);
                if (imgDiri != null)
                    EmbedImage(ws, row, 2, imgDiri, "st_diri");
                else
                    ws.Cell(row, 2).Value = r.FotoDiri ?? "";

                var imgKegiatan = await DownloadImageBytes(r.FotoKegiatan);
                if (imgKegiatan != null)
                    EmbedImage(ws, row, 3, imgKegiatan, "st_kegiatan");
                else
                    ws.Cell(row, 3).Value = r.FotoKegiatan ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
            if (ws.Column(3).Width < 18) ws.Column(3).Width = 18;
        }

        private async Task BuildP5mSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.P5ms.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Tanggal >= start && p.Tanggal <= end);
            if (allowedIds != null)
                query = query.Where(p => p.PerusahaanId.HasValue && allowedIds.Contains(p.PerusahaanId.Value));

            var data = await query.OrderByDescending(p => p.Tanggal).ThenByDescending(p => p.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("P5M");
            var headers = new[] { "No", "Foto Kegiatan", "Tanggal", "Waktu", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Topik", "Judul", "Keterangan", "List Pertanyaan", "Jawaban", "Catatan" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 3).Value = r.Tanggal;
                ws.Cell(row, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 4).Value = r.Waktu;
                ws.Cell(row, 4).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 5).Value = r.Nama;
                ws.Cell(row, 6).Value = r.Nik;
                ws.Cell(row, 7).Value = r.Departemen ?? "";
                ws.Cell(row, 8).Value = r.Area ?? "";
                ws.Cell(row, 9).Value = r.Lokasi ?? "";
                ws.Cell(row, 10).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 11).Value = r.Topik ?? "";
                ws.Cell(row, 12).Value = r.Judul ?? "";
                ws.Cell(row, 13).Value = r.Keterangan ?? "";
                ws.Cell(row, 14).Value = r.ListPertanyaan ?? "";
                ws.Cell(row, 15).Value = r.Jawaban ?? "";
                ws.Cell(row, 16).Value = r.Catatan ?? "";

                var imgBytes = await DownloadImageBytes(r.FotoKegiatan);
                if (imgBytes != null)
                    EmbedImage(ws, row, 2, imgBytes, "p5m");
                else
                    ws.Cell(row, 2).Value = r.FotoKegiatan ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
        }

        private async Task BuildObservationSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.Observations.AsNoTracking()
                .Where(o => !o.IsDeleted && o.Date >= start && o.Date <= end);
            // Observation has no PerusahaanId, skip company filter

            var data = await query.OrderByDescending(o => o.Date).ToListAsync();

            var ws = wb.Worksheets.Add("Observation");
            var headers = new[] { "No", "Foto", "Tanggal", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Kegiatan Yang Diamati", "Departemen Yang Diamati", "Dokumen Pendukung", "Resiko Kritis", "Tingkat Resiko", "Perihal Yang Diamati", "Hasil Observasi", "Keterangan" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 3).Value = r.Date;
                ws.Cell(row, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 4).Value = r.Nama;
                ws.Cell(row, 5).Value = r.Nik;
                ws.Cell(row, 6).Value = r.Departemen;
                ws.Cell(row, 7).Value = r.Area;
                ws.Cell(row, 8).Value = r.Lokasi;
                ws.Cell(row, 9).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 10).Value = r.KegiatanYangDiamati ?? "";
                ws.Cell(row, 11).Value = r.DepartemenYangDiamati ?? "";
                ws.Cell(row, 12).Value = r.DokumenPendukung ?? "";
                ws.Cell(row, 13).Value = r.ResikoKritis ?? "";
                ws.Cell(row, 14).Value = r.TingkatResiko ?? "";
                ws.Cell(row, 15).Value = r.PerihalYangDiamati ?? "";
                ws.Cell(row, 16).Value = r.HasilObservasi ?? "";
                ws.Cell(row, 17).Value = r.Keterangan ?? "";

                var imgBytes = await DownloadImageBytes(r.FotoUrl);
                if (imgBytes != null)
                    EmbedImage(ws, row, 2, imgBytes, "obs");
                else
                    ws.Cell(row, 2).Value = r.FotoUrl ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
        }

        private async Task BuildCoachingSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.Coachings.AsNoTracking()
                .Where(c => !c.IsDeleted && c.Tanggal >= start && c.Tanggal <= end);
            if (allowedIds != null)
                query = query.Where(c => c.PerusahaanId.HasValue && allowedIds.Contains(c.PerusahaanId.Value));

            var data = await query.OrderByDescending(c => c.Tanggal).ThenByDescending(c => c.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("Coaching");
            var headers = new[] { "No", "Foto Kegiatan", "Tanggal", "Waktu", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Tema", "Feedback", "Komitmen" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 3).Value = r.Tanggal;
                ws.Cell(row, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 4).Value = r.Waktu;
                ws.Cell(row, 4).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 5).Value = r.Nama;
                ws.Cell(row, 6).Value = r.Nik;
                ws.Cell(row, 7).Value = r.Departemen ?? "";
                ws.Cell(row, 8).Value = r.Area ?? "";
                ws.Cell(row, 9).Value = r.Lokasi ?? "";
                ws.Cell(row, 10).Value = r.DetilLokasi ?? "";
                ws.Cell(row, 11).Value = r.Tema ?? "";
                ws.Cell(row, 12).Value = r.Feedback ?? "";
                ws.Cell(row, 13).Value = r.Komitmen ?? "";

                var imgBytes = await DownloadImageBytes(r.Foto);
                if (imgBytes != null)
                    EmbedImage(ws, row, 2, imgBytes, "coaching");
                else
                    ws.Cell(row, 2).Value = r.Foto ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
        }

        private async Task BuildP2hSheet(XLWorkbook wb, DateTime start, DateTime end, List<int>? allowedIds)
        {
            var query = _context.P2hReports.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Tanggal >= start && p.Tanggal <= end);
            // P2hReport has no PerusahaanId

            var data = await query.OrderByDescending(p => p.Tanggal).ThenByDescending(p => p.Waktu).ToListAsync();

            var ws = wb.Worksheets.Add("P2H");
            var headers = new[] { "No", "Foto Speedometer", "Tanggal", "Waktu", "Nama", "NIK", "Jenis Kendaraan", "No Lambung", "Kilometer", "Merek", "Simper/Kimper" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];
            StyleReportHeader(ws, 1, headers.Length);

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 3).Value = r.Tanggal;
                ws.Cell(row, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(row, 4).Value = r.Waktu;
                ws.Cell(row, 4).Style.NumberFormat.Format = "hh:mm";
                ws.Cell(row, 5).Value = r.Nama;
                ws.Cell(row, 6).Value = r.Nik;
                ws.Cell(row, 7).Value = r.JenisKendaraan;
                ws.Cell(row, 8).Value = r.NoLambung;
                ws.Cell(row, 9).Value = r.Kilometer;
                ws.Cell(row, 10).Value = r.Merek;
                ws.Cell(row, 11).Value = r.SimperKimper;

                var imgBytes = await DownloadImageBytes(r.FotoSpeedometer);
                if (imgBytes != null)
                    EmbedImage(ws, row, 2, imgBytes, "p2h");
                else
                    ws.Cell(row, 2).Value = r.FotoSpeedometer ?? "";
            }

            ws.Columns().AdjustToContents();
            if (ws.Column(2).Width < 18) ws.Column(2).Width = 18;
        }

        [HttpGet]
        public async Task<IActionResult> SapQuality(
            int? companyId = null, 
            string? programType = null, 
            string? ratingFilter = null, 
            string? search = null, 
            string? startDate = null,
            string? endDate = null,
            int page = 1)
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }

            ViewData["HeaderTitle"] = "Audit Kualitas SAP";
            ViewData["ActiveTab"] = "SapQuality";

            DateTime start = DateTime.Today.AddDays(-30);
            DateTime end = DateTime.Today;
            if (DateTime.TryParse(startDate, out var parsedStart)) start = parsedStart.Date;
            if (DateTime.TryParse(endDate, out var parsedEnd)) end = parsedEnd.Date;

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            var companies = await _context.Perusahaans
                .Where(p => p.StatusAktif && !ExcludedCompanies.Ids.Contains(p.PerusahaanId))
                .OrderBy(p => p.NamaPerusahaan)
                .ToListAsync();
            ViewBag.Companies = companies;
            ViewBag.SelectedCompanyId = companyId;
            ViewBag.SelectedProgramType = programType;
            ViewBag.SelectedRatingFilter = ratingFilter;
            ViewBag.SearchQuery = search;

            var assessments = await _context.SapQualityAssessments.AsNoTracking().ToListAsync();
            var assessmentDict = assessments
                .GroupBy(a => $"{a.ProgramType.ToLowerInvariant()}_{a.ProgramId}")
                .ToDictionary(g => g.Key, g => g.First());

            var records = new List<SapQualityRecordViewModel>();
            var normalizedSearch = search?.Trim().ToLowerInvariant();

            // 1. Hazard Reports
            if (string.IsNullOrEmpty(programType) || string.Equals(programType, "Hazard", StringComparison.OrdinalIgnoreCase))
            {
                var q = _context.HazardReports.AsNoTracking()
                    .Where(h => !h.IsDeleted && h.Tanggal >= start && h.Tanggal <= end);
                if (companyId.HasValue) q = q.Where(h => h.PerusahaanId == companyId.Value);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    q = q.Where(h => h.Nik.ToLower().Contains(normalizedSearch) || 
                                     h.Nama.ToLower().Contains(normalizedSearch) || 
                                     h.Temuan.ToLower().Contains(normalizedSearch));
                }

                var list = await q.Select(h => new { h.Id, h.Tanggal, h.Nik, h.Nama, h.PerusahaanId, h.Temuan, h.Lokasi, h.FotoTemuan }).ToListAsync();
                foreach (var r in list)
                {
                    var comp = companies.FirstOrDefault(c => c.PerusahaanId == r.PerusahaanId)?.NamaPerusahaan ?? "Unknown";
                    var key = $"hazard_{r.Id}";
                    assessmentDict.TryGetValue(key, out var assess);

                    records.Add(new SapQualityRecordViewModel
                    {
                        ProgramType = "Hazard",
                        Id = r.Id,
                        Title = "Temuan Hazard",
                        Description = r.Temuan ?? "-",
                        Tanggal = r.Tanggal,
                        Nik = r.Nik,
                        Nama = r.Nama,
                        PerusahaanId = r.PerusahaanId,
                        CompanyName = comp,
                        Lokasi = r.Lokasi,
                        PhotoUrl = NormalizeImagePath(r.FotoTemuan),
                        Rating = assess?.Rating,
                        Notes = assess?.Notes
                    });
                }
            }

            // 2. Inspections
            if (string.IsNullOrEmpty(programType) || string.Equals(programType, "Inspection", StringComparison.OrdinalIgnoreCase))
            {
                var q = _context.Inspections.AsNoTracking()
                    .Where(i => !i.IsDeleted && i.Tanggal >= start && i.Tanggal <= end);
                if (companyId.HasValue) q = q.Where(i => i.PerusahaanId == companyId.Value);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    q = q.Where(i => i.Nik.ToLower().Contains(normalizedSearch) || 
                                     i.Nama.ToLower().Contains(normalizedSearch) || 
                                     i.JenisInspeksi.ToLower().Contains(normalizedSearch) ||
                                     (i.Catatan != null && i.Catatan.ToLower().Contains(normalizedSearch)));
                }

                var list = await q.Select(i => new { i.Id, i.Tanggal, i.Nik, i.Nama, i.PerusahaanId, i.JenisInspeksi, i.Catatan, i.Lokasi, i.LampiranJson }).ToListAsync();
                foreach (var r in list)
                {
                    var comp = companies.FirstOrDefault(c => c.PerusahaanId == r.PerusahaanId)?.NamaPerusahaan ?? "Unknown";
                    var key = $"inspection_{r.Id}";
                    assessmentDict.TryGetValue(key, out var assess);

                    records.Add(new SapQualityRecordViewModel
                    {
                        ProgramType = "Inspection",
                        Id = r.Id,
                        Title = $"Inspeksi: {r.JenisInspeksi}",
                        Description = r.Catatan ?? "-",
                        Tanggal = r.Tanggal,
                        Nik = r.Nik,
                        Nama = r.Nama,
                        PerusahaanId = r.PerusahaanId,
                        CompanyName = comp,
                        Lokasi = r.Lokasi,
                        PhotoUrl = ExtractFirstInspectionImageUrl(r.LampiranJson),
                        Rating = assess?.Rating,
                        Notes = assess?.Notes
                    });
                }
            }

            // 3. Safety Talks
            if (string.IsNullOrEmpty(programType) || string.Equals(programType, "SafetyTalk", StringComparison.OrdinalIgnoreCase))
            {
                var q = _context.SafetyTalks.AsNoTracking()
                    .Where(s => !s.IsDeleted && s.Tanggal >= start && s.Tanggal <= end);
                if (companyId.HasValue) q = q.Where(s => s.PerusahaanId == companyId.Value);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    q = q.Where(s => s.Nik.ToLower().Contains(normalizedSearch) || 
                                     s.Nama.ToLower().Contains(normalizedSearch) || 
                                     (s.Judul != null && s.Judul.ToLower().Contains(normalizedSearch)) ||
                                     (s.Keterangan != null && s.Keterangan.ToLower().Contains(normalizedSearch)));
                }

                var list = await q.Select(s => new { s.Id, s.Tanggal, s.Nik, s.Nama, s.PerusahaanId, s.Judul, s.Keterangan, s.Lokasi, s.FotoKegiatan, s.FotoDiri }).ToListAsync();
                foreach (var r in list)
                {
                    var comp = companies.FirstOrDefault(c => c.PerusahaanId == r.PerusahaanId)?.NamaPerusahaan ?? "Unknown";
                    var key = $"safetytalk_{r.Id}";
                    assessmentDict.TryGetValue(key, out var assess);

                    records.Add(new SapQualityRecordViewModel
                    {
                        ProgramType = "SafetyTalk",
                        Id = r.Id,
                        Title = $"Safety Talk: {r.Judul}",
                        Description = r.Keterangan ?? "-",
                        Tanggal = r.Tanggal,
                        Nik = r.Nik,
                        Nama = r.Nama,
                        PerusahaanId = r.PerusahaanId,
                        CompanyName = comp,
                        Lokasi = r.Lokasi,
                        PhotoUrl = NormalizeImagePath(r.FotoKegiatan ?? r.FotoDiri),
                        Rating = assess?.Rating,
                        Notes = assess?.Notes
                    });
                }
            }

            // 4. Observations
            if (string.IsNullOrEmpty(programType) || string.Equals(programType, "Observation", StringComparison.OrdinalIgnoreCase))
            {
                var q = from o in _context.Observations.AsNoTracking()
                        join k in _context.Karyawans on o.Nik equals k.NoNik
                        where !o.IsDeleted && o.Date >= start && o.Date <= end && k.StatusAktif
                        select new { o.Id, Tanggal = o.Date, o.Nik, o.Nama, Kegiatan = o.KegiatanYangDiamati, Perihal = o.PerihalYangDiamati, o.Lokasi, PerusahaanId = k.IdPerusahaan, o.FotoUrl };

                if (companyId.HasValue) q = q.Where(x => x.PerusahaanId == companyId.Value);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    q = q.Where(x => x.Nik.ToLower().Contains(normalizedSearch) || 
                                     x.Nama.ToLower().Contains(normalizedSearch) || 
                                     (x.Kegiatan != null && x.Kegiatan.ToLower().Contains(normalizedSearch)) ||
                                     (x.Perihal != null && x.Perihal.ToLower().Contains(normalizedSearch)));
                }

                var list = await q.ToListAsync();
                foreach (var r in list)
                {
                    var comp = companies.FirstOrDefault(c => c.PerusahaanId == r.PerusahaanId)?.NamaPerusahaan ?? "Unknown";
                    var key = $"observation_{r.Id}";
                    assessmentDict.TryGetValue(key, out var assess);

                    records.Add(new SapQualityRecordViewModel
                    {
                        ProgramType = "Observation",
                        Id = r.Id,
                        Title = $"Observasi: {r.Perihal}",
                        Description = r.Kegiatan ?? "-",
                        Tanggal = r.Tanggal,
                        Nik = r.Nik,
                        Nama = r.Nama,
                        PerusahaanId = r.PerusahaanId,
                        CompanyName = comp,
                        Lokasi = r.Lokasi,
                        PhotoUrl = NormalizeImagePath(r.FotoUrl),
                        Rating = assess?.Rating,
                        Notes = assess?.Notes
                    });
                }
            }

            // 5. Coachings
            if (string.IsNullOrEmpty(programType) || string.Equals(programType, "Coaching", StringComparison.OrdinalIgnoreCase))
            {
                var q = _context.Coachings.AsNoTracking()
                    .Where(c => !c.IsDeleted && c.Tanggal >= start && c.Tanggal <= end);
                if (companyId.HasValue) q = q.Where(c => c.PerusahaanId == companyId.Value);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    q = q.Where(c => c.Nik.ToLower().Contains(normalizedSearch) || 
                                     c.Nama.ToLower().Contains(normalizedSearch) || 
                                     (c.Tema != null && c.Tema.ToLower().Contains(normalizedSearch)) ||
                                     (c.Feedback != null && c.Feedback.ToLower().Contains(normalizedSearch)));
                }

                var list = await q.Select(c => new { c.Id, c.Tanggal, c.Nik, c.Nama, c.PerusahaanId, c.Tema, c.Feedback, c.Lokasi, c.Foto }).ToListAsync();
                foreach (var r in list)
                {
                    var comp = companies.FirstOrDefault(c => c.PerusahaanId == r.PerusahaanId)?.NamaPerusahaan ?? "Unknown";
                    var key = $"coaching_{r.Id}";
                    assessmentDict.TryGetValue(key, out var assess);

                    records.Add(new SapQualityRecordViewModel
                    {
                        ProgramType = "Coaching",
                        Id = r.Id,
                        Title = $"Coaching: {r.Tema}",
                        Description = r.Feedback ?? "-",
                        Tanggal = r.Tanggal,
                        Nik = r.Nik,
                        Nama = r.Nama,
                        PerusahaanId = r.PerusahaanId,
                        CompanyName = comp,
                        Lokasi = r.Lokasi,
                        PhotoUrl = NormalizeImagePath(r.Foto),
                        Rating = assess?.Rating,
                        Notes = assess?.Notes
                    });
                }
            }

            if (!string.IsNullOrEmpty(ratingFilter) && ratingFilter != "all")
            {
                if (ratingFilter == "unrated")
                {
                    records = records.Where(r => r.Rating == null).ToList();
                }
                else if (ratingFilter == "low")
                {
                    records = records.Where(r => r.Rating.HasValue && r.Rating.Value <= 2).ToList();
                }
                else if (ratingFilter == "high")
                {
                    records = records.Where(r => r.Rating.HasValue && r.Rating.Value >= 3).ToList();
                }
            }

            records = records.OrderByDescending(r => r.Tanggal).ThenByDescending(r => r.Id).ToList();

            int pageSize = 15;
            int totalCount = records.Count;
            var paginated = records.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Run AI Audit on-the-fly for only the paginated (displayed) unrated items to prevent heavy database writes
            var newAssessments = new List<SapQualityAssessment>();
            var systemUser = "System-ML";
            var now = DateTime.Now;

            foreach (var item in paginated)
            {
                if (item.Rating == null)
                {
                    var (suggestedRating, aiNotes) = Services.SapQualityMlEngine.AssessQuality(item.ProgramType, item.Title, item.Description);
                    item.Rating = suggestedRating;
                    item.Notes = aiNotes;

                    newAssessments.Add(new SapQualityAssessment
                    {
                        ProgramType = item.ProgramType,
                        ProgramId = item.Id,
                        Rating = suggestedRating,
                        Notes = aiNotes,
                        CreatedBy = systemUser,
                        CreatedAt = now
                    });
                }
            }

            if (newAssessments.Any())
            {
                _context.SapQualityAssessments.AddRange(newAssessments);
                await _context.SaveChangesAsync();
            }

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return View(paginated);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RateSap(string programType, int programId, int rating, string? notes)
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }

            if (rating < 1 || rating > 5)
            {
                return BadRequest("Rating harus bernilai 1 - 5.");
            }

            var typeLower = programType.Trim().ToLowerInvariant();
            var allowedTypes = new[] { "hazard", "inspection", "safetytalk", "observation", "coaching" };
            if (!allowedTypes.Contains(typeLower))
            {
                return BadRequest("Tipe program tidak valid.");
            }

            var assessment = await _context.SapQualityAssessments
                .FirstOrDefaultAsync(a => a.ProgramType.ToLower() == typeLower && a.ProgramId == programId);

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                          ?? User.FindFirst("Nrp")?.Value 
                          ?? User.Identity?.Name 
                          ?? "System";

            if (assessment == null)
            {
                assessment = new SapQualityAssessment
                {
                    ProgramType = programType,
                    ProgramId = programId,
                    Rating = rating,
                    Notes = notes,
                    CreatedBy = userNik,
                    CreatedAt = DateTime.Now
                };
                _context.SapQualityAssessments.Add(assessment);
            }
            else
            {
                assessment.Rating = rating;
                assessment.Notes = notes;
                assessment.CreatedBy = userNik;
                assessment.CreatedAt = DateTime.Now;
                _context.SapQualityAssessments.Update(assessment);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, rating = assessment.Rating, notes = assessment.Notes });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoAuditSap(string programType, int programId)
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }

            var typeLower = programType.Trim().ToLowerInvariant();
            string title = "";
            string description = "";

            if (typeLower == "hazard")
            {
                var r = await _context.HazardReports.FindAsync(programId);
                if (r != null) { title = "Temuan Hazard"; description = r.Temuan ?? ""; }
            }
            else if (typeLower == "inspection")
            {
                var r = await _context.Inspections.FindAsync(programId);
                if (r != null) 
                { 
                    title = $"Inspeksi: {r.JenisInspeksi}"; 
                    int safeCount = 0;
                    int hazardCount = 0;
                    int naCount = 0;
                    int[] scores = new[] {
                        r.Q1_1, r.Q1_2, r.Q1_3,
                        r.Q2_1, r.Q2_2, r.Q2_3,
                        r.Q3_1, r.Q3_2, r.Q3_3,
                        r.Q4_1, r.Q4_2, r.Q4_3,
                        r.Q5_1, r.Q5_2, r.Q5_3
                    };
                    foreach (var s in scores)
                    {
                        if (s == 2) safeCount++;
                        else if (s == 0) hazardCount++;
                        else if (s == 1) naCount++;
                    }
                    description = $"INSPECTION_AUDIT | Catatan: {r.Catatan ?? "-"} | YA: {safeCount} | TIDAK: {hazardCount} | NA: {naCount}"; 
                }
            }
            else if (typeLower == "safetytalk")
            {
                var r = await _context.SafetyTalks.FindAsync(programId);
                if (r != null) { title = $"Safety Talk: {r.Judul}"; description = r.Keterangan ?? ""; }
            }
            else if (typeLower == "observation")
            {
                var r = await _context.Observations.FindAsync(programId);
                if (r != null) 
                { 
                    title = $"Observasi: {r.PerihalYangDiamati}"; 
                    description = $"OBSERVATION_AUDIT | Kegiatan: {r.KegiatanYangDiamati ?? "-"} | Perihal: {r.PerihalYangDiamati ?? "-"} | Hasil: {r.HasilObservasi ?? "-"} | Keterangan: {r.Keterangan ?? "-"}"; 
                }
            }
            else if (typeLower == "coaching")
            {
                var r = await _context.Coachings.FindAsync(programId);
                if (r != null) { title = $"Coaching: {r.Tema}"; description = r.Feedback ?? ""; }
            }

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description))
            {
                return NotFound("Data SAP tidak ditemukan.");
            }

            // Run quality assessment using ML Heuristics Engine
            var (suggestedRating, aiNotes) = Services.SapQualityMlEngine.AssessQuality(programType, title, description);

            var assessment = await _context.SapQualityAssessments
                .FirstOrDefaultAsync(a => a.ProgramType.ToLower() == typeLower && a.ProgramId == programId);

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                          ?? User.FindFirst("Nrp")?.Value 
                          ?? User.Identity?.Name 
                          ?? "System-ML";

            if (assessment == null)
            {
                assessment = new SapQualityAssessment
                {
                    ProgramType = programType,
                    ProgramId = programId,
                    Rating = suggestedRating,
                    Notes = aiNotes,
                    CreatedBy = userNik,
                    CreatedAt = DateTime.Now
                };
                _context.SapQualityAssessments.Add(assessment);
            }
            else
            {
                assessment.Rating = suggestedRating;
                assessment.Notes = aiNotes;
                assessment.CreatedBy = userNik;
                assessment.CreatedAt = DateTime.Now;
                _context.SapQualityAssessments.Update(assessment);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, rating = assessment.Rating, notes = assessment.Notes });
        }

        [HttpGet]
        public async Task<IActionResult> GetSapDetails(string programType, int programId)
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }

            var typeLower = programType.Trim().ToLowerInvariant();
            object? data = null;

            if (typeLower == "hazard")
            {
                var r = await _context.HazardReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId);
                if (r != null)
                {
                    var comp = (await _context.Perusahaans.FindAsync(r.PerusahaanId))?.NamaPerusahaan ?? "Unknown";
                    data = new {
                        Type = "Hazard",
                        r.Id,
                        Date = r.Tanggal.ToString("yyyy-MM-dd"),
                        Time = r.Waktu.ToString(@"hh\:mm"),
                        r.Nama,
                        r.Nik,
                        CompanyName = comp,
                        r.Area,
                        r.Lokasi,
                        r.DetilLokasi,
                        Title = "Temuan Hazard",
                        Description = r.Temuan ?? "-",
                        ExtraInfo = $"Kategori: {r.KategoriBahaya} | Jenis: {r.JenisBahaya} | Resiko: {r.TingkatResiko} | Perbaikan: {r.Perbaikan}",
                        PhotoUrl = NormalizeImagePath(r.FotoTemuan)
                    };
                }
            }
            else if (typeLower == "inspection")
            {
                var r = await _context.Inspections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId);
                if (r != null)
                {
                    var comp = (await _context.Perusahaans.FindAsync(r.PerusahaanId))?.NamaPerusahaan ?? "Unknown";
                    data = new {
                        Type = "Inspection",
                        r.Id,
                        Date = r.Tanggal.ToString("yyyy-MM-dd"),
                        Time = r.Waktu.ToString(@"hh\:mm"),
                        r.Nama,
                        r.Nik,
                        CompanyName = comp,
                        r.Area,
                        r.Lokasi,
                        r.DetilLokasi,
                        Title = $"Inspeksi: {r.JenisInspeksi}",
                        Description = r.Catatan ?? "-",
                        ExtraInfo = "Evaluasi Kriteria Inspeksi Terlampir",
                        PhotoUrl = ExtractFirstInspectionImageUrl(r.LampiranJson)
                    };
                }
            }
            else if (typeLower == "safetytalk")
            {
                var r = await _context.SafetyTalks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId);
                if (r != null)
                {
                    var comp = (await _context.Perusahaans.FindAsync(r.PerusahaanId))?.NamaPerusahaan ?? "Unknown";
                    data = new {
                        Type = "Safety Talk",
                        r.Id,
                        Date = r.Tanggal.ToString("yyyy-MM-dd"),
                        Time = r.Waktu.ToString(@"hh\:mm"),
                        r.Nama,
                        r.Nik,
                        CompanyName = comp,
                        r.Area,
                        r.Lokasi,
                        r.DetilLokasi,
                        Title = $"Safety Talk: {r.Judul}",
                        Description = r.Keterangan ?? "-",
                        ExtraInfo = "",
                        PhotoUrl = NormalizeImagePath(r.FotoKegiatan ?? r.FotoDiri)
                    };
                }
            }
            else if (typeLower == "observation")
            {
                var r = await _context.Observations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId);
                if (r != null)
                {
                    var k = await _context.Karyawans.AsNoTracking().Where(x => x.NoNik == r.Nik).OrderByDescending(x => x.StatusAktif).FirstOrDefaultAsync();
                    var comp = k != null ? (await _context.Perusahaans.FindAsync(k.IdPerusahaan))?.NamaPerusahaan ?? "Unknown" : "Unknown";
                    data = new {
                        Type = "Observation",
                        r.Id,
                        Date = r.Date.ToString("yyyy-MM-dd"),
                        Time = "00:00",
                        r.Nama,
                        r.Nik,
                        CompanyName = comp,
                        r.Area,
                        r.Lokasi,
                        r.DetilLokasi,
                        Title = $"Observasi: {r.PerihalYangDiamati}",
                        Description = r.KegiatanYangDiamati ?? "-",
                        ExtraInfo = $"Resiko: {r.TingkatResiko} | Hasil: {r.HasilObservasi}",
                        PhotoUrl = NormalizeImagePath(r.FotoUrl)
                    };
                }
            }
            else if (typeLower == "coaching")
            {
                var r = await _context.Coachings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId);
                if (r != null)
                {
                    var comp = (await _context.Perusahaans.FindAsync(r.PerusahaanId))?.NamaPerusahaan ?? "Unknown";
                    data = new {
                        Type = "Coaching",
                        r.Id,
                        Date = r.Tanggal.ToString("yyyy-MM-dd"),
                        Time = r.Waktu.ToString(@"hh\:mm"),
                        r.Nama,
                        r.Nik,
                        CompanyName = comp,
                        r.Area,
                        r.Lokasi,
                        r.DetilLokasi,
                        Title = $"Coaching: {r.Tema}",
                        Description = r.Feedback ?? "-",
                        ExtraInfo = $"Komitmen: {r.Komitmen}",
                        PhotoUrl = NormalizeImagePath(r.Foto)
                    };
                }
            }

            if (data == null) return NotFound("Data tidak ditemukan.");
            return Json(data);
        }

        [HttpGet("Admin/SapQuality/Dasboard")]
        public async Task<IActionResult> SapQualityDasboard(int? year = null, int? month = null)
        {
            if (!IsAuthorizedUser())
            {
                return Forbid();
            }

            var assessmentQuery = _context.SapQualityAssessments.AsNoTracking();
            if (year.HasValue) assessmentQuery = assessmentQuery.Where(a => a.CreatedAt.Year == year.Value);
            if (month.HasValue) assessmentQuery = assessmentQuery.Where(a => a.CreatedAt.Month == month.Value);
            var assessments = await assessmentQuery.ToListAsync();

            // Total active counts of each SAP type (filtered by year/month if selected)
            var hazardQuery = _context.HazardReports.AsNoTracking().Where(x => !x.IsDeleted);
            var inspectionQuery = _context.Inspections.AsNoTracking().Where(x => !x.IsDeleted);
            var safetyTalkQuery = _context.SafetyTalks.AsNoTracking().Where(x => !x.IsDeleted);
            var observationQuery = _context.Observations.AsNoTracking().Where(x => !x.IsDeleted);
            var coachingQuery = _context.Coachings.AsNoTracking().Where(x => !x.IsDeleted);

            if (year.HasValue)
            {
                hazardQuery = hazardQuery.Where(x => x.Tanggal.Year == year.Value);
                inspectionQuery = inspectionQuery.Where(x => x.Tanggal.Year == year.Value);
                safetyTalkQuery = safetyTalkQuery.Where(x => x.Tanggal.Year == year.Value);
                observationQuery = observationQuery.Where(x => x.Date.Year == year.Value);
                coachingQuery = coachingQuery.Where(x => x.Tanggal.Year == year.Value);
            }

            if (month.HasValue)
            {
                hazardQuery = hazardQuery.Where(x => x.Tanggal.Month == month.Value);
                inspectionQuery = inspectionQuery.Where(x => x.Tanggal.Month == month.Value);
                safetyTalkQuery = safetyTalkQuery.Where(x => x.Tanggal.Month == month.Value);
                observationQuery = observationQuery.Where(x => x.Date.Month == month.Value);
                coachingQuery = coachingQuery.Where(x => x.Tanggal.Month == month.Value);
            }

            int totalHazard = await hazardQuery.CountAsync();
            int totalInspection = await inspectionQuery.CountAsync();
            int totalSafetyTalk = await safetyTalkQuery.CountAsync();
            int totalObservation = await observationQuery.CountAsync();
            int totalCoaching = await coachingQuery.CountAsync();

            int totalAllSap = totalHazard + totalInspection + totalSafetyTalk + totalObservation + totalCoaching;

            // Audit statistics
            int totalAssessed = assessments.Count;
            int star1 = assessments.Count(a => a.Rating == 1);
            int star2 = assessments.Count(a => a.Rating == 2);
            int star3 = assessments.Count(a => a.Rating == 3);
            int star4 = assessments.Count(a => a.Rating == 4);
            int star5 = assessments.Count(a => a.Rating == 5);

            int kejarTarget = star1 + star2;
            int kualitasBaik = star3 + star4 + star5;

            // Assessed counts by type
            int assessedHazard = assessments.Count(a => string.Equals(a.ProgramType, "Hazard", StringComparison.OrdinalIgnoreCase));
            int assessedInspection = assessments.Count(a => string.Equals(a.ProgramType, "Inspection", StringComparison.OrdinalIgnoreCase));
            int assessedSafetyTalk = assessments.Count(a => string.Equals(a.ProgramType, "SafetyTalk", StringComparison.OrdinalIgnoreCase));
            int assessedObservation = assessments.Count(a => string.Equals(a.ProgramType, "Observation", StringComparison.OrdinalIgnoreCase));
            int assessedCoaching = assessments.Count(a => string.Equals(a.ProgramType, "Coaching", StringComparison.OrdinalIgnoreCase));

            // Average ratings by type
            double avgHazard = assessedHazard > 0 ? assessments.Where(a => string.Equals(a.ProgramType, "Hazard", StringComparison.OrdinalIgnoreCase)).Average(a => a.Rating) : 0;
            double avgInspection = assessedInspection > 0 ? assessments.Where(a => string.Equals(a.ProgramType, "Inspection", StringComparison.OrdinalIgnoreCase)).Average(a => a.Rating) : 0;
            double avgSafetyTalk = assessedSafetyTalk > 0 ? assessments.Where(a => string.Equals(a.ProgramType, "SafetyTalk", StringComparison.OrdinalIgnoreCase)).Average(a => a.Rating) : 0;
            double avgObservation = assessedObservation > 0 ? assessments.Where(a => string.Equals(a.ProgramType, "Observation", StringComparison.OrdinalIgnoreCase)).Average(a => a.Rating) : 0;
            double avgCoaching = assessedCoaching > 0 ? assessments.Where(a => string.Equals(a.ProgramType, "Coaching", StringComparison.OrdinalIgnoreCase)).Average(a => a.Rating) : 0;

            ViewBag.SelectedYear = year;
            ViewBag.SelectedMonth = month;

            ViewBag.TotalAllSap = totalAllSap;
            ViewBag.TotalHazard = totalHazard;
            ViewBag.TotalInspection = totalInspection;
            ViewBag.TotalSafetyTalk = totalSafetyTalk;
            ViewBag.TotalObservation = totalObservation;
            ViewBag.TotalCoaching = totalCoaching;

            ViewBag.TotalAssessed = totalAssessed;
            ViewBag.Star1 = star1;
            ViewBag.Star2 = star2;
            ViewBag.Star3 = star3;
            ViewBag.Star4 = star4;
            ViewBag.Star5 = star5;
            ViewBag.KejarTarget = kejarTarget;
            ViewBag.KualitasBaik = kualitasBaik;

            ViewBag.AssessedHazard = assessedHazard;
            ViewBag.AssessedInspection = assessedInspection;
            ViewBag.AssessedSafetyTalk = assessedSafetyTalk;
            ViewBag.AssessedObservation = assessedObservation;
            ViewBag.AssessedCoaching = assessedCoaching;

            ViewBag.AvgHazard = Math.Round(avgHazard, 2);
            ViewBag.AvgInspection = Math.Round(avgInspection, 2);
            ViewBag.AvgSafetyTalk = Math.Round(avgSafetyTalk, 2);
            ViewBag.AvgObservation = Math.Round(avgObservation, 2);
            ViewBag.AvgCoaching = Math.Round(avgCoaching, 2);

            return View();
        }

        [HttpGet("Admin/GetBulkAuditStatus")]
        public IActionResult GetBulkAuditStatus()
        {
            return Json(new {
                isRunning = _isBulkAuditing,
                total = _bulkTotal,
                processed = _bulkProcessed,
                percent = _bulkTotal > 0 ? (int)(_bulkProcessed * 100.0 / _bulkTotal) : 0,
                message = _bulkStatusMsg
            });
        }

        [HttpPost("Admin/StartBulkAudit")]
        [ValidateAntiForgeryToken]
        public IActionResult StartBulkAudit()
        {
            if (_isBulkAuditing)
            {
                return Json(new { success = false, message = "Proses audit massal sedang berjalan." });
            }

            _isBulkAuditing = true;
            _bulkProcessed = 0;
            _bulkTotal = 0;
            _bulkStatusMsg = "Menginisialisasi data...";

            var serviceProvider = HttpContext.RequestServices;

            // Start background thread
            _ = Task.Run(async () => {
                try
                {
                    using (var scope = serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Database.SetCommandTimeout(600); // 10 minutes timeout

                        // Set transaction isolation level to READ UNCOMMITTED to prevent deadlocks/timeouts on heavy tables
                        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

                        // Reset old system-generated assessments to trigger clean recalculation of all ratings
                        await db.Database.ExecuteSqlRawAsync("DELETE FROM tbl_m_penilaian_kualitas_sap WHERE created_by IN ('System-ML', 'System-ML-Bulk', 'System-ML-Bulk-Temp');");

                        _bulkStatusMsg = "Menghitung data unrated...";
                        int unratedHazards = await (from h in db.HazardReports
                                                   where !h.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Hazard" && a.ProgramId == h.Id)
                                                   select h.Id).CountAsync();
                        // Limit to latest 20,000 for server stability
                        unratedHazards = Math.Min(unratedHazards, 20000);

                        int unratedInspections = await (from i in db.Inspections
                                                       where !i.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Inspection" && a.ProgramId == i.Id)
                                                       select i.Id).CountAsync();
                        unratedInspections = Math.Min(unratedInspections, 20000);

                        int unratedSafetyTalks = await (from s in db.SafetyTalks
                                                       where !s.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "SafetyTalk" && a.ProgramId == s.Id)
                                                       select s.Id).CountAsync();
                        unratedSafetyTalks = Math.Min(unratedSafetyTalks, 20000);

                        int unratedObservations = await (from o in db.Observations
                                                       where !o.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Observation" && a.ProgramId == o.Id)
                                                       select o.Id).CountAsync();
                        unratedObservations = Math.Min(unratedObservations, 20000);

                        int unratedCoachings = await (from c in db.Coachings
                                                     where !c.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Coaching" && a.ProgramId == c.Id)
                                                     select c.Id).CountAsync();
                        unratedCoachings = Math.Min(unratedCoachings, 20000);

                        _bulkTotal = unratedHazards + unratedInspections + unratedSafetyTalks + unratedObservations + unratedCoachings;
                        
                        if (_bulkTotal == 0)
                        {
                            _isBulkAuditing = false;
                            _bulkStatusMsg = "Semua data sudah ter-audit.";
                            return;
                        }

                        var now = DateTime.Now;

                        // Process Hazard
                        int processedHazards = 0;
                        while (processedHazards < unratedHazards)
                        {
                            _bulkStatusMsg = $"Mengaudit Hazard Reports ({processedHazards}/{unratedHazards})...";
                            var batch = await (from h in db.HazardReports
                                               where !h.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Hazard" && a.ProgramId == h.Id)
                                               orderby h.Id descending
                                               select new TempAuditItem { Id = h.Id, Desc = h.Temuan }).Take(2500).ToListAsync();
                            if (!batch.Any()) break;

                            var list = new List<SapQualityAssessment>();
                            foreach (var item in batch)
                            {
                                var (rating, notes) = Services.SapQualityMlEngine.AssessQuality("Hazard", "Hazard Report", item.Desc ?? "");
                                list.Add(new SapQualityAssessment
                                {
                                    ProgramType = "Hazard",
                                    ProgramId = item.Id,
                                    Rating = rating,
                                    Notes = notes,
                                    CreatedBy = "System-ML-Bulk",
                                    CreatedAt = now
                                });
                            }
                            db.SapQualityAssessments.AddRange(list);
                            await db.SaveChangesAsync();
                            _bulkProcessed += list.Count;
                            processedHazards += list.Count;
                        }

                        // Process Inspection
                        int processedInspections = 0;
                        while (processedInspections < unratedInspections)
                        {
                            _bulkStatusMsg = $"Mengaudit Inspeksi K3 ({processedInspections}/{unratedInspections})...";
                            var rawBatch = await (from i in db.Inspections
                                                   where !i.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Inspection" && a.ProgramId == i.Id)
                                                   orderby i.Id descending
                                                   select new { 
                                                       i.Id, 
                                                       i.Catatan,
                                                       i.Q1_1, i.Q1_2, i.Q1_3,
                                                       i.Q2_1, i.Q2_2, i.Q2_3,
                                                       i.Q3_1, i.Q3_2, i.Q3_3,
                                                       i.Q4_1, i.Q4_2, i.Q4_3,
                                                       i.Q5_1, i.Q5_2, i.Q5_3
                                                   }).Take(2500).ToListAsync();
                            if (!rawBatch.Any()) break;

                            var batch = rawBatch.Select(r => {
                                int safeCount = 0;
                                int hazardCount = 0;
                                int naCount = 0;
                                int[] scores = new[] {
                                    r.Q1_1, r.Q1_2, r.Q1_3,
                                    r.Q2_1, r.Q2_2, r.Q2_3,
                                    r.Q3_1, r.Q3_2, r.Q3_3,
                                    r.Q4_1, r.Q4_2, r.Q4_3,
                                    r.Q5_1, r.Q5_2, r.Q5_3
                                };
                                foreach (var s in scores)
                                {
                                    if (s == 2) safeCount++;
                                    else if (s == 0) hazardCount++;
                                    else if (s == 1) naCount++;
                                }
                                return new TempAuditItem { 
                                    Id = r.Id, 
                                    Desc = $"INSPECTION_AUDIT | Catatan: {r.Catatan ?? "-"} | YA: {safeCount} | TIDAK: {hazardCount} | NA: {naCount}" 
                                };
                             }).ToList();

                            var list = new List<SapQualityAssessment>();
                            foreach (var item in batch)
                            {
                                var (rating, notes) = Services.SapQualityMlEngine.AssessQuality("Inspection", "Inspeksi K3", item.Desc ?? "");
                                list.Add(new SapQualityAssessment
                                {
                                    ProgramType = "Inspection",
                                    ProgramId = item.Id,
                                    Rating = rating,
                                    Notes = notes,
                                    CreatedBy = "System-ML-Bulk",
                                    CreatedAt = now
                                });
                            }
                            db.SapQualityAssessments.AddRange(list);
                            await db.SaveChangesAsync();
                            _bulkProcessed += list.Count;
                            processedInspections += list.Count;
                        }

                        // Process Safety Talk
                        int processedSafetyTalks = 0;
                        while (processedSafetyTalks < unratedSafetyTalks)
                        {
                            _bulkStatusMsg = $"Mengaudit Safety Talks ({processedSafetyTalks}/{unratedSafetyTalks})...";
                            var batch = await (from s in db.SafetyTalks
                                               where !s.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "SafetyTalk" && a.ProgramId == s.Id)
                                               orderby s.Id descending
                                               select new TempAuditItem { Id = s.Id, Desc = s.Keterangan }).Take(2500).ToListAsync();
                            if (!batch.Any()) break;

                            var list = new List<SapQualityAssessment>();
                            foreach (var item in batch)
                            {
                                var (rating, notes) = Services.SapQualityMlEngine.AssessQuality("SafetyTalk", "Safety Talk", item.Desc ?? "");
                                list.Add(new SapQualityAssessment
                                {
                                    ProgramType = "SafetyTalk",
                                    ProgramId = item.Id,
                                    Rating = rating,
                                    Notes = notes,
                                    CreatedBy = "System-ML-Bulk",
                                    CreatedAt = now
                                });
                            }
                            db.SapQualityAssessments.AddRange(list);
                            await db.SaveChangesAsync();
                            _bulkProcessed += list.Count;
                            processedSafetyTalks += list.Count;
                        }

                        // Process Observation
                        int processedObservations = 0;
                        while (processedObservations < unratedObservations)
                        {
                            _bulkStatusMsg = $"Mengaudit Observasi K3 ({processedObservations}/{unratedObservations})...";
                            var rawBatch = await (from o in db.Observations
                                                   where !o.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Observation" && a.ProgramId == o.Id)
                                                   orderby o.Id descending
                                                   select new {
                                                       o.Id,
                                                       o.KegiatanYangDiamati,
                                                       o.PerihalYangDiamati,
                                                       o.HasilObservasi,
                                                       o.Keterangan
                                                   }).Take(2500).ToListAsync();
                            if (!rawBatch.Any()) break;

                            var batch = rawBatch.Select(r => new TempAuditItem {
                                 Id = r.Id,
                                 Desc = $"OBSERVATION_AUDIT | Kegiatan: {r.KegiatanYangDiamati ?? "-"} | Perihal: {r.PerihalYangDiamati ?? "-"} | Hasil: {r.HasilObservasi ?? "-"} | Keterangan: {r.Keterangan ?? "-"}"
                            }).ToList();

                            var list = new List<SapQualityAssessment>();
                            foreach (var item in batch)
                            {
                                var (rating, notes) = Services.SapQualityMlEngine.AssessQuality("Observation", "Observasi K3", item.Desc ?? "");
                                list.Add(new SapQualityAssessment
                                {
                                    ProgramType = "Observation",
                                    ProgramId = item.Id,
                                    Rating = rating,
                                    Notes = notes,
                                    CreatedBy = "System-ML-Bulk",
                                    CreatedAt = now
                                });
                            }
                            db.SapQualityAssessments.AddRange(list);
                            await db.SaveChangesAsync();
                            _bulkProcessed += list.Count;
                            processedObservations += list.Count;
                        }

                        // Process Coaching
                        int processedCoachings = 0;
                        while (processedCoachings < unratedCoachings)
                        {
                            _bulkStatusMsg = $"Mengaudit Coaching K3 ({processedCoachings}/{unratedCoachings})...";
                            var batch = await (from c in db.Coachings
                                               where !c.IsDeleted && !db.SapQualityAssessments.Any(a => a.ProgramType == "Coaching" && a.ProgramId == c.Id)
                                               orderby c.Id descending
                                               select new TempAuditItem { Id = c.Id, Desc = c.Feedback }).Take(2500).ToListAsync();
                            if (!batch.Any()) break;

                            var list = new List<SapQualityAssessment>();
                            foreach (var item in batch)
                            {
                                var (rating, notes) = Services.SapQualityMlEngine.AssessQuality("Coaching", "Coaching K3", item.Desc ?? "");
                                list.Add(new SapQualityAssessment
                                {
                                    ProgramType = "Coaching",
                                    ProgramId = item.Id,
                                    Rating = rating,
                                    Notes = notes,
                                    CreatedBy = "System-ML-Bulk",
                                    CreatedAt = now
                                });
                            }
                            db.SapQualityAssessments.AddRange(list);
                            await db.SaveChangesAsync();
                            _bulkProcessed += list.Count;
                            processedCoachings += list.Count;
                        }

                        _bulkStatusMsg = "Selesai! Seluruh data berhasil diaudit.";
                    }
                }
                catch (Exception ex)
                {
                    _bulkStatusMsg = $"Gagal: {ex.Message}";
                }
                finally
                {
                    _isBulkAuditing = false;
                }
            });

            return Json(new { success = true, message = "Proses audit massal telah dimulai di latar belakang." });
        }

        private static string? NormalizeImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var trimmed = path.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "/ImageProxy/Get?url=" + Uri.EscapeDataString(trimmed);
            }
            if (trimmed.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return trimmed;
        }

        private static string? ExtractFirstInspectionImageUrl(string? lampiranJson)
        {
            if (string.IsNullOrWhiteSpace(lampiranJson)) return null;
            try
            {
                if (lampiranJson.Trim().StartsWith("["))
                {
                    var list = JsonSerializer.Deserialize<List<string>>(lampiranJson);
                    if (list != null && list.Count > 0) return NormalizeImagePath(list[0]);
                }
                else
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(lampiranJson);
                    if (dict != null && dict.Count > 0)
                    {
                        foreach (var val in dict.Values)
                        {
                            if (!string.IsNullOrWhiteSpace(val)) return NormalizeImagePath(val);
                        }
                    }
                }
            }
            catch {}
            return null;
        }
    }

    public class SapQualityRecordViewModel
    {
        public string ProgramType { get; set; } = null!;
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public DateTime Tanggal { get; set; }
        public string Nik { get; set; } = null!;
        public string Nama { get; set; } = null!;
        public int? PerusahaanId { get; set; }
        public string CompanyName { get; set; } = null!;
        public string? Lokasi { get; set; }
        public string? PhotoUrl { get; set; }
        public int? Rating { get; set; }
        public string? Notes { get; set; }
    }
}

