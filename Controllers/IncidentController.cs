using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.Security.Claims;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class IncidentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;

        public IncidentController(AppDbContext context, MBS_SAP.Services.ImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }

        private bool IsAdmin() => User.IsInRole("Admin");

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["ActiveTab"] = "Incident";
            ViewData["IsAdmin"] = IsAdmin();

            var incidents = await _context.IncidentNewsList
                .Where(i => i.IsPublished)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();

            return View(incidents);
        }

        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            ViewData["ActiveTab"] = "Incident";
            ViewData["IsAdmin"] = IsAdmin();

            var incident = await _context.IncidentNewsList
                .FirstOrDefaultAsync(i => i.Id == id);

            if (incident == null || (!incident.IsPublished && !IsAdmin()))
            {
                TempData["ErrorMessage"] = "Berita insiden tidak ditemukan.";
                return RedirectToAction("Index");
            }

            return View(incident);
        }

        [HttpGet]
        public IActionResult Create()
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk membuat berita insiden.";
                return RedirectToAction("Index");
            }

            ViewData["ActiveTab"] = "Incident";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(string judul, string konten, string? lokasi, 
            DateTime? tanggalKejadian, string? kategori, IFormFile? gambar)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(judul) || string.IsNullOrWhiteSpace(konten))
            {
                TempData["ErrorMessage"] = "Judul dan konten wajib diisi!";
                ViewData["ActiveTab"] = "Incident";
                return View();
            }

            string? gambarUrl = null;
            if (gambar != null && gambar.Length > 0)
            {
                gambarUrl = await _imageUploadService.UploadAndCompressImageAsync(gambar, "incidents", "inc");
            }

            var incident = new IncidentNews
            {
                Judul = judul,
                Konten = konten,
                GambarUrl = gambarUrl,
                Lokasi = lokasi,
                TanggalKejadian = tanggalKejadian,
                Kategori = kategori,
                DibuatOleh = User.Identity?.Name ?? "Admin",
                NikPembuat = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0",
                CreatedAt = DateTime.Now
            };

            _context.IncidentNewsList.Add(incident);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Berita insiden berhasil dipublikasikan!";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses.";
                return RedirectToAction("Index");
            }

            var incident = await _context.IncidentNewsList.FindAsync(id);
            if (incident != null)
            {
                _context.IncidentNewsList.Remove(incident);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Berita insiden berhasil dihapus.";
            }

            return RedirectToAction("Index");
        }
    }
}
