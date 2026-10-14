using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <inheritdoc />
    public partial class Racks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Racks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RackTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FedFromSubmainId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    LayoutVersion = table.Column<int>(type: "integer", nullable: false),
                    SwitchesNamed = table.Column<int>(type: "integer", nullable: false),
                    PatchPanelsNamed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Racks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Racks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Racks_RackTypes_RackTypeId",
                        column: x => x.RackTypeId,
                        principalTable: "RackTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Racks_Submains_FedFromSubmainId",
                        column: x => x.FedFromSubmainId,
                        principalTable: "Submains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RackItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RackId = table.Column<Guid>(type: "uuid", nullable: false),
                    RackDeviceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopU = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RackItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RackItems_RackDeviceTypes_RackDeviceTypeId",
                        column: x => x.RackDeviceTypeId,
                        principalTable: "RackDeviceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RackItems_Racks_RackId",
                        column: x => x.RackId,
                        principalTable: "Racks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RackPorts",
                columns: table => new
                {
                    RackItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    Poe = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RackPorts", x => new { x.RackItemId, x.Number });
                    table.ForeignKey(
                        name: "FK_RackPorts_RackItems_RackItemId",
                        column: x => x.RackItemId,
                        principalTable: "RackItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RackItems_RackDeviceTypeId",
                table: "RackItems",
                column: "RackDeviceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RackItems_RackId_TopU",
                table: "RackItems",
                columns: new[] { "RackId", "TopU" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Racks_FedFromSubmainId",
                table: "Racks",
                column: "FedFromSubmainId");

            migrationBuilder.CreateIndex(
                name: "IX_Racks_ProjectId",
                table: "Racks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Racks_RackTypeId",
                table: "Racks",
                column: "RackTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RackPorts");

            migrationBuilder.DropTable(
                name: "RackItems");

            migrationBuilder.DropTable(
                name: "Racks");
        }
    }
}
