using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <inheritdoc />
    public partial class RackCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RackDeviceTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    PartNumber = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    HeightU = table.Column<int>(type: "integer", nullable: false),
                    PortGroupsJson = table.Column<string>(type: "text", nullable: false),
                    PoeBudgetW = table.Column<int>(type: "integer", nullable: true),
                    RequiredAccessoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RackDeviceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RackTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    PartNumber = table.Column<string>(type: "text", nullable: false),
                    HeightU = table.Column<int>(type: "integer", nullable: false),
                    Mounting = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RackTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RackDeviceTypes_PartNumber",
                table: "RackDeviceTypes",
                column: "PartNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RackDeviceTypes");

            migrationBuilder.DropTable(
                name: "RackTypes");
        }
    }
}
