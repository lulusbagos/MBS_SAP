using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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

        public BbsMasterController(AppDbContext context, ILogger<BbsMasterController> logger)
        {
            _context = context;
            _logger = logger;
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
    }
}
