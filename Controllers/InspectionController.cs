using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class InspectionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ExcelService _excelService;
        private readonly ImageUploadService _imageUploadService;

        public InspectionController(AppDbContext context, ExcelService excelService, ImageUploadService imageUploadService)
        {
            _context = context;
            _excelService = excelService;
            _imageUploadService = imageUploadService;
        }

        // GET: Inspection
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Safety Inspeksi";
            ViewData["ActiveTab"] = "Inspection";

            var userCompanyIdStr = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(userCompanyIdStr, out var cid) && cid > 0 ? cid : null;

            IQueryable<Inspection> query = _context.Inspections.Where(i => !i.IsDeleted);
            if (!User.IsInRole("Admin") && userCompanyId.HasValue)
            {
                query = query.Where(i => i.PerusahaanId == userCompanyId.Value);
            }

            ViewBag.JenisInspeksiList = await query
                .Where(i => !string.IsNullOrEmpty(i.JenisInspeksi))
                .Select(i => i.JenisInspeksi!)
                .Distinct()
                .ToListAsync();

            ViewBag.AreaList = await query
                .Where(i => !string.IsNullOrEmpty(i.Area))
                .Select(i => i.Area!)
                .Distinct()
                .ToListAsync();

            var inspections = await query
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();

            return View(inspections);
        }

        // POST: Inspection/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            int? id,
            DateTime tanggal,
            string waktuStr,
            string? jenisInspeksi,
            string? area,
            string? lokasi,
            string? detilLokasi,
            string? pja,
            string? nikPja,
            string? departemenPja,
            int q1_1, int q1_2, int q1_3,
            int q2_1, int q2_2, int q2_3,
            int q3_1, int q3_2, int q3_3,
            int q4_1, int q4_2, int q4_3,
            int q5_1, int q5_2, int q5_3,
            string? catatan)
        {
            TimeSpan waktu = DateTime.Now.TimeOfDay;
            if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
            {
                waktu = parsedWaktu;
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            var userName = User.Identity?.Name ?? "Anonymous";
            var userDept = User.FindFirst("Department")?.Value ?? "General";

            Inspection? inspection;
            bool isNew = true;

            if (id.HasValue && id.Value > 0)
            {
                inspection = await _context.Inspections.FindAsync(id.Value);
                if (inspection == null || inspection.IsDeleted) return NotFound();

                if (inspection.Nik != userNik && !User.IsInRole("Admin"))
                {
                    TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah inspeksi ini.";
                    return RedirectToAction(nameof(Index));
                }
                isNew = false;
            }
            else
            {
                var userCompanyIdStr = User.FindFirst("CompanyId")?.Value;
                int? userCompanyId = int.TryParse(userCompanyIdStr, out var cid) && cid > 0 ? cid : null;

                inspection = new Inspection
                {
                    Nama = userName,
                    Nik = userNik,
                    Departemen = userDept,
                    PerusahaanId = userCompanyId,
                    CreatedAt = DateTime.Now
                };
            }

            inspection.Tanggal = tanggal == default ? DateTime.Today : tanggal;
            inspection.Waktu = waktu;
            inspection.Area = area?.ToUpper();
            inspection.Lokasi = lokasi?.ToUpper();
            inspection.DetilLokasi = detilLokasi?.ToUpper();
            inspection.JenisInspeksi = jenisInspeksi?.ToUpper() ?? "UMUM";
            var pjaName = pja?.Trim().ToUpper();
            var pjaDept = departemenPja?.Trim().ToUpper();
            var pjaNik = nikPja?.Trim();

            if (TryParseCompanyNikToken(pjaNik, out var selectedCompanyId))
            {
                inspection.Pja = pjaName;
                inspection.NikPja = null;
                inspection.DepartemenPja = "PERUSAHAAN";
                if (selectedCompanyId > 0)
                {
                    inspection.PerusahaanId = selectedCompanyId;
                }
            }
            else
            {
                inspection.Pja = pjaName;
                inspection.NikPja = pjaNik;
                inspection.DepartemenPja = pjaDept;
            }
            
            inspection.Q1_1 = q1_1;
            inspection.Q1_2 = q1_2;
            inspection.Q1_3 = q1_3;
            inspection.Q2_1 = q2_1;
            inspection.Q2_2 = q2_2;
            inspection.Q2_3 = q2_3;
            inspection.Q3_1 = q3_1;
            inspection.Q3_2 = q3_2;
            inspection.Q3_3 = q3_3;
            inspection.Q4_1 = q4_1;
            inspection.Q4_2 = q4_2;
            inspection.Q4_3 = q4_3;
            inspection.Q5_1 = q5_1;
            inspection.Q5_2 = q5_2;
            inspection.Q5_3 = q5_3;
            inspection.Catatan = catatan;

            // Handle Photo Uploads for 15 questions
            var lampiranDict = new System.Collections.Generic.Dictionary<string, string>();
            if (!string.IsNullOrEmpty(inspection.LampiranJson))
            {
                try { lampiranDict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(inspection.LampiranJson) ?? new System.Collections.Generic.Dictionary<string, string>(); } catch {}
            }

            for (int m = 1; m <= 5; m++)
            {
                for (int q = 1; q <= 3; q++)
                {
                    string key = $"{m}_{q}";
                    var file = Request.Form.Files[$"foto_{key}"];
                    if (file != null && file.Length > 0)
                    {
                        try
                        {
                            var path = await _imageUploadService.UploadAndCompressImageAsync(file, "inspections");
                            if (!string.IsNullOrEmpty(path))
                            {
                                lampiranDict[key] = path;
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            if (lampiranDict.Count > 0)
            {
                inspection.LampiranJson = System.Text.Json.JsonSerializer.Serialize(lampiranDict);
            }

            // Save Inspection to Database
            if (isNew)
            {
                _context.Inspections.Add(inspection);
            }
            else
            {
                _context.Inspections.Update(inspection);
            }
            await _context.SaveChangesAsync();

            // Notify PJA
            if (isNew && !string.IsNullOrWhiteSpace(inspection.Pja))
            {
                if (!string.IsNullOrWhiteSpace(inspection.NikPja))
                {
                    var notif = new Notification
                    {
                        RecipientNik = inspection.NikPja,
                        Title = "Penugasan Inspeksi Baru",
                        Message = $"Anda ditunjuk sebagai PJA untuk inspeksi {inspection.JenisInspeksi} di {inspection.Lokasi ?? inspection.Area} oleh {inspection.Nama}.",
                        Url = "/Inspection/Index"
                    };
                    _context.Notifications.Add(notif);
                    await _context.SaveChangesAsync();
                }
                else if (inspection.PerusahaanId.HasValue)
                {
                    await CreateCompanyBroadcastNotificationAsync(
                        inspection.PerusahaanId.Value,
                        "Penugasan Inspeksi Baru",
                        $"Inspeksi baru pada perusahaan {inspection.Pja ?? "-"} di {inspection.Lokasi ?? inspection.Area} membutuhkan tindak lanjut.",
                        "/Inspection/Index");
                }
            }

            // Append to Excel Sheet D:\SAP.xlsx
            try
            {
                _excelService.AppendInspection(inspection);
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "Data disimpan di database, tetapi gagal ditulis ke Excel: " + ex.Message;
            }

            // Check if any check item is 0, then spawn ActionPlan
            var checks = new[]
            {
                new { Name = "Modul 1: Kepatuhan & Sistem", Score = Math.Min(q1_1, Math.Min(q1_2, q1_3)) },
                new { Name = "Modul 2: Risiko & Keselamatan", Score = Math.Min(q2_1, Math.Min(q2_2, q2_3)) },
                new { Name = "Modul 3: SDM & Kesehatan Kerja", Score = Math.Min(q3_1, Math.Min(q3_2, q3_3)) },
                new { Name = "Modul 4: Operasi & Lingkungan", Score = Math.Min(q4_1, Math.Min(q4_2, q4_3)) },
                new { Name = "Modul 5: Monitoring & Perbaikan", Score = Math.Min(q5_1, Math.Min(q5_2, q5_3)) }
            };

            foreach (var check in checks)
            {
                if (check.Score == 0)
                {
                    var actionPlan = new ActionPlan
                    {
                        Tanggal = inspection.Tanggal,
                        Waktu = inspection.Waktu,
                        Nama = userName,
                        Nik = userNik,
                        Departemen = userDept,
                        Area = area,
                        Lokasi = lokasi,
                        DetilLokasi = detilLokasi,
                        ItemSap = "inspection",
                        KategoriTemuan = check.Name,
                        DetilTemuan = $"Temuan ketidaksesuaian (skor 0) saat inspeksi '{inspection.JenisInspeksi}' pada {check.Name}. Catatan: {catatan}",
                        Status = "Open",
                        Pja = inspection.Pja,
                        NikPja = inspection.NikPja,
                        DepartemenPja = inspection.DepartemenPja,
                        PerusahaanId = inspection.PerusahaanId,
                        CreatedAt = DateTime.Now
                    };

                    _context.ActionPlans.Add(actionPlan);
                    await _context.SaveChangesAsync();

                    try
                    {
                        _excelService.AppendActionPlan(actionPlan);
                    }
                    catch (Exception)
                    {
                        // Fail silently for secondary Excel write or log it
                    }
                }
            }

            TempData["SuccessMessage"] = isNew ? "Formulir Safety Inspeksi berhasil dikirim." : "Formulir Safety Inspeksi berhasil diperbarui.";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var inspection = await _context.Inspections.FindAsync(id);
            if (inspection == null || inspection.IsDeleted) return NotFound();
            return Json(inspection);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var inspection = await _context.Inspections.FindAsync(id);
            if (inspection == null || inspection.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (inspection.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            inspection.IsDeleted = true;
            _context.Inspections.Update(inspection);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Inspeksi berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        private static bool TryParseCompanyNikToken(string? nikToken, out int perusahaanId)
        {
            perusahaanId = 0;
            if (string.IsNullOrWhiteSpace(nikToken))
            {
                return false;
            }

            const string prefix = "COMPANY:";
            if (!nikToken.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var raw = nikToken.Substring(prefix.Length).Trim();
            return int.TryParse(raw, out perusahaanId) && perusahaanId > 0;
        }

        private async Task<int> CreateCompanyBroadcastNotificationAsync(int perusahaanId, string title, string message, string url)
        {
            // Prioritas: semua akun login perusahaan (tbl_t_app_user).
            var recipientNiks = await _context.AppUsers
                .Where(a => a.IdPerusahaan == perusahaanId && !string.IsNullOrEmpty(a.Nik))
                .Select(a => a.Nik)
                .Distinct()
                .ToListAsync();

            // Fallback: jika belum ada riwayat login, kirim ke seluruh karyawan aktif perusahaan.
            if (recipientNiks.Count == 0)
            {
                recipientNiks = await _context.Karyawans
                    .Where(k => k.StatusAktif && k.IdPerusahaan == perusahaanId)
                    .Select(k => k.NoNik)
                    .Distinct()
                    .ToListAsync();
            }

            if (recipientNiks.Count == 0)
            {
                return 0;
            }

            var notifications = new List<Notification>();
            foreach (var nik in recipientNiks)
            {
                notifications.Add(new Notification
                {
                    RecipientNik = nik,
                    Title = title,
                    Message = message,
                    Url = url
                });
            }

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();
            return notifications.Count;
        }
    }
}
