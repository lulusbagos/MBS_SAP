using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddDpaDriverMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_m_dpa_driver",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    driver_nama = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    driver_nama_normalized = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    perusahaan_id = table.Column<int>(type: "int", nullable: true),
                    created_by_nik = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_m_dpa_driver", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tbl_m_dpa_driver_driver_nama_normalized",
                table: "tbl_m_dpa_driver",
                column: "driver_nama_normalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_m_dpa_driver");
        }
    }
}
