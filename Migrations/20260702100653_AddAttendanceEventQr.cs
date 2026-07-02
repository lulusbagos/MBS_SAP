using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceEventQr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_t_attendance_event",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    event_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    event_location = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: true),
                    event_description = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    start_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    end_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    qr_token = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_t_attendance_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tbl_t_attendance_record",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    attendance_event_id = table.Column<int>(type: "int", nullable: false),
                    nik = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    nama = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    jabatan = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    perusahaan = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    scan_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_t_attendance_record", x => x.id);
                    table.ForeignKey(
                        name: "FK_tbl_t_attendance_record_tbl_t_attendance_event_attendance_event_id",
                        column: x => x.attendance_event_id,
                        principalTable: "tbl_t_attendance_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tbl_t_attendance_event_qr_token",
                table: "tbl_t_attendance_event",
                column: "qr_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tbl_t_attendance_record_attendance_event_id_nik",
                table: "tbl_t_attendance_record",
                columns: new[] { "attendance_event_id", "nik" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_t_attendance_record");

            migrationBuilder.DropTable(
                name: "tbl_t_attendance_event");
        }
    }
}
