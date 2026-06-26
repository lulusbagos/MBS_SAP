using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class ApiController : Controller
    {
        private readonly AppDbContext _context;

        public ApiController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> SearchEmployee(string query, bool lintasPerusahaan = false)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            {
                return Json(new object[] { });
            }

            query = query.ToLower();
            
            int? userCompanyId = null;
            if (!lintasPerusahaan)
            {
                var compIdClaim = User.FindFirst("CompanyId")?.Value;
                if (int.TryParse(compIdClaim, out int cid) && cid > 0)
                {
                    userCompanyId = cid;
                }
            }

            // Perform search on Database Views (vw_karyawan, vw_personal, etc)
            var employeesQuery = from k in _context.Karyawans
                                 join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                 join j in _context.Jabatans on k.IdJabatan equals j.JabatanId into jg
                                 from j in jg.DefaultIfEmpty()
                                 join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                 from d in dg.DefaultIfEmpty()
                                 join c in _context.Perusahaans on k.IdPerusahaan equals c.PerusahaanId into cg
                                 from c in cg.DefaultIfEmpty()
                                 where k.StatusAktif == true &&
                                       (p.NamaLengkap.ToLower().Contains(query) || k.NoNik.ToLower().Contains(query))
                                 select new
                                 {
                                     Nik = k.NoNik,
                                     Nama = p.NamaLengkap,
                                     Departemen = d.NamaDepartemen,
                                     Jabatan = j.NamaJabatan,
                                     Perusahaan = c.NamaPerusahaan,
                                     IdPerusahaan = k.IdPerusahaan
                                 };

            if (userCompanyId.HasValue)
            {
                employeesQuery = employeesQuery.Where(e => e.IdPerusahaan == userCompanyId.Value);
            }

            var employees = await employeesQuery.Take(15).ToListAsync();

            return Json(employees);
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var nik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(nik)) return Json(new object[] { });

            var notifs = await _context.Notifications
                .Where(n => n.RecipientNik == nik)
                .OrderByDescending(n => n.CreatedAt)
                .Take(20)
                .Select(n => new {
                    id = n.Id,
                    title = n.Title,
                    message = n.Message,
                    url = n.Url,
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt.ToString("o")
                })
                .ToListAsync();

            return Json(notifs);
        }

        [HttpPost]
        public async Task<IActionResult> MarkNotificationRead(int id)
        {
            var nik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientNik == nik);
            if (notif != null)
            {
                notif.IsRead = true;
                await _context.SaveChangesAsync();
                return Ok();
            }
            return NotFound();
        }
        
        [HttpPost]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            var nik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var unreadNotifs = await _context.Notifications.Where(n => n.RecipientNik == nik && !n.IsRead).ToListAsync();
            
            if (unreadNotifs.Any())
            {
                foreach(var n in unreadNotifs) n.IsRead = true;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> ReassignPja([FromBody] ReassignRequest req)
        {
            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userName = User.Identity?.Name ?? "Sistem";

            if (req.ItemType == "Hazard")
            {
                var hazard = await _context.HazardReports.FirstOrDefaultAsync(h => h.Id == req.ItemId);
                if (hazard != null)
                {
                    hazard.NikPja = req.NewNik;
                    hazard.Pja = req.NewNama;
                    hazard.DepartemenPja = req.NewDepartemen;
                    
                    var notif = new Notification
                    {
                        RecipientNik = req.NewNik,
                        Title = "Pengalihan Hazard",
                        Message = $"Laporan Hazard di {hazard.Lokasi ?? hazard.Area} telah dialihkan kepada Anda oleh {userName}.",
                        Url = "/Hazard/Index"
                    };
                    _context.Notifications.Add(notif);
                    await _context.SaveChangesAsync();
                    return Ok();
                }
            }
            else if (req.ItemType == "ActionPlan")
            {
                var action = await _context.ActionPlans.FirstOrDefaultAsync(a => a.Id == req.ItemId);
                if (action != null)
                {
                    // Track reassignment history
                    action.ReassignedFrom = action.Pja; // nama PJA lama
                    action.ReassignedTo = req.NewNama;   // nama PJA baru
                    action.ReassignedAt = DateTime.Now;

                    action.NikPja = req.NewNik;
                    action.Pja = req.NewNama;
                    action.DepartemenPja = req.NewDepartemen;
                    
                    var notif = new Notification
                    {
                        RecipientNik = req.NewNik,
                        Title = "Pengalihan Action Plan",
                        Message = $"Action Plan untuk {action.KategoriTemuan} di {action.Lokasi ?? action.Area} telah dialihkan kepada Anda oleh {userName}.",
                        Url = "/ActionPlan/Index"
                    };
                    _context.Notifications.Add(notif);
                    await _context.SaveChangesAsync();
                    return Ok();
                }
            }

            return BadRequest("Item not found");
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetAreas()
        {
            var compIdClaim = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(compIdClaim, out int cid) && cid > 0)
            {
                var areas = await _context.MasterAreas
                    .Where(a => a.PerusahaanId == cid)
                    .OrderBy(a => a.NamaArea)
                    .Select(a => new { id = a.Id, namaArea = a.NamaArea })
                    .ToListAsync();
                return Json(areas);
            }
            return Json(new object[] { });
        }

        [HttpPost]
        public async Task<IActionResult> AddArea(string namaArea)
        {
            if (string.IsNullOrWhiteSpace(namaArea)) return BadRequest("Nama area tidak boleh kosong.");
            namaArea = namaArea.Trim().ToUpper();

            var compIdClaim = User.FindFirst("CompanyId")?.Value;
            if (!int.TryParse(compIdClaim, out int cid) || cid <= 0)
            {
                return Unauthorized("Anda tidak memiliki akses perusahaan.");
            }

            var existingAreas = await _context.MasterAreas
                .Where(a => a.PerusahaanId == cid)
                .Select(a => a.NamaArea)
                .ToListAsync();

            // Check exact match
            if (existingAreas.Any(a => a == namaArea))
            {
                return BadRequest($"Area '{namaArea}' sudah ada.");
            }

            // Check 60% similarity
            foreach (var existing in existingAreas)
            {
                double similarity = MBS_SAP.Utils.StringSimilarity.CalculateSimilarity(namaArea, existing);
                if (similarity >= 60.0)
                {
                    return BadRequest($"Area ditolak karena mirip ({similarity:F0}%) dengan area yang sudah ada: '{existing}'.");
                }
            }

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0000";
            var userName = User.FindFirst("FullName")?.Value ?? "Unknown";

            var newArea = new MasterArea
            {
                NamaArea = namaArea,
                PerusahaanId = cid,
                CreatedByNik = userNik,
                CreatedByName = userName,
                CreatedAt = System.DateTime.Now
            };

            _context.MasterAreas.Add(newArea);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Area berhasil ditambahkan.", data = new { id = newArea.Id, namaArea = newArea.NamaArea } });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteArea(int id, string passcode)
        {
            if (passcode != "Indexim.1995")
            {
                return BadRequest("Passcode salah!");
            }

            var compIdClaim = User.FindFirst("CompanyId")?.Value;
            if (!int.TryParse(compIdClaim, out int cid) || cid <= 0)
            {
                return Unauthorized("Anda tidak memiliki akses perusahaan.");
            }

            var area = await _context.MasterAreas.FirstOrDefaultAsync(a => a.Id == id && a.PerusahaanId == cid);
            if (area == null)
            {
                return NotFound("Area tidak ditemukan.");
            }

            _context.MasterAreas.Remove(area);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Area berhasil dihapus." });
        }
    }

    public class ReassignRequest
    {
        public int ItemId { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string NewNik { get; set; } = string.Empty;
        public string NewNama { get; set; } = string.Empty;
        public string NewDepartemen { get; set; } = string.Empty;
    }
}
