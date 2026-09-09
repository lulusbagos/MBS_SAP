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
        /// Source: [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown.
        /// </summary>
        public async Task<List<string>> GetDepartmentsByCompanyAsync(int companyId)
        {
            if (companyId <= 0) return new List<string>();

            try
            {
                var conn = _context.Database.GetDbConnection();
                bool wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        WITH source_rows AS (
                            SELECT
                                COALESCE(
                                    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(250), option_label))), ''),
                                    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(250), nama_departemen))), '')
                                ) AS dept_label,
                                ISNULL(sort_order, 999) AS sort_order,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), perusahaan_status_aktif))), '') AS perusahaan_status,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), departemen_status_aktif))), '') AS departemen_status,
                                perusahaan_deleted_at
                            FROM [ONE_DB_MITRA].[dbo].[vw_m_departemen_dropdown]
                            WHERE id_perusahaan = @companyId
                        )
                        SELECT dept_label
                        FROM source_rows
                        WHERE dept_label IS NOT NULL
                          AND perusahaan_deleted_at IS NULL
                          AND (
                              perusahaan_status IS NULL
                              OR TRY_CONVERT(bit, perusahaan_status) = 1
                              OR UPPER(perusahaan_status) IN ('Y', 'YES', 'TRUE', 'AKTIF', 'ACTIVE')
                          )
                          AND (
                              departemen_status IS NULL
                              OR TRY_CONVERT(bit, departemen_status) = 1
                              OR UPPER(departemen_status) IN ('Y', 'YES', 'TRUE', 'AKTIF', 'ACTIVE')
                          )
                        GROUP BY dept_label
                        ORDER BY MIN(sort_order), dept_label";

                    var p = cmd.CreateParameter();
                    p.ParameterName = "@companyId";
                    p.Value = companyId;
                    cmd.Parameters.Add(p);

                    var depts = new List<string>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var dName = reader["dept_label"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(dName) && !depts.Contains(dName, StringComparer.OrdinalIgnoreCase))
                        {
                            depts.Add(dName);
                        }
                    }

                    return depts;
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

            return new List<string>();
        }

        /// <summary>
        /// Retrieves a dictionary of CompanyId to department names from [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown.
        /// </summary>
        public async Task<Dictionary<int, List<string>>> GetAllCompanyDepartmentsMapAsync()
        {
            var map = new Dictionary<int, List<string>>();

            try
            {
                var conn = _context.Database.GetDbConnection();
                bool wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        WITH source_rows AS (
                            SELECT
                                id_perusahaan,
                                COALESCE(
                                    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(250), option_label))), ''),
                                    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(250), nama_departemen))), '')
                                ) AS dept_label,
                                ISNULL(sort_order, 999) AS sort_order,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), perusahaan_status_aktif))), '') AS perusahaan_status,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), departemen_status_aktif))), '') AS departemen_status,
                                perusahaan_deleted_at
                            FROM [ONE_DB_MITRA].[dbo].[vw_m_departemen_dropdown]
                        )
                        SELECT id_perusahaan, dept_label
                        FROM source_rows
                        WHERE dept_label IS NOT NULL
                          AND perusahaan_deleted_at IS NULL
                          AND (
                              perusahaan_status IS NULL
                              OR TRY_CONVERT(bit, perusahaan_status) = 1
                              OR UPPER(perusahaan_status) IN ('Y', 'YES', 'TRUE', 'AKTIF', 'ACTIVE')
                          )
                          AND (
                              departemen_status IS NULL
                              OR TRY_CONVERT(bit, departemen_status) = 1
                              OR UPPER(departemen_status) IN ('Y', 'YES', 'TRUE', 'AKTIF', 'ACTIVE')
                          )
                        GROUP BY id_perusahaan, dept_label
                        ORDER BY id_perusahaan, MIN(sort_order), dept_label";

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
                            var deptName = reader["dept_label"]?.ToString()?.Trim();
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

            return map;
        }

        /// <summary>
        /// Retrieves list of active companies from [ONE_DB_MITRA].dbo.vw_m_departemen_dropdown.
        /// </summary>
        public async Task<List<CompanyDropdownItem>> GetCompaniesAsync()
        {
            try
            {
                var conn = _context.Database.GetDbConnection();
                bool wasClosed = conn.State == ConnectionState.Closed;
                if (wasClosed) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        WITH source_rows AS (
                            SELECT
                                id_perusahaan,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50), kode_perusahaan))), '') AS kode_perusahaan,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(250), nama_perusahaan))), '') AS nama_perusahaan,
                                ISNULL(sort_order, 999) AS sort_order,
                                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), perusahaan_status_aktif))), '') AS perusahaan_status,
                                perusahaan_deleted_at
                            FROM [ONE_DB_MITRA].[dbo].[vw_m_departemen_dropdown]
                        )
                        SELECT
                            id_perusahaan,
                            MAX(kode_perusahaan) AS kode_perusahaan,
                            nama_perusahaan,
                            MIN(sort_order) AS sort_order
                        FROM source_rows
                        WHERE nama_perusahaan IS NOT NULL
                          AND perusahaan_deleted_at IS NULL
                          AND (
                              perusahaan_status IS NULL
                              OR TRY_CONVERT(bit, perusahaan_status) = 1
                              OR UPPER(perusahaan_status) IN ('Y', 'YES', 'TRUE', 'AKTIF', 'ACTIVE')
                          )
                        GROUP BY id_perusahaan, nama_perusahaan
                        ORDER BY MIN(sort_order), nama_perusahaan";

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

                    return companies;
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

            return new List<CompanyDropdownItem>();
        }
    }
}
