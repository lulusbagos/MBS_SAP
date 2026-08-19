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

            var nik = "23091840871";
            var obs = await context.Observations
                .Where(o => o.Nik == nik && !o.IsDeleted)
                .ToListAsync();

            Console.WriteLine($"Total Observations for {nik}: {obs.Count}");

            var grouped = obs.GroupBy(o => new {
                Date = o.Date.Date,
                Time = o.Date.TimeOfDay,
                Kegiatan = o.KegiatanYangDiamati,
                Perihal = o.PerihalYangDiamati
            }).Where(g => g.Count() > 1).ToList();

            Console.WriteLine($"Duplicate groups found: {grouped.Count}");
            foreach (var g in grouped.Take(5))
            {
                Console.WriteLine($"- Date: {g.Key.Date:yyyy-MM-dd}, Time: {g.Key.Time}, Kegiatan: {g.Key.Kegiatan}, Perihal: {g.Key.Perihal}, Count: {g.Count()}");
                foreach (var item in g)
                {
                    Console.WriteLine($"   -> Id: {item.Id}, CreatedAt: {item.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                }
            }
            
            // Also let's check BuildObservationKey logic in ReplicationService by looking at how replication stores things.
        }
    }
}
