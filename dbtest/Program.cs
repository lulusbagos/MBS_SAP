using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MBS_SAP.Data;
using MBS_SAP.Models;

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
                Console.WriteLine("=== RELATIONSHIPS IN tbl_r_perusahaan FOR MGE (Parent ID = 5) ===");
                
                try
                {
                    var sql = @"
                        SELECT r.id, r.id_parent, r.id_perusahaan, r.status_aktif, 
                               p.nama_perusahaan as child_name
                        FROM ONE_DB_MITRA.dbo.tbl_r_perusahaan r
                        LEFT JOIN ONE_DB_MITRA.dbo.tbl_m_perusahaan p ON p.id = r.id_perusahaan
                        WHERE r.id_parent = 5 AND r.deleted_at IS NULL";
                        
                    var conn = context.Database.GetDbConnection();
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        using (var reader = cmd.ExecuteReader())
                        {
                            int count = 0;
                            while (reader.Read())
                            {
                                Console.WriteLine($"ID={reader["id"]}, Parent={reader["id_parent"]}, Child={reader["id_perusahaan"]} ({reader["child_name"]}), Status={reader["status_aktif"]}");
                                count++;
                            }
                            if (count == 0)
                            {
                                Console.WriteLine("No relationship records found in tbl_r_perusahaan for parent ID = 5.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }
    }
}
