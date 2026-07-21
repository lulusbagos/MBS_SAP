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
                Console.WriteLine("=== CONNECTED TO DB_SAP ===");

                // Update ONE_DB_MITRA.dbo.tbl_m_pengguna
                using (var cmd = new SqlCommand("UPDATE ONE_DB_MITRA.dbo.tbl_m_pengguna SET nama_lengkap = 'RIAN PATTAN', email = '' WHERE username = '21031930291'", conn))
                {
                    int rows = cmd.ExecuteNonQuery();
                    Console.WriteLine($"Updated ONE_DB_MITRA.dbo.tbl_m_pengguna: {rows} row(s) affected.");
                }

                // Update DB_SAP.dbo.tbl_t_app_user if exists
                using (var cmd = new SqlCommand("UPDATE tbl_t_app_user SET nama = 'RIAN PATTAN' WHERE nik = '21031930291'", conn))
                {
                    int rows = cmd.ExecuteNonQuery();
                    Console.WriteLine($"Updated tbl_t_app_user: {rows} row(s) affected.");
                }

                Console.WriteLine("\n--- VERIFICATION AFTER UPDATE ---");
                Console.WriteLine("\n[vw_pengguna for 21031930291]");
                QueryTable(conn, "SELECT * FROM vw_pengguna WHERE username = '21031930291'");

                Console.WriteLine("\n[tbl_t_app_user for 21031930291]");
                QueryTable(conn, "SELECT * FROM tbl_t_app_user WHERE nik = '21031930291'");

                Console.WriteLine("\n[vw_karyawan + vw_personal for 21031930291]");
                QueryTable(conn, "SELECT k.no_nik, k.id_perusahaan, k.id_departemen, k.id_jabatan, k.status_aktif, p.nama_lengkap, p.no_ktp FROM vw_karyawan k JOIN vw_personal p ON k.id_personal = p.id_personal WHERE k.no_nik = '21031930291'");
            }
        }

        static void QueryTable(SqlConnection conn, string sql)
        {
            try
            {
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    int count = 0;
                    while (reader.Read())
                    {
                        count++;
                        Console.WriteLine($"Row #{count}:");
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            Console.WriteLine($"  {reader.GetName(i)}: {reader.GetValue(i)}");
                        }
                    }
                    if (count == 0)
                    {
                        Console.WriteLine("  No rows returned.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
        }
    }
}
