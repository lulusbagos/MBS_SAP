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

            // 1. Action plans assigned specifically to SHE HAULING (PJA/PIC) in September 2026
            var assignedMtd = await context.ActionPlans
                .Where(a => !a.IsDeleted && (
                    (a.DepartemenPja != null && a.DepartemenPja.Trim().ToLower() == "she hauling") ||
                    (a.DepartemenPic != null && a.DepartemenPic.Trim().ToLower() == "she hauling")
                ) && ((a.Tanggal >= startOfMonth && a.Tanggal <= endOfMonth) || (a.CreatedAt >= startOfMonth && a.CreatedAt <= endOfMonth)))
                .Select(a => new { a.Id, a.Tanggal, a.CreatedAt, a.Status, a.Departemen, a.DepartemenPja, a.DepartemenPic, a.Pic, a.Pja, a.DetilTemuan })
                .ToListAsync();

            Console.WriteLine($"--- ACTION PLANS DIARAHKAN KE 'SHE HAULING' (PJA/PIC) - MTD Sept 2026 ---");
            Console.WriteLine($"Total: {assignedMtd.Count}, Closed: {assignedMtd.Count(a => isClosed(a.Status))}, Open: {assignedMtd.Count(a => !isClosed(a.Status))}");
            foreach (var a in assignedMtd)
            {
                Console.WriteLine($"ID: {a.Id} | Tanggal: {a.Tanggal:yyyy-MM-dd} | Status: {a.Status} | Pelapor Dept: {a.Departemen} | PJA Dept: {a.DepartemenPja} | PIC Dept: {a.DepartemenPic} | PIC: {a.Pic}");
            }

            // 2. Action plans created by SHE HAULING directed to OTHER departments
            var createdBySheMtd = await context.ActionPlans
                .Where(a => !a.IsDeleted && a.Departemen != null && a.Departemen.Contains("HAULING") &&
                    ((a.Tanggal >= startOfMonth && a.Tanggal <= endOfMonth) || (a.CreatedAt >= startOfMonth && a.CreatedAt <= endOfMonth)))
                .Select(a => new { a.Id, a.Tanggal, a.Status, a.Departemen, a.DepartemenPja, a.DepartemenPic, a.Pic })
                .ToListAsync();

            Console.WriteLine($"\n--- ACTION PLANS DIBUAT OLEH SHE HAULING (Pelapor) - MTD Sept 2026 ---");
            Console.WriteLine($"Total: {createdBySheMtd.Count}, Closed: {createdBySheMtd.Count(a => isClosed(a.Status))}, Open: {createdBySheMtd.Count(a => !isClosed(a.Status))}");
            foreach (var a in createdBySheMtd.Where(x => !isClosed(x.Status)))
            {
                Console.WriteLine($"OPEN temuan dibuat SHE: ID={a.Id} | Tanggal={a.Tanggal:yyyy-MM-dd} | Status={a.Status} | Diarahkan ke PJA Dept: {a.DepartemenPja} | PIC Dept: {a.DepartemenPic} | PIC: {a.Pic}");
            }
        }
    }
}
