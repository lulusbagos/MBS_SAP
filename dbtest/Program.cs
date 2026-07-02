using Npgsql;

var connStr = "Host=172.16.1.96;Port=5432;Database=sysinteg_indexsafe2;Username=postgres;Password=index.123;";

var views = new[]
{
    "vw_actionplandetail",
    "vw_coachingdetail",
    "vw_hazardreportdetail",
    "vw_inspectiondetail",
    "vw_observationdetail",
    "vw_p2hdetail",
    "vw_p5mdetail",
    "vw_safetytalkdetail"
};

var sql = @"
SELECT table_schema, table_name, column_name, data_type, ordinal_position
FROM information_schema.columns
WHERE table_schema NOT IN ('information_schema','pg_catalog')
  AND table_name = ANY (@views)
ORDER BY table_name, ordinal_position;";

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand(sql, conn);
cmd.Parameters.AddWithValue("views", views);

await using var reader = await cmd.ExecuteReaderAsync();

string? current = null;
while (await reader.ReadAsync())
{
    var viewName = reader.GetString(reader.GetOrdinal("table_name"));
    var columnName = reader.GetString(reader.GetOrdinal("column_name"));
    var dataType = reader.GetString(reader.GetOrdinal("data_type"));
    var position = reader.GetInt32(reader.GetOrdinal("ordinal_position"));

    if (!string.Equals(current, viewName, StringComparison.OrdinalIgnoreCase))
    {
        current = viewName;
        Console.WriteLine();
        Console.WriteLine($"=== {viewName} ===");
    }

    Console.WriteLine($"{position,2}. {columnName} ({dataType})");
}
