using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.Security.Claims;

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
        public async Task<IActionResult> Index()
        {
            ViewData["ActiveTab"] = "UserMgmt";

            var users = await _context.AppUsers
                .OrderByDescending(u => u.LastLogin)
                .ToListAsync();

            return View(users);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateRole(string nik, string role)
        {
            if (string.IsNullOrEmpty(nik) || string.IsNullOrEmpty(role))
            {
                TempData["ErrorMessage"] = "NIK dan Role wajib diisi!";
                return RedirectToAction("Index");
            }

            var user = await _context.AppUsers.FindAsync(nik);
            if (user == null)
            {
                TempData["ErrorMessage"] = $"User dengan NIK {nik} tidak ditemukan.";
                return RedirectToAction("Index");
            }

            user.Role = NormalizeRole(role, "Operator");
            _context.AppUsers.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Role {user.Nama} berhasil diubah menjadi {user.Role}. Perubahan berlaku saat user login ulang.";
            return RedirectToAction("Index");
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
    }
}
