using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwitchUplinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UplinkPort",
                table: "RackItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UplinkToItemId",
                table: "RackItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UplinkToPort",
                table: "RackItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UplinkToText",
                table: "RackItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RackItems_UplinkToItemId_UplinkToPort",
                table: "RackItems",
                columns: new[] { "UplinkToItemId", "UplinkToPort" },
                unique: true,
                filter: "\"UplinkToItemId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RackItems_RackItems_UplinkToItemId",
                table: "RackItems",
                column: "UplinkToItemId",
                principalTable: "RackItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RackItems_RackItems_UplinkToItemId",
                table: "RackItems");

            migrationBuilder.DropIndex(
                name: "IX_RackItems_UplinkToItemId_UplinkToPort",
                table: "RackItems");

            migrationBuilder.DropColumn(
                name: "UplinkPort",
                table: "RackItems");

            migrationBuilder.DropColumn(
                name: "UplinkToItemId",
                table: "RackItems");

            migrationBuilder.DropColumn(
                name: "UplinkToPort",
                table: "RackItems");

            migrationBuilder.DropColumn(
                name: "UplinkToText",
                table: "RackItems");
        }
    }
}
