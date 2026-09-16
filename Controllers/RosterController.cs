using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MBS_SAP.Data;
using MBS_SAP.Models;
using MBS_SAP.Services;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class RosterController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CompanyHierarchyService _companyHierarchyService;
        private readonly IMemoryCache _cache;

        public RosterController(
            AppDbContext context, 
            CompanyHierarchyService companyHierarchyService,
            IMemoryCache cache)
        {
            _context = context;
            _companyHierarchyService = companyHierarchyService;
            _cache = cache;
        }

        private bool HasAccess()
        {
            var isAdmin = User.IsInRole("Administrator") || User.IsInRole("Admin") || 
                          string.Equals(User.FindFirst(ClaimTypes.Role)?.Value?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(User.FindFirst(ClaimTypes.Role)?.Value?.Trim(), "Administrator", StringComparison.OrdinalIgnoreCase);

            var jobTitle = User.FindFirst("JobTitle")?.Value ?? "";
            var department = User.FindFirst("Department")?.Value ?? "";

            bool isSafetyDept = department.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
                                department.Contains("ohs", StringComparison.OrdinalIgnoreCase) ||
                                department.Contains("hse", StringComparison.OrdinalIgnoreCase) ||
                                department.Contains("she", StringComparison.OrdinalIgnoreCase) ||
                                jobTitle.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
                                jobTitle.Contains("hse", StringComparison.OrdinalIgnoreCase) ||
                                jobTitle.Contains("ohs", StringComparison.OrdinalIgnoreCase) ||
                                jobTitle.Contains("she", StringComparison.OrdinalIgnoreCase);

            return isAdmin || isSafetyDept;
        }

        private async Task<List<int>> GetUserAccessibleCompanyIdsAsync()
        {
            var isAdmin = User.IsInRole("Administrator") || User.IsInRole("Admin") || 
                          string.Equals(User.FindFirst(ClaimTypes.Role)?.Value?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(User.FindFirst(ClaimTypes.Role)?.Value?.Trim(), "Administrator", StringComparison.OrdinalIgnoreCase);

            if (isAdmin)
            {
                return await _context.Perusahaans
                    .Where(p => p.StatusAktif)
                    .Select(p => p.PerusahaanId)
                    .ToListAsync();
            }

            var userCompanyStr = User.FindFirst("CompanyId")?.Value;

            if (int.TryParse(userCompanyStr, out int userCompanyId) && userCompanyId > 0)
            {
                var accessible = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(userCompanyId);
                if (accessible != null && accessible.Count > 0)
                {
                    return accessible;
                }
                return new List<int> { userCompanyId };
            }

            // If CompanyId claim is missing, fallback to lookup by user NIK
            var userNik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? 
                          User.FindFirst("NIK")?.Value ?? 
                          User.FindFirst("NoNik")?.Value;

            if (!string.IsNullOrEmpty(userNik))
            {
                var myKaryawan = await _context.Karyawans.AsNoTracking()
                    .FirstOrDefaultAsync(k => k.NoNik == userNik && k.StatusAktif);

                if (myKaryawan != null && myKaryawan.IdPerusahaan > 0)
                {
                    var accessible = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(myKaryawan.IdPerusahaan);
                    if (accessible != null && accessible.Count > 0)
                    {
                        return accessible;
                    }
                    return new List<int> { myKaryawan.IdPerusahaan };
                }
            }

            // Fallback: all active companies
            return await _context.Perusahaans
                .Where(p => p.StatusAktif)
                .Select(p => p.PerusahaanId)
                .ToListAsync();
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!HasAccess())
            {
                return RedirectToAction("Index", "Home");
            }

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var companies = await _context.Perusahaans
                .Where(p => accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(p.PerusahaanId))
                .OrderBy(p => p.NamaPerusahaan)
                .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = p.PerusahaanId.ToString(),
                    Text = p.NamaPerusahaan
                })
                .ToListAsync();

            var deptsQuery = _context.Departemens
                .Where(d => d.NamaDepartemen != null && d.IdPerusahaan != null && (accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(d.IdPerusahaan.Value)));

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
            ViewData["ActiveTab"] = "RosterSap";
            ViewData["HeaderTitle"] = "Manajemen Roster & Penugasan SAP";

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetRosterData()
        {
            if (!HasAccess()) return Unauthorized();

            var draw = Request.Form["draw"].FirstOrDefault();
            var start = Request.Form["start"].FirstOrDefault();
            var length = Request.Form["length"].FirstOrDefault();
            var searchValue = Request.Form["search[value]"].FirstOrDefault();
            var perusahaanIdFilter = Request.Form["perusahaanId"].FirstOrDefault();
            var departemenFilter = Request.Form["departemen"].FirstOrDefault();
            var statusFilter = Request.Form["status"].FirstOrDefault();
            var customSearch = Request.Form["customSearch"].FirstOrDefault();

            if (string.IsNullOrEmpty(searchValue) && !string.IsNullOrEmpty(customSearch))
            {
                searchValue = customSearch;
            }

            int pageSize = length != null ? Convert.ToInt32(length) : 10;
            int skip = start != null ? Convert.ToInt32(start) : 0;

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var usersQuery = from k in _context.Karyawans.AsNoTracking()
                             join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal into persGroup
                             from p in persGroup.DefaultIfEmpty()
                             join c in _context.Perusahaans.AsNoTracking() on k.IdPerusahaan equals c.PerusahaanId into compGroup
                             from c in compGroup.DefaultIfEmpty()
                             join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into deptGroup
                             from d in deptGroup.DefaultIfEmpty()
                             join j in _context.Jabatans.AsNoTracking() on k.IdJabatan equals j.JabatanId into jabGroup
                             from j in jabGroup.DefaultIfEmpty()
                             where k.StatusAktif && (accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(k.IdPerusahaan))
                             select new {
                                 KaryawanId = (int?)k.IdKaryawan,
                                 Nik = k.NoNik,
                                 Nama = p != null ? p.NamaLengkap : k.NoNik,
                                 IdPerusahaan = k.IdPerusahaan,
                                 Perusahaan = c != null ? c.NamaPerusahaan : "Unknown",
                                 Departemen = d != null ? d.NamaDepartemen : "-",
                                 Jabatan = j != null ? j.NamaJabatan : "-"
                             };

            if (!string.IsNullOrEmpty(perusahaanIdFilter) && int.TryParse(perusahaanIdFilter, out int filterCompanyId) && filterCompanyId > 0)
            {
                usersQuery = usersQuery.Where(u => u.IdPerusahaan == filterCompanyId);
            }

            if (!string.IsNullOrEmpty(departemenFilter))
            {
                usersQuery = usersQuery.Where(u => u.Departemen == departemenFilter);
            }

            if (!string.IsNullOrEmpty(searchValue))
            {
                searchValue = searchValue.Trim();
                usersQuery = usersQuery.Where(u => 
                    (u.Nama != null && (u.Nama.Contains(searchValue) || EF.Functions.Like(u.Nama, $"%{searchValue}%"))) || 
                    (u.Nik != null && (u.Nik.Contains(searchValue) || EF.Functions.Like(u.Nik, $"%{searchValue}%"))));
            }

            var allFilteredUsers = await usersQuery.ToListAsync();
            var userNiks = allFilteredUsers.Select(u => u.Nik).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

            // Fetch latest roster per NIK in batches to prevent SQL parameter limits
            var allRosters = new List<Roster>();
            var mitraRosters = new Dictionary<string, MitraRosterView>();
            const int batchSize = 1000;

            for (int i = 0; i < userNiks.Count; i += batchSize)
            {
                var batch = userNiks.Skip(i).Take(batchSize).ToList();
                var batchRosters = await _context.Rosters.AsNoTracking()
                    .Where(r => batch.Contains(r.Nik))
                    .OrderByDescending(r => r.AkhirCuti)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToListAsync();
                allRosters.AddRange(batchRosters);

                var batchMitra = await _context.MitraRosters.AsNoTracking()
                    .Where(m => batch.Contains(m.NoNik))
                    .ToListAsync();
                foreach (var m in batchMitra)
                {
                    if (!mitraRosters.ContainsKey(m.NoNik))
                    {
                        mitraRosters[m.NoNik] = m;
                    }
                }
            }

            var latestRostersMap = allRosters
                .GroupBy(r => r.Nik)
                .ToDictionary(g => g.Key, g => g.First());

            var today = DateTime.Today;

            var mappedList = allFilteredUsers.Select(u => {
                var hasRoster = latestRostersMap.TryGetValue(u.Nik, out var r);
                var hasMitra = mitraRosters.TryGetValue(u.Nik, out var m);

                string status = "belum_set";
                string statusLabel = "Belum Diatur";
                string statusClass = "badge-warning";
                string tipeRoster = hasRoster ? (r!.TipeRoster ?? "REGULER") : "REGULER";

                if (hasRoster)
                {
                    if (string.Equals(r!.TipeRoster, "TUGAS", StringComparison.OrdinalIgnoreCase))
                    {
                        if (r.AkhirCuti >= today)
                        {
                            status = "tugas";
                            statusLabel = "Periode Tugas";
                            statusClass = "badge-info";
                        }
                        else
                        {
                            status = "expired";
                            statusLabel = "Tugas Expired";
                            statusClass = "badge-danger";
                        }
                    }
                    else
                    {
                        if (r.AkhirCuti >= today)
                        {
                            status = "aktif";
                            statusLabel = "Aktif";
                            statusClass = "badge-success";
                        }
                        else
                        {
                            status = "expired";
                            statusLabel = "Expired";
                            statusClass = "badge-danger";
                        }
                    }
                }

                int defaultOnsite = (hasMitra && m!.HariOnsite.HasValue) ? m.HariOnsite.Value : 42;
                int defaultOffsite = (hasMitra && m!.HariOffsite.HasValue) ? m.HariOffsite.Value : 14;

                return new {
                    karyawanId = u.KaryawanId,
                    nik = u.Nik,
                    nama = u.Nama,
                    perusahaan = u.Perusahaan,
                    departemen = u.Departemen,
                    jabatan = u.Jabatan,
                    status = status,
                    statusLabel = statusLabel,
                    statusClass = statusClass,
                    tipeRoster = tipeRoster,
                    rosterId = hasRoster ? (int?)r!.Id : null,
                    awalDinas = hasRoster ? r!.AwalDinas.ToString("yyyy-MM-dd") : "",
                    akhirDinas = hasRoster ? r!.AkhirDinas.ToString("yyyy-MM-dd") : "",
                    awalCuti = hasRoster ? r!.AwalCuti.ToString("yyyy-MM-dd") : "",
                    akhirCuti = hasRoster ? r!.AkhirCuti.ToString("yyyy-MM-dd") : "",
                    awalDinasFormatted = hasRoster ? r!.AwalDinas.ToString("dd MMM yyyy") : "-",
                    akhirDinasFormatted = hasRoster ? r!.AkhirDinas.ToString("dd MMM yyyy") : "-",
                    awalCutiFormatted = hasRoster ? r!.AwalCuti.ToString("dd MMM yyyy") : "-",
                    akhirCutiFormatted = hasRoster ? r!.AkhirCuti.ToString("dd MMM yyyy") : "-",
                    keterangan = hasRoster ? (r!.Keterangan ?? "") : "",
                    defaultPattern = $"{defaultOnsite} : {defaultOffsite}",
                    defaultOnsite = defaultOnsite,
                    defaultOffsite = defaultOffsite,
                    updatedAt = hasRoster ? (r!.UpdatedAt ?? r.CreatedAt).ToString("dd/MM/yyyy HH:mm") : "-"
                };
            });

            // Filter by status if requested
            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (statusFilter == "aktif")
                {
                    mappedList = mappedList.Where(x => x.status == "aktif");
                }
                else if (statusFilter == "tugas")
                {
                    mappedList = mappedList.Where(x => x.status == "tugas");
                }
                else if (statusFilter == "expired")
                {
                    mappedList = mappedList.Where(x => x.status == "expired");
                }
                else if (statusFilter == "belum_set")
                {
                    mappedList = mappedList.Where(x => x.status == "belum_set");
                }
            }

            var totalCount = mappedList.Count();
            var pagedData = mappedList
                .OrderBy(x => x.nama)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            // Calculate stats summary
            var allStatsList = allFilteredUsers.Select(u => {
                var hasRoster = latestRostersMap.TryGetValue(u.Nik, out var r);
                if (!hasRoster) return "belum_set";
                if (string.Equals(r!.TipeRoster, "TUGAS", StringComparison.OrdinalIgnoreCase))
                {
                    return r.AkhirCuti >= today ? "tugas" : "expired";
                }
                return r.AkhirCuti >= today ? "aktif" : "expired";
            }).ToList();

            var statsSummary = new {
                total = allFilteredUsers.Count,
                aktif = allStatsList.Count(s => s == "aktif"),
                tugas = allStatsList.Count(s => s == "tugas"),
                expired = allStatsList.Count(s => s == "expired"),
                belumSet = allStatsList.Count(s => s == "belum_set")
            };

            return Json(new { 
                draw = draw, 
                recordsFiltered = totalCount, 
                recordsTotal = totalCount, 
                data = pagedData,
                stats = statsSummary
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeRosterDetail(string nik)
        {
            if (!HasAccess()) return Unauthorized();

            if (string.IsNullOrWhiteSpace(nik))
            {
                return BadRequest("NIK tidak boleh kosong.");
            }

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var karyawan = await (from k in _context.Karyawans.AsNoTracking()
                                  join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal into persGroup
                                  from p in persGroup.DefaultIfEmpty()
                                  join c in _context.Perusahaans.AsNoTracking() on k.IdPerusahaan equals c.PerusahaanId into compGroup
                                  from c in compGroup.DefaultIfEmpty()
                                  join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into deptGroup
                                  from d in deptGroup.DefaultIfEmpty()
                                  join j in _context.Jabatans.AsNoTracking() on k.IdJabatan equals j.JabatanId into jabGroup
                                  from j in jabGroup.DefaultIfEmpty()
                                  where k.NoNik == nik && (accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(k.IdPerusahaan))
                                  select new {
                                      k.IdKaryawan,
                                      k.NoNik,
                                      NamaLengkap = p != null ? p.NamaLengkap : k.NoNik,
                                      NamaPerusahaan = c != null ? c.NamaPerusahaan : "Unknown",
                                      Departemen = d != null ? d.NamaDepartemen : "-",
                                      Jabatan = j != null ? j.NamaJabatan : "-"
                                  }).FirstOrDefaultAsync();

            if (karyawan == null)
            {
                return NotFound("Karyawan tidak ditemukan atau di luar wewenang akses Anda.");
            }

            var history = await _context.Rosters.AsNoTracking()
                .Where(r => r.Nik == nik)
                .OrderByDescending(r => r.AkhirCuti)
                .ThenByDescending(r => r.CreatedAt)
                .Select(r => new {
                    r.Id,
                    r.Nik,
                    r.AwalDinas,
                    r.AkhirDinas,
                    r.AwalCuti,
                    r.AkhirCuti,
                    AwalDinasStr = r.AwalDinas.ToString("yyyy-MM-dd"),
                    AkhirDinasStr = r.AkhirDinas.ToString("yyyy-MM-dd"),
                    AwalCutiStr = r.AwalCuti.ToString("yyyy-MM-dd"),
                    AkhirCutiStr = r.AkhirCuti.ToString("yyyy-MM-dd"),
                    AwalDinasFormatted = r.AwalDinas.ToString("dd MMM yyyy"),
                    AkhirDinasFormatted = r.AkhirDinas.ToString("dd MMM yyyy"),
                    AwalCutiFormatted = r.AwalCuti.ToString("dd MMM yyyy"),
                    AkhirCutiFormatted = r.AkhirCuti.ToString("dd MMM yyyy"),
                    TipeRoster = r.TipeRoster ?? "REGULER",
                    r.Keterangan,
                    CreatedAt = r.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    UpdatedAt = (r.UpdatedAt ?? r.CreatedAt).ToString("dd/MM/yyyy HH:mm")
                })
                .ToListAsync();

            var mitra = await _context.MitraRosters.AsNoTracking()
                .FirstOrDefaultAsync(m => m.NoNik == nik);

            return Json(new {
                success = true,
                karyawan = karyawan,
                history = history,
                defaultOnsite = mitra?.HariOnsite ?? 42,
                defaultOffsite = mitra?.HariOffsite ?? 14
            });
        }

        [HttpPost]
        public async Task<IActionResult> SaveEmployeeRoster([FromBody] AdminRosterSaveRequest req)
        {
            if (!HasAccess()) return Unauthorized();

            if (req == null || string.IsNullOrWhiteSpace(req.Nik))
            {
                return BadRequest("Data request tidak valid.");
            }

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var karyawan = await _context.Karyawans.AsNoTracking()
                .FirstOrDefaultAsync(k => k.NoNik == req.Nik && k.StatusAktif && (accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(k.IdPerusahaan)));

            if (karyawan == null)
            {
                return BadRequest("Karyawan tidak ditemukan dalam daftar perusahaan yang dapat Anda kelola.");
            }

            bool isTugas = string.Equals(req.TipeRoster, "TUGAS", StringComparison.OrdinalIgnoreCase);

            if (isTugas)
            {
                if (!DateTime.TryParse(req.AwalDinas, out DateTime awalTugas) ||
                    !DateTime.TryParse(req.AkhirDinas, out DateTime akhirTugas))
                {
                    return BadRequest("Format tanggal periode tugas tidak valid.");
                }

                if (awalTugas > akhirTugas)
                {
                    return BadRequest("Tanggal mulai tugas tidak boleh lebih besar dari akhir tugas.");
                }

                Roster? rosterTugasToUpdate = null;
                if (req.Id.HasValue && req.Id.Value > 0)
                {
                    rosterTugasToUpdate = await _context.Rosters
                        .FirstOrDefaultAsync(r => r.Id == req.Id.Value && r.Nik == req.Nik);
                }

                if (rosterTugasToUpdate == null)
                {
                    rosterTugasToUpdate = await _context.Rosters
                        .Where(r => r.Nik == req.Nik && r.AkhirCuti >= DateTime.Today)
                        .OrderByDescending(r => r.AkhirCuti)
                        .FirstOrDefaultAsync();
                }

                if (rosterTugasToUpdate != null)
                {
                    rosterTugasToUpdate.AwalDinas = awalTugas;
                    rosterTugasToUpdate.AkhirDinas = akhirTugas;
                    rosterTugasToUpdate.AwalCuti = akhirTugas;
                    rosterTugasToUpdate.AkhirCuti = akhirTugas;
                    rosterTugasToUpdate.TipeRoster = "TUGAS";
                    rosterTugasToUpdate.Keterangan = req.Keterangan;
                    rosterTugasToUpdate.UpdatedAt = DateTime.Now;
                    _context.Rosters.Update(rosterTugasToUpdate);
                }
                else
                {
                    var newRosterTugas = new Roster
                    {
                        Nik = req.Nik,
                        AwalDinas = awalTugas,
                        AkhirDinas = akhirTugas,
                        AwalCuti = akhirTugas,
                        AkhirCuti = akhirTugas,
                        TipeRoster = "TUGAS",
                        Keterangan = req.Keterangan,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    _context.Rosters.Add(newRosterTugas);
                }

                await _context.SaveChangesAsync();
                _cache.Remove($"UserDashboardStats_{req.Nik}");
                return Ok(new { success = true, message = $"Periode Tugas untuk NIK {req.Nik} berhasil disimpan." });
            }

            // REGULER ROSTER
            if (!DateTime.TryParse(req.AwalDinas, out DateTime awalDinas) ||
                !DateTime.TryParse(req.AkhirDinas, out DateTime akhirDinas) ||
                !DateTime.TryParse(req.AwalCuti, out DateTime awalCuti) ||
                !DateTime.TryParse(req.AkhirCuti, out DateTime akhirCuti))
            {
                return BadRequest("Format tanggal dinas/cuti tidak valid.");
            }

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

            Roster? rosterRegulerToUpdate = null;
            if (req.Id.HasValue && req.Id.Value > 0)
            {
                rosterRegulerToUpdate = await _context.Rosters
                    .FirstOrDefaultAsync(r => r.Id == req.Id.Value && r.Nik == req.Nik);
            }

            if (rosterRegulerToUpdate == null)
            {
                rosterRegulerToUpdate = await _context.Rosters
                    .Where(r => r.Nik == req.Nik && r.AkhirCuti >= DateTime.Today)
                    .OrderByDescending(r => r.AkhirCuti)
                    .FirstOrDefaultAsync();
            }

            if (rosterRegulerToUpdate != null)
            {
                rosterRegulerToUpdate.AwalDinas = awalDinas;
                rosterRegulerToUpdate.AkhirDinas = akhirDinas;
                rosterRegulerToUpdate.AwalCuti = awalCuti;
                rosterRegulerToUpdate.AkhirCuti = akhirCuti;
                rosterRegulerToUpdate.TipeRoster = "REGULER";
                rosterRegulerToUpdate.Keterangan = null;
                rosterRegulerToUpdate.UpdatedAt = DateTime.Now;
                _context.Rosters.Update(rosterRegulerToUpdate);
            }
            else
            {
                var newRoster = new Roster
                {
                    Nik = req.Nik,
                    AwalDinas = awalDinas,
                    AkhirDinas = akhirDinas,
                    AwalCuti = awalCuti,
                    AkhirCuti = akhirCuti,
                    TipeRoster = "REGULER",
                    Keterangan = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _context.Rosters.Add(newRoster);
            }

            await _context.SaveChangesAsync();
            _cache.Remove($"UserDashboardStats_{req.Nik}");
            return Ok(new { success = true, message = $"Roster kerja untuk NIK {req.Nik} berhasil disimpan." });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEmployeeRoster([FromQuery] int id, [FromQuery] string nik)
        {
            if (!HasAccess()) return Unauthorized();

            if (id <= 0 || string.IsNullOrWhiteSpace(nik))
            {
                return BadRequest("Parameter id dan NIK tidak valid.");
            }

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var karyawan = await _context.Karyawans.AsNoTracking()
                .FirstOrDefaultAsync(k => k.NoNik == nik && (accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(k.IdPerusahaan)));

            if (karyawan == null)
            {
                return BadRequest("Karyawan tidak ditemukan dalam wewenang akses Anda.");
            }

            var rosterToDelete = await _context.Rosters
                .FirstOrDefaultAsync(r => r.Id == id && r.Nik == nik);

            if (rosterToDelete == null)
            {
                return NotFound("Data roster tidak ditemukan.");
            }

            string tipe = rosterToDelete.TipeRoster == "TUGAS" ? "Periode Tugas" : "Roster";
            _context.Rosters.Remove(rosterToDelete);
            await _context.SaveChangesAsync();
            _cache.Remove($"UserDashboardStats_{nik}");

            return Ok(new { success = true, message = $"{tipe} untuk NIK {nik} berhasil dihapus." });
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsByCompany(int? perusahaanId)
        {
            if (!HasAccess()) return Unauthorized();

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var query = _context.Departemens
                .Where(d => d.IdPerusahaan != null && d.NamaDepartemen != null);

            if (perusahaanId.HasValue && perusahaanId.Value > 0)
            {
                query = query.Where(d => d.IdPerusahaan == perusahaanId.Value);
            }
            else
            {
                if (accessibleCompanyIds.Count > 0)
                {
                    query = query.Where(d => d.IdPerusahaan.HasValue && accessibleCompanyIds.Contains(d.IdPerusahaan.Value));
                }
            }

            var depts = await query
                .Select(d => d.NamaDepartemen)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();

            return Json(depts);
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(int? perusahaanId, string? departemen, string? status, string? search)
        {
            if (!HasAccess()) return Unauthorized();

            var accessibleCompanyIds = await GetUserAccessibleCompanyIdsAsync();

            var usersQuery = from k in _context.Karyawans.AsNoTracking()
                             join p in _context.Personals.AsNoTracking() on k.IdPersonal equals p.IdPersonal into persGroup
                             from p in persGroup.DefaultIfEmpty()
                             join c in _context.Perusahaans.AsNoTracking() on k.IdPerusahaan equals c.PerusahaanId into compGroup
                             from c in compGroup.DefaultIfEmpty()
                             join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals d.DepartemenId into deptGroup
                             from d in deptGroup.DefaultIfEmpty()
                             join j in _context.Jabatans.AsNoTracking() on k.IdJabatan equals j.JabatanId into jabGroup
                             from j in jabGroup.DefaultIfEmpty()
                             where k.StatusAktif && (accessibleCompanyIds.Count == 0 || accessibleCompanyIds.Contains(k.IdPerusahaan))
                             select new {
                                 KaryawanId = (int?)k.IdKaryawan,
                                 Nik = k.NoNik,
                                 Nama = p != null ? p.NamaLengkap : k.NoNik,
                                 IdPerusahaan = k.IdPerusahaan,
                                 Perusahaan = c != null ? c.NamaPerusahaan : "Unknown",
                                 Departemen = d != null ? d.NamaDepartemen : "-",
                                 Jabatan = j != null ? j.NamaJabatan : "-"
                             };

            if (perusahaanId.HasValue && perusahaanId.Value > 0)
            {
                usersQuery = usersQuery.Where(u => u.IdPerusahaan == perusahaanId.Value);
            }

            if (!string.IsNullOrEmpty(departemen))
            {
                usersQuery = usersQuery.Where(u => u.Departemen == departemen);
            }

            if (!string.IsNullOrEmpty(search))
            {
                search = search.Trim();
                usersQuery = usersQuery.Where(u => 
                    (u.Nama != null && (u.Nama.Contains(search) || EF.Functions.Like(u.Nama, $"%{search}%"))) || 
                    (u.Nik != null && (u.Nik.Contains(search) || EF.Functions.Like(u.Nik, $"%{search}%"))));
            }

            var allFilteredUsers = await usersQuery.OrderBy(u => u.Nama).ToListAsync();
            var userNiks = allFilteredUsers.Select(u => u.Nik).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

            var allRosters = new List<Roster>();
            var mitraRosters = new Dictionary<string, MitraRosterView>();
            const int batchSize = 1000;

            for (int i = 0; i < userNiks.Count; i += batchSize)
            {
                var batch = userNiks.Skip(i).Take(batchSize).ToList();
                var batchRosters = await _context.Rosters.AsNoTracking()
                    .Where(r => batch.Contains(r.Nik))
                    .OrderByDescending(r => r.AkhirCuti)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToListAsync();
                allRosters.AddRange(batchRosters);

                var batchMitra = await _context.MitraRosters.AsNoTracking()
                    .Where(m => batch.Contains(m.NoNik))
                    .ToListAsync();
                foreach (var m in batchMitra)
                {
                    if (!mitraRosters.ContainsKey(m.NoNik))
                    {
                        mitraRosters[m.NoNik] = m;
                    }
                }
            }

            var latestRostersMap = allRosters
                .GroupBy(r => r.Nik)
                .ToDictionary(g => g.Key, g => g.First());

            var today = DateTime.Today;

            var exportList = allFilteredUsers.Select(u => {
                var hasRoster = latestRostersMap.TryGetValue(u.Nik, out var r);
                var hasMitra = mitraRosters.TryGetValue(u.Nik, out var m);

                string statusVal = "Belum Diatur";
                string tipeRoster = hasRoster ? (r!.TipeRoster ?? "REGULER") : "REGULER";

                if (hasRoster)
                {
                    if (string.Equals(r!.TipeRoster, "TUGAS", StringComparison.OrdinalIgnoreCase))
                    {
                        statusVal = r.AkhirCuti >= today ? "Periode Tugas (Bebas SAP)" : "Tugas Expired";
                    }
                    else
                    {
                        statusVal = r.AkhirCuti >= today ? "Aktif" : "Expired";
                    }
                }

                int defaultOnsite = (hasMitra && m!.HariOnsite.HasValue) ? m.HariOnsite.Value : 42;
                int defaultOffsite = (hasMitra && m!.HariOffsite.HasValue) ? m.HariOffsite.Value : 14;

                int dinasDuration = hasRoster ? (r!.AkhirDinas - r.AwalDinas).Days + 1 : 0;
                int cutiDuration = (hasRoster && r!.TipeRoster != "TUGAS") ? (r.AkhirCuti - r.AwalCuti).Days + 1 : 0;

                return new {
                    u.Nik,
                    u.Nama,
                    u.Perusahaan,
                    u.Departemen,
                    u.Jabatan,
                    TipeRoster = tipeRoster,
                    Status = statusVal,
                    RosterDefault = $"{defaultOnsite} Dinas : {defaultOffsite} Cuti",
                    AwalDinas = hasRoster ? r!.AwalDinas.ToString("dd/MM/yyyy") : "-",
                    AkhirDinas = hasRoster ? r!.AkhirDinas.ToString("dd/MM/yyyy") : "-",
                    AwalCuti = (hasRoster && r!.TipeRoster != "TUGAS") ? r!.AwalCuti.ToString("dd/MM/yyyy") : "-",
                    AkhirCuti = (hasRoster && r!.TipeRoster != "TUGAS") ? r!.AkhirCuti.ToString("dd/MM/yyyy") : "-",
                    DurasiDinas = dinasDuration > 0 ? dinasDuration.ToString() : "-",
                    DurasiCuti = cutiDuration > 0 ? cutiDuration.ToString() : "-",
                    Keterangan = hasRoster ? (r!.Keterangan ?? "-") : "-",
                    TerakhirUpdate = hasRoster ? (r!.UpdatedAt ?? r.CreatedAt).ToString("dd/MM/yyyy HH:mm") : "-"
                };
            }).ToList();

            // Status filter
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "aktif") exportList = exportList.Where(x => x.Status == "Aktif").ToList();
                else if (status == "tugas") exportList = exportList.Where(x => x.Status.Contains("Periode Tugas")).ToList();
                else if (status == "expired") exportList = exportList.Where(x => x.Status.Contains("Expired")).ToList();
                else if (status == "belum_set") exportList = exportList.Where(x => x.Status == "Belum Diatur").ToList();
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Rekapitulasi Roster");

            // Title Block
            worksheet.Cell(1, 1).Value = "REKAPITULASI ROSTER KERJA & PENUGASAN KARYAWAN (SAP)";
            worksheet.Cell(1, 1).Style.Font.FontSize = 15;
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0284c7");

            worksheet.Cell(2, 1).Value = $"Waktu Ekspor: {DateTime.Now:dd MMMM yyyy HH:mm:ss} | Total Data: {exportList.Count} Karyawan";
            worksheet.Cell(2, 1).Style.Font.FontSize = 10;
            worksheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");

            // Headers
            string[] headers = new string[]
            {
                "No", "NIK", "Nama Karyawan", "Perusahaan", "Departemen", "Jabatan",
                "Tipe Roster", "Status Roster", "Pola Default", 
                "Mulai Dinas / Tugas", "Selesai Dinas / Tugas", "Mulai Cuti", "Selesai Cuti",
                "Durasi Dinas (Hari)", "Durasi Cuti (Hari)", "Keterangan", "Terakhir Diperbarui"
            };

            int headerRow = 4;
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0284c7"); // Info Blue
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            int rowIdx = headerRow + 1;
            int num = 1;
            foreach (var item in exportList)
            {
                worksheet.Cell(rowIdx, 1).Value = num++;
                worksheet.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell(rowIdx, 2).SetValue(item.Nik);
                worksheet.Cell(rowIdx, 3).Value = item.Nama;
                worksheet.Cell(rowIdx, 4).Value = item.Perusahaan;
                worksheet.Cell(rowIdx, 5).Value = item.Departemen;
                worksheet.Cell(rowIdx, 6).Value = item.Jabatan;
                worksheet.Cell(rowIdx, 7).Value = item.TipeRoster;
                worksheet.Cell(rowIdx, 8).Value = item.Status;

                // Colorize Status column
                if (item.Status == "Aktif")
                {
                    worksheet.Cell(rowIdx, 8).Style.Font.FontColor = XLColor.FromHtml("#059669");
                    worksheet.Cell(rowIdx, 8).Style.Font.Bold = true;
                }
                else if (item.Status.Contains("Periode Tugas"))
                {
                    worksheet.Cell(rowIdx, 8).Style.Font.FontColor = XLColor.FromHtml("#0284c7");
                    worksheet.Cell(rowIdx, 8).Style.Font.Bold = true;
                }
                else if (item.Status.Contains("Expired"))
                {
                    worksheet.Cell(rowIdx, 8).Style.Font.FontColor = XLColor.FromHtml("#dc2626");
                }
                else
                {
                    worksheet.Cell(rowIdx, 8).Style.Font.FontColor = XLColor.FromHtml("#d97706");
                }

                worksheet.Cell(rowIdx, 9).Value = item.RosterDefault;
                worksheet.Cell(rowIdx, 10).Value = item.AwalDinas;
                worksheet.Cell(rowIdx, 11).Value = item.AkhirDinas;
                worksheet.Cell(rowIdx, 12).Value = item.AwalCuti;
                worksheet.Cell(rowIdx, 13).Value = item.AkhirCuti;
                worksheet.Cell(rowIdx, 14).Value = item.DurasiDinas;
                worksheet.Cell(rowIdx, 15).Value = item.DurasiCuti;
                worksheet.Cell(rowIdx, 16).Value = item.Keterangan;
                worksheet.Cell(rowIdx, 17).Value = item.TerakhirUpdate;

                // Center align dates and numbers
                for (int c = 10; c <= 15; c++)
                {
                    worksheet.Cell(rowIdx, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // Zebra striping
                if (num % 2 == 0)
                {
                    worksheet.Row(rowIdx).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                }

                rowIdx++;
            }

            // Table styling
            var dataRange = worksheet.Range(headerRow, 1, Math.Max(rowIdx - 1, headerRow), headers.Length);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cbd5e1");
            dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#e2e8f0");

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(
                content, 
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
                $"Rekapitulasi_Roster_SAP_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            );
        }
    }

    public class AdminRosterSaveRequest
    {
        public int? Id { get; set; }
        public string Nik { get; set; } = string.Empty;
        public string? TipeRoster { get; set; } = "REGULER"; // "REGULER" or "TUGAS"
        public string? Keterangan { get; set; }
        public string AwalDinas { get; set; } = string.Empty;
        public string AkhirDinas { get; set; } = string.Empty;
        public string AwalCuti { get; set; } = string.Empty;
        public string AkhirCuti { get; set; } = string.Empty;
    }
}
