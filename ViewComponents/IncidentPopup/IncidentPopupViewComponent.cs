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
            var threeDaysAgo = DateTime.UtcNow.AddDays(-3);
            var incident = await _context.IncidentNewsList
                .Where(i => i.IsPublished && i.CreatedAt >= threeDaysAgo)
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync<IncidentNews>();
            return View(incident);
        }
    }
}
