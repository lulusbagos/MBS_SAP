using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;

namespace MBS_SAP.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserManagementController : Controller
    {
        private readonly AppDbContext _context;

        public UserManagementController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? q = null, string? filterRole = null, int page = 1, int pageSize = 20)
        {
            ViewData["ActiveTab"] = "UserMgmt";

            var allowedPageSizes = new[] { 10, 20, 50, 100 };
            if (!allowedPageSizes.Contains(pageSize))
            {
                pageSize = 20;
            }

            if (page < 1)
            {
                page = 1;
            }

            var normalizedQuery = (q ?? string.Empty).Trim();
            var normalizedRole = (filterRole ?? string.Empty).Trim();

            var query = _context.AppUsers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedQuery))
            {
                var keyword = normalizedQuery.ToLower();
                query = query.Where(u =>
                    (u.Nama ?? string.Empty).ToLower().Contains(keyword) ||
                    (u.Nik ?? string.Empty).ToLower().Contains(keyword) ||
                    (u.Departemen ?? string.Empty).ToLower().Contains(keyword) ||
                    (u.Perusahaan ?? string.Empty).ToLower().Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(normalizedRole) && !string.Equals(normalizedRole, "All", StringComparison.OrdinalIgnoreCase))
            {
                var roleKey = normalizedRole.ToLower();
                query = query.Where(u => (u.Role ?? "Operator").ToLower() == roleKey);
            }

            var totalAllUsers = await _context.AppUsers.AsNoTracking().CountAsync();
            var totalFilteredUsers = await query.CountAsync();

            var totalPages = totalFilteredUsers == 0 ? 1 : (int)Math.Ceiling(totalFilteredUsers / (double)pageSize);
            if (page > totalPages)
            {
                page = totalPages;
            }

            var users = await query
                .OrderByDescending(u => u.LastLogin)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var vm = new UserManagementIndexViewModel
            {
                Users = users,
                Query = normalizedQuery,
                FilterRole = string.IsNullOrWhiteSpace(normalizedRole) ? "All" : normalizedRole,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalFilteredUsers = totalFilteredUsers,
                TotalAllUsers = totalAllUsers
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRole(string nik, string role, string? q = null, string? filterRole = null, int page = 1, int pageSize = 20)
        {
            if (string.IsNullOrEmpty(nik) || string.IsNullOrEmpty(role))
            {
                TempData["ErrorMessage"] = "NIK dan Role wajib diisi!";
                return RedirectToAction("Index", new { q, filterRole, page, pageSize });
            }

            var user = await _context.AppUsers.FindAsync(nik);
            if (user == null)
            {
                TempData["ErrorMessage"] = $"User dengan NIK {nik} tidak ditemukan.";
                return RedirectToAction("Index", new { q, filterRole, page, pageSize });
            }

            user.Role = NormalizeRole(role, "Operator");
            _context.AppUsers.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Role {user.Nama} berhasil diubah menjadi {user.Role}. Perubahan berlaku saat user login ulang.";
            return RedirectToAction("Index", new { q, filterRole, page, pageSize });
        }

        private static string NormalizeRole(string? role, string fallback)
        {
            var value = (role ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (string.Equals(value, "Admin", StringComparison.OrdinalIgnoreCase)) return "Admin";
            if (string.Equals(value, "Owner", StringComparison.OrdinalIgnoreCase)) return "Owner";
            if (string.Equals(value, "Maincon", StringComparison.OrdinalIgnoreCase)) return "Maincon";
            if (string.Equals(value, "Subcon", StringComparison.OrdinalIgnoreCase)) return "Subcon";
            if (string.Equals(value, "Vendor", StringComparison.OrdinalIgnoreCase)) return "Vendor";
            if (string.Equals(value, "Operator", StringComparison.OrdinalIgnoreCase)) return "Operator";

            return fallback;
        }

        public class UserManagementIndexViewModel
        {
            public List<AppUser> Users { get; set; } = new();
            public string Query { get; set; } = string.Empty;
            public string FilterRole { get; set; } = "All";
            public int Page { get; set; }
            public int PageSize { get; set; }
            public int TotalPages { get; set; }
            public int TotalFilteredUsers { get; set; }
            public int TotalAllUsers { get; set; }
        }
    }
}
