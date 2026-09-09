using MBS_SAP.Data;
using MBS_SAP.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        /// If the company has no direct departments (e.g. child contractor / subcon / project),
        /// it looks up departments from parent companies in hierarchy relations, parent company ID,
        /// or employee mappings, avoiding fallback to unrelated company departments.
        /// </summary>
        public async Task<List<string>> GetDepartmentsByCompanyAsync(int companyId)
        {
            if (companyId <= 0) return new List<string>();

            // 1. Direct departments in vw_departemen
            var depts = await _context.Departemens
                .AsNoTracking()
                .Where(d => d.IdPerusahaan == companyId && (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen))
                .OrderBy(d => d.NamaDepartemen)
                .Select(d => d.NamaDepartemen!)
                .Distinct()
                .ToListAsync();

            if (depts.Any()) return depts;

            // 2. Parent company departments via hierarchy relations
            var parentDepts = await (
                from h in _context.PerusahaanHierarchyRelations.AsNoTracking()
                where h.ChildCompanyId == companyId && h.ParentCompanyId != null
                join d in _context.Departemens.AsNoTracking() on h.ParentCompanyId equals d.IdPerusahaan
                where (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen)
                orderby d.NamaDepartemen
                select d.NamaDepartemen!
            ).Distinct().ToListAsync();

            if (parentDepts.Any()) return parentDepts;

            // 3. Parent company departments via PerusahaanIndukId
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

            // 4. Departments from Employees belonging to this company
            var empDepts = await (
                from k in _context.Karyawans.AsNoTracking()
                where k.IdPerusahaan == companyId && k.IdDepartemen != null
                join d in _context.Departemens.AsNoTracking() on k.IdDepartemen equals (int?)d.DepartemenId
                where (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen)
                orderby d.NamaDepartemen
                select d.NamaDepartemen!
            ).Distinct().ToListAsync();

            if (empDepts.Any()) return empDepts;

            // 5. Name match heuristic (e.g. if company name contains parent name like "UNGGUL DINAMIKA UTAMA")
            if (comp != null && !string.IsNullOrWhiteSpace(comp.NamaPerusahaan))
            {
                var targetName = comp.NamaPerusahaan.Trim();
                var allActiveComps = await _context.Perusahaans.AsNoTracking()
                    .Where(p => p.PerusahaanId != companyId && p.StatusAktif)
                    .ToListAsync();

                var matchingComp = allActiveComps.FirstOrDefault(p =>
                    (!string.IsNullOrEmpty(p.NamaPerusahaan) && p.NamaPerusahaan.Length >= 5 && targetName.Contains(p.NamaPerusahaan, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(p.NamaPerusahaan) && targetName.Length >= 5 && p.NamaPerusahaan.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                );

                if (matchingComp != null)
                {
                    var matchedDepts = await _context.Departemens
                        .AsNoTracking()
                        .Where(d => d.IdPerusahaan == matchingComp.PerusahaanId && (d.StatusAktif == null || (d.StatusAktif != "N" && d.StatusAktif != "0")) && !string.IsNullOrEmpty(d.NamaDepartemen))
                        .OrderBy(d => d.NamaDepartemen)
                        .Select(d => d.NamaDepartemen!)
                        .Distinct()
                        .ToListAsync();

                    if (matchedDepts.Any()) return matchedDepts;
                }
            }

            return new List<string>();
        }
    }
}
