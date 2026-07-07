using Npgsql;
using System;

var pgConnStr = "Host=172.16.1.96;Port=5432;Database=sysinteg_indexsafe2;Username=postgres;Password=index.123;";

using var connPg = new NpgsqlConnection(pgConnStr);
connPg.Open();

string nik = "24011950928";
Console.WriteLine($"\n=== CHECKING vw_safetytalkdetail FOR NIK {nik} TODAY ===");
try
{
    using var cmd = new NpgsqlCommand($@"
        SELECT *
        FROM public.vw_safetytalkdetail 
        WHERE employee_nik = @nik AND date::date = current_date
        ORDER BY date DESC, time DESC", connPg);
    cmd.Parameters.AddWithValue("nik", nik);

    using var reader = cmd.ExecuteReader();
    if (!reader.HasRows) {
        Console.WriteLine($"TIDAK ADA data safety talk untuk NIK {nik} pada hari ini dari postgres.");
    }
    while (reader.Read())
    {
        Console.WriteLine($"- SUDAH ADA: Tanggal: {reader["date"]}, Waktu: {reader["time"]}, Topik: {reader["title"]}, Lokasi: {reader["location_name"]}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
