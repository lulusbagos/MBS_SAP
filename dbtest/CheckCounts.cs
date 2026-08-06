using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MBS_SAP.Data;

namespace dbtest
{
    class CheckCounts
    {
        public static async Task Main(string[] args)
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            var sqlConnStr = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnStr));

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER VIEW vw_perusahaan AS SELECT id AS perusahaan_id, kode_perusahaan, nama_perusahaan, pjo AS nama_pjo, tipe_perusahaan_id, perusahaan_induk_id, status_aktif FROM ONE_DB_MITRA.dbo.tbl_m_perusahaan";
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine("vw_perusahaan altered successfully with pjo!");
        }
    }
}
