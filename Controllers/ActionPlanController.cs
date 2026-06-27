using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class ActionPlanController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ExcelService _excelService;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;

        public ActionPlanController(AppDbContext context, IWebHostEnvironment webHostEnvironment, ExcelService excelService, MBS_SAP.Services.ImageUploadService imageUploadService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _excelService = excelService;
            _imageUploadService = imageUploadService;
        }

        // GET: ActionPlan
        public async Task<IActionResult> Index()
        {
            ViewData["HeaderTitle"] = "Action Plan Temuan";
            ViewData["ActiveTab"] = "ActionPlan";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;
            var isAdmin = User.IsInRole("Admin");

            var query = _context.ActionPlans.Where(r => !r.IsDeleted);

            if (companyId.HasValue)
            {
                // Tampilkan semua record perusahaan yang sama ATAU data lama (PerusahaanId null = sebelum ada tracking company)
                query = query.Where(r =>
                    r.PerusahaanId == companyId.Value ||
                    r.PerusahaanId == null
                );
            }

            var reports = await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        // POST: ActionPlan/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(
            int id,
            string status,
            string? pic,
            string? nikPic,
            string? departemenPic,
            string? rencanaPerbaikan,
            DateTime? tanggalRencanaPerbaikan,
            string? perbaikan,
            DateTime? tanggalPerbaikan,
            string? overdue,
            string? alasanOverdue,
            IFormFile? fotoPerbaikan)
        {
            var plan = await _context.ActionPlans.FindAsync(id);
            if (plan == null)
            {
                TempData["ErrorMessage"] = "Rencana perbaikan tidak ditemukan!";
                return RedirectToAction(nameof(Index));
            }

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;

            var canUpdate = User.IsInRole("Admin")
                            || (!string.IsNullOrWhiteSpace(userNik) && plan.Nik == userNik)
                            || (!string.IsNullOrWhiteSpace(userNik) && !string.IsNullOrWhiteSpace(plan.NikPja) && plan.NikPja == userNik)
                            || (string.IsNullOrWhiteSpace(plan.NikPja) && plan.PerusahaanId.HasValue && userCompanyId.HasValue && plan.PerusahaanId == userCompanyId);

            if (!canUpdate)
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk memperbarui action plan ini.";
                return RedirectToAction(nameof(Index));
            }

            plan.Status = status;
            plan.Pic = pic;
            plan.NikPic = nikPic;
            plan.DepartemenPic = departemenPic;
            plan.RencanaPerbaikan = rencanaPerbaikan;
            plan.TanggalRencanaPerbaikan = tanggalRencanaPerbaikan;
            plan.Perbaikan = perbaikan;
            plan.TanggalPerbaikan = tanggalPerbaikan;
            plan.Overdue = overdue;
            plan.AlasanOverdue = alasanOverdue;

            // Handle Photo Upload for Perbaikan
            if (fotoPerbaikan != null && fotoPerbaikan.Length > 0)
            {
                try
                {
                    plan.FotoPerbaikan = await _imageUploadService.UploadAndCompressImageAsync(fotoPerbaikan, "actions");
                }
                catch (Exception)
                {
                    // Fail silently
                }
            }

            _context.ActionPlans.Update(plan);
            await _context.SaveChangesAsync();

            // Sync back to HazardReport if it came from hazard
            if (plan.ItemSap != null && plan.ItemSap.StartsWith("hazard:"))
            {
                if (int.TryParse(plan.ItemSap.Substring("hazard:".Length), out int hazardId))
                {
                    var hazard = await _context.HazardReports.FindAsync(hazardId);
                    if (hazard != null)
                    {
                        hazard.StatusTemuan = status;
                        hazard.Perbaikan = perbaikan;
                        _context.HazardReports.Update(hazard);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // Update Excel sheet ACTION PLAN
            try
            {
                _excelService.UpdateActionPlan(plan);
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "Data disimpan di database, tetapi gagal memperbarui Excel: " + ex.Message;
            }

            TempData["SuccessMessage"] = "Action Plan berhasil diperbarui!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int id)
        {
            var plan = await _context.ActionPlans.FindAsync(id);
            if (plan == null || plan.IsDeleted) return NotFound();
            return Json(plan);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _context.ActionPlans.FindAsync(id);
            if (plan == null || plan.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            if (plan.Nik != userNik && !User.IsInRole("Admin"))
            {
                return Unauthorized();
            }

            plan.IsDeleted = true;
            _context.ActionPlans.Update(plan);

            // Sync deletion to HazardReport if it came from hazard
            if (plan.ItemSap != null && plan.ItemSap.StartsWith("hazard:"))
            {
                if (int.TryParse(plan.ItemSap.Substring("hazard:".Length), out int hazardId))
                {
                    var hazard = await _context.HazardReports.FindAsync(hazardId);
                    if (hazard != null)
                    {
                        hazard.IsDeleted = true;
                        _context.HazardReports.Update(hazard);
                    }
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Action Plan berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }
    }
}
