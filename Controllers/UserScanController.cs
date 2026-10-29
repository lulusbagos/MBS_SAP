using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserScanController : Controller
    {
        private readonly AppDbContext _context;

        public UserScanController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? q = null,
            string? filterDept = null,
            string? filterScan = null,
            int page = 1,
            int pageSize = 20)
        {
            ViewData["HeaderTitle"] = "User Scan Permission";
            ViewData["ActiveTab"] = "UserScanAdmin";

            var allowedPageSizes = new[] { 10, 20, 50, 100 };
            if (!allowedPageSizes.Contains(pageSize)) pageSize = 20;
            if (page < 1) page = 1;

            var normalizedQuery = (q ?? string.Empty).Trim().ToLower();
            var normalizedDept = (filterDept ?? string.Empty).Trim();
            var normalizedScan = (filterScan ?? string.Empty).Trim();

            // 1. Ambil data AppUsers
            var usersQuery = _context.AppUsers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedQuery))
            {
                usersQuery = usersQuery.Where(u =>
                    (u.Nama ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                    (u.Nik ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                    (u.Departemen ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                    (u.Perusahaan ?? string.Empty).ToLower().Contains(normalizedQuery));
            }

            if (!string.IsNullOrWhiteSpace(normalizedDept) && !string.Equals(normalizedDept, "All", StringComparison.OrdinalIgnoreCase))
            {
                usersQuery = usersQuery.Where(u => (u.Departemen ?? string.Empty) == normalizedDept);
            }

            // 2. Load Permissions
            var rawPermissions = await _context.UserScanPermissions.AsNoTracking().ToListAsync();
            var allPermissions = rawPermissions.GroupBy(p => (p.Nik ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Filter scan type if specified
            var candidateUsers = await usersQuery.OrderBy(u => u.Nama).ToListAsync();

            if (!string.IsNullOrWhiteSpace(normalizedScan) && !string.Equals(normalizedScan, "All", StringComparison.OrdinalIgnoreCase))
            {
                candidateUsers = candidateUsers.Where(u =>
                {
                    allPermissions.TryGetValue((u.Nik ?? string.Empty).Trim(), out var perm);
                    if (normalizedScan == "HasSpecial")
                    {
                        return perm != null && (perm.CanScanMinePermit || perm.CanScanPermitPelanggaran || perm.CanScanPermitKeluarMasuk || perm.CanScanSimper || perm.CanScanCommissioning || perm.CanScanSpecialPermit || perm.CanScanOther);
                    }
                    if (normalizedScan == "MinePermit") return perm?.CanScanMinePermit == true;
                    if (normalizedScan == "PermitPelanggaran") return perm?.CanScanPermitPelanggaran == true;
                    if (normalizedScan == "PermitKeluarMasuk") return perm?.CanScanPermitKeluarMasuk == true;
                    if (normalizedScan == "Simper") return perm?.CanScanSimper == true;
                    if (normalizedScan == "Commissioning") return perm?.CanScanCommissioning == true;
                    if (normalizedScan == "SpecialPermit") return perm?.CanScanSpecialPermit == true;
                    if (normalizedScan == "Other") return perm?.CanScanOther == true;
                    return true;
                }).ToList();
            }

            var totalFilteredUsers = candidateUsers.Count;
            var totalPages = totalFilteredUsers == 0 ? 1 : (int)Math.Ceiling(totalFilteredUsers / (double)pageSize);
            if (page > totalPages) page = totalPages;

            var pagedUsers = candidateUsers
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var rows = pagedUsers.Select(u =>
            {
                allPermissions.TryGetValue((u.Nik ?? string.Empty).Trim(), out var p);
                return new UserScanRowViewModel
                {
                    Nik = u.Nik ?? string.Empty,
                    Nama = u.Nama ?? "-",
                    Departemen = u.Departemen ?? "-",
                    Perusahaan = u.Perusahaan ?? "-",
                    Role = u.Role ?? "Operator",
                    CanScanAcara = p?.CanScanAcara ?? true, // Default: semua orang bisa scan acara
                    CanScanMinePermit = p?.CanScanMinePermit ?? false, // Scan Detail Permit
                    CanScanPermitPelanggaran = p?.CanScanPermitPelanggaran ?? false, // Scan Pelanggaran Permit
                    CanScanPermitKeluarMasuk = p?.CanScanPermitKeluarMasuk ?? false, // Scan Keluar Masuk Permit
                    CanScanSimper = p?.CanScanSimper ?? false,
                    CanScanCommissioning = p?.CanScanCommissioning ?? false,
                    CanScanSpecialPermit = p?.CanScanSpecialPermit ?? false,
                    CanScanOther = p?.CanScanOther ?? false,
                    UpdatedAt = p?.UpdatedAt,
                    UpdatedBy = p?.UpdatedBy
                };
            }).ToList();

            // Stats
            var stats = new UserScanStatsViewModel
            {
                TotalUsers = await _context.AppUsers.CountAsync(),
                TotalMinePermit = allPermissions.Values.Count(p => p.CanScanMinePermit),
                TotalPermitPelanggaran = allPermissions.Values.Count(p => p.CanScanPermitPelanggaran),
                TotalPermitKeluarMasuk = allPermissions.Values.Count(p => p.CanScanPermitKeluarMasuk),
                TotalSimper = allPermissions.Values.Count(p => p.CanScanSimper),
                TotalCommissioning = allPermissions.Values.Count(p => p.CanScanCommissioning),
                TotalSpecialPermit = allPermissions.Values.Count(p => p.CanScanSpecialPermit),
                TotalOther = allPermissions.Values.Count(p => p.CanScanOther)
            };

            var departments = await _context.AppUsers
                .AsNoTracking()
                .Where(u => !string.IsNullOrEmpty(u.Departemen))
                .Select(u => u.Departemen!)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();

            var vm = new UserScanIndexViewModel
            {
                Rows = rows,
                Stats = stats,
                Departments = departments,
                Query = q ?? string.Empty,
                FilterDept = filterDept ?? "All",
                FilterScan = filterScan ?? "All",
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalFilteredUsers = totalFilteredUsers
            };

            return View(vm);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TogglePermission(string nik, string type, bool isChecked)
        {
            if (string.IsNullOrWhiteSpace(nik) || string.IsNullOrWhiteSpace(type))
            {
                return Json(new { success = false, message = "Parameter tidak valid." });
            }

            var cleanNik = nik.Trim();
            var currentUser = User.Identity?.Name ?? "Admin";

            try
            {
                var perm = await _context.UserScanPermissions.FirstOrDefaultAsync(p => p.Nik == cleanNik);
                if (perm == null)
                {
                    var appUser = await _context.AppUsers.FirstOrDefaultAsync(u => u.Nik == cleanNik);
                    perm = new UserScanPermission
                    {
                        Nik = cleanNik,
                        Nama = appUser?.Nama,
                        Departemen = appUser?.Departemen,
                        Perusahaan = appUser?.Perusahaan,
                        CanScanAcara = true,
                        CanScanMinePermit = false,
                        CanScanPermitPelanggaran = false,
                        CanScanPermitKeluarMasuk = false,
                        CanScanSimper = false,
                        CanScanCommissioning = false,
                        CanScanSpecialPermit = false,
                        CanScanOther = false,
                        UpdatedAt = DateTime.Now,
                        UpdatedBy = currentUser
                    };
                    _context.UserScanPermissions.Add(perm);
                }

                switch (type.ToLower())
                {
                    case "acara":
                        perm.CanScanAcara = isChecked;
                        break;
                    case "minepermit":
                    case "permitdetail":
                    case "detailpermit":
                    case "permit":
                        perm.CanScanMinePermit = isChecked;
                        break;
                    case "permitpelanggaran":
                    case "pelanggaran":
                    case "violation":
                        perm.CanScanPermitPelanggaran = isChecked;
                        break;
                    case "permitkeluarmasuk":
                    case "keluarmasuk":
                    case "gatelog":
                    case "gate":
                        perm.CanScanPermitKeluarMasuk = isChecked;
                        break;
                    case "simper":
                        perm.CanScanSimper = isChecked;
                        break;
                    case "commissioning":
                        perm.CanScanCommissioning = isChecked;
                        break;
                    case "specialpermit":
                        perm.CanScanSpecialPermit = isChecked;
                        break;
                    case "other":
                        perm.CanScanOther = isChecked;
                        break;
                    default:
                        return Json(new { success = false, message = "Jenis scan tidak dikenali." });
                }

                perm.UpdatedAt = DateTime.Now;
                perm.UpdatedBy = currentUser;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Izin {type} untuk NIK {cleanNik} berhasil diubah menjadi {(isChecked ? "AKTIF" : "NONAKTIF")}."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Terjadi kesalahan saat menyimpan izin: " + ex.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveModal(UserScanSaveForm form)
        {
            if (string.IsNullOrWhiteSpace(form.Nik))
            {
                TempData["ErrorMessage"] = "NIK wajib diisi.";
                return RedirectToAction(nameof(Index));
            }

            var cleanNik = form.Nik.Trim();
            var currentUser = User.Identity?.Name ?? "Admin";

            var perm = await _context.UserScanPermissions.FirstOrDefaultAsync(p => p.Nik == cleanNik);
            if (perm == null)
            {
                var appUser = await _context.AppUsers.FirstOrDefaultAsync(u => u.Nik == cleanNik);
                perm = new UserScanPermission
                {
                    Nik = cleanNik,
                    Nama = appUser?.Nama,
                    Departemen = appUser?.Departemen,
                    Perusahaan = appUser?.Perusahaan
                };
                _context.UserScanPermissions.Add(perm);
            }

            perm.CanScanAcara = form.CanScanAcara;
            perm.CanScanMinePermit = form.CanScanMinePermit;
            perm.CanScanPermitPelanggaran = form.CanScanPermitPelanggaran;
            perm.CanScanPermitKeluarMasuk = form.CanScanPermitKeluarMasuk;
            perm.CanScanSimper = form.CanScanSimper;
            perm.CanScanCommissioning = form.CanScanCommissioning;
            perm.CanScanSpecialPermit = form.CanScanSpecialPermit;
            perm.CanScanOther = form.CanScanOther;
            perm.UpdatedAt = DateTime.Now;
            perm.UpdatedBy = currentUser;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Pengaturan izin scan untuk NIK {cleanNik} berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAssignDepartment(string department, string scanType, bool enable)
        {
            if (string.IsNullOrWhiteSpace(department) || string.IsNullOrWhiteSpace(scanType))
            {
                TempData["ErrorMessage"] = "Departemen dan Jenis Scan wajib dipilih.";
                return RedirectToAction(nameof(Index));
            }

            var usersInDept = await _context.AppUsers
                .AsNoTracking()
                .Where(u => u.Departemen == department)
                .ToListAsync();

            if (!usersInDept.Any())
            {
                TempData["ErrorMessage"] = $"Tidak ada karyawan pada departemen {department}.";
                return RedirectToAction(nameof(Index));
            }

            var nikoList = usersInDept.Select(u => u.Nik).ToList();
            var existingPerms = await _context.UserScanPermissions
                .Where(p => nikoList.Contains(p.Nik))
                .ToDictionaryAsync(p => p.Nik);

            var currentUser = User.Identity?.Name ?? "Admin";

            foreach (var user in usersInDept)
            {
                if (!existingPerms.TryGetValue(user.Nik, out var perm))
                {
                    perm = new UserScanPermission
                    {
                        Nik = user.Nik,
                        Nama = user.Nama,
                        Departemen = user.Departemen,
                        Perusahaan = user.Perusahaan,
                        CanScanAcara = true
                    };
                    _context.UserScanPermissions.Add(perm);
                }

                switch (scanType.ToLower())
                {
                    case "acara": perm.CanScanAcara = enable; break;
                    case "minepermit": perm.CanScanMinePermit = enable; break;
                    case "permitpelanggaran":
                    case "pelanggaran": perm.CanScanPermitPelanggaran = enable; break;
                    case "permitkeluarmasuk":
                    case "keluarmasuk": perm.CanScanPermitKeluarMasuk = enable; break;
                    case "simper": perm.CanScanSimper = enable; break;
                    case "commissioning": perm.CanScanCommissioning = enable; break;
                    case "specialpermit": perm.CanScanSpecialPermit = enable; break;
                    case "other": perm.CanScanOther = enable; break;
                }

                perm.UpdatedAt = DateTime.Now;
                perm.UpdatedBy = currentUser;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Berhasil memperbarui izin {scanType} untuk {usersInDept.Count} karyawan di departemen {department}.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class UserScanIndexViewModel
    {
        public List<UserScanRowViewModel> Rows { get; set; } = new List<UserScanRowViewModel>();
        public UserScanStatsViewModel Stats { get; set; } = new UserScanStatsViewModel();
        public List<string> Departments { get; set; } = new List<string>();
        public string Query { get; set; } = string.Empty;
        public string FilterDept { get; set; } = "All";
        public string FilterScan { get; set; } = "All";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages { get; set; } = 1;
        public int TotalFilteredUsers { get; set; } = 0;
    }

    public class UserScanRowViewModel
    {
        public string Nik { get; set; } = string.Empty;
        public string Nama { get; set; } = string.Empty;
        public string Departemen { get; set; } = string.Empty;
        public string Perusahaan { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator";
        public bool CanScanAcara { get; set; } = true;
        public bool CanScanMinePermit { get; set; } = false; // Scan Detail Permit
        public bool CanScanPermitPelanggaran { get; set; } = false; // Scan Pelanggaran Permit
        public bool CanScanPermitKeluarMasuk { get; set; } = false; // Scan Keluar Masuk Permit
        public bool CanScanSimper { get; set; } = false;
        public bool CanScanCommissioning { get; set; } = false;
        public bool CanScanSpecialPermit { get; set; } = false;
        public bool CanScanOther { get; set; } = false;
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UserScanStatsViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalMinePermit { get; set; }
        public int TotalPermitPelanggaran { get; set; }
        public int TotalPermitKeluarMasuk { get; set; }
        public int TotalSimper { get; set; }
        public int TotalCommissioning { get; set; }
        public int TotalSpecialPermit { get; set; }
        public int TotalOther { get; set; }
    }

    public class UserScanSaveForm
    {
        public string Nik { get; set; } = string.Empty;
        public bool CanScanAcara { get; set; } = true;
        public bool CanScanMinePermit { get; set; }
        public bool CanScanPermitPelanggaran { get; set; }
        public bool CanScanPermitKeluarMasuk { get; set; }
        public bool CanScanSimper { get; set; }
        public bool CanScanCommissioning { get; set; }
        public bool CanScanSpecialPermit { get; set; }
        public bool CanScanOther { get; set; }
    }
}
