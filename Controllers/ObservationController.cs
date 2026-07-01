using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MBS_SAP.Data;
using MBS_SAP.Models;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class ObservationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ObservationController> _logger;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;
        private readonly MBS_SAP.Services.CompanyHierarchyService _companyHierarchyService;

        public ObservationController(AppDbContext context, ILogger<ObservationController> logger, MBS_SAP.Services.ImageUploadService imageUploadService, MBS_SAP.Services.CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _logger = logger;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Observasi Lapangan";
            ViewData["ActiveTab"] = "Observation";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");
            var companyScopedNiks = await GetCurrentCompanyNiksAsync();

            var query = _context.Observations.Where(r => !r.IsDeleted);

            // Batasi data observasi berdasarkan perusahaan user aktif (strict company scope).
            if (companyScopedNiks.Count == 0)
            {
                query = query.Where(r => false);
            }
            else
            {
                query = query.Where(r => companyScopedNiks.Contains(r.Nik));
            }

            if (!isAdmin && !string.IsNullOrEmpty(userNik))
            {
                query = query.Where(r => r.Nik == userNik);
            }

            var observations = await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            await PopulateViewBagAsync();
            return View(observations);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int? id,
            Observation observation,
            DateTime tanggal,
            string waktuStr,
            IFormFile? foto,
            List<string>? hasilObservasiList,
            List<string>? keteranganList,
            List<IFormFile>? fotoList)
        {
            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "General";

                var isNew = !(id.HasValue && id.Value > 0);

                TimeSpan waktu = DateTime.Now.TimeOfDay;
                if (!string.IsNullOrEmpty(waktuStr) && TimeSpan.TryParse(waktuStr, out var parsedWaktu))
                {
                    waktu = parsedWaktu;
                }

                var baseDate = tanggal.Date.Add(waktu);

                if (!isNew)
                {
                    var report = await _context.Observations.FindAsync(id!.Value);
                    if (report == null || report.IsDeleted) return NotFound();

                    if (report.Nik != userNik && !User.IsInRole("Admin"))
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah laporan ini.";
                        return RedirectToAction(nameof(Index));
                    }

                    report.Date = baseDate;
                    report.Area = observation.Area;
                    report.Lokasi = observation.Lokasi;
                    report.DetilLokasi = observation.DetilLokasi;
                    report.KegiatanYangDiamati = observation.KegiatanYangDiamati;
                    report.DepartemenYangDiamati = observation.DepartemenYangDiamati;
                    report.DokumenPendukung = observation.DokumenPendukung;
                    report.ResikoKritis = observation.ResikoKritis;
                    report.TingkatResiko = observation.TingkatResiko;
                    report.PerihalYangDiamati = observation.PerihalYangDiamati;
                    report.HasilObservasi = observation.HasilObservasi;
                    report.Keterangan = observation.Keterangan;

                    if (foto != null && foto.Length > 0)
                    {
                        try
                        {
                            report.FotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "observations");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error uploading observation photo");
                        }
                    }

                    _context.Observations.Update(report);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Data Observasi berhasil diperbarui!";
                    return RedirectToAction(nameof(Index));
                }

                var normalizedResults = (hasilObservasiList ?? new List<string>())
                    .Select(x => x?.Trim() ?? string.Empty)
                    .ToList();
                var normalizedNotes = (keteranganList ?? new List<string>())
                    .Select(x => x?.Trim())
                    .ToList();

                // Backward compatibility for old form payload.
                if (!normalizedResults.Any() && !string.IsNullOrWhiteSpace(observation.HasilObservasi))
                {
                    normalizedResults.Add(observation.HasilObservasi.Trim());
                    normalizedNotes.Add(observation.Keterangan?.Trim());
                }

                if (!normalizedResults.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    TempData["ErrorMessage"] = "Minimal 1 hasil observasi wajib diisi.";
                    return RedirectToAction(nameof(Index));
                }

                int createdCount = 0;
                for (int i = 0; i < normalizedResults.Count; i++)
                {
                    var hasil = normalizedResults[i];
                    if (string.IsNullOrWhiteSpace(hasil))
                    {
                        continue;
                    }

                    string? fotoUrl = null;
                    if (fotoList != null && i < fotoList.Count && fotoList[i] != null && fotoList[i].Length > 0)
                    {
                        try
                        {
                            fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(fotoList[i], "observations");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error uploading observation photo list");
                        }
                    }
                    else if (i == 0 && foto != null && foto.Length > 0)
                    {
                        // Backward compatibility for old single photo input.
                        try
                        {
                            fotoUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "observations");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error uploading observation photo");
                        }
                    }

                    var report = new Observation
                    {
                        Nama = userName,
                        Nik = userNik,
                        Departemen = userDept,
                        CreatedAt = DateTime.Now,
                        Date = baseDate,
                        Area = observation.Area,
                        Lokasi = observation.Lokasi,
                        DetilLokasi = observation.DetilLokasi,
                        KegiatanYangDiamati = observation.KegiatanYangDiamati,
                        DepartemenYangDiamati = observation.DepartemenYangDiamati,
                        DokumenPendukung = observation.DokumenPendukung,
                        ResikoKritis = observation.ResikoKritis,
                        TingkatResiko = observation.TingkatResiko,
                        PerihalYangDiamati = observation.PerihalYangDiamati,
                        HasilObservasi = hasil,
                        Keterangan = i < normalizedNotes.Count ? normalizedNotes[i] : null,
                        FotoUrl = fotoUrl
                    };

                    _context.Observations.Add(report);
                    createdCount++;
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = createdCount > 1
                    ? $"{createdCount} data observasi berhasil disimpan!"
                    : "Data Observasi berhasil disimpan!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving observation");
                TempData["ErrorMessage"] = "Terjadi kesalahan saat menyimpan data.";
            }

            await PopulateViewBagAsync();
            
            var query = _context.Observations.Where(r => !r.IsDeleted);
            var userNikForError = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var companyScopedNiks = await GetCurrentCompanyNiksAsync();
            if (companyScopedNiks.Count == 0)
            {
                query = query.Where(r => false);
            }
            else
            {
                query = query.Where(r => companyScopedNiks.Contains(r.Nik));
            }

            if (!User.IsInRole("Admin") && !string.IsNullOrEmpty(userNikForError))
            {
                query = query.Where(r => r.Nik == userNikForError);
            }
            var observations = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            return View("Index", observations);
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var report = await _context.Observations.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");
            var companyScopedNiks = await GetCurrentCompanyNiksAsync();

            if (companyScopedNiks.Count == 0 || !companyScopedNiks.Contains(report.Nik))
            {
                return Unauthorized();
            }

            if (!isAdmin && !string.Equals(report.Nik, userNik, StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            return Json(new {
                id = report.Id,
                tanggal = report.Date.ToString("yyyy-MM-dd"),
                waktu = report.Date.ToString("HH:mm"),
                area = report.Area,
                lokasi = report.Lokasi,
                detilLokasi = report.DetilLokasi,
                kegiatanYangDiamati = report.KegiatanYangDiamati,
                departemenYangDiamati = report.DepartemenYangDiamati,
                dokumenPendukung = report.DokumenPendukung,
                resikoKritis = report.ResikoKritis,
                tingkatResiko = report.TingkatResiko,
                perihalYangDiamati = report.PerihalYangDiamati,
                hasilObservasi = report.HasilObservasi,
                keterangan = report.Keterangan,
                fotoUrl = report.FotoUrl
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var report = await _context.Observations.FindAsync(id);
            if (report == null || report.IsDeleted) return NotFound();

            var companyScopedNiks = await GetCurrentCompanyNiksAsync();
            if (companyScopedNiks.Count == 0 || !companyScopedNiks.Contains(report.Nik))
            {
                return Unauthorized();
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (report.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            report.IsDeleted = true;
            _context.Observations.Update(report);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Laporan Observasi berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateViewBagAsync()
        {
            var compIdClaim = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(compIdClaim, out int cid) && cid > 0 ? cid : null;

            var areaQuery = _context.MasterAreas.AsQueryable();
            if (userCompanyId.HasValue)
            {
                areaQuery = areaQuery.Where(a => a.PerusahaanId == userCompanyId.Value);
            }
            else
            {
                areaQuery = areaQuery.Where(a => false);
            }

            var areaList = await areaQuery
                .OrderBy(a => a.NamaArea)
                .Select(a => a.NamaArea)
                .ToListAsync();

            ViewBag.AreaList = areaList;

            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "General";

            // Load departments from dynamic partner DB view matching current user's company
            List<string> deptList = new List<string>();
            if (userCompanyId.HasValue)
            {
                deptList = await _context.Departemens
                    .Where(d => d.IdPerusahaan == userCompanyId.Value && (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")))
                    .OrderBy(d => d.NamaDepartemen)
                    .Select(d => d.NamaDepartemen ?? string.Empty)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToListAsync();
            }

            // Fallback to default list if empty
            if (deptList == null || !deptList.Any())
            {
                deptList = new List<string>
                {
                    "MINING OPERATION",
                    "MAINTENANCE",
                    "PIT SERVICE AND DEVELOPMENT",
                    "HRM, EARTHWORKS & INFRAS",
                    "ENGINEERING DEPARTMENT",
                    "GENERAL AFFAIR",
                    "HSE AND TRAINING"
                };
            }

            ViewBag.DeptList = deptList;
        }

        private async Task<HashSet<string>> GetCurrentCompanyNiksAsync()
        {
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            if (!int.TryParse(companyIdStr, out var companyId) || companyId <= 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var niks = await _context.AppUsers
                .Where(u => u.IdPerusahaan == companyId && !string.IsNullOrEmpty(u.Nik))
                .Select(u => u.Nik!)
                .Distinct()
                .ToListAsync();

            return new HashSet<string>(niks.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        }
    }
}
