using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class SapTargetController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CompanyHierarchyService _companyHierarchyService;

        public SapTargetController(AppDbContext context, CompanyHierarchyService companyHierarchyService)
        {
            _context = context;
            _companyHierarchyService = companyHierarchyService;
        }

        private bool HasAccess()
        {
            var isAdministrator = User.IsInRole("Administrator") || User.IsInRole("Admin");
            var jobTitle = User.FindFirst("JobTitle")?.Value ?? "";
            var department = User.FindFirst("Department")?.Value ?? "";
            var isSafety = jobTitle.Contains("Safety", System.StringComparison.OrdinalIgnoreCase) || 
                           department.Contains("Safety", System.StringComparison.OrdinalIgnoreCase) ||
                           department.Contains("HSE", System.StringComparison.OrdinalIgnoreCase);
            return isAdministrator || isSafety;
        }

        public async Task<IActionResult> Index()
        {
            if (!HasAccess())
            {
                return RedirectToAction("Index", "Home"); 
            }
            
            // Get user's company and children for the filter dropdown
            var userCompanyStr = User.FindFirst("CompanyId")?.Value;
            var accessibleCompanyIds = new List<int>();
            if (int.TryParse(userCompanyStr, out int userCompanyId))
            {
                accessibleCompanyIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(userCompanyId);
            }

            var companies = await _context.Perusahaans
                .Where(p => accessibleCompanyIds.Contains(p.PerusahaanId))
                .OrderBy(p => p.NamaPerusahaan)
                .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = p.PerusahaanId.ToString(),
                    Text = p.NamaPerusahaan
                })
                .ToListAsync();

            var deptsQuery = _context.Departemens.Where(d => d.NamaDepartemen != null);
            if (int.TryParse(userCompanyStr, out int parsedCompanyId))
            {
                deptsQuery = deptsQuery.Where(d => d.IdPerusahaan == parsedCompanyId);
            }
            else
            {
                deptsQuery = deptsQuery.Where(d => d.IdPerusahaan != null && accessibleCompanyIds.Contains(d.IdPerusahaan.Value));
            }

            var depts = await deptsQuery
                .Select(d => d.NamaDepartemen)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();

            var departments = depts.Select(n => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = n,
                Text = n
            }).ToList();

            ViewData["Companies"] = companies;
            ViewData["Departments"] = departments;
            ViewData["UserCompanyId"] = userCompanyStr;
            ViewData["ActiveTab"] = "SapTarget";
            ViewData["HeaderTitle"] = "SAP Target Override";
            
            return View();
        }
        
        [HttpPost]
        public async Task<IActionResult> GetEmployeeTargetsData()
        {
            if (!HasAccess()) return Unauthorized();

            var draw = Request.Form["draw"].FirstOrDefault();
            var start = Request.Form["start"].FirstOrDefault();
            var length = Request.Form["length"].FirstOrDefault();
            var searchValue = Request.Form["search[value]"].FirstOrDefault();
            var perusahaanIdFilter = Request.Form["perusahaanId"].FirstOrDefault();
            var departemenFilter = Request.Form["departemen"].FirstOrDefault();
            var customSearch = Request.Form["customSearch"].FirstOrDefault();

            if (string.IsNullOrEmpty(searchValue) && !string.IsNullOrEmpty(customSearch))
            {
                searchValue = customSearch;
            }

            int pageSize = length != null ? System.Convert.ToInt32(length) : 10;
            int skip = start != null ? System.Convert.ToInt32(start) : 0;

            // Handle security / data scoping
            var userCompanyStr = User.FindFirst("CompanyId")?.Value;
            var accessibleCompanyIds = new List<int>();
            if (int.TryParse(userCompanyStr, out int userCompanyId))
            {
                accessibleCompanyIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(userCompanyId);
            }

            var usersQuery = from k in _context.Karyawans.AsNoTracking()
                             join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal
                             join c in _context.Perusahaans.AsNoTracking() on k.IdPerusahaan equals c.PerusahaanId
                             join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into deptGroup
                             from d in deptGroup.DefaultIfEmpty()
                             where k.StatusAktif && accessibleCompanyIds.Contains(k.IdPerusahaan)
                             select new {
                                 KaryawanId = (int?)k.IdKaryawan,
                                 Nik = k.NoNik,
                                 Nama = p.NamaLengkap,
                                 IdPerusahaan = k.IdPerusahaan,
                                 Perusahaan = c.NamaPerusahaan,
                                 Departemen = d != null ? d.NamaDepartemen : "-"
                             };

            if (!string.IsNullOrEmpty(perusahaanIdFilter) && int.TryParse(perusahaanIdFilter, out int filterCompanyId))
            {
                usersQuery = usersQuery.Where(u => u.IdPerusahaan == filterCompanyId);
            }

            if (!string.IsNullOrEmpty(departemenFilter))
            {
                usersQuery = usersQuery.Where(u => u.Departemen == departemenFilter);
            }

            if (!string.IsNullOrEmpty(searchValue))
            {
                searchValue = searchValue.ToLower();
                usersQuery = usersQuery.Where(u => 
                    u.Nama.ToLower().Contains(searchValue) || 
                    u.Nik.ToLower().Contains(searchValue));
            }

            int recordsTotal = await usersQuery.CountAsync();

            var users = await usersQuery
                .OrderBy(u => u.Nama)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            var karyawanIds = users.Where(u => u.KaryawanId.HasValue).Select(u => u.KaryawanId!.Value).ToList();

            var targets = await _context.Set<KaryawanJabatanMappingPreviewView>()
                .Where(t => karyawanIds.Contains(t.KaryawanId))
                .ToDictionaryAsync(t => t.KaryawanId);

            var data = users.Select(u => {
                var kId = u.KaryawanId ?? 0;
                var tgt = targets.ContainsKey(kId) ? targets[kId] : null;
                return new {
                    karyawanId = u.KaryawanId,
                    nik = u.Nik,
                    nama = u.Nama,
                    departemen = u.Departemen,
                    jabatan = tgt?.NamaJabatanStandar ?? "-",
                    kategori_pengawas = tgt?.KategoriPengawas ?? "-",
                    perusahaan = u.Perusahaan,
                    t_inspeksi = tgt?.TargetInspeksi ?? 0,
                    t_observasi = tgt?.TargetObservasi ?? 0,
                    t_hazard = tgt?.TargetHazardReport ?? 0,
                    t_coaching = tgt?.TargetCoaching ?? 0,
                    t_safetytalk = tgt?.TargetSafetyTalk ?? 0,
                    alasan = tgt?.AlasanTargetZero ?? ""
                };
            });

            return Json(new { draw = draw, recordsFiltered = recordsTotal, recordsTotal = recordsTotal, data = data });
        }

        [HttpPost]
        public async Task<IActionResult> SaveTarget(string karyawanId, int targetInspeksi, int targetObservasi, int targetHazard, int targetCoaching, int targetSafetyTalk, string kategoriPengawas)
        {
            if (!HasAccess()) return Unauthorized();

            if (!int.TryParse(karyawanId, out int kId))
            {
                return BadRequest("Invalid Karyawan ID");
            }

            var finalKategori = string.IsNullOrEmpty(kategoriPengawas) || kategoriPengawas == "-" ? "NON SAP" : kategoriPengawas;

            string sql = @"
                UPDATE [ONE_DB_MITRA].[dbo].[tbl_t_karyawan] 
                SET level_jabatan = {0}
                WHERE id_karyawan = {1}";

            try
            {
                await _context.Database.ExecuteSqlRawAsync(sql, finalKategori, kId);
                return Json(new { success = true, message = "Kategori Pengawas berhasil dioverride. Target SAP akan otomatis terhitung kembali." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Gagal menyimpan target: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsByCompany(int? perusahaanId)
        {
            if (!HasAccess()) return Unauthorized();

            var userCompanyStr = User.FindFirst("CompanyId")?.Value;
            var accessibleCompanyIds = new List<int>();
            if (int.TryParse(userCompanyStr, out int userCompanyId))
            {
                accessibleCompanyIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(userCompanyId);
            }

            var query = _context.Departemens
                .Where(d => d.IdPerusahaan != null && d.NamaDepartemen != null);

            if (perusahaanId.HasValue && perusahaanId.Value > 0)
            {
                query = query.Where(d => d.IdPerusahaan == perusahaanId.Value);
            }
            else
            {
                query = query.Where(d => accessibleCompanyIds.Contains(d.IdPerusahaan.Value));
            }

            var depts = await query
                .Select(d => d.NamaDepartemen)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();

            return Json(depts);
        }

        [HttpPost]
        public async Task<IActionResult> BulkUpdateDepartemen(int perusahaanId, string departemen, string kategoriPengawas)
        {
            if (!HasAccess()) return Unauthorized();
            
            if (string.IsNullOrEmpty(departemen)) return BadRequest("Departemen harus dipilih");
            
            // Get all active employees in this department and company
            var finalKategori = string.IsNullOrEmpty(kategoriPengawas) || kategoriPengawas == "-" ? "NON SAP" : kategoriPengawas;

            string sql = @"
                UPDATE k
                SET k.level_jabatan = {0}
                FROM [ONE_DB_MITRA].[dbo].[tbl_t_karyawan] k
                JOIN [ONE_DB_MITRA].[dbo].[tbl_m_departemen] d ON k.id_departemen = d.id
                WHERE k.status_aktif = 1 
                  AND k.id_perusahaan = {1} 
                  AND d.nama_departemen = {2}";

            try 
            {
                int affectedRows = await _context.Database.ExecuteSqlRawAsync(sql, finalKategori, perusahaanId, departemen);
                return Json(new { success = true, message = $"{affectedRows} karyawan berhasil diupdate secara massal ke kategori '{finalKategori}'." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Gagal melakukan update massal: " + ex.Message });
            }
        }
    }
}
