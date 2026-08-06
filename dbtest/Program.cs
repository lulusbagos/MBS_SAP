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
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var names = new List<string> {
                "CV SALIM BERKAT SEJAHTERA",
                "GOGM PT UNGGUL DINAMIKA UTAMA",
                "PT BORNEO MAJU JAYA",
                "PT CAHAYA ENGINEERING SERVICES",
                "PT GROUNDPROBE INDONESIA",
                "PT INDO TRAKTOR UTAMA",
                "PT INDONESIA COMNETS PLUS",
                "PT LANGIT MANDIRI SUKSES",
                "PT PETRO PERKASA INDONESIA",
                "PT PRESISI DIGITAL MODEREN TEKNOLOGI",
                "PT PUTERA WIBOWO BORNEO",
                "PT SAMUDERA INTEGRASI GEMILANG",
                "PT SANGGAR SARANA BAJA",
                "PT SANY HEAVY INDUSTRY INDONESIA",
                "PT SPEEDWORK SOLUSI UTAMA",
                "PT UNGGUL DIESEL PART",
                "RUMAH SAKIT PUPUK KALTIM",
                "SISWA MAGANG UNGGUL"
            };

            var comps = await context.Perusahaans.Where(p => names.Contains(p.NamaPerusahaan.ToUpper())).ToListAsync();
            Console.WriteLine($"Found {comps.Count} companies.");
            foreach(var c in comps)
            {
                Console.WriteLine($"{c.PerusahaanId}: {c.NamaPerusahaan}");
            }
            Console.WriteLine($"Array format: {string.Join(", ", comps.Select(c => c.PerusahaanId))}");
        }
    }
}
