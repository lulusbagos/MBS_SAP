using Microsoft.Data.SqlClient;
using System;

var connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;";

var sql = @"
SELECT TOP 10 * FROM (
SELECT 'Hazard' as Tipe, Id, Nama, Tanggal, Waktu, created_at, is_deleted FROM tbl_t_hazard_report WHERE Tanggal > '2026-07-03'
UNION ALL
SELECT 'Inspection', Id, Nama, Tanggal, Waktu, created_at, is_deleted FROM tbl_t_inspection WHERE Tanggal > '2026-07-03'
UNION ALL
SELECT 'ActionPlan', Id, Nama, Tanggal, Waktu, created_at, is_deleted FROM tbl_t_action_plan WHERE Tanggal > '2026-07-03'
UNION ALL
SELECT 'SafetyTalk', Id, Nama, Tanggal, Waktu, created_at, is_deleted FROM tbl_t_safety_talk WHERE Tanggal > '2026-07-03'
UNION ALL
SELECT 'P5M', Id, Nama, Tanggal, Waktu, created_at, is_deleted FROM tbl_t_p5m WHERE Tanggal > '2026-07-03'
UNION ALL
SELECT 'Observation', Id, Nama, Date, Date, created_at, is_deleted FROM tbl_t_observation WHERE Date > '2026-07-03'
) as AllItems
WHERE is_deleted = 0
ORDER BY created_at DESC;
";

using var conn = new SqlConnection(connStr);
conn.Open();

using var cmd = new SqlCommand(sql, conn);
using var reader = cmd.ExecuteReader();

while (reader.Read())
{
    Console.WriteLine($"[{reader["Tipe"]}] ID: {reader["Id"]}, Nama: {reader["Nama"]}, Tanggal: {reader["Tanggal"]:yyyy-MM-dd}, Waktu: {reader["Waktu"]}, CreatedAt: {reader["created_at"]:yyyy-MM-dd HH:mm:ss}");
}


