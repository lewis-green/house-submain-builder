using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Tests;

[Collection("postgres")]
public class CatalogueSeederTests(PostgresFixture fixture)
{
    private static SeedDocument Document(string tag)
    {
        // Distinct ids per test: the fixture's database is shared across the collection.
        Guid Id(int n) => new($"{tag}-0000-4000-8000-{n:D12}");

        var dimmer = Id(1);
        var tapeDimmer = Id(2);
        var relay = Id(3);
        var psu = Id(4);
        var terminal = Id(5);
        var isolator = Id(6);
        var bar = Id(7);
        var endStop = Id(8);
        var dcPos = Id(11);
        var dcNeg = Id(12);

        return new SeedDocument(
            Version: 1,
            DeviceTypes:
            [
                new SeedDeviceType(dimmer,     "Shelly", "Dimmer",      $"{tag}-DIM",   "Dimmer240",   2, 2, 200, 400, 60m, true),
                new SeedDeviceType(tapeDimmer, "Shelly", "Tape dimmer", $"{tag}-DIM10", "Dimmer0_10V", 2, 2, null, null, 55m, true),
                new SeedDeviceType(relay,      "Shelly", "Relay",       $"{tag}-REL",   "Relay",       4, 4, 3680, 7360, 95m, true),
                new SeedDeviceType(psu,        "Test",   "Driver",      $"{tag}-PSU",   "ExternalDriver", 0, 0, null, 240, 85m, true),
                new SeedDeviceType(isolator,   "Test",   "Isolator",    $"{tag}-ISO",   "Isolator",    6, 0, null, null, 18m, true),
                new SeedDeviceType(dcPos,      "WAGO",   "+24V",        $"{tag}-DC+",   "Dc24VPositive", 4, 12, null, null, 7m, true),
                new SeedDeviceType(dcNeg,      "WAGO",   "-24V",        $"{tag}-DC-",   "Dc24VNegative", 4, 12, null, null, 7m, true),
                new SeedDeviceType(terminal,   "WAGO",   "Terminal",    $"{tag}-TB",    "Terminal240", 1, 0, null, null, 1.5m, true),
                new SeedDeviceType(bar,        "WAGO",   "Jumper bar",  $"{tag}-BAR",   "Accessory",   0, 0, null, null, 3m, true),
                new SeedDeviceType(endStop,    "WAGO",   "End stop",    $"{tag}-STOP",  "Accessory",   0, 0, null, null, 0.8m, true)
            ],
            Enclosures: [new SeedEnclosure(Id(9), "Hager", $"Box {tag}", 6, 24, "IP30", 220m)],
            RuleSets:
            [
                new SeedRuleSet(Id(10), $"Rules {tag}", 1, true, new RuleSetPayload(
                    Layouts:
                    [
                        new PanelLayoutOption(
                        [
                            new PackingZone(
                                [DeviceCategory.Terminal240, DeviceCategory.Dc24VPositive, DeviceCategory.Dc24VNegative],
                                [DeviceCategory.Isolator]),
                            new PackingZone([DeviceCategory.Dimmer240, DeviceCategory.Dimmer0_10V], []),
                            new PackingZone([], [DeviceCategory.Relay]),
                        ]),
                        new PanelLayoutOption(
                        [
                            new PackingZone(
                                [DeviceCategory.Terminal240, DeviceCategory.Dc24VPositive, DeviceCategory.Dc24VNegative],
                                [DeviceCategory.Isolator]),
                            new PackingZone(
                                [DeviceCategory.Dimmer240, DeviceCategory.Dimmer0_10V],
                                [DeviceCategory.Relay]),
                        ]),
                    ],
                    PsuDeratingFactor: 0.8m,
                    PreferredDevice: new PreferredDevices(isolator, dimmer, tapeDimmer, relay, dcPos, dcNeg, [psu]),
                    Terminals: new TerminalRules(
                        DeviceTypeId: terminal,
                        BlocksPerCircuit: 1,
                        BridgeBarDeviceTypeId: bar,
                        BridgeBarWays: 10,
                        EndStopDeviceTypeId: endStop,
                        EndStopsPerBank: 2)))
            ]);
    }

    [Fact]
    public async Task Seeding_inserts_rows_and_running_it_again_changes_nothing()
    {
        await using var db = fixture.CreateContext();
        var seed = Document("aaaaaaaa");

        var firstRun = await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);
        var secondRun = await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        Assert.Equal(12, firstRun);
        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task Seeding_does_not_overwrite_an_admin_edit()
    {
        await using var db = fixture.CreateContext();
        var seed = Document("bbbbbbbb");
        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        var row = await db.DeviceTypes.SingleAsync(d => d.Id == seed.DeviceTypes[0].Id);
        row.Cost = 999.99m;
        await db.SaveChangesAsync(CancellationToken.None);

        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(999.99m, (await db.DeviceTypes.SingleAsync(d => d.Id == row.Id)).Cost);
    }

    [Fact]
    public async Task A_seeded_ruleset_payload_round_trips_back_into_the_domain_type()
    {
        await using var db = fixture.CreateContext();
        var seed = Document("cccccccc");
        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        var row = await db.RuleSets.SingleAsync(r => r.Id == seed.RuleSets[0].Id);
        var payload = DomainMapper.ToDomain(row);

        Assert.Equal(0.8m, payload.PsuDeratingFactor);
        // Termination on top of the finest layout: terminals left, isolator right.
        var finest = payload.Layouts[0].Zones;
        Assert.Equal(DeviceCategory.Terminal240, finest[0].FromLeft[0]);
        Assert.Equal(DeviceCategory.Isolator, finest[0].FromRight[0]);
        Assert.Equal(seed.DeviceTypes[0].Id, payload.PreferredDevice.Dimmer240);
        Assert.Equal(2, payload.Terminals.EndStopsPerBank);
    }

    [Fact]
    public void A_seed_document_only_references_device_types_it_defines()
    {
        var seed = Document("dddddddd");
        var known = seed.DeviceTypes.Select(d => d.Id).ToHashSet();

        foreach (var ruleSet in seed.RuleSets)
        {
            var p = ruleSet.Payload;
            var referenced = new List<Guid>
            {
                p.PreferredDevice.Isolator, p.PreferredDevice.Dimmer240, p.PreferredDevice.Dimmer0_10V,
                p.PreferredDevice.Relay, p.PreferredDevice.Dc24VPositive, p.PreferredDevice.Dc24VNegative,
                p.Terminals.DeviceTypeId, p.Terminals.BridgeBarDeviceTypeId, p.Terminals.EndStopDeviceTypeId
            };
            referenced.AddRange(p.PreferredDevice.ExternalDriver);

            Assert.All(referenced, id => Assert.Contains(id, known));
        }
    }
}
