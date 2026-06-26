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
            
            // Individual Achievements
            var hazardCount = await _context.HazardReports.CountAsync(h => h.Nik == nrp);
            var p5mCount = await _context.P5ms.CountAsync(p => p.Nik == nrp);
            var inspectionCount = await _context.Inspections.CountAsync(i => i.Nik == nrp);
            var actionPlanCount = await _context.ActionPlans.CountAsync(a => a.NikPic == nrp && a.Status == "Selesai");

            ViewBag.HazardCount = hazardCount;
            ViewBag.P5mCount = p5mCount;
            ViewBag.InspectionCount = inspectionCount;
            ViewBag.ActionPlanCount = actionPlanCount;

            return View();
        }
    }
}
