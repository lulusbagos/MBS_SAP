using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class P2hController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<P2hController> _logger;
        private readonly ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public P2hController(AppDbContext context, ILogger<P2hController> logger, ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _logger = logger;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        // Define checklist questions
        public static readonly string[] GolA = new string[]
        {
            "Klakson/Horn",
            "Rem Tangan/Park Brake",
            "Masa Berlaku Stiker Komisioning/Commissioning Sticker Expired Date",
            "APAR",
            "Lampu Mundur Dan Alarm Mundur",
            "Radio Komunikasi",
            "Lampu Rotary",
            "Level Oli Kemudi/Level Oil Steering",
            "Sabuk Pengaman/Safety Belt",
            "Rem Kaki/Foot Brake",
            "Tebal Ban/Tyre Tread",
            "Lampu Depan/Head Light",
            "Baut Roda/Wheel Stud",
            "Lampu Belok Dan Lampu Belakang/Sign And Tail Lights",
            "Lampu Rem/Brake Light",
            "Kemudi/Steering"
        };

        public static readonly string[] GolB = new string[]
        {
            "Kaca Depan/Windscreen",
            "Bendera Dan Bugywhip",
            "Level Oli Mesin/Engine Oil Level",
            "Level Oli Persneling/Clutch Oil Level",
            "Panel Gauge Dan Indikator",
            "Level Air/Water Level",
            "Level Minyak Rem/Brake Fluids Level",
            "Masa Servis/Service Due",
            "Level Air Radiator/Radiator Water Level",
            "Kotak P3K/First Aid Kit"
        };

        public static readonly string[] GolC = new string[]
        {
            "Cairan Wiper/Windscreen Washer",
            "Rangka Pengaman Kabin",
            "Kerusakan Lain",
            "Kebersihan Luar Mobil/Exterior Clean",
            "Pintu-pintu/Doors",
            "Aki/Battery Pack",
            "Ban Serep/Spare Tyre",
            "Kebersihan Dalam Mobil/Interior Clean",
            "Segitiga Pengaman/Triangle Safety Cone",
            "Dongkrak/Jack",
            "Kaca/Mirror"
        };

        public class ChecklistItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Status { get; set; } = "GOOD"; // GOOD / NOT_GOOD
            public string? PhotoUrl { get; set; }
        }

        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "P2H Kendaraan Harian";
            ViewData["ActiveTab"] = "P2h";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(companyIdStr, out var cid) && cid > 0 ? cid : null;

            var query = _context.P2hReports.Where(r => !r.IsDeleted);

            // Filter hierarki perusahaan mutlak untuk semua role (admin & non-admin)
            if (companyId.HasValue)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(companyId.Value);
                var allowedNiks = _context.AppUsers
                    .Where(u => u.IdPerusahaan.HasValue && allowedIds.Contains(u.IdPerusahaan.Value))
                    .Select(u => u.Nik);
                query = query.Where(r => allowedNiks.Contains(r.Nik));
            }

            if (!isAdmin && !string.IsNullOrEmpty(userNik))
            {
                query = query.Where(r => r.Nik == userNik);
            }

            var reports = await query
                .OrderByDescending(r => r.Tanggal)
                .ThenByDescending(r => r.Waktu)
                .ToListAsync();

            // Populate Vehicles list for dropdown search
            var vehicles = await _context.P2hVehicles
                .Where(v => !v.IsDeleted)
                .OrderBy(v => v.NoLambung)
                .ToListAsync();

            ViewBag.Vehicles = vehicles;
            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";

            return View(reports);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int? id, string noLambung, string jenisKendaraan, string merek, double kilometer, string simperKimper, IFormFile? fotoSpeedometer, DateTime tanggal, string waktuStr)
        {
            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";

                if (string.IsNullOrEmpty(noLambung))
                {
                    TempData["ErrorMessage"] = "No Lambung Kendaraan wajib diisi.";
                    return RedirectToAction(nameof(Index));
                }

                noLambung = noLambung.Trim().ToUpper();

                // 1. Manage/Save Vehicle Master
                var existingVehicle = await _context.P2hVehicles
                    .FirstOrDefaultAsync(v => v.NoLambung.ToLower() == noLambung.ToLower() && !v.IsDeleted);

                if (existingVehicle == null)
                {
                    // Create new Vehicle if it doesn't exist
                    existingVehicle = new P2hVehicle
                    {
                        NoLambung = noLambung,
                        JenisKendaraan = jenisKendaraan,
                        Merek = merek,
                        CreatedAt = DateTime.Now
                    };
                    _context.P2hVehicles.Add(existingVehicle);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    // Update metadata if changed
                    bool changed = false;
                    if (!string.IsNullOrEmpty(jenisKendaraan) && existingVehicle.JenisKendaraan != jenisKendaraan)
                    {
                        existingVehicle.JenisKendaraan = jenisKendaraan;
                        changed = true;
                    }
                    if (!string.IsNullOrEmpty(merek) && existingVehicle.Merek != merek)
                    {
                        existingVehicle.Merek = merek;
                        changed = true;
                    }
                    if (changed)
                    {
                        _context.P2hVehicles.Update(existingVehicle);
                        await _context.SaveChangesAsync();
                    }
                }

                // 2. Fetch or initialize P2H Report
                P2hReport? report;
                bool isNew = true;

                if (id.HasValue && id.Value > 0)
                {
                    report = await _context.P2hReports.FindAsync(id.Value);
                    if (report == null || report.IsDeleted) return NotFound();

                    if (report.Nik != userNik && !User.IsInRole("Admin"))
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan ini.";
                        return RedirectToAction(nameof(Index));
                    }
                    isNew = false;
                }
                else
                {
                    report = new P2hReport
                    {
                        Nama = userName,
                        Nik = userNik,
                        CreatedAt = DateTime.Now
                    };
                }

                // Handle Date & Time
                TimeSpan waktu = DateTime.Now.TimeOfDay;
                if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
                {
                    waktu = parsedWaktu;
                }
                report.Tanggal = tanggal.Date;
                report.Waktu = waktu;

                // Set Metadata
                report.NoLambung = noLambung;
                report.JenisKendaraan = jenisKendaraan;
                report.Merek = merek;
                report.Kilometer = kilometer;
                report.SimperKimper = simperKimper;

                // Handle Speedometer Photo Upload
                if (fotoSpeedometer != null && fotoSpeedometer.Length > 0)
                {
                    var uploadedPath = await _imageUploadService.UploadAndCompressImageAsync(fotoSpeedometer, "p2h", "speedo");
                    if (uploadedPath != null)
                    {
                        report.FotoSpeedometer = uploadedPath;
                    }
                }

                // Parse existing JSONs to retain photos if we are editing
                var oldGolA = string.IsNullOrEmpty(report.GolA_Json) ? new List<ChecklistItem>() : JsonSerializer.Deserialize<List<ChecklistItem>>(report.GolA_Json);
                var oldGolB = string.IsNullOrEmpty(report.GolB_Json) ? new List<ChecklistItem>() : JsonSerializer.Deserialize<List<ChecklistItem>>(report.GolB_Json);
                var oldGolC = string.IsNullOrEmpty(report.GolC_Json) ? new List<ChecklistItem>() : JsonSerializer.Deserialize<List<ChecklistItem>>(report.GolC_Json);

                // 3. Process Checklist for Golongan A
                var listA = new List<ChecklistItem>();
                for (int i = 0; i < GolA.Length; i++)
                {
                    string status = Request.Form["golA_status_" + i].ToString();
                    if (string.IsNullOrEmpty(status)) status = "GOOD";

                    var item = new ChecklistItem
                    {
                        Id = i + 1,
                        Name = GolA[i],
                        Status = status
                    };

                    // Check if a new file is uploaded
                    var file = Request.Form.Files["golA_photo_" + i];
                    if (file != null && file.Length > 0)
                    {
                        item.PhotoUrl = await _imageUploadService.UploadAndCompressImageAsync(file, "p2h", $"golA_{i}");
                    }
                    else if (!isNew && oldGolA != null)
                    {
                        // Retain old photo if status didn't change to GOOD
                        var oldItem = oldGolA.FirstOrDefault(x => x.Id == i + 1);
                        if (oldItem != null && item.Status == "NOT_GOOD")
                        {
                            item.PhotoUrl = oldItem.PhotoUrl;
                        }
                    }
                    listA.Add(item);
                }
                report.GolA_Json = JsonSerializer.Serialize(listA);

                // 4. Process Checklist for Golongan B
                var listB = new List<ChecklistItem>();
                for (int i = 0; i < GolB.Length; i++)
                {
                    string status = Request.Form["golB_status_" + i].ToString();
                    if (string.IsNullOrEmpty(status)) status = "GOOD";

                    var item = new ChecklistItem
                    {
                        Id = i + 1,
                        Name = GolB[i],
                        Status = status
                    };

                    var file = Request.Form.Files["golB_photo_" + i];
                    if (file != null && file.Length > 0)
                    {
                        item.PhotoUrl = await _imageUploadService.UploadAndCompressImageAsync(file, "p2h", $"golB_{i}");
                    }
                    else if (!isNew && oldGolB != null)
                    {
                        var oldItem = oldGolB.FirstOrDefault(x => x.Id == i + 1);
                        if (oldItem != null && item.Status == "NOT_GOOD")
                        {
                            item.PhotoUrl = oldItem.PhotoUrl;
                        }
                    }
                    listB.Add(item);
                }
                report.GolB_Json = JsonSerializer.Serialize(listB);

                // 5. Process Checklist for Golongan C
                var listC = new List<ChecklistItem>();
                for (int i = 0; i < GolC.Length; i++)
                {
                    string status = Request.Form["golC_status_" + i].ToString();
                    if (string.IsNullOrEmpty(status)) status = "GOOD";

                    var item = new ChecklistItem
                    {
                        Id = i + 1,
                        Name = GolC[i],
                        Status = status
                    };

                    var file = Request.Form.Files["golC_photo_" + i];
                    if (file != null && file.Length > 0)
                    {
                        item.PhotoUrl = await _imageUploadService.UploadAndCompressImageAsync(file, "p2h", $"golC_{i}");
                    }
                    else if (!isNew && oldGolC != null)
                    {
                        var oldItem = oldGolC.FirstOrDefault(x => x.Id == i + 1);
                        if (oldItem != null && item.Status == "NOT_GOOD")
                        {
                            item.PhotoUrl = oldItem.PhotoUrl;
                        }
                    }
                    listC.Add(item);
                }
                report.GolC_Json = JsonSerializer.Serialize(listC);

                if (isNew)
                {
                    _context.P2hReports.Add(report);
                }
                else
                {
                    _context.P2hReports.Update(report);
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = isNew ? "Laporan P2H berhasil disimpan!" : "Laporan P2H berhasil diperbarui!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving P2H report");
                TempData["ErrorMessage"] = "Terjadi kesalahan saat menyimpan data P2H.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var report = await _context.P2hReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            return Json(new
            {
                id = report.Id,
                nik = report.Nik,
                nama = report.Nama,
                tanggal = report.Tanggal.ToString("yyyy-MM-dd"),
                waktu = report.Waktu.ToString(@"hh\:mm"),
                jenisKendaraan = report.JenisKendaraan,
                noLambung = report.NoLambung,
                kilometer = report.Kilometer,
                merek = report.Merek,
                simperKimper = report.SimperKimper,
                fotoSpeedometer = report.FotoSpeedometer,
                golA = string.IsNullOrEmpty(report.GolA_Json) ? new List<ChecklistItem>() : JsonSerializer.Deserialize<List<ChecklistItem>>(report.GolA_Json),
                golB = string.IsNullOrEmpty(report.GolB_Json) ? new List<ChecklistItem>() : JsonSerializer.Deserialize<List<ChecklistItem>>(report.GolB_Json),
                golC = string.IsNullOrEmpty(report.GolC_Json) ? new List<ChecklistItem>() : JsonSerializer.Deserialize<List<ChecklistItem>>(report.GolC_Json)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var report = await _context.P2hReports.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (report.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            report.IsDeleted = true;
            _context.P2hReports.Update(report);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan P2H berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> CheckDuplicateVehicle(string noLambung)
        {
            if (string.IsNullOrEmpty(noLambung)) return Json(false);
            
            var exists = await _context.P2hVehicles
                .AnyAsync(v => v.NoLambung.ToLower() == noLambung.Trim().ToLower() && !v.IsDeleted);

            return Json(exists);
        }
    }
}
