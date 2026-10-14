using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <inheritdoc />
    public partial class CircuitTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CircuitId",
                table: "DeviceInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminalCount",
                table: "Circuits",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TerminalPosition",
                table: "Circuits",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CircuitId",
                table: "DeviceInstances");

            migrationBuilder.DropColumn(
                name: "TerminalCount",
                table: "Circuits");

            migrationBuilder.DropColumn(
                name: "TerminalPosition",
                table: "Circuits");
        }
    }
}
