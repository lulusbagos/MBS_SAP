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
                                       (p.NamaLengkap.ToLower().Contains(query) || 
                                        k.NoNik.ToLower().Contains(query) || 
                                        (c != null && c.NamaPerusahaan != null && c.NamaPerusahaan.ToLower().Contains(query)) ||
                                        (c != null && c.KodePerusahaan != null && c.KodePerusahaan.ToLower().Contains(query)))
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
        public async Task<IActionResult> GetPjaReference(bool lintasPerusahaan = false)
        {
            int? userCompanyId = null;
            if (!lintasPerusahaan)
            {
                var compIdClaim = User.FindFirst("CompanyId")?.Value;
                if (int.TryParse(compIdClaim, out int cid) && cid > 0)
                {
                    userCompanyId = cid;
                }
            }

            var refs = new List<PjaCompanyRef>();

            // Source list sesuai kebutuhan: tbl_m_perusahaan (status aktif + PJO)
            var conn = _context.Database.GetDbConnection();
            bool wasClosed = conn.State == System.Data.ConnectionState.Closed;
            if (wasClosed)
            {
                await conn.OpenAsync();
            }
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT id, nama_perusahaan, pjo, id_pjo
FROM [ONE_DB_MITRA].[dbo].[tbl_m_perusahaan]
WHERE status_aktif = '1'
" + (userCompanyId.HasValue ? " AND id = @companyId" : "") + @"
ORDER BY nama_perusahaan";

                if (userCompanyId.HasValue)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@companyId";
                    p.Value = userCompanyId.Value;
                    cmd.Parameters.Add(p);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    refs.Add(new PjaCompanyRef
                    {
                        PerusahaanId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        NamaPerusahaan = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        Pjo = reader.IsDBNull(2) ? null : reader.GetString(2),
                        IdPjo = reader.IsDBNull(3) ? null : reader.GetInt32(3)
                    });
                }
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync();
                }
            }

            var result = new List<object>();
            
            foreach (var item in refs)
            {
                var pjoName = (item.Pjo ?? string.Empty).Trim();

                // 1. Selalu tambahkan Perusahaan sebagai opsi PJA langsung
                result.Add(new
                {
                    nik = $"COMPANY:{item.PerusahaanId}",
                    nama = item.NamaPerusahaan,
                    departemen = "PERUSAHAAN",
                    jabatan = !string.IsNullOrEmpty(pjoName) ? $"PJO: {pjoName}" : "PJO BELUM TERDAFTAR",
                    perusahaan = item.NamaPerusahaan,
                    companyId = item.PerusahaanId,
                    companyOnly = true,
                    source = "tbl_m_perusahaan (Company PJA)"
                });

                // 2. Jika ada PJO, tambahkan juga PJO sebagai opsi individu
                if (item.IdPjo.HasValue && item.IdPjo.Value > 0)
                {
                    var mappedById = await (from k in _context.Karyawans
                                            join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                            join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                            from d in dg.DefaultIfEmpty()
                                            where k.StatusAktif == true
                                                  && k.IdKaryawan == item.IdPjo.Value
                                            select new
                                            {
                                                nik = k.NoNik,
                                                nama = p.NamaLengkap,
                                                departemen = d != null ? d.NamaDepartemen : "GENERAL",
                                                jabatan = "PJO (INDIVIDU)",
                                                perusahaan = item.NamaPerusahaan,
                                                companyId = item.PerusahaanId,
                                                companyOnly = false,
                                                source = "tbl_m_perusahaan.id_pjo"
                                            }).FirstOrDefaultAsync();

                    if (mappedById != null)
                    {
                        result.Add(mappedById);
                    }
                }

                if (!string.IsNullOrEmpty(pjoName))
                {
                    var mapped = await (from k in _context.Karyawans
                                        join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                        join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                        from d in dg.DefaultIfEmpty()
                                        where k.StatusAktif == true
                                              && k.IdPerusahaan == item.PerusahaanId
                                              && p.NamaLengkap.ToLower() == pjoName.ToLower()
                                        select new
                                        {
                                            nik = k.NoNik,
                                            nama = p.NamaLengkap,
                                            departemen = d != null ? d.NamaDepartemen : "GENERAL",
                                            jabatan = "PJO (INDIVIDU)",
                                            perusahaan = item.NamaPerusahaan,
                                            companyId = item.PerusahaanId,
                                            companyOnly = false,
                                            source = "tbl_m_perusahaan.pjo"
                                        }).FirstOrDefaultAsync();

                    if (mapped == null)
                    {
                        mapped = await (from k in _context.Karyawans
                                        join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                        join d in _context.Departemens on k.IdDepartemen equals d.DepartemenId into dg
                                        from d in dg.DefaultIfEmpty()
                                        where k.StatusAktif == true
                                              && p.NamaLengkap.ToLower() == pjoName.ToLower()
                                        select new
                                        {
                                            nik = k.NoNik,
                                            nama = p.NamaLengkap,
                                            departemen = d != null ? d.NamaDepartemen : "GENERAL",
                                            jabatan = "PJO (INDIVIDU)",
                                            perusahaan = item.NamaPerusahaan,
                                            companyId = item.PerusahaanId,
                                            companyOnly = false,
                                            source = "tbl_m_perusahaan.pjo.global"
                                        }).FirstOrDefaultAsync();
                    }

                    if (mapped != null)
                    {
                        result.Add(mapped);
                    }
                }
            }

            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var nik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(nik)) return Json(new object[] { });

            var notifs = await _context.Notifications
                .Where(n => n.RecipientNik == nik)
                .OrderByDescending(n => n.CreatedAt)
                .Take(30)
                .Select(n => new {
                    id = n.Id,
                    title = n.Title,
                    message = n.Message,
                    url = n.Url,
                    isRead = n.IsRead,
                    notifType = n.NotifType ?? "general",
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

            if (req == null || req.ItemId <= 0 || string.IsNullOrWhiteSpace(req.ItemType) || string.IsNullOrWhiteSpace(req.NewNama))
            {
                return BadRequest("Data pengalihan tidak valid.");
            }

            var newNik = req.NewNik?.Trim();
            var newNama = req.NewNama?.Trim().ToUpper();
            var newDept = req.NewDepartemen?.Trim().ToUpper();
            var itemType = req.ItemType.Trim();
            var isCompanyTarget = TryParseCompanyNikToken(newNik, out var targetCompanyId);

            if (itemType == "Hazard")
            {
                var hazard = await _context.HazardReports.FirstOrDefaultAsync(h => h.Id == req.ItemId);
                if (hazard != null)
                {
                    if (isCompanyTarget)
                    {
                        hazard.NikPja = null;
                        hazard.Pja = newNama;
                        hazard.DepartemenPja = "PERUSAHAAN";
                        hazard.PerusahaanId = targetCompanyId;
                    }
                    else
                    {
                        hazard.NikPja = newNik;
                        hazard.Pja = newNama;
                        hazard.DepartemenPja = newDept;
                    }

                    var actionPlanItemSap = $"hazard:{hazard.Id}";
                    var relatedAction = await _context.ActionPlans.FirstOrDefaultAsync(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted);
                    if (relatedAction != null)
                    {
                        relatedAction.ReassignedFrom = relatedAction.Pja;
                        relatedAction.ReassignedTo = newNama;
                        relatedAction.ReassignedAt = DateTime.Now;
                        relatedAction.ReassignNote = req.Keterangan;

                        relatedAction.Pja = hazard.Pja;
                        relatedAction.NikPja = hazard.NikPja;
                        relatedAction.DepartemenPja = hazard.DepartemenPja;
                        relatedAction.PerusahaanId = hazard.PerusahaanId;
                    }

                    if (!string.IsNullOrWhiteSpace(hazard.NikPja))
                    {
                        _context.Notifications.Add(new Notification
                        {
                            RecipientNik = hazard.NikPja,
                            Title = "Pengalihan Hazard",
                            Message = $"Laporan Hazard di {hazard.Lokasi ?? hazard.Area} telah dialihkan kepada Anda oleh {userName}.",
                            Url = "/Hazard/Index",
                            NotifType = "hazard_reassign"
                        });
                    }
                    else if (hazard.PerusahaanId.HasValue)
                    {
                        await CreateCompanyBroadcastNotificationAsync(
                            hazard.PerusahaanId.Value,
                            "Pengalihan Hazard",
                            $"Laporan Hazard di {hazard.Lokasi ?? hazard.Area} dialihkan ke penanggung jawab perusahaan oleh {userName}.",
                            "/Hazard/Index",
                            "hazard_reassign");
                    }

                    await _context.SaveChangesAsync();
                    return Ok();
                }
            }
            else if (itemType == "ActionPlan")
            {
                var action = await _context.ActionPlans.FirstOrDefaultAsync(a => a.Id == req.ItemId);
                if (action != null)
                {
                    // Track reassignment history
                    action.ReassignedFrom = action.Pja; // nama PJA lama
                    action.ReassignedTo = newNama;   // nama PJA baru
                    action.ReassignedAt = DateTime.Now;
                    action.ReassignNote = req.Keterangan;

                    if (isCompanyTarget)
                    {
                        action.NikPja = null;
                        action.Pja = newNama;
                        action.DepartemenPja = "PERUSAHAAN";
                        action.PerusahaanId = targetCompanyId;
                    }
                    else
                    {
                        action.NikPja = newNik;
                        action.Pja = newNama;
                        action.DepartemenPja = newDept;
                    }

                    if (action.ItemSap != null && action.ItemSap.StartsWith("hazard:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(action.ItemSap.Substring("hazard:".Length), out int hazardId))
                        {
                            var hazard = await _context.HazardReports.FirstOrDefaultAsync(h => h.Id == hazardId && !h.IsDeleted);
                            if (hazard != null)
                            {
                                hazard.NikPja = action.NikPja;
                                hazard.Pja = action.Pja;
                                hazard.DepartemenPja = action.DepartemenPja;
                                hazard.PerusahaanId = action.PerusahaanId;
                            }
                        }
                    }
                    else if (action.ItemSap != null && action.ItemSap.StartsWith("inspection:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(action.ItemSap.Substring("inspection:".Length), out int inspectionId))
                        {
                            var inspection = await _context.Inspections.FirstOrDefaultAsync(i => i.Id == inspectionId && !i.IsDeleted);
                            if (inspection != null)
                            {
                                inspection.NikPja = action.NikPja;
                                inspection.Pja = action.Pja;
                                inspection.DepartemenPja = action.DepartemenPja;
                                inspection.PerusahaanId = action.PerusahaanId;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(action.NikPja))
                    {
                        _context.Notifications.Add(new Notification
                        {
                            RecipientNik = action.NikPja,
                            Title = "Pengalihan Action Plan",
                            Message = $"Action Plan untuk {action.KategoriTemuan} di {action.Lokasi ?? action.Area} telah dialihkan kepada Anda oleh {userName}.",
                            Url = "/ActionPlan/Index",
                            NotifType = "actionplan_reassign"
                        });
                    }
                    else if (action.PerusahaanId.HasValue)
                    {
                        await CreateCompanyBroadcastNotificationAsync(
                            action.PerusahaanId.Value,
                            "Pengalihan Action Plan",
                            $"Action Plan untuk {action.KategoriTemuan} di {action.Lokasi ?? action.Area} dialihkan ke penanggung jawab perusahaan oleh {userName}.",
                            "/ActionPlan/Index",
                            "actionplan_reassign");
                    }

                    await _context.SaveChangesAsync();
                    return Ok();
                }
            }
            else if (itemType == "Inspection")
            {
                var inspection = await _context.Inspections.FirstOrDefaultAsync(i => i.Id == req.ItemId);
                if (inspection != null)
                {
                    if (isCompanyTarget)
                    {
                        inspection.NikPja = null;
                        inspection.Pja = newNama;
                        inspection.DepartemenPja = "PERUSAHAAN";
                        inspection.PerusahaanId = targetCompanyId;
                    }
                    else
                    {
                        inspection.NikPja = newNik;
                        inspection.Pja = newNama;
                        inspection.DepartemenPja = newDept;
                    }

                    // Sync related ActionPlans
                    var actionPlanItemSap = $"inspection:{inspection.Id}";
                    var relatedActions = await _context.ActionPlans
                        .Where(a => a.ItemSap == actionPlanItemSap && !a.IsDeleted)
                        .ToListAsync();
                    
                    foreach (var action in relatedActions)
                    {
                        action.ReassignedFrom = action.Pja;
                        action.ReassignedTo = newNama;
                        action.ReassignedAt = DateTime.Now;
                        action.ReassignNote = req.Keterangan;

                        action.Pja = inspection.Pja;
                        action.NikPja = inspection.NikPja;
                        action.DepartemenPja = inspection.DepartemenPja;
                        action.PerusahaanId = inspection.PerusahaanId;
                    }

                    if (!string.IsNullOrWhiteSpace(inspection.NikPja))
                    {
                        _context.Notifications.Add(new Notification
                        {
                            RecipientNik = inspection.NikPja,
                            Title = "Pengalihan Inspeksi",
                            Message = $"Penugasan Inspeksi di {inspection.Lokasi ?? inspection.Area} telah dialihkan kepada Anda oleh {userName}.",
                            Url = "/Inspection/Index",
                            NotifType = "inspection_reassign"
                        });
                    }
                    else if (inspection.PerusahaanId.HasValue)
                    {
                        await CreateCompanyBroadcastNotificationAsync(
                            inspection.PerusahaanId.Value,
                            "Pengalihan Inspeksi",
                            $"Penugasan Inspeksi di {inspection.Lokasi ?? inspection.Area} dialihkan ke penanggung jawab perusahaan oleh {userName}.",
                            "/Inspection/Index",
                            "inspection_reassign");
                    }

                    await _context.SaveChangesAsync();
                    return Ok();
                }
            }

            return BadRequest("Item not found");
        }

        private static bool TryParseCompanyNikToken(string? nikToken, out int perusahaanId)
        {
            perusahaanId = 0;
            if (string.IsNullOrWhiteSpace(nikToken))
            {
                return false;
            }

            const string prefix = "COMPANY:";
            if (!nikToken.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var raw = nikToken.Substring(prefix.Length).Trim();
            return int.TryParse(raw, out perusahaanId) && perusahaanId > 0;
        }

        private async Task<int> CreateCompanyBroadcastNotificationAsync(int perusahaanId, string title, string message, string url, string notifType = "general")
        {
            var recipientNiks = await GetCompanyNotificationRecipientsAsync(perusahaanId);

            if (recipientNiks.Count == 0)
            {
                return 0;
            }

            var notifications = new List<Notification>();
            foreach (var nik in recipientNiks)
            {
                notifications.Add(new Notification
                {
                    RecipientNik = nik,
                    Title = title,
                    Message = message,
                    Url = url,
                    NotifType = notifType
                });
            }

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();
            return notifications.Count;
        }

        private async Task<List<string>> GetCompanyNotificationRecipientsAsync(int perusahaanId)
        {
            int? idPjo = null;
            string? pjoName = null;
            var conn = _context.Database.GetDbConnection();
            bool wasClosed = conn.State == System.Data.ConnectionState.Closed;
            if (wasClosed)
            {
                await conn.OpenAsync();
            }
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id_pjo, pjo FROM [ONE_DB_MITRA].[dbo].[tbl_m_perusahaan] WHERE id = @companyId";
                var p = cmd.CreateParameter();
                p.ParameterName = "@companyId";
                p.Value = perusahaanId;
                cmd.Parameters.Add(p);
                
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0)) idPjo = reader.GetInt32(0);
                    if (!reader.IsDBNull(1)) pjoName = reader.GetString(1)?.Trim();
                }
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync();
                }
            }

            string? pjoNik = null;
            
            // 1. Coba cari berdasarkan ID PJO (Prioritas Utama)
            if (idPjo.HasValue && idPjo.Value > 0)
            {
                pjoNik = await _context.Karyawans
                    .Where(k => k.StatusAktif && k.IdKaryawan == idPjo.Value)
                    .Select(k => k.NoNik)
                    .FirstOrDefaultAsync();
            }

            // 2. Jika ID PJO tidak menghasilkan/null, tapi nama PJO terisi, coba cari berdasarkan Nama (Pencarian Kasus Sensitif di Perusahaan yang Sama)
            if (string.IsNullOrEmpty(pjoNik) && !string.IsNullOrEmpty(pjoName))
            {
                pjoNik = await (from k in _context.Karyawans
                                join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                where k.StatusAktif == true 
                                      && k.IdPerusahaan == perusahaanId 
                                      && p.NamaLengkap.ToLower() == pjoName.ToLower()
                                select k.NoNik).FirstOrDefaultAsync();

                // 3. Fallback: Cari secara global di semua perusahaan jika nama unik
                if (string.IsNullOrEmpty(pjoNik))
                {
                    pjoNik = await (from k in _context.Karyawans
                                    join p in _context.Personals on k.IdPersonal equals p.IdPersonal
                                    where k.StatusAktif == true 
                                          && p.NamaLengkap.ToLower() == pjoName.ToLower()
                                    select k.NoNik).FirstOrDefaultAsync();
                }
            }

            var recipientNiks = new List<string>();

            if (!string.IsNullOrEmpty(pjoNik))
            {
                recipientNiks.Add(pjoNik);
            }
            else
            {
                recipientNiks = await _context.AppUsers
                    .Where(a => a.IdPerusahaan == perusahaanId && !string.IsNullOrEmpty(a.Nik))
                    .Select(a => a.Nik)
                    .Distinct()
                    .ToListAsync();

                if (recipientNiks.Count == 0)
                {
                    recipientNiks = await _context.Karyawans
                        .Where(k => k.StatusAktif && k.IdPerusahaan == perusahaanId)
                        .Select(k => k.NoNik)
                        .Distinct()
                        .ToListAsync();
                }
            }

            return recipientNiks;
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

        [HttpGet]
        public async Task<IActionResult> GetBenchmarks(string areaUtama)
        {
            var compIdClaim = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(compIdClaim, out int cid) && cid > 0 && !string.IsNullOrWhiteSpace(areaUtama))
            {
                var benchmarks = await _context.Benchmarks
                    .Where(b => b.PerusahaanId == cid && b.AreaUtama == areaUtama)
                    .OrderBy(b => b.NamaBenchmark)
                    .Select(b => new { id = b.Id, namaBenchmark = b.NamaBenchmark })
                    .ToListAsync();
                return Json(benchmarks);
            }
            return Json(new object[] { });
        }

        [HttpPost]
        public async Task<IActionResult> AddBenchmark(string areaUtama, string namaBenchmark)
        {
            if (string.IsNullOrWhiteSpace(areaUtama)) return BadRequest("Area utama tidak boleh kosong.");
            if (string.IsNullOrWhiteSpace(namaBenchmark)) return BadRequest("Nama benchmark tidak boleh kosong.");
            
            namaBenchmark = namaBenchmark.Trim().ToUpper();

            var compIdClaim = User.FindFirst("CompanyId")?.Value;
            if (!int.TryParse(compIdClaim, out int cid) || cid <= 0)
            {
                return Unauthorized("Anda tidak memiliki akses perusahaan.");
            }

            var existingBenchmarks = await _context.Benchmarks
                .Where(b => b.PerusahaanId == cid && b.AreaUtama == areaUtama)
                .Select(b => b.NamaBenchmark)
                .ToListAsync();

            if (existingBenchmarks.Any(b => b == namaBenchmark))
            {
                return BadRequest($"Benchmark '{namaBenchmark}' sudah ada di area ini.");
            }

            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0000";
            var userName = User.FindFirst("FullName")?.Value ?? "Unknown";

            var newBenchmark = new MasterBenchmark
            {
                AreaUtama = areaUtama,
                NamaBenchmark = namaBenchmark,
                PerusahaanId = cid,
                CreatedByNik = userNik,
                CreatedByName = userName,
                CreatedAt = System.DateTime.Now
            };

            _context.Benchmarks.Add(newBenchmark);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Benchmark berhasil ditambahkan.", data = new { id = newBenchmark.Id, namaBenchmark = newBenchmark.NamaBenchmark } });
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

        [HttpPost]
        public async Task<IActionResult> SaveRoster([FromBody] RosterSaveRequest req)
        {
            var userNik = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userNik))
            {
                return Unauthorized("NIK tidak ditemukan.");
            }

            // Allowed for all companies during testing / usage

            if (req == null || 
                !DateTime.TryParse(req.AwalDinas, out DateTime awalDinas) ||
                !DateTime.TryParse(req.AkhirDinas, out DateTime akhirDinas) ||
                !DateTime.TryParse(req.AwalCuti, out DateTime awalCuti) ||
                !DateTime.TryParse(req.AkhirCuti, out DateTime akhirCuti))
            {
                return BadRequest("Format tanggal tidak valid.");
            }

            // Validasi urutan tanggal
            if (awalDinas > akhirDinas)
            {
                return BadRequest("Tanggal awal dinas tidak boleh lebih besar dari akhir dinas.");
            }
            if (akhirDinas >= awalCuti)
            {
                return BadRequest("Tanggal akhir dinas harus lebih kecil dari awal cuti.");
            }
            if (awalCuti > akhirCuti)
            {
                return BadRequest("Tanggal awal cuti tidak boleh lebih besar dari akhir cuti.");
            }

            // Validasi durasi terhadap ONE_DB_MITRA.dbo.vw_m_roster jika ada (dengan toleransi pergeseran)
            var mitraRoster = await _context.MitraRosters.FirstOrDefaultAsync(m => m.NoNik == userNik);
            if (mitraRoster != null && mitraRoster.HariOnsite.HasValue && mitraRoster.HariOffsite.HasValue)
            {
                int expectedOnsite = mitraRoster.HariOnsite.Value;
                int expectedOffsite = mitraRoster.HariOffsite.Value;

                int actualOnsite = (akhirDinas - awalDinas).Days + 1;
                int actualOffsite = (akhirCuti - awalCuti).Days + 1;

                // Toleransi dinas: +/- 14 hari (2 minggu)
                if (Math.Abs(actualOnsite - expectedOnsite) > 14)
                {
                    return BadRequest($"Durasi dinas (onsite) Anda ({actualOnsite} hari) menyimpang lebih dari 14 hari dari ketentuan database OneEv ({expectedOnsite} hari).");
                }

                // Toleransi cuti: +/- 7 hari (1 minggu)
                if (Math.Abs(actualOffsite - expectedOffsite) > 7)
                {
                    return BadRequest($"Durasi cuti (offsite) Anda ({actualOffsite} hari) menyimpang lebih dari 7 hari dari ketentuan database OneEv ({expectedOffsite} hari).");
                }

                if ((awalCuti - akhirDinas).Days != 1)
                {
                    return BadRequest("Masa cuti harus dimulai tepat 1 hari setelah masa dinas aktif berakhir.");
                }
            }

            // Cari roster terbaru dari user
            var latestRoster = await _context.Rosters
                .Where(r => r.Nik == userNik)
                .OrderByDescending(r => r.AkhirCuti)
                .FirstOrDefaultAsync();

            // Validasi fleksibilitas pergeseran jadwal (maju/mundur maks 14 hari / 2 minggu)
            int activeRosterId = (latestRoster != null && latestRoster.AkhirCuti >= DateTime.Today) ? latestRoster.Id : 0;
            var previousRoster = await _context.Rosters
                .Where(r => r.Nik == userNik && r.Id != activeRosterId)
                .OrderByDescending(r => r.AkhirCuti)
                .FirstOrDefaultAsync();

            if (previousRoster != null)
            {
                DateTime expectedStart = previousRoster.AkhirCuti.AddDays(1);
                int shiftDays = (awalDinas - expectedStart).Days;

                if (Math.Abs(shiftDays) > 14)
                {
                    string direction = shiftDays > 0 ? "maju" : "mundur";
                    return BadRequest($"Tanggal mulai dinas Anda hanya diperbolehkan maju/mundur maksimal 14 hari dari jadwal seharusnya ({expectedStart:dd MMM yyyy}). Anda mencoba {direction} {Math.Abs(shiftDays)} hari.");
                }
            }

            // Jika roster terakhir ada dan belum expired (atau kita mau update roster yang sedang berjalan),
            // kita update roster tersebut. Jika tidak, buat baru.
            if (latestRoster != null && latestRoster.AkhirCuti >= DateTime.Today)
            {
                latestRoster.AwalDinas = awalDinas;
                latestRoster.AkhirDinas = akhirDinas;
                latestRoster.AwalCuti = awalCuti;
                latestRoster.AkhirCuti = akhirCuti;
                latestRoster.UpdatedAt = DateTime.Now;
                _context.Rosters.Update(latestRoster);
            }
            else
            {
                var newRoster = new Roster
                {
                    Nik = userNik,
                    AwalDinas = awalDinas,
                    AkhirDinas = akhirDinas,
                    AwalCuti = awalCuti,
                    AkhirCuti = akhirCuti,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _context.Rosters.Add(newRoster);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Roster berhasil disimpan." });
        }
    }

    public class RosterSaveRequest
    {
        public string AwalDinas { get; set; } = string.Empty;
        public string AkhirDinas { get; set; } = string.Empty;
        public string AwalCuti { get; set; } = string.Empty;
        public string AkhirCuti { get; set; } = string.Empty;
    }

    public class ReassignRequest
    {
        public int ItemId { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string NewNik { get; set; } = string.Empty;
        public string NewNama { get; set; } = string.Empty;
        public string NewDepartemen { get; set; } = string.Empty;
        public string? Keterangan { get; set; }
    }

    public class PjaCompanyRef
    {
        public int PerusahaanId { get; set; }
        public string NamaPerusahaan { get; set; } = string.Empty;
        public string? Pjo { get; set; }
        public int? IdPjo { get; set; }
    }
}
