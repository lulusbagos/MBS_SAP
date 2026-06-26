using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentNewsAndAppUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "tbl_t_app_user",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tbl_t_incident_news",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    judul = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    konten = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    gambar_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    lokasi = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    tanggal_kejadian = table.Column<DateTime>(type: "datetime2", nullable: true),
                    kategori = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    dibuat_oleh = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    nik_pembuat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_t_incident_news", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_t_incident_news");

            migrationBuilder.DropColumn(
                name: "role",
                table: "tbl_t_app_user");
        }
    }
}
