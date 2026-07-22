using System;
using Microsoft.Data.SqlClient;

namespace dbtest
{
    class Program
    {
        static void Main(string[] args)
        {
            var connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;";

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                Console.WriteLine("=== DB_SAP REPLICATION SUMMARY FOR NIK 26071701184 ===");
                
                string[] tables = new[] {
                    "tbl_t_inspection",
                    "tbl_t_hazard_report",
                    "tbl_t_coaching",
                    "tbl_t_observation",
                    "tbl_t_p2h_report",
                    "tbl_t_p5m",
                    "tbl_t_safety_talk",
                    "tbl_t_app_user"
                };

                foreach (var table in tables)
                {
                    using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM {table} WITH (NOLOCK) WHERE nik = '26071701184'", conn))
                    {
                        Console.WriteLine($"  {table}: {cmd.ExecuteScalar()} row(s)");
                    }
                }
            }
        }
    }
}
