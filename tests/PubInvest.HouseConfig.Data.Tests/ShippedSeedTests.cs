using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Data.Tests;

/// Guards the catalogue that actually ships. A broken id reference here would
/// only show up as NO_PREFERRED_DEVICE at generation time, on site.
[Collection("postgres")]
public class ShippedSeedTests(PostgresFixture fixture)
{
    private static SeedDocument Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "seed", "catalogue.v1.json");
        return JsonSerializer.Deserialize<SeedDocument>(File.ReadAllText(path), DomainMapper.Json)!;
    }

    [Fact]
    public void The_shipped_seed_parses()
    {
        var seed = Load();

        Assert.Equal(1, seed.Version);
        Assert.NotEmpty(seed.DeviceTypes);
        Assert.NotEmpty(seed.Enclosures);
        Assert.Single(seed.RuleSets);
    }

    [Fact]
    public void Every_device_type_id_the_default_ruleset_references_exists()
    {
        var seed = Load();
        var known = seed.DeviceTypes.Select(d => d.Id).ToHashSet();
        var p = seed.RuleSets.Single(r => r.IsDefault).Payload;

        var referenced = new List<Guid>
        {
            p.PreferredDevice.Dimmer240, p.PreferredDevice.Dimmer0_10V, p.PreferredDevice.Relay,
            p.Terminals.Line.DeviceTypeId, p.Terminals.Neutral.DeviceTypeId, p.Terminals.Earth.DeviceTypeId,
            p.Terminals.BridgeBarDeviceTypeId, p.Terminals.EndStopDeviceTypeId
        };
        referenced.AddRange(p.PreferredDevice.Psu24V);

        Assert.All(referenced, id => Assert.Contains(id, known));
    }

    [Fact]
    public void The_confirmed_widths_are_recorded_in_slot_units_of_a_third_of_a_module()
    {
        var seed = Load();

        var dimmer = seed.DeviceTypes.Single(d => d.PartNumber == "SHELLY-PRO-DIMMER-2PM");
        Assert.Equal(DinUnits.PerModule, dimmer.ModuleWidth);   // 1 T
        Assert.Equal(2, dimmer.ChannelCount);

        var relay = seed.DeviceTypes.Single(d => d.PartNumber == "SHELLY-PRO-RELAY-4");
        Assert.Equal(DinUnits.FromModules(3), relay.ModuleWidth); // 3 T
        Assert.Equal(4, relay.ChannelCount);

        var terminal = seed.DeviceTypes.Single(d => d.PartNumber == "2003-7646");
        Assert.Equal(1, terminal.ModuleWidth);                   // 3 per T
    }

    [Fact]
    public void Enclosure_rows_are_whole_din_modules()
    {
        var seed = Load();

        Assert.All(seed.Enclosures, e => Assert.Equal(0, e.SlotsPerRow % DinUnits.PerModule));
    }

    [Fact]
    public void Accessories_take_no_rail_space()
    {
        var seed = Load();

        Assert.All(
            seed.DeviceTypes.Where(d => d.Category == "Accessory"),
            d => Assert.Equal(0, d.ModuleWidth));
    }

    [Fact]
    public async Task The_shipped_seed_loads_into_the_database()
    {
        await using var db = fixture.CreateContext();
        var seed = Load();

        var inserted = await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        Assert.Equal(
            seed.DeviceTypes.Count + seed.Enclosures.Count + seed.RuleSets.Count,
            inserted);

        var row = await db.RuleSets.SingleAsync(r => r.Id == seed.RuleSets[0].Id);
        Assert.Equal(0.8m, DomainMapper.ToDomain(row).PsuDeratingFactor);
    }
}
