using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddObservationTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_t_observation",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    nama = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    nik = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    departemen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    lokasi = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    detil_lokasi = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    kegiatan_yang_diamati = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    departemen_yang_diamati = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    dokumen_pendukung = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    resiko_kritis = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tingkat_resiko = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    perihal_yang_diamati = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    hasil_observasi = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_t_observation", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_t_observation");
        }
    }
}
