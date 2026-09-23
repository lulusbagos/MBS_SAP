using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MBS_SAP.Data;
using System.Collections.Generic;

namespace dbtest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "appsettings.json");
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false, reloadOnChange: true)
                .Build();

            var sqlConnStr = configuration.GetConnectionString("DefaultConnection");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnStr));

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            int selectedYear = 2026;
            int selectedMonth = 9;
            var startOfMonth = new DateTime(selectedYear, selectedMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

            bool isClosed(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                var t = s.Trim();
                return t.Equals("Closed", StringComparison.OrdinalIgnoreCase) || t.Equals("Close", StringComparison.OrdinalIgnoreCase) || t.Equals("Selesai", StringComparison.OrdinalIgnoreCase) || t.Equals("Complete", StringComparison.OrdinalIgnoreCase);
            }

            var allMtdAp = await context.ActionPlans
                .AsNoTracking()
                .Where(a => !a.IsDeleted && ((a.Tanggal >= startOfMonth && a.Tanggal <= endOfMonth) || (a.CreatedAt >= startOfMonth && a.CreatedAt <= endOfMonth)))
                .Select(a => new { a.Id, a.Tanggal, a.Status, a.Departemen, a.DepartemenPja, a.DepartemenPic, a.PerusahaanId })
                .ToListAsync();

            var depts = allMtdAp
                .Select(a => !string.IsNullOrEmpty(a.DepartemenPja) ? a.DepartemenPja.Trim() : (!string.IsNullOrEmpty(a.DepartemenPic) ? a.DepartemenPic.Trim() : (!string.IsNullOrEmpty(a.Departemen) ? a.Departemen.Trim() : "General")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d)
                .ToList();

            var sheHaulingAp = allMtdAp.Where(a => 
                (!string.IsNullOrEmpty(a.DepartemenPja) && string.Equals(a.DepartemenPja.Trim(), "SHE HAULING", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(a.DepartemenPic) && string.Equals(a.DepartemenPic.Trim(), "SHE HAULING", StringComparison.OrdinalIgnoreCase))
            ).ToList();

            Console.WriteLine($"=== SHE HAULING ASSIGNED ITEMS ===");
            foreach (var a in sheHaulingAp)
            {
                Console.WriteLine($"ID: {a.Id} | Status: {a.Status} | Dept: {a.Departemen} | PJA: {a.DepartemenPja} | PIC: {a.DepartemenPic}");
            }
        }
    }
}
