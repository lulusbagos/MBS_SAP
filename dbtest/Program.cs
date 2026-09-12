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
using Npgsql;

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
            var pgConnStr = configuration.GetSection("PostgresReplication")["ConnectionString"];

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnStr));

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Console.WriteLine("=======================================================================");
            Console.WriteLine(" DIAGNOSTIK KINERJA CLOSURE / ACTION PLAN PT KALIMANTAN PRIMA PERSADA ");
            Console.WriteLine("=======================================================================\n");

            // 1. Cek PostgreSQL vs SQL Server untuk bulan September 2026
            var startDate = new DateTime(2026, 9, 1);
            var endDate = new DateTime(2026, 9, 30, 23, 59, 59);

            Console.WriteLine("--- 1. POSTGRESQL vs SQL SERVER: STATUS TEMUAN HAZARD (September 2026) ---");
            if (!string.IsNullOrEmpty(pgConnStr))
            {
                using var pgConn = new NpgsqlConnection(pgConnStr);
                await pgConn.OpenAsync();
                using var cmd = pgConn.CreateCommand();
                cmd.CommandText = @"
                    SELECT 
                        company_name,
                        status_temuan,
                        COUNT(*) as cnt
                    FROM vw_hazardreportdetail
                    WHERE tanggal >= '2026-09-01' AND tanggal <= '2026-09-30'
                      AND (company_name ILIKE '%KALIMANTAN PRIMA PERSADA%' 
                           OR company_name ILIKE '%UNGGUL DINAMIKA UTAMA%'
                           OR company_name ILIKE '%MEGA GLOBAL ENERGY%'
                           OR company_name ILIKE '%INDEXIM COALINDO%')
                    GROUP BY company_name, status_temuan
                    ORDER BY company_name, status_temuan;";
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    Console.WriteLine($"[PG] Company: {r[0],-30} | Status: {r[1],-10} | Count: {r[2]}");
                }
            }

            Console.WriteLine("\n--- 2. SQL SERVER: STATUS TEMUAN HAZARD (September 2026) ---");
            var hazardsInDb = await context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && h.Tanggal >= startDate && h.Tanggal <= endDate)
                .GroupBy(h => new { h.PerusahaanId, h.StatusTemuan })
                .Select(g => new { g.Key.PerusahaanId, g.Key.StatusTemuan, Count = g.Count() })
                .ToListAsync();

            var companies = await context.Perusahaans.AsNoTracking().ToDictionaryAsync(p => p.PerusahaanId, p => p.NamaPerusahaan);

            foreach (var h in hazardsInDb.OrderBy(x => x.PerusahaanId).ThenBy(x => x.StatusTemuan))
            {
                var compName = h.PerusahaanId.HasValue && companies.ContainsKey(h.PerusahaanId.Value) ? companies[h.PerusahaanId.Value] : $"ID {h.PerusahaanId}";
                Console.WriteLine($"[SQL-Hazard] Company: {compName,-30} | Status: {h.StatusTemuan,-10} | Count: {h.Count}");
            }

            Console.WriteLine("\n--- 3. SQL SERVER: ACTION PLANS (September 2026) ---");
            var apsInDb = await context.ActionPlans.AsNoTracking()
                .Where(a => !a.IsDeleted && a.Tanggal >= startDate && a.Tanggal <= endDate)
                .GroupBy(a => new { a.PerusahaanId, a.Status })
                .Select(g => new { g.Key.PerusahaanId, g.Key.Status, Count = g.Count() })
                .ToListAsync();

            foreach (var a in apsInDb.OrderBy(x => x.PerusahaanId).ThenBy(x => x.Status))
            {
                var compName = a.PerusahaanId.HasValue && companies.ContainsKey(a.PerusahaanId.Value) ? companies[a.PerusahaanId.Value] : $"ID {a.PerusahaanId}";
                Console.WriteLine($"[SQL-AP]     Company: {compName,-30} | Status: {a.Status,-10} | Count: {a.Count}");
            }

            Console.WriteLine("\n--- 4. BREAKDOWN KPP ACTION PLANS BERDASARKAN DEPARTEMEN & PIC (September 2026) ---");
            var kppAps = await context.ActionPlans.AsNoTracking()
                .Where(a => !a.IsDeleted && a.PerusahaanId == 4 && a.Tanggal >= startDate && a.Tanggal <= endDate)
                .ToListAsync();

            var byDept = kppAps.GroupBy(a => a.Departemen ?? "(Tanpa Departemen)")
                .Select(g => new {
                    Dept = g.Key,
                    Total = g.Count(),
                    Closed = g.Count(x => x.Status == "Closed" || x.Status == "Selesai"),
                    OpenWithPlan = g.Count(x => (x.Status == "Open" || x.Status == null) && !string.IsNullOrEmpty(x.RencanaPerbaikan)),
                    OpenNoPlan = g.Count(x => (x.Status == "Open" || x.Status == null) && string.IsNullOrEmpty(x.RencanaPerbaikan))
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            Console.WriteLine($"{"Departemen",-25} | {"Total",-6} | {"Closed",-6} | {"Open (Ada Rencana)",-20} | {"Open (Tanpa Rencana)",-20} | {"Close Rate (%)",-15}");
            Console.WriteLine(new string('-', 105));
            foreach (var d in byDept)
            {
                double rate = d.Total > 0 ? Math.Round((double)d.Closed / d.Total * 100.0, 1) : 0;
                Console.WriteLine($"{d.Dept,-25} | {d.Total,6} | {d.Closed,6} | {d.OpenWithPlan,20} | {d.OpenNoPlan,20} | {rate,15:F1}%");
            }

            Console.WriteLine("\n--- 5. DETAIL TEMUAN KPP YANG MASIH OPEN DENGAN RISIKO TINGGI / CRITICAL ---");
            var kppHazardsOpen = await context.HazardReports.AsNoTracking()
                .Where(h => !h.IsDeleted && h.PerusahaanId == 4 && h.Tanggal >= startDate && h.Tanggal <= endDate && h.StatusTemuan == "Open")
                .OrderByDescending(h => h.Tanggal)
                .Take(10)
                .ToListAsync();

            foreach (var h in kppHazardsOpen)
            {
                Console.WriteLine($"[Tgl: {h.Tanggal:yyyy-MM-dd}] [Risk: {h.TingkatResiko,-8}] [Dept: {h.Departemen,-15}] [PJA: {h.Pja,-20}] {h.Temuan}");
            }
        }
    }
}

