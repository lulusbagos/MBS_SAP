using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _webHostEnvironment;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;

        public AccountController(AppDbContext context, Microsoft.AspNetCore.Hosting.IWebHostEnvironment webHostEnvironment, MBS_SAP.Services.ImageUploadService imageUploadService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _imageUploadService = imageUploadService;
        }

        [HttpGet]
        public async Task<IActionResult> Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string nrp, string password, bool rememberMe = false)
        {
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;

            nrp = (nrp ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(nrp))
            {
                ModelState.AddModelError("Nrp", "NRP/NIK wajib diisi!");
                return View();
            }
            if (string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("Password", "Password wajib diisi!");
                return View();
            }

            // 1. Check override in tbl_m_pengguna_sandi
            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
            bool isValid = false;
            string fullName = "";
            string role = "Operator";
            int? idPerusahaan = null;
            int? idDepartemen = null;
            int? idJabatan = null;
            // Jika ada duplikat NIK (rehire/pindah perusahaan), prioritaskan yang status_aktif = 1
            var karyawanMaster = await _context.Karyawans
                .Where(k => k.NoNik == nrp)
                .OrderByDescending(k => k.StatusAktif)
                .FirstOrDefaultAsync();

            if (overridePwd != null)
            {
                if (overridePwd.KataSandi == password)
                {
                    isValid = true;
                }
            }
            else
            {
                // 2. Check vw_pengguna
                var pengguna = await _context.Penggunas.FirstOrDefaultAsync(p => p.Username == nrp && p.IsAktif);
                if (pengguna != null)
                {
                    if (pengguna.KataSandi == password)
                    {
                        isValid = true;
                        fullName = pengguna.NamaLengkap;
                        idPerusahaan = pengguna.PerusahaanId;
                        idDepartemen = pengguna.DepartemenId;
                        idJabatan = pengguna.JabatanId;
                        role = pengguna.PeranId == 1 ? "Admin" : "Operator";
                    }
                    else if (password == "123456") // Fallback default password for active employees
                    {
                        var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == nrp && k.StatusAktif);
                        if (karyawan != null)
                        {
                            isValid = true;
                            fullName = pengguna.NamaLengkap;
                            idPerusahaan = karyawan.IdPerusahaan;
                            idDepartemen = karyawan.IdDepartemen;
                            idJabatan = karyawan.IdJabatan;
                            role = "Operator";
                        }
                    }
                }
                else
                {
                    // 3. Check active employee fallback
                    var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == nrp && k.StatusAktif);
                    if (karyawan != null && password == "123456")
                    {
                        isValid = true;
                        idPerusahaan = karyawan.IdPerusahaan;
                        idDepartemen = karyawan.IdDepartemen;
                        idJabatan = karyawan.IdJabatan;
                        role = "Operator";
                    }
                }
            }

            // If valid but details not resolved yet
            if (isValid && string.IsNullOrEmpty(fullName))
            {
                var pg = await _context.Penggunas.FirstOrDefaultAsync(p => p.Username == nrp);
                if (pg != null)
                {
                    fullName = pg.NamaLengkap;
                    idPerusahaan ??= pg.PerusahaanId;
                    idDepartemen ??= pg.DepartemenId;
                    idJabatan ??= pg.JabatanId;
                    role = pg.PeranId == 1 ? "Admin" : "Operator";
                }
                else
                {
                    var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == nrp);
                    if (karyawan != null)
                    {
                        idPerusahaan ??= karyawan.IdPerusahaan;
                        idDepartemen ??= karyawan.IdDepartemen;
                        idJabatan ??= karyawan.IdJabatan;
                        var personal = await _context.Personals.FirstOrDefaultAsync(p => p.IdPersonal == karyawan.IdPersonal);
                        if (personal != null)
                        {
                            fullName = personal.NamaLengkap;
                        }
                    }
                }
            }

            if (!isValid)
            {
                ModelState.AddModelError("Nrp", "NRP atau Password salah, atau akun dinonaktifkan!");
                return View();
            }

            // Status karyawan di ONE DB MITRA menjadi acuan utama aktivasi akun login.
            if (karyawanMaster != null)
            {
                if (!karyawanMaster.StatusAktif)
                {
                    ModelState.AddModelError("Nrp", "Akun tidak dapat digunakan karena status karyawan sudah non aktif di ONE DB MITRA.");
                    return View();
                }

                idPerusahaan ??= karyawanMaster.IdPerusahaan;
                idDepartemen ??= karyawanMaster.IdDepartemen;
                idJabatan ??= karyawanMaster.IdJabatan;
            }

            if (string.IsNullOrEmpty(fullName))
            {
                fullName = nrp;
            }

            // Resolve names and company type role
            string companyName = "PT INDEXIM COALINDO";
            string deptName = "General";
            string mappedRole = "Operator";
            if (idPerusahaan.HasValue)
            {
                var p = await _context.Perusahaans.FirstOrDefaultAsync(x => x.PerusahaanId == idPerusahaan.Value);
                if (p != null)
                {
                    companyName = p.NamaPerusahaan ?? companyName;
                    mappedRole = p.TipePerusahaanId switch
                    {
                        1 => "Owner",
                        2 => "Maincon",
                        3 => "Subcon",
                        4 => "Vendor",
                        _ => "Operator"
                    };
                }
            }
            if (idDepartemen.HasValue)
            {
                var d = await _context.Departemens.FirstOrDefaultAsync(x => x.DepartemenId == idDepartemen.Value);
                if (d != null) deptName = d.NamaDepartemen ?? deptName;
            }

            string jobTitle = "Staff/Operator";
            if (idJabatan.HasValue)
            {
                var jab = await _context.Jabatans.FirstOrDefaultAsync(x => x.JabatanId == idJabatan.Value);
                if (!string.IsNullOrWhiteSpace(jab?.NamaJabatan))
                {
                    jobTitle = jab.NamaJabatan;
                }
            }

            // Check for role override from AppUser (managed via User Management)
            var existingAppUser = await _context.AppUsers.FindAsync(nrp);
            if (existingAppUser != null && !string.IsNullOrEmpty(existingAppUser.Role))
            {
                role = existingAppUser.Role;
            }
            else
            {
                // Jika tidak ada override manual, dan bukan Admin, gunakan role dari tipe perusahaan
                if (role != "Admin")
                {
                    role = mappedRole;
                }
            }

            // Sign in claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.NameIdentifier, nrp),
                new Claim(ClaimTypes.Role, role),
                new Claim("Company", companyName),
                new Claim("Department", deptName),
                new Claim("JobTitle", jobTitle),
                new Claim("CompanyId", idPerusahaan?.ToString() ?? "0")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Update or Insert AppUser (Login History)
            var appUser = await _context.AppUsers.FindAsync(nrp);
            if (appUser == null)
            {
                appUser = new AppUser
                {
                    Nik = nrp,
                    Nama = fullName,
                    Departemen = deptName,
                    Perusahaan = companyName,
                    IdPerusahaan = idPerusahaan,
                    Role = role,
                    LastLogin = DateTime.Now
                };
                _context.AppUsers.Add(appUser);
            }
            else
            {
                appUser.Nama = fullName;
                appUser.Departemen = deptName;
                appUser.Perusahaan = companyName;
                appUser.IdPerusahaan = idPerusahaan;
                if (string.IsNullOrEmpty(appUser.Role))
                {
                    appUser.Role = role;
                }
                appUser.LastLogin = DateTime.Now;
                _context.AppUsers.Update(appUser);
            }
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Home");
        }

        [HttpGet, HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> ChangePassword()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login");
            }

            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            string currentPassword = "123456"; // default fallback

            if (!string.IsNullOrEmpty(nrp))
            {
                var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
                if (overridePwd != null && !string.IsNullOrEmpty(overridePwd.KataSandi))
                {
                    currentPassword = overridePwd.KataSandi;
                }
                else
                {
                    // coba ambil dari vw_pengguna
                    var pengguna = await _context.Penggunas.FirstOrDefaultAsync(p => p.Username == nrp && p.IsAktif);
                    if (pengguna != null && !string.IsNullOrEmpty(pengguna.KataSandi))
                    {
                        currentPassword = pengguna.KataSandi;
                    }
                }
            }

            ViewData["CurrentPassword"] = currentPassword;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, string confirmPassword)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login");
            }

            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
            {
                ViewData["Error"] = "Semua kolom wajib diisi!";
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ViewData["Error"] = "Password baru dan konfirmasi password tidak cocok!";
                return View();
            }

            var userNrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userNrp))
            {
                return RedirectToAction("Login");
            }

            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == userNrp);
            bool isOldValid = false;

            if (overridePwd != null)
            {
                if (overridePwd.KataSandi == oldPassword)
                {
                    isOldValid = true;
                }
            }
            else
            {
                var pengguna = await _context.Penggunas.FirstOrDefaultAsync(p => p.Username == userNrp && p.IsAktif);
                if (pengguna != null)
                {
                    if (pengguna.KataSandi == oldPassword)
                    {
                        isOldValid = true;
                    }
                    else if (oldPassword == "123456")
                    {
                        var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == userNrp && k.StatusAktif);
                        if (karyawan != null)
                        {
                            isOldValid = true;
                        }
                    }
                }
                else
                {
                    var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == userNrp && k.StatusAktif);
                    if (karyawan != null && oldPassword == "123456")
                    {
                        isOldValid = true;
                    }
                }
            }

            if (!isOldValid)
            {
                ViewData["Error"] = "Password lama salah!";
                return View();
            }

            if (overridePwd != null)
            {
                overridePwd.KataSandi = newPassword;
                overridePwd.DiubahPada = DateTime.Now;
                _context.PasswordOverrides.Update(overridePwd);
            }
            else
            {
                var newOverride = new PasswordOverride
                {
                    Nrp = userNrp,
                    KataSandi = newPassword,
                    DiubahPada = DateTime.Now
                };
                _context.PasswordOverrides.Add(newOverride);
            }

            await _context.SaveChangesAsync();
            ViewData["Success"] = "Password berhasil diubah!";
            return View();
        }
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (nrp == null) return RedirectToAction("Login");

            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
            
            var pengguna = await _context.Penggunas.FirstOrDefaultAsync(p => p.Username == nrp);
            var karyawan = await _context.Karyawans.FirstOrDefaultAsync(k => k.NoNik == nrp);
            var personal = karyawan != null ? await _context.Personals.FirstOrDefaultAsync(p => p.IdPersonal == karyawan.IdPersonal) : null;
            
            int? deptId = pengguna?.DepartemenId ?? karyawan?.IdDepartemen;
            int? compId = pengguna?.PerusahaanId ?? karyawan?.IdPerusahaan;
            int? jabId = pengguna?.JabatanId ?? karyawan?.IdJabatan;

            var dept = deptId.HasValue ? await _context.Departemens.FirstOrDefaultAsync(d => d.DepartemenId == deptId) : null;
            var comp = compId.HasValue ? await _context.Perusahaans.FirstOrDefaultAsync(c => c.PerusahaanId == compId) : null;
            var jab = jabId.HasValue ? await _context.Jabatans.FirstOrDefaultAsync(j => j.JabatanId == jabId) : null;

            // Hitung pencapaian safety bulan ini
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            int myHazards = await _context.HazardReports.CountAsync(h => !h.IsDeleted && h.Nik == nrp && h.CreatedAt >= startOfMonth);
            int myInspections = await _context.Inspections.CountAsync(i => !i.IsDeleted && i.Nik == nrp && i.CreatedAt >= startOfMonth);
            int mySafetyTalks = await _context.SafetyTalks.CountAsync(s => !s.IsDeleted && s.Nik == nrp && s.CreatedAt >= startOfMonth);
            int myP5ms = await _context.P5ms.CountAsync(p => !p.IsDeleted && p.Nik == nrp && p.CreatedAt >= startOfMonth);

            int totalSubmissions = myHazards + myInspections + mySafetyTalks + myP5ms;
            double complianceRate = Math.Min((totalSubmissions / 4.0) * 100.0, 100.0);

            string badgeName = "Safety Novice";
            string badgeIcon = "bi-shield-slash";
            string badgeColor = "#9ca3af";

            if (totalSubmissions >= 5)
            {
                badgeName = "Safety Hero (Gold)";
                badgeIcon = "bi-shield-fill-check";
                badgeColor = "#fbbf24";
            }
            else if (totalSubmissions >= 3)
            {
                badgeName = "Safety Champion (Silver)";
                badgeIcon = "bi-shield-fill-star";
                badgeColor = "#cbd5e1";
            }
            else if (totalSubmissions >= 1)
            {
                badgeName = "Safety Aware (Bronze)";
                badgeIcon = "bi-shield-fill";
                badgeColor = "#b45309";
            }

            ViewData["ProfilePic"] = overridePwd?.ProfilePicture;
            ViewData["HasAgreed"] = overridePwd?.HasAgreedToTerms ?? false;
            ViewData["Department"] = dept?.NamaDepartemen ?? User.FindFirst("Department")?.Value ?? "-";
            ViewData["Company"] = comp?.NamaPerusahaan ?? User.FindFirst("Company")?.Value ?? "-";
            ViewData["JobTitle"] = jab?.NamaJabatan ?? "Staff/Operator";
            ViewData["FullName"] = User.Identity?.Name;
            ViewData["Nrp"] = nrp;
            ViewData["Email"] = pengguna?.Email ?? personal?.EmailPribadi ?? "-";
            ViewData["Phone"] = personal?.Hp1 ?? "-";

            // Safety stats
            ViewData["MyHazards"] = myHazards;
            ViewData["MyInspections"] = myInspections;
            ViewData["MySafetyTalks"] = mySafetyTalks;
            ViewData["MyP5ms"] = myP5ms;
            ViewData["TotalSubmissions"] = totalSubmissions;
            ViewData["ComplianceRate"] = Math.Round(complianceRate, 0);
            ViewData["BadgeName"] = badgeName;
            ViewData["BadgeIcon"] = badgeIcon;
            ViewData["BadgeColor"] = badgeColor;

            ViewData["ActiveTab"] = "Profile";
            return View();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UpdateProfilePicture(IFormFile photo)
        {
            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (nrp == null || photo == null || photo.Length == 0) return RedirectToAction("Profile");

            var photoPath = await _imageUploadService.UploadAndCompressImageAsync(photo, "profiles", nrp);
            
            if (photoPath != null)
            {
                var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
                if (overridePwd == null)
                {
                    overridePwd = new PasswordOverride { Nrp = nrp, KataSandi = "123456", ProfilePicture = photoPath };
                    _context.PasswordOverrides.Add(overridePwd);
                }
                else
                {
                    overridePwd.ProfilePicture = photoPath;
                }
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "Foto profil berhasil diperbarui!";
            return RedirectToAction("Profile");
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> UserAgreement()
        {
            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
            ViewData["HasAgreed"] = overridePwd?.HasAgreedToTerms ?? false;
            return View();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AgreeToTerms()
        {
            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (nrp == null) return RedirectToAction("Login");

            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
            if (overridePwd == null)
            {
                overridePwd = new PasswordOverride { Nrp = nrp, KataSandi = "123456", HasAgreedToTerms = true };
                _context.PasswordOverrides.Add(overridePwd);
            }
            else
            {
                overridePwd.HasAgreedToTerms = true;
            }
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Home");
        }
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ResetPassword()
        {
            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (nrp == null) return RedirectToAction("Login");

            const string defaultPassword = "123456";

            var overridePwd = await _context.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
            if (overridePwd != null)
            {
                overridePwd.KataSandi = defaultPassword;
                overridePwd.DiubahPada = DateTime.Now;
                _context.PasswordOverrides.Update(overridePwd);
            }
            else
            {
                // Buat entri baru dengan password default
                var newOverride = new PasswordOverride
                {
                    Nrp = nrp,
                    KataSandi = defaultPassword,
                    DiubahPada = DateTime.Now
                };
                _context.PasswordOverrides.Add(newOverride);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Password berhasil direset ke default: 123456. Silakan ganti password Anda setelah login berikutnya.";
            return RedirectToAction("Profile");
        }
    }
}
