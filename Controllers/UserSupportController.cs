using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class UserSupportController : Controller
    {
        private readonly AppDbContext _context;

        public UserSupportController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult ResetPassword()
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var jobTitle = User.FindFirst("JobTitle")?.Value ?? "";
            var department = User.FindFirst("Department")?.Value ?? "";
            
            bool isSafetyAdmin = role == "Admin" || 
                                 jobTitle.Contains("Safety", StringComparison.OrdinalIgnoreCase) || 
                                 department.Contains("Safety", StringComparison.OrdinalIgnoreCase) ||
                                 department.Contains("HSE", StringComparison.OrdinalIgnoreCase);
            if (!isSafetyAdmin)
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            ViewData["ActiveTab"] = "ResetPassword";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string nik)
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var jobTitle = User.FindFirst("JobTitle")?.Value ?? "";
            var department = User.FindFirst("Department")?.Value ?? "";
            
            bool isSafetyAdmin = role == "Admin" || 
                                 jobTitle.Contains("Safety", StringComparison.OrdinalIgnoreCase) || 
                                 department.Contains("Safety", StringComparison.OrdinalIgnoreCase) ||
                                 department.Contains("HSE", StringComparison.OrdinalIgnoreCase);
            if (!isSafetyAdmin)
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            ViewData["ActiveTab"] = "ResetPassword";

            if (string.IsNullOrWhiteSpace(nik))
            {
                ViewData["Error"] = "NIK wajib diisi!";
                return View();
            }

            var cleanNik = nik.Trim();

            // Cek apakah NIK ada di tbl_m_karyawan atau vw_pengguna
            var userExists = await _context.Karyawans.AnyAsync(k => k.NoNik == cleanNik) || 
                             await _context.Penggunas.AnyAsync(p => p.Username == cleanNik);

            if (!userExists)
            {
                ViewData["Error"] = $"User dengan NIK {cleanNik} tidak ditemukan.";
                return View();
            }

            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == cleanNik);
            if (overridePwd != null)
            {
                overridePwd.KataSandi = "123456";
                overridePwd.DiubahPada = DateTime.Now;
                _context.PasswordOverrides.Update(overridePwd);
            }
            else
            {
                var newOverride = new PasswordOverride
                {
                    Nrp = cleanNik,
                    KataSandi = "123456",
                    DiubahPada = DateTime.Now
                };
                _context.PasswordOverrides.Add(newOverride);
            }

            await _context.SaveChangesAsync();

            ViewData["Success"] = $"Reset password untuk NIK {cleanNik} berhasil! Password dikembalikan ke 123456.";
            return View();
        }
    }
}
