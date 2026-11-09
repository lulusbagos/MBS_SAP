using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class AreaPjaController : Controller
    {
        private readonly AppDbContext _context;

        public AreaPjaController(AppDbContext context)
        {
            _context = context;
        }

        private bool CheckIsAdmin()
        {
            return User.IsInRole("Admin") || CompanyHierarchyService.IsAdminUser(User);
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            ViewData["HeaderTitle"] = "Setting PJA Area Utama";
            ViewData["ActiveTab"] = "AreaPjaAdmin";

            // Quick summary statistics
            var totalArea = await _context.MasterAreas.CountAsync();
            var areaKhususCount = await _context.MasterAreas.CountAsync(a => !a.IsAllKaryawan);
            var areaAllCount = await _context.MasterAreas.CountAsync(a => a.IsAllKaryawan);
            var totalPjaAssigned = await _context.AreaPjaRelations.CountAsync();

            ViewBag.TotalArea = totalArea;
            ViewBag.AreaKhususCount = areaKhususCount;
            ViewBag.AreaAllCount = areaAllCount;
            ViewBag.TotalPjaAssigned = totalPjaAssigned;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAreasData([FromQuery] string? search, [FromQuery] string? filterType, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            var query = _context.MasterAreas
                .Include(a => a.PjaList)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var cleanSearch = search.Trim().ToLower();
                query = query.Where(a => a.NamaArea.ToLower().Contains(cleanSearch) ||
                                         a.PjaList.Any(p => p.NamaPja.ToLower().Contains(cleanSearch) || p.NikPja.ToLower().Contains(cleanSearch)));
            }

            if (!string.IsNullOrWhiteSpace(filterType))
            {
                if (filterType == "pja_khusus")
                {
                    query = query.Where(a => !a.IsAllKaryawan);
                }
                else if (filterType == "all_karyawan")
                {
                    query = query.Where(a => a.IsAllKaryawan);
                }
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (page < 1) page = 1;

            var items = await query
                .OrderBy(a => a.NamaArea)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.Id,
                    namaArea = a.NamaArea,
                    perusahaanId = a.PerusahaanId,
                    isAllKaryawan = a.IsAllKaryawan,
                    createdByNik = a.CreatedByNik,
                    createdByName = a.CreatedByName,
                    createdAt = a.CreatedAt.ToString("dd MMM yyyy HH:mm"),
                    updatedAt = a.UpdatedAt.HasValue ? a.UpdatedAt.Value.ToString("dd MMM yyyy HH:mm") : null,
                    updatedByName = a.UpdatedByName,
                    pjaCount = a.PjaList.Count,
                    pjas = a.PjaList.Select(p => new
                    {
                        id = p.Id,
                        nik = p.NikPja,
                        nama = p.NamaPja,
                        jabatan = p.JabatanPja ?? "-",
                        departemen = p.DepartemenPja ?? "-",
                        perusahaan = p.PerusahaanPja ?? "-",
                        isPicUtama = p.IsPicUtama
                    }).ToList()
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                data = items,
                totalItems,
                totalPages,
                currentPage = page
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetAreaDetail(int id)
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            var area = await _context.MasterAreas
                .Include(a => a.PjaList)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

            if (area == null)
            {
                return NotFound(new { success = false, message = "Area tidak ditemukan." });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    id = area.Id,
                    namaArea = area.NamaArea,
                    isAllKaryawan = area.IsAllKaryawan,
                    pjas = area.PjaList.Select(p => new
                    {
                        nik = p.NikPja,
                        nama = p.NamaPja,
                        jabatan = p.JabatanPja,
                        departemen = p.DepartemenPja,
                        perusahaan = p.PerusahaanPja,
                        isPicUtama = p.IsPicUtama
                    }).ToList()
                }
            });
        }

        public class SaveAreaPjaDto
        {
            public int? Id { get; set; }
            public string NamaArea { get; set; } = string.Empty;
            public bool IsAllKaryawan { get; set; } = true;
            public List<PjaItemDto> Pjas { get; set; } = new();
        }

        public class PjaItemDto
        {
            public string Nik { get; set; } = string.Empty;
            public string Nama { get; set; } = string.Empty;
            public string? Jabatan { get; set; }
            public string? Departemen { get; set; }
            public string? Perusahaan { get; set; }
            public bool IsPicUtama { get; set; } = false;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveArea([FromBody] SaveAreaPjaDto dto)
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.NamaArea))
            {
                return BadRequest(new { success = false, message = "Nama Area Utama wajib diisi." });
            }

            var cleanNama = dto.NamaArea.Trim().ToUpper();
            var curNik = User.FindFirst("Nik")?.Value ?? "ADMIN";
            var curNama = User.Identity?.Name ?? "Administrator";

            MasterArea? area;

            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                area = await _context.MasterAreas
                    .Include(a => a.PjaList)
                    .FirstOrDefaultAsync(a => a.Id == dto.Id.Value);

                if (area == null)
                {
                    return NotFound(new { success = false, message = "Data Area tidak ditemukan untuk diubah." });
                }

                // Check duplicate name on other record
                var isDup = await _context.MasterAreas
                    .AnyAsync(a => a.Id != dto.Id.Value && a.NamaArea.ToLower() == cleanNama.ToLower());
                if (isDup)
                {
                    return BadRequest(new { success = false, message = $"Area dengan nama '{cleanNama}' sudah terdaftar." });
                }

                area.NamaArea = cleanNama;
                area.IsAllKaryawan = dto.IsAllKaryawan;
                area.UpdatedAt = DateTime.Now;
                area.UpdatedByNik = curNik;
                area.UpdatedByName = curNama;
            }
            else
            {
                // Create New Area
                var isDup = await _context.MasterAreas
                    .AnyAsync(a => a.NamaArea.ToLower() == cleanNama.ToLower());
                if (isDup)
                {
                    return BadRequest(new { success = false, message = $"Area dengan nama '{cleanNama}' sudah ada. Silakan gunakan tombol Edit pada tabel." });
                }

                var cidClaim = User.FindFirst("CompanyId")?.Value;
                var cid = int.TryParse(cidClaim, out var c) ? c : 1;

                area = new MasterArea
                {
                    NamaArea = cleanNama,
                    PerusahaanId = cid,
                    IsAllKaryawan = dto.IsAllKaryawan,
                    CreatedByNik = curNik,
                    CreatedByName = curNama,
                    CreatedAt = DateTime.Now
                };

                _context.MasterAreas.Add(area);
                await _context.SaveChangesAsync();
            }

            // Sync PJAs in tbl_r_area_pja
            // Remove existing PJAs for this area
            var existingPjas = await _context.AreaPjaRelations
                .Where(p => p.AreaId == area.Id)
                .ToListAsync();

            if (existingPjas.Any())
            {
                _context.AreaPjaRelations.RemoveRange(existingPjas);
            }

            // If not all karyawan, or if specific PJAs provided, add them
            if (dto.Pjas != null && dto.Pjas.Count > 0)
            {
                var distinctPjas = dto.Pjas
                    .GroupBy(p => p.Nik.Trim())
                    .Select(g => g.First())
                    .ToList();

                foreach (var p in distinctPjas)
                {
                    if (string.IsNullOrWhiteSpace(p.Nik)) continue;

                    var rel = new AreaPjaRelation
                    {
                        AreaId = area.Id,
                        NamaArea = area.NamaArea,
                        NikPja = p.Nik.Trim(),
                        NamaPja = string.IsNullOrWhiteSpace(p.Nama) ? p.Nik.Trim() : p.Nama.Trim(),
                        JabatanPja = p.Jabatan?.Trim(),
                        DepartemenPja = p.Departemen?.Trim(),
                        PerusahaanPja = p.Perusahaan?.Trim(),
                        IsPicUtama = p.IsPicUtama,
                        CreatedAt = DateTime.Now,
                        CreatedBy = curNik
                    };
                    _context.AreaPjaRelations.Add(rel);
                }
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Area '{area.NamaArea}' berhasil disimpan dengan {dto.Pjas?.Count ?? 0} PJA terdaftar.",
                areaId = area.Id
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAllKaryawan(int id)
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            var area = await _context.MasterAreas.FindAsync(id);
            if (area == null)
            {
                return NotFound(new { success = false, message = "Area tidak ditemukan." });
            }

            area.IsAllKaryawan = !area.IsAllKaryawan;
            area.UpdatedAt = DateTime.Now;
            area.UpdatedByNik = User.FindFirst("Nik")?.Value ?? "ADMIN";
            area.UpdatedByName = User.Identity?.Name ?? "Administrator";

            await _context.SaveChangesAsync();

            var statusStr = area.IsAllKaryawan ? "Semua Karyawan (All Karyawan)" : "PJA Khusus";
            return Json(new { success = true, isAllKaryawan = area.IsAllKaryawan, message = $"Status area '{area.NamaArea}' diubah menjadi: {statusStr}" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteArea(int id)
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            var area = await _context.MasterAreas
                .Include(a => a.PjaList)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (area == null)
            {
                return NotFound(new { success = false, message = "Area tidak ditemukan." });
            }

            var nama = area.NamaArea;
            _context.MasterAreas.Remove(area);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Area '{nama}' dan penugasan PJA terkait berhasil dihapus." });
        }

        [HttpGet]
        public async Task<IActionResult> SearchEmployees(string? term)
        {
            if (!CheckIsAdmin())
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
            {
                return Json(new List<object>());
            }

            var q = term.Trim().ToLower();

            var employeesQuery = from k in _context.Karyawans.AsNoTracking()
                                 join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal
                                 join j in _context.Jabatans.AsNoTracking() on k.IdJabatan equals j.JabatanId into jg
                                 from j in jg.DefaultIfEmpty()
                                 join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into dg
                                 from d in dg.DefaultIfEmpty()
                                 join c in _context.Perusahaans.AsNoTracking() on k.IdPerusahaan equals c.PerusahaanId into cg
                                 from c in cg.DefaultIfEmpty()
                                 where k.StatusAktif == true &&
                                       (p.NamaLengkap.ToLower().Contains(q) ||
                                        k.NoNik.ToLower().Contains(q) ||
                                        (d != null && d.NamaDepartemen != null && d.NamaDepartemen.ToLower().Contains(q)) ||
                                        (c != null && c.NamaPerusahaan != null && c.NamaPerusahaan.ToLower().Contains(q)))
                                 select new
                                 {
                                     nik = k.NoNik,
                                     nama = p.NamaLengkap,
                                     jabatan = j != null ? j.NamaJabatan : "Staff",
                                     departemen = d != null ? d.NamaDepartemen : "GENERAL",
                                     perusahaan = c != null ? c.NamaPerusahaan : "PT INDEXIM COALINDO"
                                 };

            var result = await employeesQuery.Take(25).ToListAsync();
            return Json(result);
        }
    }
}
