using System;
using System.Data.SqlClient;

namespace dbtest {
    class Program {
        static void Main(string[] args) {
            string connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;";
            using (SqlConnection conn = new SqlConnection(connStr)) {
                conn.Open();
                
                string sql = @"
                    INSERT INTO tbl_t_safety_talk (Nik, Nama, Departemen, PerusahaanId, Tanggal, Waktu, Area, Lokasi, Materi, CreatedAt, IsDeleted)
                    VALUES ('24041930970', 'MUHAMMAD FAQIH (TEST)', 'MIND', 1, GETDATE(), '12:00', 'Test Area', 'Test Lokasi', 'UJI COBA NOTIFIKASI SUARA', GETDATE(), 0);
                    SELECT SCOPE_IDENTITY();
                ";

                using (SqlCommand cmd = new SqlCommand(sql, conn)) {
                    var newId = cmd.ExecuteScalar();
                    Console.WriteLine("Inserted Test Safety Talk with ID: " + newId);
                }
            }
        }
    }
}
