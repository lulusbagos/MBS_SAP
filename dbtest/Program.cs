using System;
using Microsoft.Data.SqlClient;

namespace dbtest
{
    class Program
    {
        static void Main(string[] args)
        {
            var connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;";

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                var companyId = 89;
                Console.WriteLine($"=== DIAGNOSING COMPANY ID: {companyId} ===");

                using (var cmd = new SqlCommand("SELECT * FROM vw_perusahaan WHERE perusahaan_id = @companyId", conn))
                {
                    cmd.Parameters.AddWithValue("@companyId", companyId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            Console.WriteLine("vw_perusahaan columns:");
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                Console.WriteLine($" - {reader.GetName(i)}: {reader.GetValue(i)}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Company not found!");
                        }
                    }
                }

                // Check parent company and hierarchy relation
                using (var cmd = new SqlCommand("SELECT * FROM vw_m_hirarki_perusahaan WHERE child_company_id = @companyId AND child_is_active = 1", conn))
                {
                    cmd.Parameters.AddWithValue("@companyId", companyId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            Console.WriteLine("\nHierarchy context:");
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                Console.WriteLine($" - {reader.GetName(i)}: {reader.GetValue(i)}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("\nNo hierarchy relation found!");
                        }
                    }
                }
            }
        }
    }
}
