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
    [Authorize(Roles = "Admin")]
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

        public AdminController(AppDbContext context, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _companyHierarchyService = companyHierarchyService;
        }

        public IActionResult Index()
        {
            ViewData["HeaderTitle"] = "Admin Area";
            ViewData["ActiveTab"] = "Admin";
            return View();
        }

        [HttpGet]
        public IActionResult DownloadTemplate()
        {
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
                        var nik = GetString(row, 5);
                        var tanggal = GetDate(row, 2);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;
                        if (nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        string temuan = GetString(row, 11) ?? "-";
                        if (!_context.HazardReports.Any(h => h.Nik == nik && h.Tanggal == tanggal.Value.Date && h.Temuan == temuan))
                        {
                            _context.HazardReports.Add(new HazardReport
                            {
                                FotoTemuan = GetString(row, 1),
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 3) ?? TimeSpan.Zero,
                                Nama = GetString(row, 4) ?? "",
                                Nik = nik,
                                Departemen = GetString(row, 6),
                                Area = GetString(row, 7),
                                Lokasi = GetString(row, 8),
                                DetilLokasi = GetString(row, 9),
                                Temuan = temuan,
                                KategoriBahaya = GetString(row, 12),
                                JenisBahaya = GetString(row, 13),
                                JenisKetidaksesuaian = GetString(row, 14),
                                TingkatResiko = GetString(row, 15),
                                Perbaikan = GetString(row, 16),
                                TindakanPerbaikan = GetString(row, 17),
                                Pja = GetString(row, 18),
                                NikPja = GetString(row, 19),
                                DepartemenPja = GetString(row, 20),
                                StatusTemuan = GetString(row, 21) ?? "Open",
                                CreatedAt = DateTime.Now
                            });
                            addedHazards++;
                        }
                        else { skippedHazards++; }

                        // Parse Action Plan from Hazard
                        var pic = GetString(row, 22);
                        var status = GetString(row, 21);
                        if (!string.IsNullOrEmpty(pic) || status != null)
                        {
                            string apDetil = temuan;
                            if (!_context.ActionPlans.Any(a => a.Nik == nik && a.Tanggal == tanggal.Value.Date && a.DetilTemuan == apDetil))
                            {
                                _context.ActionPlans.Add(new ActionPlan
                                {
                                    FotoTemuan = GetString(row, 1),
                                    FotoPerbaikan = GetString(row, 31),
                                    Tanggal = tanggal.Value.Date,
                                    Waktu = GetTime(row, 3) ?? TimeSpan.Zero,
                                    Nama = GetString(row, 4) ?? "",
                                    Nik = nik,
                                    Departemen = GetString(row, 6),
                                    Area = GetString(row, 7),
                                    Lokasi = GetString(row, 8),
                                    DetilLokasi = GetString(row, 9),
                                    ItemSap = "Hazard",
                                    KategoriTemuan = GetString(row, 10),
                                    DetilTemuan = apDetil,
                                    Status = status ?? "Open",
                                    Pja = GetString(row, 18),
                                    NikPja = GetString(row, 19),
                                    DepartemenPja = GetString(row, 20),
                                    Pic = pic,
                                    NikPic = GetString(row, 23),
                                    DepartemenPic = GetString(row, 24),
                                    RencanaPerbaikan = GetString(row, 25),
                                    TanggalRencanaPerbaikan = GetDate(row, 26),
                                    Perbaikan = GetString(row, 27),
                                    TanggalPerbaikan = GetDate(row, 28),
                                    Overdue = GetString(row, 29),
                                    AlasanOverdue = GetString(row, 30),
                                    CreatedAt = DateTime.Now
                                });
                                addedActionPlans++;
                            }
                            else { skippedActionPlans++; }
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
                        var nik = GetString(row, 4);
                        var tanggal = GetDate(row, 1);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;
                        if (nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        string jenis = GetString(row, 9) ?? "-";
                        if (!_context.Inspections.Any(i => i.Nik == nik && i.Tanggal == tanggal.Value.Date && i.JenisInspeksi == jenis))
                        {
                            _context.Inspections.Add(new Inspection
                            {
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 2) ?? TimeSpan.Zero,
                                Nama = GetString(row, 3) ?? "",
                                Nik = nik,
                                Departemen = GetString(row, 5),
                                Area = GetString(row, 6),
                                Lokasi = GetString(row, 7),
                                DetilLokasi = GetString(row, 8),
                                JenisInspeksi = jenis,
                                Pja = GetString(row, 10),
                                NikPja = GetString(row, 11),
                                DepartemenPja = GetString(row, 12),
                                CreatedAt = DateTime.Now
                            });
                            addedInspections++;
                        }
                        else { skippedInspections++; }

                        // Parse Action Plan from Inspection
                        var apDetil = GetString(row, 14);
                        if (!string.IsNullOrEmpty(apDetil))
                        {
                            if (!_context.ActionPlans.Any(a => a.Nik == nik && a.Tanggal == tanggal.Value.Date && a.DetilTemuan == apDetil))
                            {
                                _context.ActionPlans.Add(new ActionPlan
                                {
                                    FotoTemuan = GetString(row, 25),
                                    FotoPerbaikan = GetString(row, 26),
                                    Tanggal = tanggal.Value.Date,
                                    Waktu = GetTime(row, 2) ?? TimeSpan.Zero,
                                    Nama = GetString(row, 3) ?? "",
                                    Nik = nik,
                                    Departemen = GetString(row, 5),
                                    Area = GetString(row, 6),
                                    Lokasi = GetString(row, 7),
                                    DetilLokasi = GetString(row, 8),
                                    ItemSap = "Inspection",
                                    KategoriTemuan = GetString(row, 13),
                                    DetilTemuan = apDetil,
                                    Status = GetString(row, 15) ?? "Open",
                                    Pja = GetString(row, 10),
                                    NikPja = GetString(row, 11),
                                    DepartemenPja = GetString(row, 12),
                                    Pic = GetString(row, 16),
                                    NikPic = GetString(row, 17),
                                    DepartemenPic = GetString(row, 18),
                                    RencanaPerbaikan = GetString(row, 19),
                                    TanggalRencanaPerbaikan = GetDate(row, 20),
                                    Perbaikan = GetString(row, 21),
                                    TanggalPerbaikan = GetDate(row, 22),
                                    Overdue = GetString(row, 23),
                                    AlasanOverdue = GetString(row, 24),
                                    CreatedAt = DateTime.Now
                                });
                                addedActionPlans++;
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
                        var nik = GetString(row, 6);
                        var tanggal = GetDate(row, 3);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;
                        if (nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        string judul = GetString(row, 11) ?? "-";
                        if (!_context.SafetyTalks.Any(s => s.Nik == nik && s.Tanggal == tanggal.Value.Date && s.Judul == judul))
                        {
                            _context.SafetyTalks.Add(new SafetyTalk
                            {
                                FotoDiri = GetString(row, 1),
                                FotoKegiatan = GetString(row, 2),
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 4) ?? TimeSpan.Zero,
                                Nama = GetString(row, 5) ?? "",
                                Nik = nik,
                                Departemen = GetString(row, 7),
                                Area = GetString(row, 8),
                                Lokasi = GetString(row, 9),
                                DetilLokasi = GetString(row, 10),
                                Judul = judul,
                                Keterangan = GetString(row, 12),
                                CreatedAt = DateTime.Now
                            });
                            addedSafetyTalks++;
                        }
                        else { skippedSafetyTalks++; }
                    }
                }

                // 4. P5M
                var wsP5 = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("P5M", StringComparison.OrdinalIgnoreCase));
                if (wsP5 != null)
                {
                    EnsureWorksheetRowLimit(wsP5);
                    foreach (var row in wsP5.RowsUsed().Skip(2))
                    {
                        var nik = GetString(row, 5);
                        var tanggal = GetDate(row, 2);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;
                        if (nik.StartsWith("CONTOH", StringComparison.OrdinalIgnoreCase)) continue;

                        string judul = GetString(row, 11) ?? "-";
                        string pertanyaan = GetString(row, 13) ?? "";
                        if (!_context.P5ms.Any(p => p.Nik == nik && p.Tanggal == tanggal.Value.Date && p.Judul == judul && p.ListPertanyaan == pertanyaan))
                        {
                            _context.P5ms.Add(new P5m
                            {
                                FotoKegiatan = GetString(row, 1),
                                Tanggal = tanggal.Value.Date,
                                Waktu = GetTime(row, 3) ?? TimeSpan.Zero,
                                Nama = GetString(row, 4) ?? "",
                                Nik = nik,
                                Departemen = GetString(row, 6),
                                Area = GetString(row, 7),
                                Lokasi = GetString(row, 8),
                                DetilLokasi = GetString(row, 9),
                                Topik = GetString(row, 10),
                                Judul = judul,
                                Keterangan = GetString(row, 12),
                                ListPertanyaan = pertanyaan,
                                Jawaban = GetString(row, 14),
                                Catatan = GetString(row, 15),
                                CreatedAt = DateTime.Now
                            });
                            addedP5m++;
                        }
                        else { skippedP5m++; }
                    }
                }

                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = $@"
                    <div class='mb-2'><strong>Berhasil Upload:</strong></div>
                    <ul class='mb-2' style='font-size: 13px;'>
                        <li>{addedHazards} Hazard Baru</li>
                        <li>{addedInspections} Inspeksi Baru</li>
                        <li>{addedActionPlans} Action Plan Baru</li>
                        <li>{addedSafetyTalks} Safety Talk Baru</li>
                        <li>{addedP5m} P5M Baru</li>
                    </ul>
                    <div class='mb-1'><strong>Dilewati (Duplikat):</strong></div>
                    <ul class='mb-0' style='font-size: 13px;'>
                        <li>{skippedHazards} Hazard</li>
                        <li>{skippedInspections} Inspeksi</li>
                        <li>{skippedActionPlans} Action Plan</li>
                        <li>{skippedSafetyTalks} Safety Talk</li>
                        <li>{skippedP5m} P5M</li>
                    </ul>";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Terjadi kesalahan saat memproses Excel: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private string? GetString(IXLRow row, int col)
        {
            var cell = row.Cell(col);
            if (cell.IsEmpty()) return null;
            return SanitizeExcelString(cell.Value.ToString());
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
    }
}

