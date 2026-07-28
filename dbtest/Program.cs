using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MBS_SAP.Data;
using MBS_SAP.Services;

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

            services.Configure<PostgresReplicationOptions>(configuration.GetSection("PostgresReplication"));
            services.AddScoped<PostgresReplicationService>();

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var replicationService = scope.ServiceProvider.GetRequiredService<PostgresReplicationService>();

            var nik = "24021850940";

            Console.WriteLine("Executing replication (lookback 30 days)...");
            var result = await replicationService.ReplicateAsync(30);

            var iCount = await db.Inspections.CountAsync(i => !i.IsDeleted && i.Nik == nik);
            var hCount = await db.HazardReports.CountAsync(h => !h.IsDeleted && h.Nik == nik);
            var oCount = await db.Observations.CountAsync(o => !o.IsDeleted && o.Nik == nik);

            Console.WriteLine("\n================================================");
            Console.WriteLine($"  STATISTIK RECORD DI MBS UNTUK NIK {nik}:");
            Console.WriteLine("================================================");
            Console.WriteLine($"  Inspeksi     : {iCount}");
            Console.WriteLine($"  Hazard Report: {hCount}");
            Console.WriteLine($"  Observasi    : {oCount}");
            Console.WriteLine("================================================");
        }
    }
}
