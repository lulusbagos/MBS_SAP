using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class QrAttendanceController : Controller
    {
        private readonly AppDbContext _context;

        public QrAttendanceController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("QrAttendance/Scan/{token}")]
        public async Task<IActionResult> Scan(string token)
        {
            ViewData["HideNav"] = null;
            ViewData["HeaderTitle"] = "Scan Absensi";
            ViewData["ActiveTab"] = "QrAttendance";

            if (User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var model = new QrAttendanceScanResultViewModel
            {
                Token = token
            };

            if (string.IsNullOrWhiteSpace(token))
            {
                model.Status = "invalid";
                model.Message = "QR tidak valid. Silakan minta QR terbaru dari admin.";
                return View(model);
            }

            var ev = await _context.AttendanceEvents
                .FirstOrDefaultAsync(x => x.QrToken == token);

            if (ev == null)
            {
                model.Status = "invalid";
                model.Message = "Event tidak ditemukan. Mungkin QR sudah kadaluarsa.";
                return View(model);
            }

            model.EventName = ev.EventName;
            model.EventLocation = ev.EventLocation;
            model.StartAt = ev.StartAt;
            model.EndAt = ev.EndAt;

            if (!ev.IsActive)
            {
                model.Status = "inactive";
                model.Message = "Event sedang tidak aktif. Silakan hubungi admin.";
                return View(model);
            }

            var now = DateTime.Now;
            if (now < ev.StartAt)
            {
                model.Status = "toosoon";
                model.Message = "Absensi belum dibuka. Silakan scan sesuai jadwal acara.";
                return View(model);
            }

            if (now > ev.EndAt)
            {
                model.Status = "closed";
                model.Message = "Absensi sudah ditutup karena melewati waktu acara.";
                return View(model);
            }

            var nik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("NIK")?.Value
                ?? User.FindFirst("NoNik")?.Value
                ?? "-";

            if (string.IsNullOrWhiteSpace(nik) || nik == "-")
            {
                model.Status = "invalid";
                model.Message = "NIK akun tidak ditemukan. Hubungi admin sebelum melakukan absensi QR.";
                return View(model);
            }

            var nama = User.Identity?.Name ?? "User";
            var jabatan = User.FindFirst("JobTitle")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value ?? "Staff/Operator";
            var perusahaan = User.FindFirst("CompanyName")?.Value ?? User.FindFirst("Company")?.Value ?? "Perusahaan";

            var existing = await _context.AttendanceRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.AttendanceEventId == ev.Id && r.Nik == nik);

            if (existing != null)
            {
                model.Status = "duplicate";
                model.Message = $"Absensi sudah terekam pada {existing.ScanAt:dd MMM yyyy HH:mm}.";
                model.AttendeeName = nama;
                model.AttendeeNik = nik;
                model.RecordedAt = existing.ScanAt;
                model.ShowPopup = true;
                model.PopupTitle = "Sudah Terekam";
                model.PopupBody = $"Anda sudah absen jam {existing.ScanAt:HH:mm}. Terima kasih :)";
                return View(model);
            }

            var record = new AttendanceRecord
            {
                AttendanceEventId = ev.Id,
                Nik = nik,
                Nama = nama,
                Jabatan = jabatan,
                Perusahaan = perusahaan,
                ScanAt = DateTime.Now,
                Source = "qr"
            };

            _context.AttendanceRecords.Add(record);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Idempotent handling for near-simultaneous scans of the same user/event.
                var latest = await _context.AttendanceRecords
                    .AsNoTracking()
                    .Where(r => r.AttendanceEventId == ev.Id && r.Nik == nik)
                    .OrderByDescending(r => r.ScanAt)
                    .FirstOrDefaultAsync();

                model.Status = "duplicate";
                model.Message = latest != null
                    ? $"Absensi sudah terekam pada {latest.ScanAt:dd MMM yyyy HH:mm}."
                    : "Absensi sudah pernah terekam untuk event ini.";
                model.AttendeeName = nama;
                model.AttendeeNik = nik;
                model.RecordedAt = latest?.ScanAt;
                model.ShowPopup = true;
                model.PopupTitle = "Sudah Terekam";
                model.PopupBody = latest != null
                    ? $"Anda sudah absen jam {latest.ScanAt:HH:mm}. Terima kasih :)"
                    : "Absensi Anda sudah tercatat. Terima kasih :)";
                return View(model);
            }

            model.Status = "success";
            model.AttendeeName = nama;
            model.AttendeeNik = nik;
            model.RecordedAt = record.ScanAt;
            model.Message = "Terima kasih, kehadiran Anda sudah terekam.";
            model.ShowPopup = true;
            model.PopupTitle = "Absensi Berhasil";
            model.PopupBody = $"Anda sudah absen jam {record.ScanAt:HH:mm}. Terima kasih :)";

            return View(model);
        }
    }

    public class QrAttendanceScanResultViewModel
    {
        public string Token { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public string Message { get; set; } = string.Empty;
        public string? EventName { get; set; }
        public string? EventLocation { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public string? AttendeeName { get; set; }
        public string? AttendeeNik { get; set; }
        public DateTime? RecordedAt { get; set; }
        public bool ShowPopup { get; set; }
        public string PopupTitle { get; set; } = string.Empty;
        public string PopupBody { get; set; } = string.Empty;
    }
}
