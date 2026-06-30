using System;
using System.Text;
using Microsoft.Data.SqlClient;

namespace dbtest
{
    class Program
    {
        static void Main(string[] args)
        {
            string connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;";
            string targetNik = "24051940986";

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                string q = @"
                    IF NOT EXISTS (
                        SELECT * FROM sys.columns 
                        WHERE object_id = OBJECT_ID(N'[tbl_t_notifications]') 
                        AND name = 'notif_type'
                    )
                    BEGIN
                        ALTER TABLE tbl_t_notifications ADD notif_type NVARCHAR(50) NULL;
                    END
                ";
                using (var cmd = new SqlCommand(q, conn))
                {
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("Column notif_type added successfully or already exists.");
                }
            }
        }
    }
}
