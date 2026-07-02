using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EventQrController : Controller
    {
        private readonly AppDbContext _context;

        public EventQrController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Event QR Absensi";
            ViewData["ActiveTab"] = "EventQrAdmin";

            var now = DateTime.Now;
            var events = await _context.AttendanceEvents
                .AsNoTracking()
                .OrderByDescending(e => e.StartAt)
                .Select(e => new EventQrRowViewModel
                {
                    Id = e.Id,
                    EventName = e.EventName,
                    EventLocation = e.EventLocation,
                    EventDescription = e.EventDescription,
                    StartAt = e.StartAt,
                    EndAt = e.EndAt,
                    IsActive = e.IsActive,
                    QrToken = e.QrToken,
                    CreatedAt = e.CreatedAt,
                    TotalAttendees = e.AttendanceRecords.Count
                })
                .ToListAsync();

            var vm = new EventQrIndexViewModel
            {
                Form = new EventQrCreateForm
                {
                    StartAt = now.AddMinutes(30),
                    EndAt = now.AddHours(2),
                    IsActive = true
                },
                Events = events
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EventQrCreateForm form)
        {
            if (form.EndAt <= form.StartAt)
            {
                ModelState.AddModelError(nameof(form.EndAt), "Waktu selesai harus lebih besar dari waktu mulai.");
            }

            if (!ModelState.IsValid)
            {
                ViewData["HeaderTitle"] = "Event QR Absensi";
                ViewData["ActiveTab"] = "EventQrAdmin";

                var events = await _context.AttendanceEvents
                    .AsNoTracking()
                    .OrderByDescending(e => e.StartAt)
                    .Select(e => new EventQrRowViewModel
                    {
                        Id = e.Id,
                        EventName = e.EventName,
                        EventLocation = e.EventLocation,
                        EventDescription = e.EventDescription,
                        StartAt = e.StartAt,
                        EndAt = e.EndAt,
                        IsActive = e.IsActive,
                        QrToken = e.QrToken,
                        CreatedAt = e.CreatedAt,
                        TotalAttendees = e.AttendanceRecords.Count
                    })
                    .ToListAsync();

                return View("Index", new EventQrIndexViewModel { Form = form, Events = events });
            }

            var creator = User.Identity?.Name ?? "admin";
            var ev = new AttendanceEvent
            {
                EventName = form.EventName.Trim(),
                EventLocation = string.IsNullOrWhiteSpace(form.EventLocation) ? null : form.EventLocation.Trim(),
                EventDescription = string.IsNullOrWhiteSpace(form.EventDescription) ? null : form.EventDescription.Trim(),
                StartAt = form.StartAt,
                EndAt = form.EndAt,
                IsActive = form.IsActive,
                QrToken = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.Now,
                CreatedBy = creator
            };

            _context.AttendanceEvents.Add(ev);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Event berhasil dibuat dan QR siap dibagikan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var ev = await _context.AttendanceEvents.FirstOrDefaultAsync(x => x.Id == id);
            if (ev == null)
            {
                TempData["ErrorMessage"] = "Event tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }

            ev.IsActive = !ev.IsActive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = ev.IsActive
                ? "Event diaktifkan kembali."
                : "Event dinonaktifkan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateQr(int id)
        {
            var ev = await _context.AttendanceEvents.FirstOrDefaultAsync(x => x.Id == id);
            if (ev == null)
            {
                TempData["ErrorMessage"] = "Event tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }

            ev.QrToken = Guid.NewGuid().ToString("N");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Token QR event berhasil diperbarui.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Attendance(int id)
        {
            ViewData["HeaderTitle"] = "Daftar Kehadiran Event";
            ViewData["ActiveTab"] = "EventQrAdmin";

            var ev = await _context.AttendanceEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (ev == null)
            {
                return NotFound();
            }

            var records = await _context.AttendanceRecords
                .AsNoTracking()
                .Where(r => r.AttendanceEventId == id)
                .OrderByDescending(r => r.ScanAt)
                .ToListAsync();

            return View(new EventQrAttendanceViewModel
            {
                Event = ev,
                Records = records
            });
        }
    }

    public class EventQrIndexViewModel
    {
        public EventQrCreateForm Form { get; set; } = new EventQrCreateForm();
        public List<EventQrRowViewModel> Events { get; set; } = new List<EventQrRowViewModel>();
    }

    public class EventQrCreateForm
    {
        [Required(ErrorMessage = "Nama acara wajib diisi")]
        [MaxLength(160)]
        public string EventName { get; set; } = string.Empty;

        [MaxLength(220)]
        public string? EventLocation { get; set; }

        [MaxLength(1200)]
        public string? EventDescription { get; set; }

        [Required]
        public DateTime StartAt { get; set; }

        [Required]
        public DateTime EndAt { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class EventQrRowViewModel
    {
        public int Id { get; set; }
        public string EventName { get; set; } = string.Empty;
        public string? EventLocation { get; set; }
        public string? EventDescription { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public bool IsActive { get; set; }
        public string QrToken { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalAttendees { get; set; }
    }

    public class EventQrAttendanceViewModel
    {
        public AttendanceEvent Event { get; set; } = new AttendanceEvent();
        public List<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
    }
}
