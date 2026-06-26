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

            user.Role = role;
            _context.AppUsers.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Role {user.Nama} berhasil diubah menjadi {role}. Perubahan berlaku saat user login ulang.";
            return RedirectToAction("Index");
        }
    }
}
