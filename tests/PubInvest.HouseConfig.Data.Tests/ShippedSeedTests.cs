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
            p.PreferredDevice.Isolator, p.PreferredDevice.Dimmer240, p.PreferredDevice.Dimmer0_10V,
            p.PreferredDevice.Relay, p.PreferredDevice.Dc24VPositive, p.PreferredDevice.Dc24VNegative,
            p.Terminals.DeviceTypeId, p.Terminals.BridgeBarDeviceTypeId, p.Terminals.EndStopDeviceTypeId
        };
        referenced.AddRange(p.PreferredDevice.ExternalDriver);

        Assert.All(referenced, id => Assert.Contains(id, known));
    }

    [Fact]
    public void The_confirmed_widths_are_recorded_in_slot_units_of_a_third_of_a_module()
    {
        var seed = Load();

        var dimmer = seed.DeviceTypes.Single(d => d.PartNumber == "SHELLY-PRO-DIMMER-2PM");
        Assert.Equal(DinUnits.PerModule, dimmer.ModuleWidth);   // 1 T
        Assert.Equal(2, dimmer.ChannelCount);

        var relay = seed.DeviceTypes.Single(d => d.PartNumber == "SHELLY-PRO-4PM");
        Assert.Equal(DinUnits.FromModules(3), relay.ModuleWidth); // 3 T
        Assert.Equal(4, relay.ChannelCount);

        var terminal = seed.DeviceTypes.Single(d => d.PartNumber == "2003-7646");
        Assert.Equal(1, terminal.ModuleWidth);                   // 3 per T
    }

    [Fact]
    public void No_two_catalogue_entries_share_an_id_or_a_part_number()
    {
        // Adding a device with an id that is already taken is silent: the seeder
        // skips ids it has seen, so an existing database would quietly drop the
        // new entry while a fresh one took it in place of the old.
        var seed = Load();

        Assert.Equal(seed.DeviceTypes.Count, seed.DeviceTypes.Select(d => d.Id).Distinct().Count());
        Assert.Equal(
            seed.DeviceTypes.Count,
            seed.DeviceTypes.Select(d => d.PartNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(seed.Enclosures.Count, seed.Enclosures.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Every_category_in_the_seed_is_one_the_domain_knows()
    {
        var seed = Load();

        Assert.All(seed.DeviceTypes, d =>
            Assert.True(Enum.TryParse<DeviceCategory>(d.Category, out _),
                $"'{d.PartNumber}' has category '{d.Category}', which is not a DeviceCategory."));
    }

    [Fact]
    public void Every_category_the_layouts_pack_is_one_the_domain_knows()
    {
        // A category named only in the ruleset would pack nothing and raise
        // nothing: the devices would fall through to the stray zone instead.
        var seed = Load();
        var payload = seed.RuleSets.Single(r => r.IsDefault).Payload;

        var packed = payload.Layouts
            .SelectMany(l => l.Zones)
            .SelectMany(z => z.FromLeft.Concat(z.FromRight))
            .Distinct();

        Assert.All(packed, c => Assert.True(Enum.IsDefined(c), $"'{c}' is not a DeviceCategory."));
    }

    [Fact]
    public void Every_placeable_category_has_a_rail_to_go_on_in_the_finest_layout()
    {
        var seed = Load();
        var payload = seed.RuleSets.Single(r => r.IsDefault).Payload;

        var placeable = seed.DeviceTypes
            .Where(d => d.ModuleWidth > 0)
            .Select(d => Enum.Parse<DeviceCategory>(d.Category))
            .Distinct();

        var packed = payload.Layouts[0].Zones
            .SelectMany(z => z.FromLeft.Concat(z.FromRight))
            .ToHashSet();

        Assert.All(placeable, c => Assert.Contains(c, packed));
    }

    [Fact]
    public void Enclosure_rows_are_whole_din_modules()
    {
        var seed = Load();

        Assert.All(seed.Enclosures, e => Assert.Equal(0, e.SlotsPerRow % DinUnits.PerModule));
    }

    [Fact]
    public void Nothing_that_is_never_placed_claims_rail_space()
    {
        var seed = Load();

        Assert.All(
            seed.DeviceTypes.Where(d => d.Category is "Accessory" or "ExternalDriver"),
            d => Assert.Equal(0, d.ModuleWidth));
    }

    [Fact]
    public void The_seed_carries_the_parts_the_wiring_rules_need()
    {
        var seed = Load();
        var byCategory = seed.DeviceTypes.ToLookup(d => d.Category);

        Assert.NotEmpty(byCategory["Isolator"]);
        Assert.NotEmpty(byCategory["Dc24VPositive"]);
        Assert.NotEmpty(byCategory["Dc24VNegative"]);
        Assert.NotEmpty(byCategory["ExternalDriver"]);
    }

    [Fact]
    public void One_three_tier_block_serves_one_circuit()
    {
        var seed = Load();
        var p = seed.RuleSets.Single(r => r.IsDefault).Payload;

        Assert.Equal(1, p.Terminals.BlocksPerCircuit);
        Assert.Contains(seed.DeviceTypes, d => d.Id == p.Terminals.DeviceTypeId);
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
