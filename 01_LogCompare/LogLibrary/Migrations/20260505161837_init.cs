using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LogLibrary.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CiscoModels",
                columns: table => new
                {
                    ModelName = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CiscoModels", x => x.ModelName);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.UniqueConstraint("AK_Devices_DeviceName", x => x.DeviceName);
                });

            migrationBuilder.CreateTable(
                name: "LogSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Command = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SummaryText = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CommandText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCommands_CiscoModels_ModelName",
                        column: x => x.ModelName,
                        principalTable: "CiscoModels",
                        principalColumn: "ModelName",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceToModelMappings",
                columns: table => new
                {
                    DeviceName = table.Column<string>(type: "text", maxLength: 200, nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceToModelMappings", x => x.DeviceName);
                    table.ForeignKey(
                        name: "FK_DeviceToModelMappings_CiscoModels_ModelName",
                        column: x => x.ModelName,
                        principalTable: "CiscoModels",
                        principalColumn: "ModelName",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceToModelMappings_Devices_DeviceName",
                        column: x => x.DeviceName,
                        principalTable: "Devices",
                        principalColumn: "DeviceName",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RawLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Command = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawLogs_Devices_DeviceName",
                        column: x => x.DeviceName,
                        principalTable: "Devices",
                        principalColumn: "DeviceName",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceName",
                table: "Devices",
                column: "DeviceName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceToModelMappings_ModelName",
                table: "DeviceToModelMappings",
                column: "ModelName");

            migrationBuilder.CreateIndex(
                name: "IX_LogSummaries_DeviceName_Date_Command",
                table: "LogSummaries",
                columns: new[] { "DeviceName", "Date", "Command" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelCommands_ModelName_CommandText",
                table: "ModelCommands",
                columns: new[] { "ModelName", "CommandText" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceName_Date",
                table: "RawLogs",
                columns: new[] { "DeviceName", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RawLogs_DeviceName",
                table: "RawLogs",
                column: "DeviceName");

            migrationBuilder.CreateIndex(
                name: "IX_RawLogs_DeviceName_Date_Command",
                table: "RawLogs",
                columns: new[] { "DeviceName", "Date", "Command" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceToModelMappings");

            migrationBuilder.DropTable(
                name: "LogSummaries");

            migrationBuilder.DropTable(
                name: "ModelCommands");

            migrationBuilder.DropTable(
                name: "RawLogs");

            migrationBuilder.DropTable(
                name: "CiscoModels");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
