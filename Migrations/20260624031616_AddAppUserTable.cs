using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_t_app_user",
                columns: table => new
                {
                    nik = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    nama = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    departemen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    perusahaan = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    id_perusahaan = table.Column<int>(type: "int", nullable: true),
                    last_login = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_t_app_user", x => x.nik);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_t_app_user");
        }
    }
}
