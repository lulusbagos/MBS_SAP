using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDeletedToAttendanceEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "tbl_t_attendance_event",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "tbl_t_attendance_event");
        }
    }
}
