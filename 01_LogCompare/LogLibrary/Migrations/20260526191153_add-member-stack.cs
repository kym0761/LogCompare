using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogLibrary.Migrations
{
    /// <inheritdoc />
    public partial class addmemberstack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StackCount",
                table: "Devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StackCount",
                table: "Devices");
        }
    }
}
