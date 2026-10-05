using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Pencapaian Saya";
            ViewData["ActiveTab"] = "Dashboard";

            var nrp = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            
            var hazardCount = await _context.HazardReports.Where(h => h.Nik == nrp && !h.IsDeleted).Select(h => new { h.Tanggal, h.Waktu, h.Lokasi }).Distinct().CountAsync();
            var p5mCount = await _context.P5ms.Where(p => p.Nik == nrp && !p.IsDeleted).Select(p => new { p.Tanggal, p.Waktu }).Distinct().CountAsync();
            var inspectionCount = await _context.Inspections.Where(i => i.Nik == nrp && !i.IsDeleted).Select(i => new { i.Tanggal, i.Waktu }).Distinct().CountAsync();
            var actionPlanCount = await _context.ActionPlans.CountAsync(a => a.NikPic == nrp && a.Status == "Selesai" && !a.IsDeleted);

            ViewBag.HazardCount = hazardCount;
            ViewBag.P5mCount = p5mCount;
            ViewBag.InspectionCount = inspectionCount;
            ViewBag.ActionPlanCount = actionPlanCount;

            return View();
        }
    }
}
