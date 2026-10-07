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

            // Execute ALTER VIEW to update target_safety_talk in database
            try
            {
                var sqlFile = Path.Combine(Directory.GetCurrentDirectory(), "dbtest", "view_definition.sql");
                if (!File.Exists(sqlFile))
                {
                    sqlFile = "view_definition.sql";
                }
                var alterSql = await File.ReadAllTextAsync(sqlFile);
                Console.WriteLine("Executing ALTER VIEW dbo.vw_r_karyawan_jabatan_mapping_preview...");
                await context.Database.ExecuteSqlRawAsync(alterSql);
                Console.WriteLine("SUCCESS: ALTER VIEW executed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing ALTER VIEW: {ex.Message}");
            }
            // Test CSR employees
            var csrEmps = await context.Karyawans.AsNoTracking()
                .Where(k => k.IdDepartemen == 3 && k.StatusAktif)
                .Select(k => new { k.NoNik, k.IdKaryawan })
                .ToListAsync();
            Console.WriteLine($"CSR active employees: {csrEmps.Count}");
            foreach (var ce in csrEmps)
            {
                var p = await context.Personals.AsNoTracking().FirstOrDefaultAsync(x => x.IdPersonal == ce.IdKaryawan);
                var rList = await context.Rosters.AsNoTracking().Where(r => r.Nik == ce.NoNik).ToListAsync();
                Console.WriteLine($"NIK: {ce.NoNik}, Rosters count: {rList.Count}");
                foreach (var r in rList)
                {
                    Console.WriteLine($"   Dinas: {r.AwalDinas:yyyy-MM-dd}..{r.AkhirDinas:yyyy-MM-dd}, Cuti: {r.AwalCuti:yyyy-MM-dd}..{r.AkhirCuti:yyyy-MM-dd}");
                }
            }
            string[] targetNiks = new[] { "25022021183", "25051771130", "23062940814" };
            foreach (var targetNik in targetNiks)
            {
                Console.WriteLine($"\n==========================================");
                Console.WriteLine($"Looking for NIK: {targetNik}");

                var kRaw = await context.Karyawans.AsNoTracking().FirstOrDefaultAsync(k => k.NoNik == targetNik);
                if (kRaw == null)
                {
                    Console.WriteLine("Karyawan NOT FOUND in Karyawans table!");
                    continue;
                }

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
                    Console.WriteLine($"KaryawanJabatanMapping (Personal): StandardJabatan='{mapping.NamaJabatanStandar}', KategoriPengawas='{mapping.KategoriPengawas}', Hazard={mapping.TargetHazardReport}, Inspeksi={mapping.TargetInspeksi}, SafetyTalk={mapping.TargetSafetyTalk}, Observasi={mapping.TargetObservasi}, Coaching={mapping.TargetCoaching}, AlasanTargetZero='{mapping.AlasanTargetZero}'");
                }
                else
                {
                    Console.WriteLine("No custom KaryawanJabatanMapping found for this KaryawanId.");
                }

                var rosters = await context.Rosters.AsNoTracking()
                    .Where(r => r.Nik == targetNik)
                    .OrderByDescending(r => r.AwalDinas)
                    .ToListAsync();
                Console.WriteLine($"Rosters count: {rosters.Count}");
                foreach (var r in rosters)
                {
                    Console.WriteLine($"  Roster Id={r.Id}: {r.TipeRoster} | Dinas: {r.AwalDinas:yyyy-MM-dd} to {r.AkhirDinas:yyyy-MM-dd} | Cuti: {r.AwalCuti:yyyy-MM-dd} to {r.AkhirCuti:yyyy-MM-dd} | Ket: {r.Keterangan}");
                }

                // Simulate GetEmployeesComplianceData for Sep and Oct 2026
                foreach (int mth in new[] { 9, 10 })
                {
                    var sDate = new DateTime(2026, mth, 1);
                    var eDate = sDate.AddMonths(1).AddTicks(-1);
                    int totalDaysInMonth = DateTime.DaysInMonth(2026, mth);
                    int onsiteDays = totalDaysInMonth;
                    bool hasRoster = false;
                    DateTime effectiveEmpStart = (kRaw.TanggalMasuk.HasValue && kRaw.TanggalMasuk.Value > sDate)
                        ? kRaw.TanggalMasuk.Value
                        : sDate;

                    // Check if employee has roster covering this month
                    var monthRosters = rosters.Where(r => 
                        r.AwalDinas <= eDate && (r.AkhirCuti >= sDate || r.AkhirDinas >= sDate)
                    ).ToList();

                    int computedOnsite = 0;
                    if (monthRosters.Any())
                    {
                        foreach (var r in monthRosters)
                        {
                            if (r.TipeRoster == "TUGAS") continue;
                            var overlapStart = r.AwalDinas > effectiveEmpStart ? r.AwalDinas : effectiveEmpStart;
                            var overlapEnd = r.AkhirDinas < eDate ? r.AkhirDinas : eDate;
                            if (overlapStart <= overlapEnd)
                            {
                                computedOnsite += (overlapEnd - overlapStart).Days + 1;
                            }
                        }
                        hasRoster = true;
                        onsiteDays = Math.Min(computedOnsite, totalDaysInMonth);
                    }
                    else
                    {
                        // Roster belum disetting untuk bulan ini -> Target FULL!
                        hasRoster = false;
                        onsiteDays = totalDaysInMonth;
                    }

                    double ratio = hasRoster ? Math.Min(1.0, (double)onsiteDays / totalDaysInMonth) : 1.0;
                    int ScaleTarget(int baseTarget, double rat, int daysOnsite)
                    {
                        if (baseTarget == 0) return 0;
                        if (daysOnsite == 0) return 0;
                        int scaled = (int)Math.Round(baseTarget * Math.Min(1.0, rat), MidpointRounding.AwayFromZero);
                        return Math.Min(baseTarget, Math.Max(scaled, 1));
                    }

                    int hTar = mapping?.TargetHazardReport ?? 2;
                    int insTar = mapping?.TargetInspeksi ?? 1;
                    int stTar = mapping?.TargetSafetyTalk ?? 1;
                    int obsTar = mapping?.TargetObservasi ?? 0;
                    int cTar = mapping?.TargetCoaching ?? 0;

                    int mtdTgtH = hasRoster ? ScaleTarget(hTar, ratio, onsiteDays) : hTar;
                    int mtdTgtI = hasRoster ? ScaleTarget(insTar, ratio, onsiteDays) : insTar;
                    int mtdTgtST = hasRoster ? ScaleTarget(stTar, ratio, onsiteDays) : stTar;
                    int mtdTgtO = hasRoster ? ScaleTarget(obsTar, ratio, onsiteDays) : obsTar;
                    int mtdTgtC = hasRoster ? ScaleTarget(cTar, ratio, onsiteDays) : cTar;
                    int totalTgt = mtdTgtH + mtdTgtI + mtdTgtST + mtdTgtO + mtdTgtC;

                    var hAct = await context.HazardReports.AsNoTracking().CountAsync(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= sDate && x.Tanggal <= eDate);
                    var iAct = await context.Inspections.AsNoTracking().CountAsync(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= sDate && x.Tanggal <= eDate);
                    var stAct = await context.SafetyTalks.AsNoTracking().CountAsync(x => !x.IsDeleted && x.Nik == targetNik && x.Tanggal >= sDate && x.Tanggal <= eDate);
                    var oAct = await context.Observations.AsNoTracking().CountAsync(x => !x.IsDeleted && x.Nik == targetNik && x.CreatedAt >= sDate && x.CreatedAt <= eDate);
                    var cAct = await context.Coachings.AsNoTracking().CountAsync(x => !x.IsDeleted && x.Nik == targetNik && x.CreatedAt >= sDate && x.CreatedAt <= eDate);

                    int totalAct = Math.Min(hAct, mtdTgtH) + Math.Min(iAct, mtdTgtI) + Math.Min(stAct, mtdTgtST) + Math.Min(oAct, mtdTgtO) + Math.Min(cAct, mtdTgtC);
                    double compliance = totalTgt > 0 ? Math.Round((double)totalAct / totalTgt * 100.0, 1) : 0;

                    Console.WriteLine($"Sim 2026-{mth:D2}: hasRoster={hasRoster}, onsiteDays={onsiteDays}/{totalDaysInMonth}, Targets=[H:{mtdTgtH}, I:{mtdTgtI}, ST:{mtdTgtST}, O:{mtdTgtO}, C:{mtdTgtC}], TotalTgt={totalTgt}, TotalAct={totalAct}, Compliance={compliance}%");
                }
            }

            // Check distribution of TargetSafetyTalk across all active employees
            Console.WriteLine("\n==========================================");
            Console.WriteLine("Distribution of TargetSafetyTalk in KaryawanJabatanMappings:");
            var allMappings = await context.KaryawanJabatanMappings.AsNoTracking().ToListAsync();
            var groupedByST = allMappings.GroupBy(m => new { m.KategoriPengawas, m.TargetSafetyTalk })
                .OrderBy(g => g.Key.KategoriPengawas)
                .ThenBy(g => g.Key.TargetSafetyTalk);
            
            foreach (var g in groupedByST)
            {
                Console.WriteLine($"Kategori: '{g.Key.KategoriPengawas}', TargetSafetyTalk: {g.Key.TargetSafetyTalk} => Count: {g.Count()}");
            }
        }
    }
}
