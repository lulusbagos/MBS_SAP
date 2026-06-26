using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MBS_SAP.Data;
using MBS_SAP.Models;
using ClosedXML.Excel;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
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
            wsHazard.Cell(2, 1).InsertData(new[] { new[] { "Foto Temuan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Kategori Temuan", "Temuan", "Kategori Bahaya", "Jenis Bahaya", "Jenis Ketidaksesuaian", "Tingkat Resiko", "Perbaikan", "Tindakan Perbaikan", "PJA", "NIK PJA", "Departemen PJA", "Status Temuan", "PIC", "NIK PIC", "Departemen PIC", "Rencana Perbaikan", "Tanggal Rencana Perbaikan", "Perbaikan", "Tanggal Perbaikan", "Overdue", "Alasan Overdue", "Foto Perbaikan" } });
            StyleHeader(wsHazard, 2);

            var wsInsp = wb.Worksheets.Add("Inspection");
            wsInsp.Cell(1, 1).Value = "Filter diterapkan:\n(Contoh Filter)";
            wsInsp.Cell(3, 1).InsertData(new[] { new[] { "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Jenis Inspeksi", "PJA", "NIK PJA", "Departemen PJA", "Kategori Temuan", "Detil Temuan", "Status", "PIC", "NIK PIC", "Departemen PIC", "Rencana Perbaikan", "Tanggal Rencana Perbaikan", "Perbaikan", "Tanggal Perbaikan", "Overdue", "Alasan Overdue", "Foto Temuan", "Foto Perbaikan" } });
            StyleHeader(wsInsp, 3);

            var wsObs = wb.Worksheets.Add("Observation");
            wsObs.Cell(3, 1).InsertData(new[] { new[] { "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Kegiatan Yang Diamati", "Departemen Yang Diamati", "Dokumen Pendukung", "Resiko Kritis", "Tingkat Resiko", "Perihal Yang Diamati", "Hasil Observasi" } });
            StyleHeader(wsObs, 3);

            var wsCoach = wb.Worksheets.Add("Coaching");
            wsCoach.Cell(3, 1).InsertData(new[] { new[] { "Foto Kegiatan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Tema", "Judul", "Feedback" } });
            StyleHeader(wsCoach, 3);

            var wsSt = wb.Worksheets.Add("Safety Talk");
            wsSt.Cell(3, 1).InsertData(new[] { new[] { "Foto Diri", "Foto Kegiatan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Judul", "Keterangan" } });
            StyleHeader(wsSt, 3);

            var wsP5 = wb.Worksheets.Add("P5M");
            wsP5.Cell(3, 1).InsertData(new[] { new[] { "Foto Kegiatan", "Tanggal", "Time", "Nama", "NIK", "Departemen", "Area", "Lokasi", "Detil Lokasi", "Topik", "Judul", "Keterangan", "List Pertanyaan", "Jawaban", "Catatan" } });
            StyleHeader(wsP5, 3);

            using var stream = new System.IO.MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Upload_SAP.xlsx");
        }

        [HttpPost]
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

            int addedHazards = 0, addedInspections = 0, addedActionPlans = 0, addedSafetyTalks = 0, addedP5m = 0;
            int skippedHazards = 0, skippedInspections = 0, skippedActionPlans = 0, skippedSafetyTalks = 0, skippedP5m = 0;

            try
            {
                using var stream = excelFile.OpenReadStream();
                using var wb = new XLWorkbook(stream);

                // 1. Hazard
                var wsHazard = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Hazard", StringComparison.OrdinalIgnoreCase));
                if (wsHazard != null)
                {
                    foreach (var row in wsHazard.RowsUsed().Skip(2)) // Data starts at row 3 (if header at row 2)
                    {
                        var nik = GetString(row, 5);
                        var tanggal = GetDate(row, 2);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;

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
                    foreach (var row in wsInsp.RowsUsed().Skip(2)) // Data starts at row 3
                    {
                        var nik = GetString(row, 4);
                        var tanggal = GetDate(row, 1);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;

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
                    foreach (var row in wsSt.RowsUsed().Skip(2))
                    {
                        var nik = GetString(row, 6);
                        var tanggal = GetDate(row, 3);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;

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
                    foreach (var row in wsP5.RowsUsed().Skip(2))
                    {
                        var nik = GetString(row, 5);
                        var tanggal = GetDate(row, 2);
                        if (string.IsNullOrEmpty(nik) || !tanggal.HasValue) continue;

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
            return cell.Value.ToString()?.Trim();
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
    }
}
