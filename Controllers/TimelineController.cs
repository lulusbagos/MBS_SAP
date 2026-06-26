using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    public class TimelineController : Controller
    {
        private readonly AppDbContext _context;

        public TimelineController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            
            // Fetch top 30 from each to keep it lightweight
            var hazards = await _context.HazardReports.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
            var inspections = await _context.Inspections.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
            var actionPlans = await _context.ActionPlans.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
            var safetyTalks = await _context.SafetyTalks.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
            var p5ms = await _context.P5ms.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
            var observations = await _context.Observations.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
            var p2hReports = await _context.P2hReports.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();

            var timelineList = new List<TimelineViewModel>();

            foreach (var h in hazards)
            {
                timelineList.Add(new TimelineViewModel {
                    ItemType = "Hazard", OriginalId = h.Id,
                    Nama = h.Nama, Nik = h.Nik, Departemen = h.Departemen, PerusahaanId = h.PerusahaanId,
                    Tanggal = h.Tanggal, Waktu = h.Waktu, CreatedAt = h.CreatedAt,
                    Area = h.Area, Lokasi = h.Lokasi, Kategori = h.JenisBahaya,
                    Content = h.Temuan, Status = h.StatusTemuan, FotoUrl = h.FotoTemuan
                });
            }
            var inspectionActionPlans = await _context.ActionPlans
                .Where(ap => !ap.IsDeleted && (ap.ItemSap == "inspection" || ap.ItemSap == "Inspection"))
                .ToListAsync();

            foreach (var i in inspections)
            {
                var hasOpenActionPlan = inspectionActionPlans.Any(ap => 
                    ap.Nik == i.Nik 
                    && ap.Tanggal.Date == i.Tanggal.Date 
                    && ap.Waktu == i.Waktu 
                    && ap.Status.Equals("Open", System.StringComparison.OrdinalIgnoreCase));

                timelineList.Add(new TimelineViewModel {
                    ItemType = "Inspection", OriginalId = i.Id,
                    Nama = i.Nama, Nik = i.Nik, Departemen = i.Departemen, PerusahaanId = i.PerusahaanId,
                    Tanggal = i.Tanggal, Waktu = i.Waktu, CreatedAt = i.CreatedAt,
                    Area = i.Area, Lokasi = i.Lokasi, Kategori = i.JenisInspeksi,
                    Title = "Laporan Inspeksi", Status = hasOpenActionPlan ? "Open" : "Closed"
                });
            }
            foreach (var a in actionPlans)
            {
                timelineList.Add(new TimelineViewModel {
                    ItemType = "ActionPlan", OriginalId = a.Id,
                    Nama = a.Nama, Nik = a.Nik, Departemen = a.Departemen, PerusahaanId = a.PerusahaanId,
                    Tanggal = a.Tanggal, Waktu = a.Waktu, CreatedAt = a.CreatedAt,
                    Area = a.Area, Lokasi = a.Lokasi, Kategori = a.KategoriTemuan,
                    Content = a.Perbaikan, Status = a.Status, FotoUrl = a.FotoPerbaikan ?? a.FotoTemuan
                });
            }
            foreach (var s in safetyTalks)
            {
                timelineList.Add(new TimelineViewModel {
                    ItemType = "SafetyTalk", OriginalId = s.Id,
                    Nama = s.Nama, Nik = s.Nik, Departemen = s.Departemen, PerusahaanId = s.PerusahaanId,
                    Tanggal = s.Tanggal, Waktu = s.Waktu, CreatedAt = s.CreatedAt,
                    Area = s.Area, Lokasi = s.Lokasi, Title = s.Judul,
                    Content = s.Keterangan, FotoUrl = s.FotoKegiatan, FotoDiriUrl = s.FotoDiri
                });
            }
            foreach (var p in p5ms)
            {
                timelineList.Add(new TimelineViewModel {
                    ItemType = "P5m", OriginalId = p.Id,
                    Nama = p.Nama, Nik = p.Nik, Departemen = p.Departemen, PerusahaanId = p.PerusahaanId,
                    Tanggal = p.Tanggal, Waktu = p.Waktu, CreatedAt = p.CreatedAt,
                    Area = p.Area, Lokasi = p.Lokasi, Title = p.Topik,
                    Content = p.Keterangan, FotoUrl = p.FotoKegiatan
                });
            }
            foreach (var o in observations)
            {
                timelineList.Add(new TimelineViewModel {
                    ItemType = "Observation", OriginalId = o.Id,
                    Nama = o.Nama, Nik = o.Nik, Departemen = o.Departemen, PerusahaanId = null,
                    Tanggal = o.Date, Waktu = o.Date.TimeOfDay, CreatedAt = o.CreatedAt,
                    Area = o.Area, Lokasi = o.Lokasi, Kategori = o.PerihalYangDiamati,
                    Title = "Observasi Lapangan", Status = o.HasilObservasi, FotoUrl = o.FotoUrl,
                    Content = $"Kegiatan yang diamati: {o.KegiatanYangDiamati}. Keterangan: {o.Keterangan}"
                });
            }

            foreach (var r in p2hReports)
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

                string contentText = defectCount == 0 
                    ? "Kondisi unit: SEMUA BAIK" 
                    : $"Kondisi unit: DITEMUKAN {defectCount} TEMUAN KERUSAKAN ({string.Join(", ", defects)})";

                timelineList.Add(new TimelineViewModel {
                    ItemType = "P2h", OriginalId = r.Id,
                    Nama = r.Nama, Nik = r.Nik, Departemen = "P2H", PerusahaanId = null,
                    Tanggal = r.Tanggal, Waktu = r.Waktu, CreatedAt = r.CreatedAt,
                    Area = r.NoLambung, Lokasi = $"{r.Merek} (KM: {r.Kilometer})", Kategori = r.JenisKendaraan,
                    Title = "Pemeriksaan Kendaraan Harian (P2H)", Status = defectCount == 0 ? "GOOD" : "NOT_GOOD", 
                    FotoUrl = r.FotoSpeedometer,
                    Content = contentText
                });
            }

            // Get all likes and comments for these items
            var itemKeys = timelineList.Select(x => x.ItemType + "_" + x.OriginalId).ToList();
            
            var allLikes = await _context.TimelineLikes.ToListAsync();
            var allComments = await _context.TimelineComments.OrderBy(c => c.CreatedAt).ToListAsync();

            foreach (var item in timelineList)
            {
                var likes = allLikes.Where(l => l.ItemType == item.ItemType && l.ItemId == item.OriginalId).ToList();
                var comments = allComments.Where(c => c.ItemType == item.ItemType && c.ItemId == item.OriginalId).ToList();
                
                item.LikesCount = likes.Count;
                item.CommentsCount = comments.Count;
                item.IsLikedByCurrentUser = likes.Any(l => l.Nik == userNik);
                item.Comments = comments;
            }

            // Sort everything by latest created
            timelineList = timelineList.OrderByDescending(x => x.CreatedAt).ToList();
            
            ViewData["ActiveTab"] = "Timeline";
            return View(timelineList);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleLike(string itemType, int itemId)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrEmpty(userNik)) return Json(new { success = false, message = "Belum login" });

            var existingLike = await _context.TimelineLikes
                .FirstOrDefaultAsync(l => l.ItemType == itemType && l.ItemId == itemId && l.Nik == userNik);

            bool isLiked = false;
            if (existingLike != null)
            {
                _context.TimelineLikes.Remove(existingLike);
            }
            else
            {
                _context.TimelineLikes.Add(new TimelineLike {
                    ItemType = itemType,
                    ItemId = itemId,
                    Nik = userNik,
                    CreatedAt = System.DateTime.Now
                });
                isLiked = true;
            }

            await _context.SaveChangesAsync();
            
            var totalLikes = await _context.TimelineLikes.CountAsync(l => l.ItemType == itemType && l.ItemId == itemId);
            return Json(new { success = true, isLiked = isLiked, totalLikes = totalLikes });
        }

        [HttpPost]
        public async Task<IActionResult> AddComment(string itemType, int itemId, string text)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            var userName = User.Identity?.Name ?? "Guest";
            
            if (string.IsNullOrWhiteSpace(text)) return Json(new { success = false, message = "Komentar kosong" });

            var comment = new TimelineComment {
                ItemType = itemType,
                ItemId = itemId,
                Nik = userNik,
                NamaPengguna = userName,
                CommentText = text,
                CreatedAt = System.DateTime.Now
            };

            _context.TimelineComments.Add(comment);
            await _context.SaveChangesAsync();

            var totalComments = await _context.TimelineComments.CountAsync(c => c.ItemType == itemType && c.ItemId == itemId);
            return Json(new { 
                success = true, 
                nama = userName, 
                text = text, 
                time = comment.CreatedAt.ToString("dd MMM, HH:mm"),
                totalComments = totalComments 
            });
        }
    }
}
