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
            var configPath = File.Exists("appsettings.json") 
                ? Path.GetFullPath("appsettings.json") 
                : Path.Combine(Directory.GetCurrentDirectory(), "MBS_SAP", "appsettings.json");
            if (!File.Exists(configPath))
            {
                configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            }
            if (!File.Exists(configPath))
            {
                configPath = @"d:\4. PROJECT\2. Web\MBS_SAP\appsettings.json";
            }
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

            await context.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

            string targetNik = "23021900738";
            Console.WriteLine($"Looking for NIK: {targetNik}");

            var kRaw = await context.Karyawans.AsNoTracking().FirstOrDefaultAsync(k => k.NoNik == targetNik);
            if (kRaw == null)
            {
                Console.WriteLine("Karyawan NOT FOUND in Karyawans table!");
                return;
            }
            Console.WriteLine($"Found Karyawan ID: {kRaw.IdKaryawan}, Personal: {kRaw.IdPersonal}, Dept: {kRaw.IdDepartemen}, Jabatan: {kRaw.IdJabatan}, Comp: {kRaw.IdPerusahaan}");

            var pRaw = await context.Personals.AsNoTracking().FirstOrDefaultAsync(p => p.IdPersonal == kRaw.IdPersonal);
            var dRaw = await context.Departemens.AsNoTracking().FirstOrDefaultAsync(d => d.DepartemenId == kRaw.IdDepartemen);
            var jRaw = await context.Jabatans.AsNoTracking().FirstOrDefaultAsync(j => j.JabatanId == kRaw.IdJabatan);
            var cRaw = await context.Perusahaans.AsNoTracking().FirstOrDefaultAsync(c => c.PerusahaanId == kRaw.IdPerusahaan);

            Console.WriteLine($"Nama: {pRaw?.NamaLengkap}");
            Console.WriteLine($"Company: {cRaw?.NamaPerusahaan} ({kRaw.IdPerusahaan})");
            Console.WriteLine($"Dept: {dRaw?.NamaDepartemen} ({kRaw.IdDepartemen})");
            Console.WriteLine($"Jabatan: {jRaw?.NamaJabatan} ({kRaw.IdJabatan})");
            Console.WriteLine($"Status: {kRaw.StatusAktif}, TglMasuk: {kRaw.TanggalMasuk}");

            var mapping = await context.KaryawanJabatanMappings.AsNoTracking()
                .FirstOrDefaultAsync(m => m.KaryawanId == kRaw.IdKaryawan);
            
            if (mapping != null)
            {
                Console.WriteLine($"Mapping: Hazard={mapping.TargetHazardReport}, Inspeksi={mapping.TargetInspeksi}, SafetyTalk={mapping.TargetSafetyTalk}, Observasi={mapping.TargetObservasi}, Coaching={mapping.TargetCoaching}");
            }
            else
            {
                Console.WriteLine("No custom mapping found (default targets apply)");
            }

            // Check rosters
            var rosters = await context.Rosters.AsNoTracking()
                .Where(r => r.Nik == targetNik)
                .ToListAsync();
            Console.WriteLine($"Rosters count: {rosters.Count}");
            foreach (var r in rosters)
            {
                Console.WriteLine($"  Roster: {r.TipeRoster} {r.AwalDinas:yyyy-MM-dd} to {r.AkhirDinas:yyyy-MM-dd}");
            }

            // Check existing activities for 2026-09
            var hList = await context.HazardReports.AsNoTracking().Where(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= startOfMonth && x.Tanggal <= endOfMonth).ToListAsync();
            var iList = await context.Inspections.AsNoTracking().Where(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= startOfMonth && x.Tanggal <= endOfMonth).ToListAsync();
            var stList = await context.SafetyTalks.AsNoTracking().Where(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= startOfMonth && x.Tanggal <= endOfMonth).ToListAsync();
            var oList = await context.Observations.AsNoTracking().Where(x => !x.IsDeleted && x.Nik == targetNik && x.CreatedAt >= startOfMonth && x.CreatedAt <= endOfMonth).ToListAsync();
            var cList = await context.Coachings.AsNoTracking().Where(x => !x.IsDeleted && x.Nik == targetNik && x.CreatedAt >= startOfMonth && x.CreatedAt <= endOfMonth).ToListAsync();
            var cpList = await context.CoachingParticipants.AsNoTracking().Where(x => x.Nik == targetNik && x.Coaching != null && !x.Coaching.IsDeleted && x.Coaching.CreatedAt >= startOfMonth && x.Coaching.CreatedAt <= endOfMonth).ToListAsync();
            var pList = await context.P5ms.AsNoTracking().Where(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= startOfMonth && x.Tanggal <= endOfMonth).ToListAsync();

            Console.WriteLine($"Actual 2026-09: Hazard={hList.Count}, Inspeksi={iList.Count}, SafetyTalk={stList.Count}, Observasi={oList.Count}, Coaching={cList.Count + cpList.Count}, P5M={pList.Count}");
        }
    }
}
