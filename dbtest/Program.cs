using System;
using System.Data.SqlClient;

namespace dbtest {
    class Program {
        static void Main(string[] args) {
            string connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;";
            using (SqlConnection conn = new SqlConnection(connStr)) {
                conn.Open();
                Console.WriteLine("=== UPDATE DATA SAFETY TALK NIK 705779 ===");
                using (SqlCommand cmd = new SqlCommand("UPDATE tbl_t_safety_talk SET perusahaan_id = 40 WHERE Nik = '705779' AND perusahaan_id IS NULL", conn)) {
                    int rows = cmd.ExecuteNonQuery();
                    Console.WriteLine($"Updated {rows} rows in tbl_t_safety_talk.");
                }

                Console.WriteLine("\n=== VERIFIKASI DATA SAFETY TALK KARYAWAN ===");
                using (SqlCommand cmd = new SqlCommand("SELECT Id, Nik, Nama, Departemen, perusahaan_id, Tanggal, Waktu, created_at, is_deleted FROM tbl_t_safety_talk WHERE Nik = '705779' ORDER BY created_at DESC", conn)) {
                    using (SqlDataReader r = cmd.ExecuteReader()) {
                        while (r.Read()) {
                            Console.WriteLine($"ID: {r["Id"]}, Nik: {r["Nik"]}, Nama: {r["Nama"]}, PerusahaanId: {r["perusahaan_id"]}, Tanggal: {r["Tanggal"]}, CreatedAt: {r["created_at"]}, IsDeleted: {r["is_deleted"]}");
                        }
                    }
                }
            }
        }
    }
}
