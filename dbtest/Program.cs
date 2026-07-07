using Npgsql;
using System;

var pgConnStr = "Host=172.16.1.96;Port=5432;Database=sysinteg_indexsafe2;Username=postgres;Password=index.123;";

using var connPg = new NpgsqlConnection(pgConnStr);
connPg.Open();

void CheckView(string viewName, string photoCol)
{
    Console.WriteLine($"\n=== CHECKING {viewName} ({photoCol}) ===");
    try
    {
        using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM public.{viewName} WHERE {photoCol} IS NOT NULL AND {photoCol} <> ''", connPg);
        var total = cmd.ExecuteScalar();
        Console.WriteLine($"Total records with photos: {total}");

        using var cmdLocal = new NpgsqlCommand($@"
            SELECT COUNT(*) FROM public.{viewName} 
            WHERE {photoCol} IS NOT NULL 
              AND ({photoCol} LIKE '%/private/var/mobile%' 
                   OR {photoCol} LIKE '%/data/user/0%' 
                   OR {photoCol} LIKE '%/cache/%'
                   OR {photoCol} LIKE '%/tmp/%')", connPg);
        var local = cmdLocal.ExecuteScalar();
        Console.WriteLine($"Records with local mobile paths: {local}");

        if (Convert.ToInt32(total) > 0)
        {
            using var cmdSample = new NpgsqlCommand($"SELECT {photoCol} FROM public.{viewName} WHERE {photoCol} IS NOT NULL AND {photoCol} <> '' LIMIT 2", connPg);
            using var reader = cmdSample.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"- Sample: '{reader[photoCol]}'");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

CheckView("vw_coachingdetail", "foto");
CheckView("vw_p5mdetail", "foto");
CheckView("vw_hazardreportdetail", "foto_temuan");
CheckView("vw_observationdetail", "foto");
