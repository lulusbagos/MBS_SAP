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

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

            var sqlConnStr = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnStr));

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var names = new List<string> {
                "PT INDEXIM COALINDO",
                "PT UNGGUL DINAMIKA UTAMA",
                "PT KALIMANTAN PRIMA PERSADA",
                "PT MEGA GLOBAL ENERGY"
            };

            var companies = await db.Perusahaans
                .Where(p => names.Contains(p.NamaPerusahaan))
                .ToListAsync();

            foreach(var c in companies) {
                Console.WriteLine($"ID {c.PerusahaanId}: {c.NamaPerusahaan}");
            }
        }
    }
}
