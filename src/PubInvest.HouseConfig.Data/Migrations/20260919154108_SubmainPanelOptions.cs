using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubmainPanelOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasIsolator",
                table: "Submains",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TerminalsAtBottom",
                table: "Submains",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasIsolator",
                table: "Submains");

            migrationBuilder.DropColumn(
                name: "TerminalsAtBottom",
                table: "Submains");
        }
    }
}
