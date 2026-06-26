using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "catatan",
                table: "tbl_t_inspection",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "q1_1",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q1_2",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q1_3",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q2_1",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q2_2",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q2_3",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q3_1",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q3_2",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q3_3",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q4_1",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q4_2",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q4_3",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q5_1",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q5_2",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "q5_3",
                table: "tbl_t_inspection",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "catatan",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q1_1",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q1_2",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q1_3",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q2_1",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q2_2",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q2_3",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q3_1",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q3_2",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q3_3",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q4_1",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q4_2",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q4_3",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q5_1",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q5_2",
                table: "tbl_t_inspection");

            migrationBuilder.DropColumn(
                name: "q5_3",
                table: "tbl_t_inspection");
        }
    }
}
