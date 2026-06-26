using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP2hTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_m_p2h_vehicle",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    no_lambung = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    jenis_kendaraan = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    merek = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_m_p2h_vehicle", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tbl_t_p2h_report",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nik = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    nama = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    tanggal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    waktu = table.Column<TimeSpan>(type: "time", nullable: false),
                    jenis_kendaraan = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    no_lambung = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    kilometer = table.Column<double>(type: "float", nullable: false),
                    merek = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    simper_kimper = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    foto_speedometer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    gol_a_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    gol_b_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    gol_c_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_t_p2h_report", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_m_p2h_vehicle");

            migrationBuilder.DropTable(
                name: "tbl_t_p2h_report");
        }
    }
}
