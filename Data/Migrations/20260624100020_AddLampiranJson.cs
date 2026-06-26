using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLampiranJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lampiran_json",
                table: "tbl_t_inspection",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lampiran_json",
                table: "tbl_t_inspection");
        }
    }
}
