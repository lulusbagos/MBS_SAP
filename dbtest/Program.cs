using System;
using System.Data.SqlClient;

namespace dbtest {
    class Program {
        static void Main(string[] args) {
            string connStr = "Server=localhost;Database=MBS_SAP;Trusted_Connection=True;TrustServerCertificate=True;";
            using (SqlConnection conn = new SqlConnection(connStr)) {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT Id, Tanggal, CreatedAt, PerusahaanId FROM tbl_t_safety_talk WHERE Nik = '24041930970' ORDER BY CreatedAt DESC", conn)) {
                    using (SqlDataReader reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            Console.WriteLine("ST Id: " + reader["Id"] + ", Tanggal: " + reader["Tanggal"] + ", CreatedAt: " + reader["CreatedAt"] + ", PerusahaanId: " + reader["PerusahaanId"]);
                        }
                    }
                }
                using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 TargetSafetyTalk FROM v_karyawan_jabatan_mapping WHERE NoNik = '24041930970'", conn)) {
                    var result = cmd.ExecuteScalar();
                    Console.WriteLine("Target ST: " + (result != DBNull.Value ? result : "NULL"));
                }
            }
        }
    }
}
