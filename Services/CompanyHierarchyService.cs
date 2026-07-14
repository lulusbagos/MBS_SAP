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
    }
}
