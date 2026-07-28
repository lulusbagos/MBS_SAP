using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MBS_SAP.Data;

namespace dbtest
{
    class CheckCounts
    {
        public static async Task Main(string[] args)
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "appsettings.json");
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            var sqlConnStr = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnStr));

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var nik = "24021850940";

            var iCountAll = await db.Inspections.CountAsync(i => !i.IsDeleted && i.Nik == nik);
            var hCountAll = await db.HazardReports.CountAsync(h => !h.IsDeleted && h.Nik == nik);
            var oCountAll = await db.Observations.CountAsync(o => !o.IsDeleted && o.Nik == nik);

            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var iCountMonth = await db.Inspections.CountAsync(i => !i.IsDeleted && i.Nik == nik && i.Tanggal >= startOfMonth);
            var hCountMonth = await db.HazardReports.CountAsync(h => !h.IsDeleted && h.Nik == nik && h.Tanggal >= startOfMonth);
            var oCountMonth = await db.Observations.CountAsync(o => !o.IsDeleted && o.Nik == nik && o.Date >= startOfMonth);

            Console.WriteLine("=================================================");
            Console.WriteLine($"  REKAP JUMLAH RECORD DI MBS UNTUK NIK {nik}:");
            Console.WriteLine("=================================================");
            Console.WriteLine($"  1. Inspeksi    : {iCountMonth} (Bulan ini) | Total Keseluruhan: {iCountAll}");
            Console.WriteLine($"  2. Hazard      : {hCountMonth} (Bulan ini) | Total Keseluruhan: {hCountAll}");
            Console.WriteLine($"  3. Observasi   : {oCountMonth} (Bulan ini) | Total Keseluruhan: {oCountAll}");
            Console.WriteLine("=================================================");
        }
    }
}
