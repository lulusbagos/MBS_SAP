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
            ViewData["HeaderTitle"] = "Display Kehadiran Event";
            ViewData["ActiveTab"] = "EventQrAdmin";
            ViewData["HideNav"] = true;
            ViewData["HideHeader"] = true;

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

            var activeEmployees = await (
                from k in _context.Karyawans
                join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                join company in _context.Perusahaans on k.IdPerusahaan equals company.PerusahaanId
                join job in _context.Jabatans on k.IdJabatan equals job.JabatanId into jobGroup
                from job in jobGroup.DefaultIfEmpty()
                join dept in _context.Departemens on k.IdDepartemen equals dept.DepartemenId into deptGroup
                from dept in deptGroup.DefaultIfEmpty()
                where k.StatusAktif && company.StatusAktif
                select new EventAttendanceEmployeeViewModel
                {
                    Nik = k.NoNik,
                    Nama = p.NamaLengkap,
                    Jabatan = job != null ? (job.NamaJabatan ?? "-") : "-",
                    Departemen = dept != null ? (dept.NamaDepartemen ?? "-") : "-",
                    CompanyId = company.PerusahaanId,
                    CompanyName = company.NamaPerusahaan ?? $"Company {company.PerusahaanId}"
                })
                .ToListAsync();

            var employeeByNik = activeEmployees
                .Where(e => !string.IsNullOrWhiteSpace(e.Nik))
                .GroupBy(e => e.Nik.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var attendeeRows = records
                .Select(r =>
                {
                    employeeByNik.TryGetValue((r.Nik ?? string.Empty).Trim(), out var emp);
                    return new EventAttendanceAttendeeViewModel
                    {
                        Nik = r.Nik,
                        Nama = !string.IsNullOrWhiteSpace(r.Nama) ? r.Nama! : emp?.Nama ?? "-",
                        Jabatan = !string.IsNullOrWhiteSpace(r.Jabatan) ? r.Jabatan! : emp?.Jabatan ?? "-",
                        Departemen = emp?.Departemen ?? "-",
                        CompanyId = emp?.CompanyId,
                        CompanyName = !string.IsNullOrWhiteSpace(r.Perusahaan) ? r.Perusahaan! : emp?.CompanyName ?? "Tanpa Perusahaan",
                        ScanAt = r.ScanAt
                    };
                })
                .OrderByDescending(x => x.ScanAt)
                .ToList();

            var activeCompanyRows = activeEmployees
                .GroupBy(e => new { e.CompanyId, e.CompanyName })
                .Select(g =>
                {
                    var companyNiks = g.Select(x => x.Nik.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var attendees = attendeeRows
                        .Where(a => !string.IsNullOrWhiteSpace(a.Nik) && companyNiks.Contains(a.Nik.Trim()))
                        .OrderByDescending(a => a.ScanAt)
                        .ToList();

                    var attendedCount = attendees
                        .Select(a => a.Nik.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    var activeCount = companyNiks.Count;
                    var pendingCount = Math.Max(0, activeCount - attendedCount);
                    var hasAttendance = attendedCount > 0;

                    return new EventAttendanceCompanyStatusViewModel
                    {
                        CompanyId = g.Key.CompanyId,
                        CompanyName = g.Key.CompanyName,
                        ActiveEmployees = activeCount,
                        AttendedEmployees = attendedCount,
                        PendingEmployees = pendingCount,
                        HasAttendance = hasAttendance,
                        AttendanceRate = activeCount > 0 ? Math.Round((double)attendedCount / activeCount * 100.0, 1) : 0.0,
                        RecentAttendees = attendees.Take(6).ToList()
                    };
                })
                .OrderByDescending(c => c.HasAttendance)
                .ThenByDescending(c => c.AttendedEmployees)
                .ThenBy(c => c.CompanyName)
                .ToList();

            var pendingCompanies = activeCompanyRows
                .Where(c => !c.HasAttendance)
                .OrderByDescending(c => c.ActiveEmployees)
                .ThenBy(c => c.CompanyName)
                .ToList();

            return View(new EventQrAttendanceViewModel
            {
                Event = ev,
                Records = records,
                CompanyStatuses = activeCompanyRows,
                PendingCompanies = pendingCompanies,
                Attendees = attendeeRows,
                TotalActiveCompanies = activeCompanyRows.Count,
                TotalPresentCompanies = activeCompanyRows.Count(c => c.HasAttendance),
                TotalActiveEmployees = activeEmployees.Select(e => e.Nik.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TotalPresentEmployees = attendeeRows.Select(a => a.Nik.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()
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
        public List<EventAttendanceCompanyStatusViewModel> CompanyStatuses { get; set; } = new List<EventAttendanceCompanyStatusViewModel>();
        public List<EventAttendanceCompanyStatusViewModel> PendingCompanies { get; set; } = new List<EventAttendanceCompanyStatusViewModel>();
        public List<EventAttendanceAttendeeViewModel> Attendees { get; set; } = new List<EventAttendanceAttendeeViewModel>();
        public int TotalActiveCompanies { get; set; }
        public int TotalPresentCompanies { get; set; }
        public int TotalActiveEmployees { get; set; }
        public int TotalPresentEmployees { get; set; }
    }

    public class EventAttendanceEmployeeViewModel
    {
        public string Nik { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Jabatan { get; set; } = string.Empty;
        public string Departemen { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
    }

    public class EventAttendanceAttendeeViewModel
    {
        public string Nik { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Jabatan { get; set; } = string.Empty;
        public string Departemen { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public DateTime ScanAt { get; set; }
    }

    public class EventAttendanceCompanyStatusViewModel
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int ActiveEmployees { get; set; }
        public int AttendedEmployees { get; set; }
        public int PendingEmployees { get; set; }
        public bool HasAttendance { get; set; }
        public double AttendanceRate { get; set; }
        public List<EventAttendanceAttendeeViewModel> RecentAttendees { get; set; } = new List<EventAttendanceAttendeeViewModel>();
    }
}
