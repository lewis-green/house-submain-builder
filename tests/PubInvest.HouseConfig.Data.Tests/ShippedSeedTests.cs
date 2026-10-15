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

        Assert.Contains(seed.DeviceTypes, d => d.Id == p.Terminals.DeviceTypeId);
    }

    [Fact]
    public void The_default_ruleset_bridges_line_tiers_with_a_catalogued_bar()
    {
        var seed = Load();
        var terminals = seed.RuleSets.Single(r => r.IsDefault).Payload.Terminals;

        Assert.True(terminals.LineBridgeBarWays > 0);
        var bar = Assert.Single(seed.DeviceTypes, d => d.Id == terminals.LineBridgeBarDeviceTypeId);
        Assert.Equal("Accessory", bar.Category);
    }

    [Fact]
    public async Task The_shipped_seed_loads_into_the_database()
    {
        await using var db = fixture.CreateContext();
        var seed = Load();

        var inserted = await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        Assert.Equal(
            seed.DeviceTypes.Count + seed.Enclosures.Count + seed.RuleSets.Count
            + seed.RackTypes!.Count + seed.RackDeviceTypes!.Count,
            inserted);

        var row = await db.RuleSets.SingleAsync(r => r.Id == seed.RuleSets[0].Id);
        Assert.Equal(0.8m, DomainMapper.ToDomain(row).PsuDeratingFactor);
    }

    [Fact]
    public void The_shipped_rack_catalogue_has_cabinets_the_three_switches_a_patch_panel_and_the_rack_kit()
    {
        var seed = Load();

        Assert.Equal(new[] { 6, 9, 12, 22, 42 }, seed.RackTypes!.Select(r => r.HeightU).Order());
        var models = seed.RackDeviceTypes!.Select(d => d.Model).ToList();
        Assert.Contains("USW-Pro-Max-48-PoE", models);
        Assert.Contains("USW-Pro-Max-24-PoE", models);
        Assert.Contains("USW-Pro-Max-16-PoE", models);
        Assert.Contains(seed.RackDeviceTypes!, d => d.Kind == "PatchPanel");
        Assert.Contains(seed.RackDeviceTypes!, d => d.Kind == "Accessory");
    }

    /// The aggregation switch is the house's 10G spine: eight SFP+ cages, no
    /// copper, no PoE, and full width, so it needs no kit to go in a rack.
    [Fact]
    public void The_shipped_rack_catalogue_has_the_aggregation_switch_with_eight_sfp_plus_ports()
    {
        var aggregation = Assert.Single(Load().RackDeviceTypes!, d => d.Model == "USW-Aggregation");

        Assert.Equal("Switch", aggregation.Kind);
        Assert.Equal(1, aggregation.HeightU);
        var group = Assert.Single(aggregation.PortGroups);
        Assert.Equal(("SFP+", 1, 8, false), (group.Name, group.From, group.To, group.PoeTick));
        Assert.Null(aggregation.PoeBudgetW);
        Assert.Null(aggregation.RequiredAccessoryId);
    }

    /// Ports run 1..N across groups with no gap and no overlap, or "port 33" on
    /// a schedule would not be the 33rd socket on the front panel.
    [Fact]
    public void Every_shipped_rack_item_numbers_its_ports_from_one_without_gaps()
    {
        foreach (var d in Load().RackDeviceTypes!.Where(d => d.Kind != "Accessory"))
        {
            var next = 1;
            foreach (var g in d.PortGroups)
            {
                Assert.True(g.From == next, $"{d.Model}: group {g.Name} starts at {g.From}, expected {next}");
                Assert.True(g.To >= g.From, $"{d.Model}: group {g.Name} ends before it starts");
                next = g.To + 1;
            }
            Assert.True(next > 1, $"{d.Model} has no ports");
            Assert.True(d.HeightU >= 1, $"{d.Model} takes no rack space");
        }
    }

    [Fact]
    public void Every_required_accessory_is_a_shipped_accessory()
    {
        var seed = Load();
        var accessories = seed.RackDeviceTypes!.Where(d => d.Kind == "Accessory").Select(d => d.Id).ToHashSet();

        foreach (var d in seed.RackDeviceTypes!.Where(d => d.RequiredAccessoryId is not null))
        {
            Assert.Contains(d.RequiredAccessoryId!.Value, accessories);
        }

        // The half-width switch cannot go in a rack without its kit.
        Assert.Contains(seed.RackDeviceTypes!, d => d.Model == "USW-Pro-Max-16-PoE" && d.RequiredAccessoryId is not null);
    }
}
