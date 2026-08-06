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

            var targetCompanyIds = new List<int> { 360 };
            
            var companies = await db.Perusahaans
                .Where(p => targetCompanyIds.Contains(p.PerusahaanId) || p.NamaPerusahaan.Contains("EKA DHARMA") || p.NamaPerusahaan.Contains("ANUGERAH AC"))
                .ToListAsync();

            foreach(var c in companies) {
                Console.WriteLine($"Company: ID {c.PerusahaanId}: {c.NamaPerusahaan} (Parent: {c.PerusahaanIndukId})");
            }
            
            var relations = await db.PerusahaanHierarchyRelations
                .Where(r => r.ParentCompanyId == 360 || r.ChildCompanyId == 360 || r.ParentCompanyName.Contains("EKA DHARMA"))
                .ToListAsync();
                
            foreach(var r in relations) {
                Console.WriteLine($"Relation: Parent {r.ParentCompanyId} ({r.ParentCompanyName}) -> Child {r.ChildCompanyId} (Active: {r.ChildIsActive})");
            }
        }
    }
}
