using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <inheritdoc />
    public partial class PositionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PositionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmainId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    RowIndex = table.Column<int>(type: "integer", nullable: false),
                    StartSlot = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionOverrides_Submains_SubmainId",
                        column: x => x.SubmainId,
                        principalTable: "Submains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PositionOverrides_SubmainId_Label",
                table: "PositionOverrides",
                columns: new[] { "SubmainId", "Label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionOverrides");
        }
    }
}
