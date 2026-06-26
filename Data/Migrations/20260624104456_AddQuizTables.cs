using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MBS_SAP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "table_m_quis",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nik = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    nama = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    score = table.Column<int>(type: "int", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_m_quis", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "table_m_quis_detail",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    quiz_id = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<int>(type: "int", nullable: false),
                    question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    correct_key = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    correct_answer_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    selected_answer = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    selected_answer_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    points_earned = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_m_quis_detail", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_m_quis_detail_table_m_quis_quiz_id",
                        column: x => x.quiz_id,
                        principalTable: "table_m_quis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_table_m_quis_detail_quiz_id",
                table: "table_m_quis_detail",
                column: "quiz_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "table_m_quis_detail");

            migrationBuilder.DropTable(
                name: "table_m_quis");
        }
    }
}
