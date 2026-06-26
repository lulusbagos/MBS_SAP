using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [AllowAnonymous]
    [Route("Display")]
    public class DisplayController : Controller
    {
        private readonly AppDbContext _context;

        public DisplayController(AppDbContext context)
        {
            _context = context;
        }

        [Route("")]
        [Route("Index")]
        public IActionResult Index()
        {
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;
            return View();
        }

        [HttpGet("GetLatestFeed")]
        public async Task<IActionResult> GetLatestFeed()
        {
            // Limit query size for performance, fetch top 20 of each
            var p5ms = await _context.P5ms.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var hazards = await _context.HazardReports.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var inspections = await _context.Inspections.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var actions = await _context.ActionPlans.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var talks = await _context.SafetyTalks.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var observations = await _context.Observations.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var p2hReports = await _context.P2hReports.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();

            var feed = new List<TimelineItem>();

            foreach(var p in p5ms)
                feed.Add(new TimelineItem { Id = p.Id, Type = "P5m", Name = p.Nama, Nik = p.Nik, Department = p.Departemen, Location = p.Lokasi ?? p.Area, Title = $"P5M: {p.Topik}", Description = p.Catatan, Status = "Closed", ImageUrl = p.FotoKegiatan, CreatedAt = p.CreatedAt });
            
            foreach(var h in hazards)
                feed.Add(new TimelineItem { Id = h.Id, Type = "Hazard", Name = h.Nama, Nik = h.Nik, Department = h.Departemen, Location = h.Lokasi ?? h.Area, Title = $"Hazard: {h.KategoriBahaya}", Description = h.Temuan, Status = h.StatusTemuan, ImageUrl = h.FotoTemuan, CreatedAt = h.CreatedAt });
            
            var inspectionActionPlans = await _context.ActionPlans
                .Where(ap => !ap.IsDeleted && (ap.ItemSap == "inspection" || ap.ItemSap == "Inspection"))
                .ToListAsync();

            foreach(var i in inspections)
            {
                var hasOpenActionPlan = inspectionActionPlans.Any(ap => 
                    ap.Nik == i.Nik 
                    && ap.Tanggal.Date == i.Tanggal.Date 
                    && ap.Waktu == i.Waktu 
                    && ap.Status.Equals("Open", System.StringComparison.OrdinalIgnoreCase));

                feed.Add(new TimelineItem { Id = i.Id, Type = "Inspection", Name = i.Nama, Nik = i.Nik, Department = i.Departemen, Location = i.Lokasi ?? i.Area, Title = $"Inspeksi: {i.JenisInspeksi}", Description = "", Status = hasOpenActionPlan ? "Open" : "Closed", ImageUrl = null, CreatedAt = i.CreatedAt });
            }
            
            foreach(var a in actions)
                feed.Add(new TimelineItem { Id = a.Id, Type = "ActionPlan", Name = a.Nama, Nik = a.Nik, Department = a.Departemen, Location = a.Lokasi ?? a.Area, Title = $"Action Plan: {a.KategoriTemuan}", Description = a.RencanaPerbaikan, Status = a.Status, ImageUrl = null, CreatedAt = a.CreatedAt });
            
            foreach(var s in talks)
                feed.Add(new TimelineItem { Id = s.Id, Type = "SafetyTalk", Name = s.Nama, Nik = s.Nik, Department = s.Departemen, Location = s.Lokasi ?? s.Area, Title = $"Safety Talk: {s.Judul}", Description = s.Keterangan, Status = "Closed", ImageUrl = s.FotoKegiatan, CreatedAt = s.CreatedAt });

            foreach(var o in observations)
                feed.Add(new TimelineItem { Id = o.Id, Type = "Observation", Name = o.Nama, Nik = o.Nik, Department = o.Departemen, Location = o.Lokasi ?? o.Area, Title = $"Observasi: {o.PerihalYangDiamati}", Description = $"Kegiatan: {o.KegiatanYangDiamati}. Keterangan: {o.Keterangan}", Status = o.HasilObservasi ?? string.Empty, ImageUrl = o.FotoUrl, CreatedAt = o.CreatedAt });

            foreach(var r in p2hReports)
            {
                int defectCount = 0;
                var defects = new List<string>();
                try
                {
                    if (!string.IsNullOrEmpty(r.GolA_Json))
                    {
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<P2hController.ChecklistItem>>(r.GolA_Json);
                        if (list != null)
                        {
                            var bad = list.Where(x => x.Status == "NOT_GOOD").Select(x => x.Name);
                            defects.AddRange(bad);
                            defectCount += bad.Count();
                        }
                    }
                    if (!string.IsNullOrEmpty(r.GolB_Json))
                    {
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<P2hController.ChecklistItem>>(r.GolB_Json);
                        if (list != null)
                        {
                            var bad = list.Where(x => x.Status == "NOT_GOOD").Select(x => x.Name);
                            defects.AddRange(bad);
                            defectCount += bad.Count();
                        }
                    }
                    if (!string.IsNullOrEmpty(r.GolC_Json))
                    {
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<P2hController.ChecklistItem>>(r.GolC_Json);
                        if (list != null)
                        {
                            var bad = list.Where(x => x.Status == "NOT_GOOD").Select(x => x.Name);
                            defects.AddRange(bad);
                            defectCount += bad.Count();
                        }
                    }
                }
                catch (Exception) { }

                string descText = defectCount == 0 
                    ? "Kondisi unit: SEMUA BAIK" 
                    : $"Kondisi unit: DITEMUKAN {defectCount} TEMUAN KERUSAKAN ({string.Join(", ", defects)})";

                feed.Add(new TimelineItem { 
                    Id = r.Id, 
                    Type = "P2h", 
                    Name = r.Nama, 
                    Nik = r.Nik, 
                    Department = "P2H", 
                    Location = r.NoLambung, 
                    Title = $"P2H: {r.JenisKendaraan} ({r.Merek})", 
                    Description = descText, 
                    Status = defectCount == 0 ? "GOOD" : "NOT_GOOD", 
                    ImageUrl = r.FotoSpeedometer, 
                    CreatedAt = r.CreatedAt 
                });
            }

            // Fetch Likes and Comments for the feed items
            var allLikes = await _context.TimelineLikes.ToListAsync();
            var allComments = await _context.TimelineComments.OrderBy(c => c.CreatedAt).ToListAsync();
            var overrides = await _context.PasswordOverrides.ToListAsync(); // to get profile pics

            foreach(var item in feed)
            {
                item.LikesCount = allLikes.Count(l => l.ItemType == item.Type && l.ItemId == item.Id);
                item.Comments = allComments.Where(c => c.ItemType == item.Type && c.ItemId == item.Id).Select(c => new CommentDto { Name = c.NamaPengguna ?? "Guest", Text = c.CommentText }).ToList();
                
                // Get User Profile Pic
                var userProf = overrides.FirstOrDefault(o => o.Nrp == item.Nik);
                item.UserProfilePic = userProf?.ProfilePicture ?? "/images/default-avatar.png";
            }

            var sortedFeed = feed.OrderByDescending(f => f.CreatedAt).Take(50).ToList();
            return Json(sortedFeed);
        }

        [HttpPost("AddLike")]
        public async Task<IActionResult> AddLike([FromBody] LikeRequest req)
        {
            var like = new TimelineLike
            {
                ItemType = req.Type,
                ItemId = req.Id,
                Nik = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null,
                CreatedAt = DateTime.Now
            };
            _context.TimelineLikes.Add(like);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("AddComment")]
        public async Task<IActionResult> AddComment([FromBody] CommentRequest req)
        {
            var nik = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
            var name = User.Identity?.IsAuthenticated == true ? User.Identity.Name : (string.IsNullOrEmpty(req.GuestName) ? "Guest" : req.GuestName);

            var comment = new TimelineComment
            {
                ItemType = req.Type,
                ItemId = req.Id,
                CommentText = req.Text,
                Nik = nik,
                NamaPengguna = name,
                CreatedAt = DateTime.Now
            };
            _context.TimelineComments.Add(comment);
            await _context.SaveChangesAsync();
            return Ok(new { Name = name, Text = req.Text });
        }
    }

    public class TimelineItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Nik { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Location { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string UserProfilePic { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int LikesCount { get; set; }
        public List<CommentDto> Comments { get; set; } = new List<CommentDto>();
    }

    public class CommentDto
    {
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public class LikeRequest
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
    }

    public class CommentRequest
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? GuestName { get; set; }
    }
}
