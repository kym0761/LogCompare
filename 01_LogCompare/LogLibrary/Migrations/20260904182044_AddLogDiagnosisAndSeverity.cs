using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LogLibrary.Migrations
{
    /// <inheritdoc />
    public partial class AddLogDiagnosisAndSeverity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "LogSummaries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.CreateTable(
                name: "LogDiagnoses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LogSummaryId = table.Column<int>(type: "integer", nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Command = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReferencedCommands = table.Column<string>(type: "text", nullable: false),
                    DiagnosisText = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Warning"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogDiagnoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogDiagnoses_LogSummaries_LogSummaryId",
                        column: x => x.LogSummaryId,
                        principalTable: "LogSummaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogDiagnoses_DeviceName_Date_Command",
                table: "LogDiagnoses",
                columns: new[] { "DeviceName", "Date", "Command" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogDiagnoses_LogSummaryId",
                table: "LogDiagnoses",
                column: "LogSummaryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogDiagnoses");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "LogSummaries");
        }
    }
}
