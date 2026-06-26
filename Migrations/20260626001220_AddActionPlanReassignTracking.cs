using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Migrations
{
    /// <inheritdoc />
    public partial class AddActionPlanReassignTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "reassigned_at",
                table: "tbl_t_action_plan",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reassigned_from",
                table: "tbl_t_action_plan",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reassigned_to",
                table: "tbl_t_action_plan",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reassigned_at",
                table: "tbl_t_action_plan");

            migrationBuilder.DropColumn(
                name: "reassigned_from",
                table: "tbl_t_action_plan");

            migrationBuilder.DropColumn(
                name: "reassigned_to",
                table: "tbl_t_action_plan");
        }
    }
}
