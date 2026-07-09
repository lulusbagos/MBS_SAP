using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.IO;
using System.Collections.Generic;

namespace dbtest
{
    class Program
    {
        static void Main(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer("Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;");

            using (var context = new AppDbContext(optionsBuilder.Options))
            {
                var parent = context.Perusahaans.FirstOrDefault(p => p.NamaPerusahaan.Contains("INDEXIM COALINDO") || p.NamaPerusahaan.Contains("PT. INDEXIM COALINDO"));
                if (parent == null) {
                    Console.WriteLine("Parent company not found!");
                    return;
                }

                Console.WriteLine($"Parent Found: {parent.NamaPerusahaan} (ID: {parent.PerusahaanId})");

                var members = context.Perusahaans.Where(p => p.PerusahaanIndukId == parent.PerusahaanId && p.StatusAktif).ToList();
                Console.WriteLine($"Found {members.Count} active member companies.");

                var neverLoggedIn = new List<string>();
                var noData = new List<string>();

                foreach (var member in members)
                {
                    bool hasUsersLogged = context.AppUsers.Any(u => u.IdPerusahaan == member.PerusahaanId);
                    if (!hasUsersLogged)
                    {
                        neverLoggedIn.Add(member.NamaPerusahaan ?? "Unknown");
                    }
                    else
                    {
                        bool hasData = 
                            context.HazardReports.Any(x => x.PerusahaanId == member.PerusahaanId) ||
                            context.Inspections.Any(x => x.PerusahaanId == member.PerusahaanId) ||
                            context.ActionPlans.Any(x => x.PerusahaanId == member.PerusahaanId) ||
                            context.SafetyTalks.Any(x => x.PerusahaanId == member.PerusahaanId) ||
                            context.P5ms.Any(x => x.PerusahaanId == member.PerusahaanId) ||
                            context.Observations.Any(x => x.Nik != null && context.AppUsers.Any(u => u.Nik == x.Nik && u.IdPerusahaan == member.PerusahaanId)) ||
                            context.Coachings.Any(x => x.PerusahaanId == member.PerusahaanId) ||
                            context.P2hReports.Any(x => x.Nik != null && context.AppUsers.Any(u => u.Nik == x.Nik && u.IdPerusahaan == member.PerusahaanId));
                        
                        if (!hasData)
                        {
                            noData.Add(member.NamaPerusahaan ?? "Unknown");
                        }
                    }
                }

                using (var writer = new StreamWriter("output.md"))
                {
                    writer.WriteLine("### 🔴 Perusahaan yang TIDAK PERNAH LOGIN sama sekali");
                    neverLoggedIn.Sort();
                    foreach (var c in neverLoggedIn) writer.WriteLine("- " + c);
                    writer.WriteLine("");
                    writer.WriteLine("### 🟡 Perusahaan yang SUDAH LOGIN tetapi BELUM ADA DATA sama sekali");
                    noData.Sort();
                    foreach (var c in noData) writer.WriteLine("- " + c);
                }

                Console.WriteLine("Done. Output written to output.md");
            }
        }
    }
}
