using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class CoachingController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public CoachingController(AppDbContext context, MBS_SAP.Services.ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        // GET: Coaching
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Coaching & Pembinaan";
            ViewData["ActiveTab"] = "Coaching";

            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(companyIdStr, out var cid) && cid > 0 ? cid : null;

            // Load master areas
            var areas = await _context.MasterAreas.OrderBy(a => a.NamaArea).ToListAsync();
            ViewBag.Areas = areas;

            // Load accessible companies for participant company filter
            List<int> accessibleIds;
            if (companyId.HasValue)
            {
                accessibleIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(companyId.Value);
            }
            else
            {
                // Admin/no-company: all active companies
                accessibleIds = await _context.Perusahaans.Where(p => p.StatusAktif).Select(p => p.PerusahaanId).ToListAsync();
            }

            var companyList = await _context.Perusahaans
                .Where(p => p.StatusAktif && accessibleIds.Contains(p.PerusahaanId))
                .OrderBy(p => p.NamaPerusahaan)
                .Select(p => new { p.PerusahaanId, p.NamaPerusahaan })
                .ToListAsync();

            ViewBag.CompanyList = companyList;
            ViewBag.UserCompanyId = companyId;

            var query = _context.Coachings.Include(c => c.Participants).Where(c => !c.IsDeleted);

            // Apply hierarchy filtering
            if (companyId.HasValue)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(companyId.Value);
                query = query.Where(c => c.PerusahaanId.HasValue && allowedIds.Contains(c.PerusahaanId.Value));
            }

            var reports = await query
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        // GET: Coaching/GetData/5
        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var coaching = await _context.Coachings.Include(c => c.Participants).FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (coaching == null) return NotFound();

            // Filter berdasarkan perusahaan login (cross-company check)
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(companyIdStr, out var cid) && cid > 0)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(cid);
                if (!coaching.PerusahaanId.HasValue || !allowedIds.Contains(coaching.PerusahaanId.Value))
                {
                    return Forbid();
                }
            }

            return Json(new {
                coaching.Id,
                tanggal = coaching.Tanggal.ToString("yyyy-MM-dd"),
                waktu = coaching.Waktu.ToString(@"hh\:mm"),
                coaching.Area,
                coaching.Lokasi,
                coaching.DetilLokasi,
                coaching.Tema,
                coaching.Feedback,
                coaching.Komitmen,
                participants = coaching.Participants.Select(p => p.Nik).ToList()
            });
        }

        // POST: Coaching/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            int? id,
            DateTime tanggal,
            string waktuStr,
            string? area,
            string? lokasi,
            string? detilLokasi,
            string? tema,
            string? feedback,
            string? komitmen,
            List<string> selectedParticipants, // List of NIKs
            IFormFile? foto)
        {
            if (string.IsNullOrEmpty(tema))
            {
                TempData["ErrorMessage"] = "Tema Coaching wajib diisi!";
                return RedirectToAction(nameof(Index));
            }

            // Restrict date to +/- 7 days
            var minDate = DateTime.Today.AddDays(-7);
            var maxDate = DateTime.Today.AddDays(7);
            if (tanggal < minDate || tanggal > maxDate)
            {
                TempData["ErrorMessage"] = "Tanggal Coaching harus berada dalam rentang 7 hari sebelum dan sesudah hari ini!";
                return RedirectToAction(nameof(Index));
            }

            TimeSpan waktu = DateTime.Now.TimeOfDay;
            if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
            {
                waktu = parsedWaktu;
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            var userName = User.Identity?.Name ?? "Anonymous";
            var userDept = User.FindFirst("Department")?.Value ?? "General";
            var userCompanyIdStr = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(userCompanyIdStr, out var ucid) && ucid > 0 ? ucid : null;
            if (!userCompanyId.HasValue)
            {
                var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == userNik && k.StatusAktif);
                if (karyawan != null) userCompanyId = karyawan.IdPerusahaan;
            }

            Coaching? coaching;
            bool isNew = true;

            if (id.HasValue && id.Value > 0)
            {
                coaching = await _context.Coachings.Include(c => c.Participants).FirstOrDefaultAsync(c => c.Id == id.Value);
                if (coaching == null || coaching.IsDeleted) return NotFound();

                if (coaching.Nik != userNik && !User.IsInRole("Admin"))
                {
                    TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan ini.";
                    return RedirectToAction(nameof(Index));
                }

                // Cross-company edit check
                if (userCompanyId.HasValue)
                {
                    var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(userCompanyId.Value);
                    if (coaching.PerusahaanId.HasValue && !allowedIds.Contains(coaching.PerusahaanId.Value))
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan dari perusahaan lain.";
                        return RedirectToAction(nameof(Index));
                    }
                }
                isNew = false;
            }
            else
            {
                // Photo is required for new coaching sessions
                if (foto == null || foto.Length == 0)
                {
                    TempData["ErrorMessage"] = "Foto bukti kegiatan coaching wajib diunggah!";
                    return RedirectToAction(nameof(Index));
                }

                coaching = new Coaching
                {
                    Nama = userName,
                    Nik = userNik,
                    Departemen = userDept,
                    PerusahaanId = userCompanyId,
                    CreatedAt = DateTime.Now
                };
            }

            coaching.Tanggal = tanggal == default ? DateTime.Today : tanggal;
            coaching.Waktu = waktu;
            coaching.Area = area;
            coaching.Lokasi = lokasi;
            coaching.DetilLokasi = detilLokasi;
            coaching.Tema = tema;
            coaching.Feedback = feedback;
            coaching.Komitmen = komitmen;

            // Handle Photo Upload
            if (foto != null && foto.Length > 0)
            {
                var relativePath = await _imageUploadService.UploadAndCompressImageAsync(foto, "CoachingKegiatan", userNik);
                if (!string.IsNullOrEmpty(relativePath))
                {
                    coaching.Foto = relativePath;
                }
            }

            if (isNew)
            {
                _context.Coachings.Add(coaching);
            }
            else
            {
                // Remove old participants and re-add
                _context.CoachingParticipants.RemoveRange(coaching.Participants);
                coaching.Participants.Clear();
            }

            // Save header to get ID if new
            await _context.SaveChangesAsync();

            // Save participants
            if (selectedParticipants != null && selectedParticipants.Any())
            {
                // Retrieve names of selected NIKs
                var activeEmps = await (from k in _context.Karyawans
                                        join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                        where k.StatusAktif && selectedParticipants.Contains(k.NoNik)
                                        select new { k.NoNik, p.NamaLengkap })
                                       .ToListAsync();

                var notifications = new List<Notification>();

                foreach (var emp in activeEmps)
                {
                    var participant = new CoachingParticipant
                    {
                        CoachingId = coaching.Id,
                        Nik = emp.NoNik,
                        Nama = emp.NamaLengkap
                    };
                    _context.CoachingParticipants.Add(participant);

                    // Add notification for participant
                    notifications.Add(new Notification
                    {
                        RecipientNik = emp.NoNik,
                        Title = "Coaching & Pembinaan Baru",
                        Message = $"Anda telah menerima coaching ({coaching.Tema}) dari {userName} pada {coaching.Tanggal.ToString("dd MMM yyyy")}.",
                        Url = "/Performance/Index",
                        NotifType = "general",
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });
                }
                _context.Notifications.AddRange(notifications);
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = isNew ? "Laporan Coaching baru berhasil disimpan!" : "Laporan Coaching berhasil diperbarui!";
            return RedirectToAction(nameof(Index));
        }

        // GET: Coaching/SearchEmployees?q=...&companyId=...
        [HttpGet]
        public async Task<IActionResult> SearchEmployees(string? q, int? companyId)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Json(new List<object>());

            var userCompanyIdStr = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(userCompanyIdStr, out var ucid) && ucid > 0 ? ucid : null;

            // Determine accessible companies
            List<int> accessibleIds;
            if (userCompanyId.HasValue)
            {
                accessibleIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(userCompanyId.Value);
            }
            else
            {
                accessibleIds = await _context.Perusahaans.Where(p => p.StatusAktif).Select(p => p.PerusahaanId).ToListAsync();
            }

            var term = q.Trim().ToLower();

            var empQuery = from k in _context.Karyawans
                           join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                           join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                           from c in cg.DefaultIfEmpty()
                           where k.StatusAktif && accessibleIds.Contains(k.IdPerusahaan)
                                 && (p.NamaLengkap.ToLower().Contains(term) || k.NoNik.ToLower().Contains(term))
                           select new
                           {
                               nik = k.NoNik,
                               nama = p.NamaLengkap,
                               companyId = k.IdPerusahaan,
                               companyName = c != null ? c.NamaPerusahaan : "—"
                           };

            if (companyId.HasValue && companyId.Value > 0)
            {
                empQuery = empQuery.Where(e => e.companyId == companyId.Value);
            }

            var result = await empQuery.OrderBy(e => e.nama).Take(30).ToListAsync();
            return Json(result);
        }

        // POST: Coaching/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var coaching = await _context.Coachings.FindAsync(id);
            if (coaching == null) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (coaching.Nik != userNik && !User.IsInRole("Admin"))
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk menghapus laporan ini.";
                return RedirectToAction(nameof(Index));
            }

            // Cross-company delete check
            var userCompanyIdStr = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(userCompanyIdStr, out var ucid) && ucid > 0)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(ucid);
                if (coaching.PerusahaanId.HasValue && !allowedIds.Contains(coaching.PerusahaanId.Value))
                {
                    TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk menghapus laporan dari perusahaan lain.";
                    return RedirectToAction(nameof(Index));
                }
            }

            coaching.IsDeleted = true;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan Coaching berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }
    }
}
