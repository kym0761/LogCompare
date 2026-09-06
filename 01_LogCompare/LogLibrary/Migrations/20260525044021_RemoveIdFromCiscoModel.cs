using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogLibrary.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIdFromCiscoModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Id",
                table: "CiscoModels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "CiscoModels",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
