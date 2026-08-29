using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class ActionPlanController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public ActionPlanController(AppDbContext context, IWebHostEnvironment webHostEnvironment, MBS_SAP.Services.ImageUploadService imageUploadService, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        // GET: ActionPlan
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, string? filter, string? dept)
        {
            ViewData["HeaderTitle"] = "Action Plan Temuan";
            ViewData["ActiveTab"] = "ActionPlan";

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            var userDept = User.FindFirst("Department")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;
            var isAdmin = User.IsInRole("Admin");

            var start = startDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate ?? DateTime.Now.Date;
            var endOfDay = end.AddDays(1).AddTicks(-1);

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            var query = _context.ActionPlans.Where(r => !r.IsDeleted && r.Tanggal >= start && r.Tanggal <= endOfDay);

            if (!string.IsNullOrEmpty(filter))
            {
                if (filter == "mine")
                {
                    query = query.Where(r => 
                        r.Nik == userNik || r.NikPja == userNik || r.NikPic == userNik);
                }
                else if (filter == "dept" && !string.IsNullOrEmpty(dept))
                {
                    query = query.Where(r => 
                        r.Departemen == dept || r.DepartemenPja == dept || r.DepartemenPic == dept);
                }
            }

            // Filter berdasarkan hierarki perusahaan (berlaku untuk Admin maupun non-Admin, kecuali jika ditugaskan langsung ke user atau departemen user)
            if (companyId.HasValue)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(companyId.Value);
                query = query.Where(r =>
                    (r.PerusahaanId.HasValue && allowedIds.Contains(r.PerusahaanId.Value)) ||
                    r.Nik == userNik ||
                    r.NikPja == userNik ||
                    r.NikPic == userNik ||
                    (userDept != null && (r.Departemen == userDept || r.DepartemenPja == userDept || r.DepartemenPic == userDept))
                );
            }

            // Non-Admin dapat melihat semua hazard di perusahaan mereka (sesuai hierarki di atas)
            // agar bisa melakukan "Take Up" pada temuan dari user lain.
            var reports = await query
                .OrderByDescending(r => r.Status == "Open")
                .ThenByDescending(r => r.Tanggal)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            // Populate TingkatResiko
            var hazardIds = reports.Where(r => r.ItemSap != null && r.ItemSap.StartsWith("hazard:"))
                .Select(r => { int.TryParse(r.ItemSap!.Substring(7), out int id); return id; })
                .Where(id => id > 0).ToList();
            var obsIds = reports.Where(r => r.ItemSap != null && r.ItemSap.StartsWith("observation:"))
                .Select(r => { int.TryParse(r.ItemSap!.Substring(12), out int id); return id; })
                .Where(id => id > 0).ToList();

            var hazards = await _context.HazardReports.Where(h => hazardIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.TingkatResiko);
            var observations = await _context.Observations.Where(o => obsIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.TingkatResiko);

            foreach (var r in reports)
            {
                if (r.ItemSap != null)
                {
                    if (r.ItemSap.StartsWith("hazard:") && int.TryParse(r.ItemSap.Substring(7), out int hId) && hazards.ContainsKey(hId))
                    {
                        r.TingkatResiko = hazards[hId];
                    }
                    else if (r.ItemSap.StartsWith("observation:") && int.TryParse(r.ItemSap.Substring(12), out int oId) && observations.ContainsKey(oId))
                    {
                        r.TingkatResiko = observations[oId];
                    }
                }
            }

            var allNiks = reports.Select(r => r.Nik)
                .Concat(reports.Where(r => !string.IsNullOrEmpty(r.NikPja)).Select(r => r.NikPja!))
                .Concat(reports.Where(r => !string.IsNullOrEmpty(r.NikPic)).Select(r => r.NikPic!))
                .Distinct()
                .ToList();

            var nikCompanyList = await (from k in _context.Karyawans
                                        join p in _context.Perusahaans on k.IdPerusahaan equals p.PerusahaanId
                                        where allNiks.Contains(k.NoNik)
                                        select new { k.NoNik, k.StatusAktif, p.NamaPerusahaan })
                                        .ToListAsync();

            var nikCompanyMap = nikCompanyList
                .OrderByDescending(x => x.StatusAktif)
                .GroupBy(x => x.NoNik)
                .ToDictionary(g => g.Key, g => g.First().NamaPerusahaan);
            ViewBag.NikCompanyMap = nikCompanyMap;

            var perusahaanIds = reports.Where(r => r.PerusahaanId.HasValue).Select(r => r.PerusahaanId!.Value).Distinct().ToList();
            var perusahaans = await _context.Perusahaans.Where(p => perusahaanIds.Contains(p.PerusahaanId)).ToListAsync();
            ViewBag.Perusahaans = perusahaans;
            
            ViewBag.Departemens = reports.Where(r => !string.IsNullOrEmpty(r.Departemen))
                                         .Select(r => r.Departemen)
                                         .Distinct()
                                         .OrderBy(d => d)
                                         .ToList();

            ViewBag.CountOutstanding = reports.Count(r => string.Equals(r.Status, "Open", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(r.RencanaPerbaikan));
            ViewBag.CountProgress = reports.Count(r => string.Equals(r.Status, "Open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(r.RencanaPerbaikan));
            ViewBag.CountClosed = reports.Count(r => string.Equals(r.Status, "Closed", StringComparison.OrdinalIgnoreCase));

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
                            || (!string.IsNullOrWhiteSpace(userNik) && !string.IsNullOrWhiteSpace(plan.NikPic) && plan.NikPic == userNik)
                            || (plan.PerusahaanId.HasValue && userCompanyId.HasValue && plan.PerusahaanId == userCompanyId); // Allow anyone in the same company to Take Up

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

        [HttpGet]
        public async Task<IActionResult> DownloadExcel(DateTime? startDate, DateTime? endDate)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var compIdStr = User.FindFirst("CompanyId")?.Value;
            int? companyId = int.TryParse(compIdStr, out int cid) && cid > 0 ? cid : (int?)null;
            var isAdmin = User.IsInRole("Admin");

            var start = startDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var end = endDate ?? DateTime.Now.Date;
            var endOfDay = end.AddDays(1).AddTicks(-1);

            var query = _context.ActionPlans.Where(r => !r.IsDeleted && r.Tanggal >= start && r.Tanggal <= endOfDay);

            var userDept = User.FindFirst("Department")?.Value;

            // Filter berdasarkan hierarki perusahaan (sama seperti Index)
            if (companyId.HasValue)
            {
                var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(companyId.Value);
                query = query.Where(r =>
                    (r.PerusahaanId.HasValue && allowedIds.Contains(r.PerusahaanId.Value)) ||
                    r.Nik == userNik ||
                    r.NikPja == userNik ||
                    r.NikPic == userNik ||
                    (userDept != null && (r.Departemen == userDept || r.DepartemenPja == userDept || r.DepartemenPic == userDept))
                );
            }

            if (!isAdmin && !string.IsNullOrEmpty(userNik))
            {
                query = query.Where(r =>
                    r.Nik == userNik || r.NikPja == userNik || r.NikPic == userNik
                );
            }

            // Urutkan status "Open" terlebih dahulu, kemudian CreatedAt terbaru
            var reports = await query
                .OrderBy(r => r.Status == "Closed" ? 1 : 0) // Open (status != Closed) first
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            // Ambil data nama perusahaan untuk mapping PerusahaanId -> NamaPerusahaan
            var companies = await _context.Perusahaans
                .AsNoTracking()
                .ToDictionaryAsync(p => p.PerusahaanId, p => p.NamaPerusahaan ?? "-");

            var allNiks = reports.Select(r => r.Nik)
                .Concat(reports.Where(r => !string.IsNullOrEmpty(r.NikPja)).Select(r => r.NikPja!))
                .Concat(reports.Where(r => !string.IsNullOrEmpty(r.NikPic)).Select(r => r.NikPic!))
                .Distinct()
                .ToList();

            var nikCompanyList = await (from k in _context.Karyawans
                                        join p in _context.Perusahaans on k.IdPerusahaan equals p.PerusahaanId
                                        where allNiks.Contains(k.NoNik)
                                        select new { k.NoNik, k.StatusAktif, p.NamaPerusahaan })
                                        .ToListAsync();

            var nikCompanyMap = nikCompanyList
                .OrderByDescending(x => x.StatusAktif)
                .GroupBy(x => x.NoNik)
                .ToDictionary(g => g.Key, g => g.First().NamaPerusahaan);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Action Plan Temuan");

            // Header Judul Laporan
            worksheet.Cell(1, 1).Value = "LAPORAN ACTION PLAN TEMUAN SAFETY (SAP)";
            worksheet.Cell(1, 1).Style.Font.FontSize = 16;
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#059669");

            var userCompanyName = companyId.HasValue && companies.TryGetValue(companyId.Value, out var cName) ? cName : "Semua Perusahaan";
            worksheet.Cell(2, 1).Value = $"Perusahaan: {userCompanyName} (Termasuk Anak Perusahaan)";
            worksheet.Cell(2, 1).Style.Font.FontSize = 11;
            worksheet.Cell(2, 1).Style.Font.Italic = true;

            worksheet.Cell(3, 1).Value = $"Tanggal Ekspor: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            worksheet.Cell(3, 1).Style.Font.FontSize = 10;
            worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;

            // Header Tabel
            string[] headers = new string[]
            {
                "No", "Perusahaan", "Tipe SAP", "Pelapor", "Tanggal Temuan", "Waktu", 
                "Departemen", "Area / Lokasi", "Kategori Temuan", "Detail Temuan", 
                "Status", "PJA (NIK - Nama)", "PIC (NIK - Nama)", "Rencana Perbaikan", 
                "Target Selesai", "Realisasi Perbaikan", "Tanggal Realisasi", "Overdue Status", "Alasan Overdue"
            };

            int startRow = 5;
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(startRow, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#059669"); // Emerald Green
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            int currentRow = startRow + 1;
            int no = 1;

            foreach (var item in reports)
            {
                var compName = item.PerusahaanId.HasValue && companies.TryGetValue(item.PerusahaanId.Value, out var n) ? n : "-";
                
                var displayType = item.ItemSap;
                if (!string.IsNullOrEmpty(displayType) && displayType.Contains(":"))
                {
                    displayType = displayType.Split(':')[0];
                }
                if (string.IsNullOrEmpty(displayType)) displayType = "Hazard";
                displayType = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayType.ToLower());

                worksheet.Cell(currentRow, 1).Value = no++;
                worksheet.Cell(currentRow, 2).Value = compName;
                worksheet.Cell(currentRow, 3).Value = displayType;
                worksheet.Cell(currentRow, 4).Value = $"{item.Nik} - {item.Nama}";
                
                var dateCell = worksheet.Cell(currentRow, 5);
                dateCell.Value = item.Tanggal;
                dateCell.Style.DateFormat.Format = "yyyy-MM-dd";
                
                var timeCell = worksheet.Cell(currentRow, 6);
                timeCell.Value = item.Waktu.ToString(@"hh\:mm");
                timeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                worksheet.Cell(currentRow, 7).Value = item.Departemen ?? "-";
                worksheet.Cell(currentRow, 8).Value = $"{item.Area ?? "-"} / {item.Lokasi ?? "-"} ({item.DetilLokasi ?? "-"})";
                worksheet.Cell(currentRow, 9).Value = item.KategoriTemuan ?? "-";
                worksheet.Cell(currentRow, 10).Value = item.DetilTemuan ?? "-";

                // Status with coloring
                var statusCell = worksheet.Cell(currentRow, 11);
                var statusVal = item.Status ?? "Open";
                statusCell.Value = statusVal;
                statusCell.Style.Font.Bold = true;
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                if (statusVal.Equals("Closed", StringComparison.OrdinalIgnoreCase))
                {
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D1FAE5"); // Light green
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#065F46"); // Dark green
                }
                else
                {
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEE2E2"); // Light red
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#991B1B"); // Dark red
                }

                var pjaComp = !string.IsNullOrEmpty(item.NikPja) && nikCompanyMap.TryGetValue(item.NikPja, out var pjc) ? pjc : null;
                var picComp = !string.IsNullOrEmpty(item.NikPic) && nikCompanyMap.TryGetValue(item.NikPic, out var picc) ? picc : null;

                worksheet.Cell(currentRow, 12).Value = string.IsNullOrEmpty(item.NikPja) ? "-" : $"{item.NikPja} - {item.Pja} ({item.DepartemenPja ?? "-"})" + (pjaComp != null ? $" - {pjaComp}" : "");
                worksheet.Cell(currentRow, 13).Value = string.IsNullOrEmpty(item.NikPic) ? "-" : $"{item.NikPic} - {item.Pic} ({item.DepartemenPic ?? "-"})" + (picComp != null ? $" - {picComp}" : "");
                worksheet.Cell(currentRow, 14).Value = item.RencanaPerbaikan ?? "-";

                var targetDateCell = worksheet.Cell(currentRow, 15);
                if (item.TanggalRencanaPerbaikan.HasValue)
                {
                    targetDateCell.Value = item.TanggalRencanaPerbaikan.Value;
                    targetDateCell.Style.DateFormat.Format = "yyyy-MM-dd";
                }
                else
                {
                    targetDateCell.Value = "-";
                }

                worksheet.Cell(currentRow, 16).Value = item.Perbaikan ?? "-";

                var realDateCell = worksheet.Cell(currentRow, 17);
                if (item.TanggalPerbaikan.HasValue)
                {
                    realDateCell.Value = item.TanggalPerbaikan.Value;
                    realDateCell.Style.DateFormat.Format = "yyyy-MM-dd";
                }
                else
                {
                    realDateCell.Value = "-";
                }

                worksheet.Cell(currentRow, 18).Value = item.Overdue ?? "-";
                worksheet.Cell(currentRow, 19).Value = item.AlasanOverdue ?? "-";

                // Zebra striping
                if (no % 2 == 0)
                {
                    for (int c = 1; c <= headers.Length; c++)
                    {
                        if (c != 11) // Skip status coloring
                        {
                            worksheet.Cell(currentRow, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
                        }
                    }
                }

                // Add cell borders
                for (int c = 1; c <= headers.Length; c++)
                {
                    worksheet.Cell(currentRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Cell(currentRow, c).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E5E7EB");
                }

                currentRow++;
            }

            // Formatting columns layout
            worksheet.Columns().AdjustToContents();

            // Set fixed widths for text-heavy columns to avoid super-wide columns
            worksheet.Column(4).Width = 25; // Pelapor
            worksheet.Column(8).Width = 35; // Area / Lokasi
            worksheet.Column(9).Width = 25; // Kategori Temuan
            worksheet.Column(10).Width = 40; // Detail Temuan
            worksheet.Column(10).Style.Alignment.WrapText = true;
            worksheet.Column(12).Width = 25; // PJA
            worksheet.Column(13).Width = 25; // PIC
            worksheet.Column(14).Width = 40; // Rencana Perbaikan
            worksheet.Column(14).Style.Alignment.WrapText = true;
            worksheet.Column(16).Width = 40; // Realisasi Perbaikan
            worksheet.Column(16).Style.Alignment.WrapText = true;
            worksheet.Column(19).Width = 30; // Alasan Overdue
            worksheet.Column(19).Style.Alignment.WrapText = true;

            // Set alignment vertical
            for (int r = startRow; r < currentRow; r++)
            {
                worksheet.Row(r).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var companySuffix = companyId.HasValue ? $"_Comp_{companyId}" : "";
            var fileName = $"ActionPlan_Report{companySuffix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
