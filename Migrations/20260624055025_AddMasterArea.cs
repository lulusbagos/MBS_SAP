using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_m_area_utama",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nama_area = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    perusahaan_id = table.Column<int>(type: "int", nullable: false),
                    created_by_nik = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_by_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_m_area_utama", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_m_area_utama");
        }
    }
}
