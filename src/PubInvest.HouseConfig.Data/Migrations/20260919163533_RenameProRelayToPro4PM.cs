using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PubInvest.HouseConfig.Data.Migrations
{
    /// <summary>
    /// Data only. The relay the default ruleset points at is the Shelly Pro 4PM;
    /// it was catalogued under a made-up part number. The seeder never touches a
    /// row whose id already exists, so without this an existing database would
    /// keep the old name while a fresh one got the right one.
    ///
    /// Both statements match on the old value, so a part number an admin has
    /// already corrected by hand is left alone.
    /// </summary>
    public partial class RenameProRelayToPro4PM : Migration
    {
        private const string Relay = "d1000000-0000-4000-8000-000000000003";
        private const string TapeDimmer = "d1000000-0000-4000-8000-000000000002";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE "DeviceTypes"
                SET "PartNumber" = 'SHELLY-PRO-4PM', "Model" = 'Pro 4PM'
                WHERE "Id" = '{Relay}' AND "PartNumber" = 'SHELLY-PRO-RELAY-4';
                """);

            migrationBuilder.Sql($"""
                UPDATE "DeviceTypes"
                SET "Model" = 'Pro Dimmer 0/1-10V PM'
                WHERE "Id" = '{TapeDimmer}' AND "Model" = 'Pro Dimmer 0/1-10V';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE "DeviceTypes"
                SET "PartNumber" = 'SHELLY-PRO-RELAY-4', "Model" = 'Pro relay'
                WHERE "Id" = '{Relay}' AND "PartNumber" = 'SHELLY-PRO-4PM';
                """);

            migrationBuilder.Sql($"""
                UPDATE "DeviceTypes"
                SET "Model" = 'Pro Dimmer 0/1-10V'
                WHERE "Id" = '{TapeDimmer}' AND "Model" = 'Pro Dimmer 0/1-10V PM';
                """);
        }
    }
}
