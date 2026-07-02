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
            const int pageSize = 12;
            var page = await BuildTimelinePageAsync(userNik, 0, pageSize);

            ViewData["ActiveTab"] = "Timeline";
            ViewBag.PageSize = pageSize;
            ViewBag.HasMore = page.HasMore;
            return View(page.Items);
        }

        [HttpGet]
        public async Task<IActionResult> LoadMore(int skip = 0, int take = 12)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            skip = skip < 0 ? 0 : skip;
            take = take <= 0 ? 12 : (take > 30 ? 30 : take);

            var page = await BuildTimelinePageAsync(userNik, skip, take);

            Response.Headers["X-Item-Count"] = page.Items.Count.ToString();
            Response.Headers["X-Has-More"] = page.HasMore ? "1" : "0";
            return PartialView("_TimelineCards", page.Items);
        }

        private async Task<(List<TimelineViewModel> Items, bool HasMore)> BuildTimelinePageAsync(string userNik, int skip, int take)
        {
            static string Norm(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

            static List<T> DeduplicateRecent<T>(
                IEnumerable<T> source,
                Func<T, string> signatureSelector,
                Func<T, System.DateTime> createdAtSelector,
                int duplicateWindowSeconds = 120)
            {
                var deduped = new List<T>();

                foreach (var item in source.OrderByDescending(createdAtSelector))
                {
                    var sig = signatureSelector(item);
                    var createdAt = createdAtSelector(item);

                    var isDuplicate = deduped.Any(existing =>
                        signatureSelector(existing) == sig
                        && System.Math.Abs((createdAtSelector(existing) - createdAt).TotalSeconds) <= duplicateWindowSeconds);

                    if (!isDuplicate)
                    {
                        deduped.Add(item);
                    }
                }

                return deduped;
            }

            var sourceTake = System.Math.Max(60, skip + take + 30);

            var hazards = await _context.HazardReports.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();
            var inspections = await _context.Inspections.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();
            var actionPlans = await _context.ActionPlans.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();
            var safetyTalks = await _context.SafetyTalks.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();
            var p5ms = await _context.P5ms.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();
            var observations = await _context.Observations.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();
            var p2hReports = await _context.P2hReports.Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).Take(sourceTake).ToListAsync();

            hazards = DeduplicateRecent(
                hazards,
                h => $"{Norm(h.Nik)}|{h.Tanggal:yyyyMMdd}|{h.Waktu}|{Norm(h.Temuan)}|{Norm(h.Area)}|{Norm(h.Lokasi)}",
                h => h.CreatedAt);

            inspections = DeduplicateRecent(
                inspections,
                i => $"{Norm(i.Nik)}|{i.Tanggal:yyyyMMdd}|{i.Waktu}|{Norm(i.JenisInspeksi)}|{Norm(i.Area)}|{Norm(i.Lokasi)}",
                i => i.CreatedAt);

            safetyTalks = DeduplicateRecent(
                safetyTalks,
                s => $"{Norm(s.Nik)}|{s.Tanggal:yyyyMMdd}|{s.Waktu}|{Norm(s.Judul)}|{Norm(s.Keterangan)}|{Norm(s.Area)}|{Norm(s.Lokasi)}",
                s => s.CreatedAt);

            p5ms = DeduplicateRecent(
                p5ms,
                p => $"{Norm(p.Nik)}|{p.Tanggal:yyyyMMdd}|{p.Waktu}|{Norm(p.Topik)}|{Norm(p.Keterangan)}|{Norm(p.Area)}|{Norm(p.Lokasi)}",
                p => p.CreatedAt);

            p2hReports = DeduplicateRecent(
                p2hReports,
                r => $"{Norm(r.Nik)}|{r.Tanggal:yyyyMMdd}|{r.Waktu}|{Norm(r.NoLambung)}|{Norm(r.JenisKendaraan)}|{Norm(r.Merek)}",
                r => r.CreatedAt);

            var timelineList = new List<TimelineViewModel>();

            var hazardActionPlans = await _context.ActionPlans
                .Where(ap => !ap.IsDeleted && ap.ItemSap != null && ap.ItemSap.StartsWith("hazard:"))
                .ToListAsync();

            foreach (var h in hazards)
            {
                var linkedAp = hazardActionPlans.FirstOrDefault(ap => ap.ItemSap == $"hazard:{h.Id}");
                string hazardStatus = h.StatusTemuan ?? "Open";
                if (hazardStatus.Equals("Open", System.StringComparison.OrdinalIgnoreCase)
                    && linkedAp != null
                    && !string.IsNullOrEmpty(linkedAp.ReassignedFrom))
                {
                    hazardStatus = "Progres";
                }

                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "Hazard",
                    OriginalId = h.Id,
                    Nama = h.Nama,
                    Nik = h.Nik,
                    Departemen = h.Departemen,
                    PerusahaanId = h.PerusahaanId,
                    Tanggal = h.Tanggal,
                    Waktu = h.Waktu,
                    CreatedAt = h.CreatedAt,
                    Area = h.Area,
                    Lokasi = h.Lokasi,
                    Kategori = h.JenisBahaya,
                    Content = h.Temuan,
                    Status = hazardStatus,
                    FotoUrl = h.FotoTemuan,
                    TingkatResiko = h.TingkatResiko
                });
            }

            var inspectionActionPlans = await _context.ActionPlans
                .Where(ap => !ap.IsDeleted && (ap.ItemSap == "inspection" || ap.ItemSap == "Inspection"))
                .ToListAsync();

            foreach (var i in inspections)
            {
                var openAps = inspectionActionPlans.Where(ap =>
                    ap.Nik == i.Nik
                    && ap.Tanggal.Date == i.Tanggal.Date
                    && ap.Waktu == i.Waktu
                    && ap.Status.Equals("Open", System.StringComparison.OrdinalIgnoreCase)).ToList();

                var hasOpenActionPlan = openAps.Any();
                var hasReassigned = openAps.Any(ap => !string.IsNullOrEmpty(ap.ReassignedFrom));
                string inspectionStatus = !hasOpenActionPlan ? "Closed" : hasReassigned ? "Progres" : "Open";

                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "Inspection",
                    OriginalId = i.Id,
                    Nama = i.Nama,
                    Nik = i.Nik,
                    Departemen = i.Departemen,
                    PerusahaanId = i.PerusahaanId,
                    Tanggal = i.Tanggal,
                    Waktu = i.Waktu,
                    CreatedAt = i.CreatedAt,
                    Area = i.Area,
                    Lokasi = i.Lokasi,
                    Kategori = i.JenisInspeksi,
                    Title = "Laporan Inspeksi",
                    Status = inspectionStatus
                });
            }

            foreach (var a in actionPlans)
            {
                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "ActionPlan",
                    OriginalId = a.Id,
                    Nama = a.Nama,
                    Nik = a.Nik,
                    Departemen = a.Departemen,
                    PerusahaanId = a.PerusahaanId,
                    Tanggal = a.Tanggal,
                    Waktu = a.Waktu,
                    CreatedAt = a.CreatedAt,
                    Area = a.Area,
                    Lokasi = a.Lokasi,
                    Kategori = a.KategoriTemuan,
                    Content = a.Perbaikan,
                    Status = a.Status,
                    FotoUrl = a.FotoPerbaikan ?? a.FotoTemuan
                });
            }

            foreach (var s in safetyTalks)
            {
                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "SafetyTalk",
                    OriginalId = s.Id,
                    Nama = s.Nama,
                    Nik = s.Nik,
                    Departemen = s.Departemen,
                    PerusahaanId = s.PerusahaanId,
                    Tanggal = s.Tanggal,
                    Waktu = s.Waktu,
                    CreatedAt = s.CreatedAt,
                    Area = s.Area,
                    Lokasi = s.Lokasi,
                    Title = s.Judul,
                    Content = s.Keterangan,
                    FotoUrl = s.FotoKegiatan,
                    FotoDiriUrl = s.FotoDiri
                });
            }

            foreach (var p in p5ms)
            {
                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "P5m",
                    OriginalId = p.Id,
                    Nama = p.Nama,
                    Nik = p.Nik,
                    Departemen = p.Departemen,
                    PerusahaanId = p.PerusahaanId,
                    Tanggal = p.Tanggal,
                    Waktu = p.Waktu,
                    CreatedAt = p.CreatedAt,
                    Area = p.Area,
                    Lokasi = p.Lokasi,
                    Title = p.Topik,
                    Content = p.Keterangan,
                    FotoUrl = p.FotoKegiatan
                });
            }

            foreach (var o in observations)
            {
                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "Observation",
                    OriginalId = o.Id,
                    Nama = o.Nama,
                    Nik = o.Nik,
                    Departemen = o.Departemen,
                    PerusahaanId = null,
                    Tanggal = o.Date,
                    Waktu = o.Date.TimeOfDay,
                    CreatedAt = o.CreatedAt,
                    Area = o.Area,
                    Lokasi = o.Lokasi,
                    Kategori = o.PerihalYangDiamati,
                    Title = "Observasi Lapangan",
                    Status = o.HasilObservasi,
                    FotoUrl = o.FotoUrl,
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
                catch (System.Exception)
                {
                }

                string contentText = defectCount == 0
                    ? "Kondisi unit: SEMUA BAIK"
                    : $"Kondisi unit: DITEMUKAN {defectCount} TEMUAN KERUSAKAN ({string.Join(", ", defects)})";

                timelineList.Add(new TimelineViewModel
                {
                    ItemType = "P2h",
                    OriginalId = r.Id,
                    Nama = r.Nama,
                    Nik = r.Nik,
                    Departemen = "P2H",
                    PerusahaanId = null,
                    Tanggal = r.Tanggal,
                    Waktu = r.Waktu,
                    CreatedAt = r.CreatedAt,
                    Area = r.NoLambung,
                    Lokasi = $"{r.Merek} (KM: {r.Kilometer})",
                    Kategori = r.JenisKendaraan,
                    Title = "Pemeriksaan Kendaraan Harian (P2H)",
                    Status = defectCount == 0 ? "GOOD" : "NOT_GOOD",
                    FotoUrl = r.FotoSpeedometer,
                    Content = contentText
                });
            }

            timelineList = timelineList.OrderByDescending(x => x.CreatedAt).ToList();
            var totalCount = timelineList.Count;
            var pagedTimeline = timelineList.Skip(skip).Take(take).ToList();

            var distinctNiks = pagedTimeline.Select(x => x.Nik).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            var employeeDetails = await (from k in _context.Karyawans
                                         join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                         join j in _context.Jabatans on k.IdJabatan equals j.JabatanId into jg
                                         from j in jg.DefaultIfEmpty()
                                         join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                                         from c in cg.DefaultIfEmpty()
                                         where distinctNiks.Contains(k.NoNik)
                                         select new
                                         {
                                             Nik = k.NoNik,
                                             Nama = p.NamaLengkap,
                                             Jabatan = j != null ? j.NamaJabatan : "Karyawan",
                                             Perusahaan = c != null ? c.NamaPerusahaan : "Mitra MBS",
                                             PathFoto = k.PathFoto,
                                             StatusAktif = k.StatusAktif
                                         }).ToListAsync();

            var employeeMap = employeeDetails
                .OrderByDescending(e => e.StatusAktif)
                .GroupBy(e => e.Nik)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in pagedTimeline)
            {
                if (!string.IsNullOrEmpty(item.Nik) && employeeMap.TryGetValue(item.Nik, out var emp))
                {
                    if (string.IsNullOrEmpty(item.Nama) || item.Nama == item.Nik)
                    {
                        item.Nama = emp.Nama;
                    }

                    item.Jabatan = emp.Jabatan;
                    item.Perusahaan = emp.Perusahaan;

                    if (string.IsNullOrEmpty(item.FotoDiriUrl) && !string.IsNullOrEmpty(emp.PathFoto))
                    {
                        var formattedFoto = emp.PathFoto;
                        if (!formattedFoto.StartsWith("/") && !formattedFoto.StartsWith("http"))
                        {
                            formattedFoto = "/uploads/karyawan/" + formattedFoto;
                        }
                        item.FotoDiriUrl = formattedFoto;
                    }
                }
                else
                {
                    item.Jabatan = "Karyawan";
                    item.Perusahaan = "Mitra MBS";
                }
            }

            var pageItemIds = pagedTimeline.Select(x => x.OriginalId).Distinct().ToList();
            var pageItemTypes = pagedTimeline.Select(x => x.ItemType).Distinct().ToList();

            var allLikes = await _context.TimelineLikes
                .Where(l => pageItemIds.Contains(l.ItemId) && pageItemTypes.Contains(l.ItemType))
                .ToListAsync();

            var allComments = await _context.TimelineComments
                .Where(c => pageItemIds.Contains(c.ItemId) && pageItemTypes.Contains(c.ItemType))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            foreach (var item in pagedTimeline)
            {
                var likes = allLikes.Where(l => l.ItemType == item.ItemType && l.ItemId == item.OriginalId).ToList();
                var comments = allComments.Where(c => c.ItemType == item.ItemType && c.ItemId == item.OriginalId).ToList();

                item.LikesCount = likes.Count;
                item.CommentsCount = comments.Count;
                item.IsLikedByCurrentUser = likes.Any(l => l.Nik == userNik);
                item.Comments = comments;
            }

            var hasMore = skip + pagedTimeline.Count < totalCount;
            return (pagedTimeline, hasMore);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleLike(string itemType, int itemId)
        {
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            var userName = User.Identity?.Name ?? "Seseorang";
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

                // Kirim notifikasi ke pemilik postingan (jika bukan diri sendiri)
                var ownerNik = await GetItemOwnerNikAsync(itemType, itemId);
                if (!string.IsNullOrEmpty(ownerNik) && ownerNik != userNik)
                {
                    _context.Notifications.Add(new Notification
                    {
                        RecipientNik = ownerNik,
                        Title = $"{userName} menyukai postingan Anda",
                        Message = $"{userName} memberikan ❤️ pada laporan {itemType} Anda.",
                        Url = "/Timeline/Index",
                        NotifType = "timeline_like"
                    });
                }
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

            // Kirim notifikasi ke pemilik postingan (jika bukan diri sendiri)
            if (!string.IsNullOrEmpty(userNik))
            {
                var ownerNik = await GetItemOwnerNikAsync(itemType, itemId);
                if (!string.IsNullOrEmpty(ownerNik) && ownerNik != userNik)
                {
                    _context.Notifications.Add(new Notification
                    {
                        RecipientNik = ownerNik,
                        Title = $"{userName} mengomentari postingan Anda",
                        Message = $"{userName} berkomentar: \"{(text.Length > 80 ? text.Substring(0, 80) + "..." : text)}\"",
                        Url = "/Timeline/Index",
                        NotifType = "timeline_comment"
                    });
                }
            }

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

        /// <summary>Ambil NIK pemilik item berdasarkan ItemType dan ItemId</summary>
        private async Task<string?> GetItemOwnerNikAsync(string itemType, int itemId)
        {
            return itemType switch
            {
                "Hazard"     => (await _context.HazardReports.Where(h => h.Id == itemId && !h.IsDeleted).Select(h => h.Nik).FirstOrDefaultAsync()),
                "Inspection" => (await _context.Inspections.Where(i => i.Id == itemId && !i.IsDeleted).Select(i => i.Nik).FirstOrDefaultAsync()),
                "P5m"        => (await _context.P5ms.Where(p => p.Id == itemId && !p.IsDeleted).Select(p => p.Nik).FirstOrDefaultAsync()),
                "SafetyTalk" => (await _context.SafetyTalks.Where(s => s.Id == itemId && !s.IsDeleted).Select(s => s.Nik).FirstOrDefaultAsync()),
                _            => null
            };
        }
    }
}
