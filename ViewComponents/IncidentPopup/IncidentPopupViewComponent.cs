using Microsoft.AspNetCore.Mvc;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MBS_SAP.ViewComponents
{
    public class IncidentPopupViewComponent : ViewComponent
    {
        private readonly AppDbContext _context;
        public IncidentPopupViewComponent(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (UserClaimsPrincipal?.Identity?.IsAuthenticated != true)
            {
                return Content(string.Empty);
            }

            var sevenDaysAgo = DateTime.Now.AddDays(-7);

            var incident = await _context.IncidentNewsList
                .Where(i => i.IsPublished && (i.TanggalKejadian ?? i.CreatedAt) >= sevenDaysAgo)
                .OrderByDescending(i => (i.TanggalKejadian ?? i.CreatedAt))
                .ThenByDescending(i => i.Id)
                .FirstOrDefaultAsync<IncidentNews>();
            return View(incident);
        }
    }
}
