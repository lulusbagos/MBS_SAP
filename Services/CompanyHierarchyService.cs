using MBS_SAP.Data;
using MBS_SAP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace MBS_SAP.Services
{
    public class CompanyHierarchyService
    {
        private readonly AppDbContext _context;

        public CompanyHierarchyService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Retrieves all descendant Company IDs for a given Company ID (including itself).
        /// </summary>
        public async Task<List<int>> GetAccessibleCompanyIdsAsync(int rootCompanyId)
        {
            var accessibleIds = new List<int> { rootCompanyId };
            var allCompanies = await _context.Perusahaans.AsNoTracking().Where(p => p.StatusAktif).ToListAsync();
            var relations = await _context.PerusahaanHierarchyRelations.AsNoTracking().ToListAsync();

            FindChildrenRecursively(rootCompanyId, allCompanies, relations, accessibleIds);

            return accessibleIds.Distinct().ToList();
        }

        private void FindChildrenRecursively(int parentId, List<PerusahaanView> allCompanies, List<PerusahaanHierarchyRelationView> relations, List<int> accessibleIds)
        {
            var childrenFromParentId = allCompanies.Where(c => c.PerusahaanIndukId == parentId).Select(c => c.PerusahaanId).ToList();
            var childrenFromRelations = relations.Where(r => r.ParentCompanyId == parentId && r.ChildCompanyId.HasValue).Select(r => r.ChildCompanyId!.Value).ToList();
            var children = childrenFromParentId.Concat(childrenFromRelations).Distinct().ToList();

            foreach (var childId in children)
            {
                if (!accessibleIds.Contains(childId))
                {
                    accessibleIds.Add(childId);
                    FindChildrenRecursively(childId, allCompanies, relations, accessibleIds);
                }
            }
        }

        /// <summary>
        /// Retrieves distinct active department names for a company.
        /// Primary source: [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown.
        /// Fallback: vw_departemen, hierarchy relations, parent company, employee mappings, name match.
        /// </summary>
        public async Task<List<string>> GetDepartmentsByCompanyAsync(int companyId)
        {
            if (companyId <= 0) return new List<string>();

            // 1. Primary Source: [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown via ADO.NET
            try
            {
                var conn = _context.Database.GetDbConnection();
                bool wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT DISTINCT nama_departemen, ISNULL(sort_order, 999) AS sort_order
                        FROM [ONE_DB_MITRA].[dbo].[vw_m_departemen_dropdown]
                        WHERE id_perusahaan = @companyId
                          AND (departemen_status_aktif IS NULL OR departemen_status_aktif = '1' OR departemen_status_aktif = 'Y' OR departemen_status_aktif = 'True' OR departemen_status_aktif <> '0')
                          AND nama_departemen IS NOT NULL AND RTRIM(LTRIM(nama_departemen)) <> ''
                        ORDER BY sort_order, nama_departemen";

                    var p = cmd.CreateParameter();
                    p.ParameterName = "@companyId";
                    p.Value = companyId;
                    cmd.Parameters.Add(p);

                    var depts = new List<string>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var dName = reader["nama_departemen"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(dName) && !depts.Contains(dName, StringComparer.OrdinalIgnoreCase))
                        {
                            depts.Add(dName);
                        }
                    }

                    if (depts.Any())
                    {
                        return depts;
                    }
                }
                finally
                {
                    if (wasClosed) await conn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetDepartmentsByCompanyAsync] Error querying ONE_DB_MITRA: {ex.Message}");
            }

            // 2. Direct departments in vw_departemen
            var directDepts = await _context.Departemens
                .AsNoTracking()
                .Where(d => d.IdPerusahaan == companyId && (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen))
                .OrderBy(d => d.NamaDepartemen)
                .Select(d => d.NamaDepartemen!)
                .Distinct()
                .ToListAsync();

            if (directDepts.Any()) return directDepts;

            // 3. Parent company departments via hierarchy relations
            var parentDepts = await (
                from h in _context.PerusahaanHierarchyRelations.AsNoTracking()
                where h.ChildCompanyId == companyId && h.ParentCompanyId != null
                join d in _context.Departemens.AsNoTracking() on h.ParentCompanyId equals d.IdPerusahaan
                where (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen)
                orderby d.NamaDepartemen
                select d.NamaDepartemen!
            ).Distinct().ToListAsync();

            if (parentDepts.Any()) return parentDepts;

            // 4. Parent company departments via PerusahaanIndukId
            var comp = await _context.Perusahaans.AsNoTracking().FirstOrDefaultAsync(p => p.PerusahaanId == companyId);
            if (comp != null && comp.PerusahaanIndukId > 0)
            {
                var indukDepts = await _context.Departemens
                    .AsNoTracking()
                    .Where(d => d.IdPerusahaan == comp.PerusahaanIndukId && (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen))
                    .OrderBy(d => d.NamaDepartemen)
                    .Select(d => d.NamaDepartemen!)
                    .Distinct()
                    .ToListAsync();

                if (indukDepts.Any()) return indukDepts;
            }

            // 5. Departments from Employees belonging to this company
            var empDepts = await (
                from k in _context.Karyawans.AsNoTracking()
                where k.IdPerusahaan == companyId && k.IdDepartemen != null
                join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals (int?)d.DepartemenId
                where (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen)
                orderby d.NamaDepartemen
                select d.NamaDepartemen!
            ).Distinct().ToListAsync();

            if (empDepts.Any()) return empDepts;

            return new List<string>();
        }

        /// <summary>
        /// Retrieves a dictionary of CompanyId to list of department names for all active companies,
        /// resolving from [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown and hierarchy relations.
        /// </summary>
        public async Task<Dictionary<int, List<string>>> GetAllCompanyDepartmentsMapAsync()
        {
            var map = new Dictionary<int, List<string>>();

            // 1. Primary Source: [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown via ADO.NET
            try
            {
                var conn = _context.Database.GetDbConnection();
                bool wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT id_perusahaan, nama_departemen, ISNULL(sort_order, 999) AS sort_order
                        FROM [ONE_DB_MITRA].[dbo].[vw_m_departemen_dropdown]
                        WHERE (perusahaan_status_aktif IS NULL OR perusahaan_status_aktif = 1 OR perusahaan_status_aktif = '1' OR perusahaan_status_aktif = 'Y' OR perusahaan_status_aktif = 'True' OR perusahaan_status_aktif <> '0')
                          AND (departemen_status_aktif IS NULL OR departemen_status_aktif = 1 OR departemen_status_aktif = '1' OR departemen_status_aktif = 'Y' OR departemen_status_aktif = 'True' OR departemen_status_aktif <> '0')
                          AND perusahaan_deleted_at IS NULL
                          AND nama_departemen IS NOT NULL AND RTRIM(LTRIM(nama_departemen)) <> ''
                        ORDER BY id_perusahaan, sort_order, nama_departemen";

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var cidObj = reader["id_perusahaan"];
                        if (cidObj != null && int.TryParse(cidObj.ToString(), out int cid))
                        {
                            if (!map.ContainsKey(cid))
                            {
                                map[cid] = new List<string>();
                            }
                            var deptName = reader["nama_departemen"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(deptName) && !map[cid].Contains(deptName, StringComparer.OrdinalIgnoreCase))
                            {
                                map[cid].Add(deptName);
                            }
                        }
                    }
                }
                finally
                {
                    if (wasClosed) await conn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllCompanyDepartmentsMapAsync] Error querying ONE_DB_MITRA: {ex.Message}");
            }

            // 2. Local vw_departemen fallback for any missing companies
            var deptList = await _context.Departemens
                .AsNoTracking()
                .Where(d => (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen) && d.IdPerusahaan.HasValue)
                .OrderBy(d => d.NamaDepartemen)
                .Select(d => new { d.IdPerusahaan, d.NamaDepartemen })
                .ToListAsync();

            foreach (var item in deptList)
            {
                if (item.IdPerusahaan.HasValue)
                {
                    int cid = item.IdPerusahaan.Value;
                    if (!map.ContainsKey(cid))
                    {
                        map[cid] = new List<string>();
                    }
                    var deptName = item.NamaDepartemen!.Trim();
                    if (!string.IsNullOrEmpty(deptName) && !map[cid].Contains(deptName, StringComparer.OrdinalIgnoreCase))
                    {
                        map[cid].Add(deptName);
                    }
                }
            }

            // 3. Resolve hierarchy relations for child companies without direct departments
            var relations = await _context.PerusahaanHierarchyRelations
                .AsNoTracking()
                .Where(h => h.ChildCompanyId.HasValue && h.ParentCompanyId.HasValue)
                .Select(h => new { ChildId = h.ChildCompanyId!.Value, ParentId = h.ParentCompanyId!.Value })
                .ToListAsync();

            foreach (var rel in relations)
            {
                if ((!map.ContainsKey(rel.ChildId) || !map[rel.ChildId].Any()) && map.ContainsKey(rel.ParentId))
                {
                    map[rel.ChildId] = new List<string>(map[rel.ParentId]);
                }
            }

            // 4. Also check PerusahaanIndukId
            var companiesWithInduk = await _context.Perusahaans
                .AsNoTracking()
                .Where(p => p.StatusAktif && p.PerusahaanIndukId.HasValue && p.PerusahaanIndukId.Value > 0)
                .Select(p => new { p.PerusahaanId, IndukId = p.PerusahaanIndukId!.Value })
                .ToListAsync();

            foreach (var c in companiesWithInduk)
            {
                if ((!map.ContainsKey(c.PerusahaanId) || !map[c.PerusahaanId].Any()) && map.ContainsKey(c.IndukId))
                {
                    map[c.PerusahaanId] = new List<string>(map[c.IndukId]);
                }
            }

            return map;
        }

        /// <summary>
        /// Retrieves list of active companies from [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown (or vw_perusahaan).
        /// </summary>
        public async Task<List<CompanyDropdownItem>> GetCompaniesAsync()
        {
            // 1. Primary Source: [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown via ADO.NET
            try
            {
                var conn = _context.Database.GetDbConnection();
                bool wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT DISTINCT id_perusahaan, kode_perusahaan, nama_perusahaan, ISNULL(sort_order, 999) AS sort_order
                        FROM [ONE_DB_MITRA].[dbo].[vw_m_departemen_dropdown]
                        WHERE (perusahaan_status_aktif IS NULL OR perusahaan_status_aktif = 1 OR perusahaan_status_aktif = '1' OR perusahaan_status_aktif = 'Y' OR perusahaan_status_aktif = 'True' OR perusahaan_status_aktif <> '0')
                          AND perusahaan_deleted_at IS NULL
                          AND nama_perusahaan IS NOT NULL AND RTRIM(LTRIM(nama_perusahaan)) <> ''
                        ORDER BY sort_order, nama_perusahaan";

                    var companies = new List<CompanyDropdownItem>();
                    var seenIds = new HashSet<int>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var cidObj = reader["id_perusahaan"];
                        if (cidObj != null && int.TryParse(cidObj.ToString(), out int cid))
                        {
                            if (!seenIds.Contains(cid))
                            {
                                seenIds.Add(cid);
                                var cNama = reader["nama_perusahaan"]?.ToString()?.Trim() ?? "";
                                var cKode = reader["kode_perusahaan"]?.ToString()?.Trim() ?? "";
                                companies.Add(new CompanyDropdownItem { Id = cid, Nama = cNama, Kode = cKode });
                            }
                        }
                    }

                    if (companies.Any())
                    {
                        return companies;
                    }
                }
                finally
                {
                    if (wasClosed) await conn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetCompaniesAsync] Error querying ONE_DB_MITRA: {ex.Message}");
            }

            var fallback = await _context.Perusahaans
                .AsNoTracking()
                .Where(p => p.StatusAktif)
                .OrderBy(p => p.NamaPerusahaan)
                .Select(p => new CompanyDropdownItem {
                    Id = p.PerusahaanId,
                    Nama = p.NamaPerusahaan ?? string.Empty,
                    Kode = p.KodePerusahaan ?? string.Empty
                })
                .ToListAsync();

            return fallback;
        }
    }
}
