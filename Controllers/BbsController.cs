using System;
using ClosedXML.Excel;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class BbsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BbsController> _logger;
        private readonly ImageUploadService _imageUploadService;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public BbsController(
            AppDbContext context,
            ILogger<BbsController> logger,
            ImageUploadService imageUploadService,
            CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _logger = logger;
            _imageUploadService = imageUploadService;
            _companyHierarchyService = companyHierarchyService;
        }

        private bool IsAdminUser()
        {
            return User.IsInRole("Admin") || User.IsInRole("Administrator") ||
                   string.Equals(User.FindFirst(ClaimTypes.Role)?.Value?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);
        }

        // ==========================================
        // 1. INDEX: FORM INPUT DATA OBSERVASI BBS (DIRECT INPUT)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(string? type, int? editId)
        {
            ViewData["HeaderTitle"] = "Observasi Perilaku (BBS)";
            ViewData["ActiveTab"] = "BBS";

            await PopulateFormDropdownsAsync();

            ViewBag.Categories = await _context.BbsCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ToListAsync();

            ViewBag.IsAdmin = IsAdminUser();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            ViewBag.CurrentUserNik = userNik ?? "";

            // Ambil riwayat observasi yang diisi oleh pengguna sendiri (bukan orang lain)
            var myHistory = await _context.BbsObservations
                .Where(o => !o.IsDeleted && o.ObserverNik == userNik)
                .OrderByDescending(o => o.Tanggal)
                .ThenByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.MyHistory = myHistory;
            ViewBag.MyTotalCount = myHistory.Count;
            ViewBag.MySafeCount = myHistory.Count(o => o.Klasifikasi == "Aman");
            ViewBag.MyAtRiskCount = myHistory.Count(o => o.Klasifikasi != "Aman");
            ViewBag.MyCoachingCount = myHistory.Count(o => !string.IsNullOrEmpty(o.CatatanCoaching));
            ViewBag.MySafeRate = myHistory.Count > 0 ? Math.Round((double)ViewBag.MySafeCount / myHistory.Count * 100.0, 1) : 100.0;

            if (editId.HasValue && editId.Value > 0)
            {
                var item = await _context.BbsObservations.FindAsync(editId.Value);
                if (item != null && !item.IsDeleted)
                {
                    if (item.ObserverNik == userNik || IsAdminUser())
                    {
                        ViewBag.IsEditMode = true;
                        ViewBag.InitialType = item.ObservationType;
                        return View(item);
                    }
                }
            }

            ViewBag.IsEditMode = false;
            var chosenType = string.Equals(type, "SpecialCase", StringComparison.OrdinalIgnoreCase) ? "SpecialCase" : "Rutin";
            ViewBag.InitialType = chosenType;

            return View(new BbsObservation
            {
                Tanggal = DateTime.Today,
                Waktu = DateTime.Now.ToString("HH:mm"),
                ObservationType = chosenType
            });
        }

        // ==========================================
        // 2. REDIRECT HISTORY LANGSUNG KE INDEX
        // ==========================================
        [HttpGet]
        public IActionResult History()
        {
            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // 3. CETAK / SHARE PDF OBSERVASI BBS
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> PrintPdf(int id)
        {
            var item = await _context.BbsObservations.FindAsync(id);
            if (item == null || item.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            if (item.ObserverNik != userNik && !IsAdminUser())
            {
                TempData["ErrorMessage"] = "Anda hanya dapat mencetak observasi milik Anda sendiri.";
                return RedirectToAction(nameof(Index));
            }

            ViewData["HeaderTitle"] = $"Laporan Observasi BBS - {item.ObservationNo}";
            ViewData["HideNav"] = true;
            ViewData["HideHeader"] = true;
            ViewData["HideFooter"] = true;
            return View(item);
        }

        // ==========================================
        // 3. CREATE ALIAS & EDIT ALIAS
        // ==========================================
        [HttpGet]
        public IActionResult Create(string? type)
        {
            return RedirectToAction(nameof(Index), new { type });
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            return RedirectToAction(nameof(Index), new { editId = id });
        }

        // ==========================================
        // 4. POST: CREATE OR UPDATE OBSERVASI BBS
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            BbsObservation model,
            string waktuStr,
            List<string>? selectedBehaviors,
            List<string>? triggers,
            List<string>? actions,
            IFormFile? foto)
        {
            try
            {
                var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim() ?? "00000";
                var userName = User.Identity?.Name ?? "Anonymous";
                var userDept = User.FindFirst("Department")?.Value ?? "General";
                var companyIdStr = User.FindFirst("CompanyId")?.Value;
                int? companyId = int.TryParse(companyIdStr, out var cid) ? cid : null;
                var isAdmin = IsAdminUser();

                string userCompName = "";
                if (companyId.HasValue)
                {
                    var userComp = await _context.Perusahaans.AsNoTracking().FirstOrDefaultAsync(p => p.PerusahaanId == companyId.Value);
                    userCompName = userComp?.NamaPerusahaan ?? "";
                }

                // Handle date & time
                if (!string.IsNullOrEmpty(waktuStr))
                {
                    model.Waktu = waktuStr.Trim();
                }
                else if (string.IsNullOrEmpty(model.Waktu))
                {
                    model.Waktu = DateTime.Now.ToString("HH:mm");
                }

                // If editing existing record
                if (model.Id > 0)
                {
                    var existing = await _context.BbsObservations.FindAsync(model.Id);
                    if (existing == null || existing.IsDeleted) return NotFound();

                    if (existing.ObserverNik != userNik && !isAdmin)
                    {
                        TempData["ErrorMessage"] = "Anda tidak memiliki hak akses untuk mengedit observasi ini.";
                        return RedirectToAction(nameof(Index));
                    }

                    existing.Tanggal = model.Tanggal;
                    existing.Waktu = model.Waktu;
                    existing.ObservationType = model.ObservationType;
                    existing.Site = model.Site;
                    existing.Area = model.Area;
                    existing.DetilLokasi = model.DetilLokasi;
                    existing.ObservedNik = model.ObservedNik;
                    existing.ObservedNama = model.ObservedNama;
                    existing.ObservedJabatan = model.ObservedJabatan;
                    existing.ObservedDept = model.ObservedDept;
                    existing.ObservedPerusahaanId = model.ObservedPerusahaanId;
                    existing.ObservedPerusahaan = model.ObservedPerusahaan;
                    existing.CategoryId = model.CategoryId;
                    existing.CategoryName = model.CategoryName;
                    existing.TopicId = model.TopicId;
                    existing.TopicName = model.TopicName;
                    existing.CustomBehavior = model.CustomBehavior;
                    existing.Klasifikasi = model.Klasifikasi;
                    existing.KondisiJalan = model.KondisiJalan;
                    existing.KondisiKerja = model.KondisiKerja;
                    existing.TingkatRisiko = model.TingkatRisiko;
                    existing.Deskripsi = model.Deskripsi;
                    existing.ResponsPekerja = model.ResponsPekerja;
                    existing.CatatanCoaching = model.CatatanCoaching;
                    existing.UpdatedAt = DateTime.Now;

                    if (selectedBehaviors != null && selectedBehaviors.Count > 0)
                    {
                        existing.SelectedBehaviorsJson = JsonSerializer.Serialize(selectedBehaviors.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList());
                    }

                    if (triggers != null)
                    {
                        existing.FaktorPemicu = string.Join(", ", triggers.Where(t => !string.IsNullOrWhiteSpace(t)));
                    }

                    if (actions != null)
                    {
                        existing.TindakanDilakukan = string.Join(", ", actions.Where(a => !string.IsNullOrWhiteSpace(a)));
                    }

                    if (foto != null && foto.Length > 0)
                    {
                        var uploadedUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "BBS", "bbs");
                        existing.FotoUrl = uploadedUrl;
                    }

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Observasi {existing.ObservationNo} berhasil diperbarui.";
                    return RedirectToAction(nameof(Index));
                }

                // If creating new record
                model.ObserverNik = userNik;
                model.ObserverNama = userName;
                model.ObserverDept = userDept;
                model.ObserverPerusahaanId = companyId;
                model.ObserverPerusahaan = userCompName;

                // Generate Unique Observation Number: BBS-YYYYMMDD-XXXX
                var todayStr = DateTime.Today.ToString("yyyyMMdd");
                var todayCount = await _context.BbsObservations
                    .CountAsync(o => o.ObservationNo.StartsWith($"BBS-{todayStr}"));
                model.ObservationNo = $"BBS-{todayStr}-{(todayCount + 1).ToString("D3")}";

                // Pack JSON lists
                if (selectedBehaviors != null && selectedBehaviors.Count > 0)
                {
                    model.SelectedBehaviorsJson = JsonSerializer.Serialize(selectedBehaviors.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList());
                }

                if (triggers != null && triggers.Count > 0)
                {
                    model.FaktorPemicu = string.Join(", ", triggers.Where(t => !string.IsNullOrWhiteSpace(t)));
                }

                if (actions != null && actions.Count > 0)
                {
                    model.TindakanDilakukan = string.Join(", ", actions.Where(a => !string.IsNullOrWhiteSpace(a)));
                }

                // Photo upload
                if (foto != null && foto.Length > 0)
                {
                    var uploadedUrl = await _imageUploadService.UploadAndCompressImageAsync(foto, "BBS", "bbs");
                    model.FotoUrl = uploadedUrl;
                }

                model.CreatedAt = DateTime.Now;
                model.IsDeleted = false;

                _context.BbsObservations.Add(model);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Observasi BBS berhasil disimpan dengan No: {model.ObservationNo}";
                TempData["LastObservationNo"] = model.ObservationNo;
                TempData["LastObservationId"] = model.Id;

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan observasi BBS");
                TempData["ErrorMessage"] = $"Terjadi kesalahan saat menyimpan: {ex.Message}";
                await PopulateFormDropdownsAsync();
                return View(model);
            }
        }

        // ==========================================
        // 5. DETAIL OBSERVASI BBS
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            var item = await _context.BbsObservations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);

            if (item == null) return NotFound();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Query.ContainsKey("json"))
            {
                List<string> behaviors = new List<string>();
                if (!string.IsNullOrEmpty(item.SelectedBehaviorsJson))
                {
                    try
                    {
                        behaviors = JsonSerializer.Deserialize<List<string>>(item.SelectedBehaviorsJson) ?? new List<string>();
                    }
                    catch { }
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        item.Id,
                        item.ObservationNo,
                        item.ObservationType,
                        tanggal = item.Tanggal.ToString("dd MMM yyyy"),
                        waktu = item.Waktu,
                        item.ObserverNama,
                        item.ObserverNik,
                        item.ObserverPerusahaan,
                        item.Site,
                        item.Area,
                        item.DetilLokasi,
                        item.ObservedNama,
                        item.ObservedNik,
                        item.ObservedJabatan,
                        item.ObservedPerusahaan,
                        item.CategoryName,
                        item.TopicName,
                        behaviors,
                        item.CustomBehavior,
                        item.Klasifikasi,
                        item.KondisiJalan,
                        item.KondisiKerja,
                        item.FaktorPemicu,
                        item.TingkatRisiko,
                        item.Deskripsi,
                        item.ResponsPekerja,
                        item.TindakanDilakukan,
                        item.CatatanCoaching,
                        item.FotoUrl,
                        createdAt = item.CreatedAt.ToString("dd MMM yyyy HH:mm")
                    }
                });
            }

            ViewData["HeaderTitle"] = $"Detail Observasi: {item.ObservationNo}";
            ViewData["ActiveTab"] = "BBS";
            return View(item);
        }

        // ==========================================
        // 6. SOFT DELETE OBSERVASI BBS
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.BbsObservations.FindAsync(id);
            if (item == null || item.IsDeleted) return NotFound();

            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            var isAdmin = IsAdminUser();

            if (item.ObserverNik != userNik && !isAdmin)
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki wewenang untuk menghapus observasi ini.";
                return RedirectToAction(nameof(Index));
            }

            item.IsDeleted = true;
            item.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Observasi {item.ObservationNo} berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // 7. AJAX ENDPOINTS UNTUK WIZARD FORM
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetCategories(string type)
        {
            var query = _context.BbsCategories.Where(c => c.IsActive);

            if (type == "Rutin")
            {
                query = query.Where(c => c.CategoryType == "Rutin" || c.CategoryType == "Both");
            }
            else if (type == "SpecialCase")
            {
                query = query.Where(c => c.CategoryType == "SpecialCase" || c.CategoryType == "Both");
            }

            var list = await query
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.CategoryType,
                    icon = string.IsNullOrEmpty(c.Icon) ? "bi-clipboard2-check" : c.Icon,
                    color = string.IsNullOrEmpty(c.Color) ? "#0284c7" : c.Color,
                    c.Description,
                    topicCount = _context.BbsTopics.Count(t => t.CategoryId == c.Id && t.IsActive),
                    behaviorCount = _context.BbsBehaviors.Count(b => b.CategoryId == c.Id && b.IsActive)
                })
                .ToListAsync();

            return Json(list);
        }

        [HttpGet]
        public async Task<IActionResult> GetTopics(int categoryId)
        {
            var list = await _context.BbsTopics
                .Where(t => t.CategoryId == categoryId && t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Name)
                .Select(t => new
                {
                    t.Id,
                    t.CategoryId,
                    t.Name,
                    icon = string.IsNullOrEmpty(t.Icon) ? "bi-tag" : t.Icon,
                    t.Description,
                    behaviorCount = _context.BbsBehaviors.Count(b => b.TopicId == t.Id && b.IsActive)
                })
                .ToListAsync();

            return Json(list);
        }

        [HttpGet]
        public async Task<IActionResult> GetBehaviors(int categoryId, int? topicId)
        {
            var query = _context.BbsBehaviors
                .Where(b => b.CategoryId == categoryId && b.IsActive);

            if (topicId.HasValue && topicId.Value > 0)
            {
                query = query.Where(b => b.TopicId == topicId.Value);
            }

            var list = await query
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.BehaviorText)
                .Select(b => new
                {
                    b.Id,
                    b.CategoryId,
                    b.TopicId,
                    b.BehaviorText,
                    b.DefaultClassification,
                    b.DefaultRiskLevel,
                    b.GuidanceNote
                })
                .ToListAsync();

            return Json(list);
        }

        [HttpGet]
        public async Task<IActionResult> SearchWorker(string q, int? companyId)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 1)
            {
                return Json(new object[] { });
            }

            var queryLower = q.Trim().ToLower();

            var query = from k in _context.Karyawans
                        join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                        join j in _context.Jabatans on k.IdJabatan equals j.JabatanId into jg
                        from j in jg.DefaultIfEmpty()
                        join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                        from d in dg.DefaultIfEmpty()
                        join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                        from c in cg.DefaultIfEmpty()
                        where k.StatusAktif == true &&
                              (p.NamaLengkap.ToLower().Contains(queryLower) ||
                               k.NoNik.ToLower().Contains(queryLower))
                        select new
                        {
                            k,
                            p,
                            j,
                            d,
                            c
                        };

            if (companyId.HasValue && companyId.Value > 0)
            {
                query = query.Where(x => x.k.IdPerusahaan == companyId.Value);
            }

            var workers = await query
                .Take(20)
                .Select(x => new
                {
                    nik = x.k.NoNik.Trim(),
                    nama = x.p.NamaLengkap.Trim(),
                    jabatan = x.j != null ? x.j.NamaJabatan : "",
                    departemen = x.d != null ? x.d.NamaDepartemen : "",
                    perusahaanId = x.k.IdPerusahaan,
                    perusahaan = x.c != null ? x.c.NamaPerusahaan : ""
                })
                .ToListAsync();

            return Json(workers);
        }

        // ==========================================
        // 5. EXPORT EXCEL REKAP OBSERVASI BBS
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> ExportExcel(string? filter = null, string? type = null, string? category = null)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
            var isAdmin = IsAdminUser();

            var query = _context.BbsObservations
                .Where(o => !o.IsDeleted);

            if (!isAdmin)
            {
                query = query.Where(o => o.ObserverNik == userNik);
            }

            if (!string.IsNullOrEmpty(filter))
            {
                if (filter.Equals("Aman", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(o => o.Klasifikasi == "Aman");
                else if (filter.Equals("Berisiko", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(o => o.Klasifikasi != "Aman");
                else if (filter.Equals("Coaching", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(o => !string.IsNullOrEmpty(o.CatatanCoaching));
            }

            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(o => o.ObservationType == type);
            }

            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(o => o.CategoryName == category);
            }

            var list = await query
                .OrderByDescending(o => o.Tanggal)
                .ThenByDescending(o => o.CreatedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Rekap Observasi BBS");

            // Title block
            ws.Cell(1, 1).Value = "LAPORAN REKAPITULASI OBSERVASI PERILAKU (BBS)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");

            ws.Cell(2, 1).Value = $"Dicetak pada: {DateTime.Now:dd MMMM yyyy HH:mm} WITA | Total Data: {list.Count} Observasi";
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 10;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

            // Headers
            string[] headers = new string[]
            {
                "No", "No Observasi", "Tipe", "Tanggal", "Waktu",
                "Area Utama", "Detil Lokasi", "Koordinat GPS",
                "Pelapor (NIK)", "Nama Pelapor", "Dept Pelapor", "Perusahaan Pelapor",
                "Pekerja (NIK)", "Nama Pekerja", "Jabatan Pekerja", "Dept Pekerja", "Perusahaan Pekerja",
                "Kategori BBS", "Sub-Topik", "Klasifikasi", "Tingkat Risiko",
                "Deskripsi / Situasi", "Respons Pekerja", "Tindakan Dilakukan", "Catatan Coaching"
            };

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(4, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0284C7");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
            ws.Row(4).Height = 24;

            int rowIdx = 5;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                ws.Cell(rowIdx, 1).Value = i + 1;
                ws.Cell(rowIdx, 2).Value = item.ObservationNo;
                ws.Cell(rowIdx, 3).Value = item.ObservationType;
                ws.Cell(rowIdx, 4).Value = item.Tanggal.ToString("yyyy-MM-dd");
                ws.Cell(rowIdx, 5).Value = item.Waktu;
                ws.Cell(rowIdx, 6).Value = item.Area;
                ws.Cell(rowIdx, 7).Value = item.DetilLokasi ?? "-";
                ws.Cell(rowIdx, 8).Value = item.Site;
                ws.Cell(rowIdx, 9).Value = item.ObserverNik;
                ws.Cell(rowIdx, 10).Value = item.ObserverNama;
                ws.Cell(rowIdx, 11).Value = item.ObserverDept ?? "-";
                ws.Cell(rowIdx, 12).Value = item.ObserverPerusahaan ?? "-";
                ws.Cell(rowIdx, 13).Value = item.ObservedNik ?? "-";
                ws.Cell(rowIdx, 14).Value = item.ObservedNama ?? "-";
                ws.Cell(rowIdx, 15).Value = item.ObservedJabatan ?? "-";
                ws.Cell(rowIdx, 16).Value = item.ObservedDept ?? "-";
                ws.Cell(rowIdx, 17).Value = item.ObservedPerusahaan ?? "-";
                ws.Cell(rowIdx, 18).Value = item.CategoryName ?? "-";
                ws.Cell(rowIdx, 19).Value = item.TopicName ?? "-";

                var cellKlasifikasi = ws.Cell(rowIdx, 20);
                cellKlasifikasi.Value = item.Klasifikasi;
                if (item.Klasifikasi == "Aman")
                {
                    cellKlasifikasi.Style.Font.FontColor = XLColor.FromHtml("#16A34A");
                    cellKlasifikasi.Style.Font.Bold = true;
                }
                else
                {
                    cellKlasifikasi.Style.Font.FontColor = XLColor.FromHtml("#DC2626");
                    cellKlasifikasi.Style.Font.Bold = true;
                }

                ws.Cell(rowIdx, 21).Value = item.TingkatRisiko;
                ws.Cell(rowIdx, 22).Value = item.Deskripsi ?? "-";
                ws.Cell(rowIdx, 23).Value = item.ResponsPekerja;
                ws.Cell(rowIdx, 24).Value = item.TindakanDilakukan ?? "-";
                ws.Cell(rowIdx, 25).Value = item.CatatanCoaching ?? "-";

                if (i % 2 == 1)
                {
                    ws.Range(rowIdx, 1, rowIdx, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }

                rowIdx++;
            }

            ws.Columns().AdjustToContents(10, 45);

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            var fileName = $"Rekap_Observasi_BBS_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ==========================================
        // HELPER: POPULATE FORM DROPDOWNS
        // ==========================================
        private async Task PopulateFormDropdownsAsync()
        {
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            int? userCompanyId = int.TryParse(companyIdStr, out var cid) ? cid : null;

            // Areas: include company-specific, shared site, or standard areas
            var areaList = await _context.MasterAreas.AsNoTracking()
                .Where(a => !userCompanyId.HasValue || a.PerusahaanId == userCompanyId.Value || a.PerusahaanId == 1 || a.PerusahaanId == 0)
                .OrderBy(a => a.NamaArea)
                .Select(a => a.NamaArea)
                .Distinct()
                .ToListAsync();
            ViewBag.AreaList = areaList;

            // Company List & Dept Map
            ViewBag.CompanyList = await _companyHierarchyService.GetCompaniesAsync();
            ViewBag.CompanyDeptMap = await _companyHierarchyService.GetAllCompanyDepartmentsMapAsync();

            List<string> deptList = new List<string>();
            if (userCompanyId.HasValue)
            {
                deptList = await _companyHierarchyService.GetDepartmentsByCompanyAsync(userCompanyId.Value);
            }
            ViewBag.DeptList = deptList;

            ViewBag.UserNama = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "00000";
            ViewBag.UserDept = User.FindFirst("Department")?.Value ?? "General";
            ViewBag.UserCompanyId = userCompanyId;

            string userCompName = "";
            if (userCompanyId.HasValue)
            {
                var userComp = await _context.Perusahaans.AsNoTracking().FirstOrDefaultAsync(p => p.PerusahaanId == userCompanyId.Value);
                userCompName = userComp?.NamaPerusahaan ?? "";
            }
            ViewBag.UserCompanyName = userCompName;
        }
    }
}
