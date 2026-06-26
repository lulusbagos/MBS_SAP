using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKeteranganAndFotoToObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "foto_url",
                table: "tbl_t_observation",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "keterangan",
                table: "tbl_t_observation",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "foto_url",
                table: "tbl_t_observation");

            migrationBuilder.DropColumn(
                name: "keterangan",
                table: "tbl_t_observation");
        }
    }
}
