using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MBS_SAP.Data;
using MBS_SAP.Models;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class BbsMasterController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BbsMasterController> _logger;
        private readonly IWebHostEnvironment _env;

        public BbsMasterController(AppDbContext context, ILogger<BbsMasterController> logger, IWebHostEnvironment env)
        {
            _context = context;
            _logger = logger;
            _env = env;
        }

        private bool IsAdminUser()
        {
            return User.IsInRole("Admin") || User.IsInRole("Administrator") ||
                   string.Equals(User.FindFirst(ClaimTypes.Role)?.Value?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);
        }

        // ==========================================
        // 1. INDEX: MASTER DATA BBS DASHBOARD
        // ==========================================
        public async Task<IActionResult> Index(string tab = "categories", int? categoryId = null, int? topicId = null, string? search = null)
        {
            if (!IsAdminUser())
            {
                TempData["ErrorMessage"] = "Akses ditolak: Menu Master BBS hanya dapat diakses oleh Administrator.";
                return RedirectToAction("Index", "Home");
            }

            ViewData["HeaderTitle"] = "Master Data BBS (Behavior Based Safety)";
            ViewData["ActiveTab"] = "BbsMaster";
            ViewBag.ActiveTabName = tab;
            ViewBag.CategoryId = categoryId;
            ViewBag.TopicId = topicId;
            ViewBag.SearchQuery = search;

            // Load Categories with counts
            var categories = await _context.BbsCategories
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Include(c => c.Topics)
                .Include(c => c.Behaviors)
                .ToListAsync();
            ViewBag.Categories = categories;

            // Load Topics
            var topicsQuery = _context.BbsTopics
                .Include(t => t.Category)
                .Include(t => t.Behaviors)
                .AsQueryable();

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                topicsQuery = topicsQuery.Where(t => t.CategoryId == categoryId.Value);
            }

            if (!string.IsNullOrEmpty(search) && tab == "topics")
            {
                var s = search.Trim().ToLower();
                topicsQuery = topicsQuery.Where(t => t.Name.ToLower().Contains(s) || (t.Description != null && t.Description.ToLower().Contains(s)));
            }

            var topics = await topicsQuery
                .OrderBy(t => t.CategoryId)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.Name)
                .ToListAsync();
            ViewBag.Topics = topics;

            // Load Behaviors
            var behaviorsQuery = _context.BbsBehaviors
                .Include(b => b.Category)
                .Include(b => b.Topic)
                .AsQueryable();

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                behaviorsQuery = behaviorsQuery.Where(b => b.CategoryId == categoryId.Value);
            }

            if (topicId.HasValue && topicId.Value > 0)
            {
                behaviorsQuery = behaviorsQuery.Where(b => b.TopicId == topicId.Value);
            }

            if (!string.IsNullOrEmpty(search) && tab == "behaviors")
            {
                var s = search.Trim().ToLower();
                behaviorsQuery = behaviorsQuery.Where(b => b.BehaviorText.ToLower().Contains(s) || (b.GuidanceNote != null && b.GuidanceNote.ToLower().Contains(s)));
            }

            var behaviors = await behaviorsQuery
                .OrderBy(b => b.CategoryId)
                .ThenBy(b => b.TopicId)
                .ThenBy(b => b.SortOrder)
                .ThenBy(b => b.BehaviorText)
                .ToListAsync();
            ViewBag.Behaviors = behaviors;

            // Load Master Documents & Checklists
            var docsQuery = _context.BbsDocuments
                .Include(d => d.Category)
                .Include(d => d.Items)
                .AsQueryable();

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                docsQuery = docsQuery.Where(d => d.CategoryId == categoryId.Value);
            }

            if (!string.IsNullOrEmpty(search) && tab == "documents")
            {
                var s = search.Trim().ToLower();
                docsQuery = docsQuery.Where(d => d.DocCode.ToLower().Contains(s) || 
                                                 d.Title.ToLower().Contains(s) || 
                                                 (d.Description != null && d.Description.ToLower().Contains(s)) ||
                                                 (d.Department != null && d.Department.ToLower().Contains(s)));
            }

            var documents = await docsQuery
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.DocCode)
                .ToListAsync();
            ViewBag.Documents = documents;
            ViewBag.TotalDocsCount = await _context.BbsDocuments.CountAsync();

            return View();
        }

        // ==========================================
        // 2. CATEGORY CRUD
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCategory(BbsCategory model)
        {
            if (!IsAdminUser()) return Forbid();

            try
            {
                if (model.Id > 0)
                {
                    var cat = await _context.BbsCategories.FindAsync(model.Id);
                    if (cat == null) return NotFound();

                    cat.Name = model.Name.Trim();
                    cat.CategoryType = model.CategoryType;
                    cat.Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-clipboard2-check" : model.Icon.Trim();
                    cat.Color = string.IsNullOrWhiteSpace(model.Color) ? "#0284c7" : model.Color.Trim();
                    cat.Description = model.Description?.Trim();
                    cat.SortOrder = model.SortOrder;
                    cat.IsActive = model.IsActive;
                    cat.UpdatedAt = DateTime.Now;

                    TempData["SuccessMessage"] = $"Kategori '{cat.Name}' berhasil diperbarui.";
                }
                else
                {
                    model.Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-clipboard2-check" : model.Icon.Trim();
                    model.Color = string.IsNullOrWhiteSpace(model.Color) ? "#0284c7" : model.Color.Trim();
                    model.CreatedAt = DateTime.Now;
                    _context.BbsCategories.Add(model);
                    TempData["SuccessMessage"] = $"Kategori baru '{model.Name}' berhasil ditambahkan.";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving BBS Category");
                TempData["ErrorMessage"] = $"Gagal menyimpan kategori: {ex.Message}";
            }

            return RedirectToAction(nameof(Index), new { tab = "categories" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCategory(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var cat = await _context.BbsCategories.FindAsync(id);
            if (cat == null) return NotFound();

            cat.IsActive = !cat.IsActive;
            cat.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Status kategori '{cat.Name}' diubah menjadi {(cat.IsActive ? "Aktif" : "Nonaktif")}.";
            return RedirectToAction(nameof(Index), new { tab = "categories" });
        }

        // ==========================================
        // 3. TOPIC CRUD
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTopic(BbsTopic model)
        {
            if (!IsAdminUser()) return Forbid();

            try
            {
                if (model.Id > 0)
                {
                    var topic = await _context.BbsTopics.FindAsync(model.Id);
                    if (topic == null) return NotFound();

                    topic.CategoryId = model.CategoryId;
                    topic.Name = model.Name.Trim();
                    topic.Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-tag" : model.Icon.Trim();
                    topic.Description = model.Description?.Trim();
                    topic.SortOrder = model.SortOrder;
                    topic.IsActive = model.IsActive;
                    topic.UpdatedAt = DateTime.Now;

                    TempData["SuccessMessage"] = $"Topik '{topic.Name}' berhasil diperbarui.";
                }
                else
                {
                    model.Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-tag" : model.Icon.Trim();
                    model.CreatedAt = DateTime.Now;
                    _context.BbsTopics.Add(model);
                    TempData["SuccessMessage"] = $"Topik baru '{model.Name}' berhasil ditambahkan.";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving BBS Topic");
                TempData["ErrorMessage"] = $"Gagal menyimpan topik: {ex.Message}";
            }

            return RedirectToAction(nameof(Index), new { tab = "topics", categoryId = model.CategoryId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTopic(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var topic = await _context.BbsTopics.FindAsync(id);
            if (topic == null) return NotFound();

            topic.IsActive = !topic.IsActive;
            topic.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Status topik '{topic.Name}' diubah menjadi {(topic.IsActive ? "Aktif" : "Nonaktif")}.";
            return RedirectToAction(nameof(Index), new { tab = "topics", categoryId = topic.CategoryId });
        }

        // ==========================================
        // 4. BEHAVIOR CRUD
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBehavior(BbsBehavior model)
        {
            if (!IsAdminUser()) return Forbid();

            try
            {
                if (model.TopicId == 0) model.TopicId = null;

                if (model.Id > 0)
                {
                    var bhv = await _context.BbsBehaviors.FindAsync(model.Id);
                    if (bhv == null) return NotFound();

                    bhv.CategoryId = model.CategoryId;
                    bhv.TopicId = model.TopicId;
                    bhv.BehaviorText = model.BehaviorText.Trim();
                    bhv.DefaultClassification = model.DefaultClassification;
                    bhv.DefaultRiskLevel = model.DefaultRiskLevel;
                    bhv.GuidanceNote = model.GuidanceNote?.Trim();
                    bhv.SortOrder = model.SortOrder;
                    bhv.IsActive = model.IsActive;
                    bhv.UpdatedAt = DateTime.Now;

                    TempData["SuccessMessage"] = "Butir perilaku berhasil diperbarui.";
                }
                else
                {
                    model.CreatedAt = DateTime.Now;
                    _context.BbsBehaviors.Add(model);
                    TempData["SuccessMessage"] = "Butir perilaku baru berhasil ditambahkan.";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving BBS Behavior");
                TempData["ErrorMessage"] = $"Gagal menyimpan butir perilaku: {ex.Message}";
            }

            return RedirectToAction(nameof(Index), new { tab = "behaviors", categoryId = model.CategoryId, topicId = model.TopicId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBehavior(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var bhv = await _context.BbsBehaviors.FindAsync(id);
            if (bhv == null) return NotFound();

            bhv.IsActive = !bhv.IsActive;
            bhv.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Status butir perilaku diubah menjadi {(bhv.IsActive ? "Aktif" : "Nonaktif")}.";
            return RedirectToAction(nameof(Index), new { tab = "behaviors", categoryId = bhv.CategoryId, topicId = bhv.TopicId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBehavior(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var bhv = await _context.BbsBehaviors.FindAsync(id);
            if (bhv == null) return NotFound();

            var cId = bhv.CategoryId;
            var tId = bhv.TopicId;
            _context.BbsBehaviors.Remove(bhv);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Butir perilaku berhasil dihapus.";
            return RedirectToAction(nameof(Index), new { tab = "behaviors", categoryId = cId, topicId = tId });
        }

        // ==========================================
        // 5. MASTER DOKUMEN & CHECKLIST CRUD
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetDocumentDetails(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var doc = await _context.BbsDocuments
                .Include(d => d.Category)
                .Include(d => d.Items)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null) return NotFound(new { success = false, message = "Dokumen tidak ditemukan." });

            var groupedSections = doc.Items
                .OrderBy(i => i.SortOrder)
                .GroupBy(i => i.SectionName)
                .Select(g => new
                {
                    sectionName = g.Key,
                    itemType = g.First().ItemType,
                    items = g.Select(i => new
                    {
                        i.Id,
                        i.ItemNo,
                        i.ItemText,
                        i.ItemType,
                        i.Classification,
                        i.RiskLevel,
                        i.SortOrder,
                        i.IsActive
                    }).ToList()
                }).ToList();

            return Json(new
            {
                success = true,
                doc = new
                {
                    doc.Id,
                    doc.DocCode,
                    doc.Title,
                    doc.CategoryId,
                    categoryName = doc.Category?.Name ?? doc.CategoryName ?? "-",
                    doc.TopicName,
                    doc.Revision,
                    doc.EffectiveDate,
                    doc.Department,
                    doc.Scope,
                    doc.Description,
                    doc.Icon,
                    doc.Color,
                    doc.FileName,
                    doc.FileUrl,
                    doc.TotalItems,
                    doc.BehaviorCount,
                    doc.ConditionCount,
                    doc.IsActive,
                    sections = groupedSections
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDocument(BbsDocument model, IFormFile? excelFile)
        {
            if (!IsAdminUser()) return Forbid();

            try
            {
                if (model.CategoryId == 0) model.CategoryId = null;

                if (model.CategoryId.HasValue)
                {
                    var cat = await _context.BbsCategories.FindAsync(model.CategoryId.Value);
                    if (cat != null) model.CategoryName = cat.Name;
                }

                // Handle file upload if provided
                if (excelFile != null && excelFile.Length > 0)
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "bbs_documents");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    var cleanFileName = Path.GetFileName(excelFile.FileName);
                    var filePath = Path.Combine(uploadsFolder, cleanFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await excelFile.CopyToAsync(stream);
                    }

                    model.FileName = cleanFileName;
                    model.FileUrl = $"/uploads/bbs_documents/{cleanFileName}";
                }

                if (model.Id > 0)
                {
                    var doc = await _context.BbsDocuments.FindAsync(model.Id);
                    if (doc == null) return NotFound();

                    doc.DocCode = model.DocCode.Trim();
                    doc.Title = model.Title.Trim();
                    doc.CategoryId = model.CategoryId;
                    doc.CategoryName = model.CategoryName;
                    doc.TopicName = model.TopicName?.Trim();
                    doc.Revision = model.Revision?.Trim() ?? "00";
                    doc.EffectiveDate = model.EffectiveDate?.Trim();
                    doc.Department = model.Department?.Trim();
                    doc.Scope = model.Scope?.Trim();
                    doc.Description = model.Description?.Trim();
                    doc.Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-file-earmark-text-fill" : model.Icon.Trim();
                    doc.Color = string.IsNullOrWhiteSpace(model.Color) ? "#0284c7" : model.Color.Trim();
                    doc.SortOrder = model.SortOrder;
                    doc.IsActive = model.IsActive;
                    doc.UpdatedAt = DateTime.Now;

                    if (!string.IsNullOrEmpty(model.FileName))
                    {
                        doc.FileName = model.FileName;
                        doc.FileUrl = model.FileUrl;
                    }

                    TempData["SuccessMessage"] = $"Dokumen '{doc.DocCode} - {doc.Title}' berhasil diperbarui.";
                }
                else
                {
                    model.DocCode = model.DocCode.Trim();
                    model.Title = model.Title.Trim();
                    model.Revision = model.Revision?.Trim() ?? "00";
                    model.Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-file-earmark-text-fill" : model.Icon.Trim();
                    model.Color = string.IsNullOrWhiteSpace(model.Color) ? "#0284c7" : model.Color.Trim();
                    model.CreatedAt = DateTime.Now;

                    _context.BbsDocuments.Add(model);
                    TempData["SuccessMessage"] = $"Dokumen baru '{model.DocCode} - {model.Title}' berhasil ditambahkan.";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving BBS Document");
                TempData["ErrorMessage"] = $"Gagal menyimpan dokumen: {ex.Message}";
            }

            return RedirectToAction(nameof(Index), new { tab = "documents" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDocument(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var doc = await _context.BbsDocuments.FindAsync(id);
            if (doc == null) return NotFound();

            doc.IsActive = !doc.IsActive;
            doc.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Status dokumen '{doc.DocCode}' diubah menjadi {(doc.IsActive ? "Aktif" : "Nonaktif")}.";
            return RedirectToAction(nameof(Index), new { tab = "documents" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var doc = await _context.BbsDocuments.Include(d => d.Items).FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return NotFound();

            _context.BbsDocuments.Remove(doc);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Dokumen '{doc.DocCode}' beserta seluruh butir checklist berhasil dihapus.";
            return RedirectToAction(nameof(Index), new { tab = "documents" });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int id)
        {
            var doc = await _context.BbsDocuments.FindAsync(id);
            if (doc == null || string.IsNullOrEmpty(doc.FileName))
            {
                TempData["ErrorMessage"] = "Berkas file dokumen tidak ditemukan.";
                return RedirectToAction(nameof(Index), new { tab = "documents" });
            }

            var filePath = Path.Combine(_env.WebRootPath, "uploads", "bbs_documents", doc.FileName);
            if (!System.IO.File.Exists(filePath))
            {
                TempData["ErrorMessage"] = $"File '{doc.FileName}' tidak ditemukan di server.";
                return RedirectToAction(nameof(Index), new { tab = "documents" });
            }

            var contentType = doc.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) 
                ? "application/vnd.ms-excel" 
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(fileBytes, contentType, doc.FileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDocumentItem(BbsDocumentItem item)
        {
            if (!IsAdminUser()) return Forbid();

            try
            {
                if (item.Id > 0)
                {
                    var existing = await _context.BbsDocumentItems.FindAsync(item.Id);
                    if (existing == null) return NotFound();

                    existing.SectionName = item.SectionName.Trim();
                    existing.ItemNo = item.ItemNo?.Trim();
                    existing.ItemText = item.ItemText.Trim();
                    existing.ItemType = item.ItemType;
                    existing.Classification = item.Classification;
                    existing.RiskLevel = item.RiskLevel;
                    existing.SortOrder = item.SortOrder;
                    existing.IsActive = item.IsActive;

                    TempData["SuccessMessage"] = "Butir checklist dokumen berhasil diperbarui.";
                }
                else
                {
                    item.CreatedAt = DateTime.Now;
                    _context.BbsDocumentItems.Add(item);
                    TempData["SuccessMessage"] = "Butir checklist dokumen berhasil ditambahkan.";
                }

                await _context.SaveChangesAsync();

                // Recount items
                var doc = await _context.BbsDocuments.Include(d => d.Items).FirstOrDefaultAsync(d => d.Id == item.DocumentId);
                if (doc != null)
                {
                    doc.TotalItems = doc.Items.Count;
                    doc.BehaviorCount = doc.Items.Count(i => i.ItemType == "Perilaku");
                    doc.ConditionCount = doc.Items.Count(i => i.ItemType != "Perilaku");
                    doc.UpdatedAt = DateTime.Now;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving BBS Document Item");
                TempData["ErrorMessage"] = $"Gagal menyimpan butir checklist: {ex.Message}";
            }

            return RedirectToAction(nameof(Index), new { tab = "documents" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocumentItem(int id)
        {
            if (!IsAdminUser()) return Forbid();

            var item = await _context.BbsDocumentItems.FindAsync(id);
            if (item == null) return NotFound();

            var docId = item.DocumentId;
            _context.BbsDocumentItems.Remove(item);
            await _context.SaveChangesAsync();

            var doc = await _context.BbsDocuments.Include(d => d.Items).FirstOrDefaultAsync(d => d.Id == docId);
            if (doc != null)
            {
                doc.TotalItems = doc.Items.Count;
                doc.BehaviorCount = doc.Items.Count(i => i.ItemType == "Perilaku");
                doc.ConditionCount = doc.Items.Count(i => i.ItemType != "Perilaku");
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "Butir checklist berhasil dihapus.";
            return RedirectToAction(nameof(Index), new { tab = "documents" });
        }
    }
}
