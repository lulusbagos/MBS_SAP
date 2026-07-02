using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class QrCodeController : Controller
    {
        public IActionResult Index()
        {
            if (User.IsInRole("Admin"))
            {
                return Forbid();
            }

            ViewData["HeaderTitle"] = "Scan QR Absensi";
            ViewData["ActiveTab"] = "QrCode";

            var companyName = User.FindFirst("CompanyName")?.Value
                ?? User.FindFirst("Company")?.Value
                ?? "Perusahaan";

            var profile = new QrProfileViewModel
            {
                Nama = User.Identity?.Name ?? "User",
                Nik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("NIK")?.Value
                    ?? "-",
                Jabatan = User.FindFirst("JobTitle")?.Value
                    ?? User.FindFirst(ClaimTypes.Role)?.Value
                    ?? "Staff/Operator",
                Perusahaan = companyName
            };

            return View(profile);
        }
    }

    public class QrProfileViewModel
    {
        public string Nama { get; set; } = string.Empty;
        public string Nik { get; set; } = string.Empty;
        public string Jabatan { get; set; } = string.Empty;
        public string Perusahaan { get; set; } = string.Empty;
    }
}
