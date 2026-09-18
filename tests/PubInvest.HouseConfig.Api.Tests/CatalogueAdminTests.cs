using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class CatalogueAdminTests(HouseConfigApiFactory factory)
{
    private record DeviceTypeDto(Guid Id, string PartNumber, int ModuleWidth, int ChannelCount, decimal Cost, bool Active);
    private record EnclosureDto(Guid Id, string Model, int Rows, int SlotsPerRow);

    private async Task Seed()
    {
        await using var db = factory.NewDbContext();
        await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
    }

    private static object DeviceType(
        string partNumber,
        string category = "Dimmer240",
        int moduleWidth = 3,
        int channelCount = 2,
        decimal cost = 60m,
        bool active = true) => new
    {
        manufacturer = "Shelly",
        model = "Test model",
        partNumber,
        category,
        moduleWidth,
        channelCount,
        maxLoadPerChannelW = (int?)null,
        maxTotalLoadW = (int?)null,
        cost,
        active,
    };

    [Fact]
    public async Task A_device_type_can_be_added_and_is_listed()
    {
        var client = factory.CreateClient();
        var part = $"NEW-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/catalogue/device-types", DeviceType(part));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var listed = await client.GetFromJsonAsync<DeviceTypeDto[]>("/catalogue/device-types");
        Assert.Contains(listed!, d => d.PartNumber == part);
    }

    [Fact]
    public async Task A_duplicate_part_number_is_rejected()
    {
        var client = factory.CreateClient();
        var part = $"DUP-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/catalogue/device-types", DeviceType(part));
        var second = await client.PostAsJsonAsync("/catalogue/device-types", DeviceType(part));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task A_rail_device_with_no_width_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/device-types",
            DeviceType($"ZERO-{Guid.NewGuid():N}", moduleWidth: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("moduleWidth", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_accessory_may_have_no_width()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/device-types",
            DeviceType($"ACC-{Guid.NewGuid():N}", category: "Accessory", moduleWidth: 0, channelCount: 0));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_dimmer_with_no_channels_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/device-types",
            DeviceType($"NOCH-{Guid.NewGuid():N}", channelCount: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("channelCount", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_category_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/device-types",
            DeviceType($"CAT-{Guid.NewGuid():N}", category: "Underfloor"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_negative_cost_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/device-types",
            DeviceType($"NEG-{Guid.NewGuid():N}", cost: -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_device_type_can_be_repriced()
    {
        var client = factory.CreateClient();
        var part = $"PRICE-{Guid.NewGuid():N}";
        var created = await (await client.PostAsJsonAsync("/catalogue/device-types", DeviceType(part)))
            .Content.ReadFromJsonAsync<DeviceTypeDto>();

        var response = await client.PatchAsJsonAsync($"/catalogue/device-types/{created!.Id}",
            DeviceType(part, cost: 72.50m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<DeviceTypeDto>();
        Assert.Equal(72.50m, updated!.Cost);
    }

    [Fact]
    public async Task Deactivating_a_device_a_ruleset_uses_is_refused_and_names_the_ruleset()
    {
        var client = factory.CreateClient();
        await Seed();

        var response = await client.PatchAsJsonAsync($"/catalogue/device-types/{TestSeed.DimmerId}",
            DeviceType("T-DIM2", active: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Test rules", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_enclosure_row_that_is_not_a_whole_number_of_modules_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/enclosures", new
        {
            manufacturer = "Test", model = "Odd", rows = 3, slotsPerRow = 25, ipRating = "IP30", cost = 100m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("multiple of 3", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_enclosure_with_whole_modules_is_accepted()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/catalogue/enclosures", new
        {
            manufacturer = "Test", model = $"Box {Guid.NewGuid():N}", rows = 3, slotsPerRow = 36,
            ipRating = "IP30", cost = 100m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<EnclosureDto>();
        Assert.Equal(36, created!.SlotsPerRow);
    }

    [Fact]
    public async Task A_ruleset_referencing_a_device_that_does_not_exist_is_refused()
    {
        var client = factory.CreateClient();
        await Seed();

        var missing = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/catalogue/rulesets", new
        {
            name = $"Bad {Guid.NewGuid():N}",
            version = 1,
            isDefault = false,
            payload = new
            {
                bandOrder = new[] { "Isolator", "Terminal240", "Dimmer240", "Relay" },
                bandStartsNewRow = true,
                psuDeratingFactor = 0.8,
                preferredDevice = new
                {
                    isolator = TestSeed.IsolatorId,
                    dimmer240 = missing,
                    dimmer0_10V = TestSeed.TapeDimId,
                    relay = TestSeed.RelayId,
                    dc24VPositive = TestSeed.DcPosId,
                    dc24VNegative = TestSeed.DcNegId,
                    externalDriver = new[] { TestSeed.Psu240Id },
                },
                terminals = new
                {
                    deviceTypeId = TestSeed.TerminalId,
                    blocksPerCircuit = 1,
                    bridgeBarDeviceTypeId = TestSeed.BridgeId,
                    bridgeBarWays = 10,
                    endStopDeviceTypeId = TestSeed.EndStopId,
                    endStopsPerBank = 2,
                },
                packing = "firstFit",
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(missing.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_derating_factor_outside_zero_to_one_is_refused()
    {
        var client = factory.CreateClient();
        await Seed();

        var response = await client.PostAsJsonAsync("/catalogue/rulesets", new
        {
            name = $"Derate {Guid.NewGuid():N}",
            version = 1,
            isDefault = false,
            payload = new
            {
                bandOrder = new[] { "Terminal240" },
                bandStartsNewRow = true,
                psuDeratingFactor = 1.5,
                preferredDevice = new
                {
                    isolator = TestSeed.IsolatorId,
                    dimmer240 = TestSeed.DimmerId,
                    dimmer0_10V = TestSeed.TapeDimId,
                    relay = TestSeed.RelayId,
                    dc24VPositive = TestSeed.DcPosId,
                    dc24VNegative = TestSeed.DcNegId,
                    externalDriver = new[] { TestSeed.Psu240Id },
                },
                terminals = new
                {
                    deviceTypeId = TestSeed.TerminalId,
                    blocksPerCircuit = 1,
                    bridgeBarDeviceTypeId = TestSeed.BridgeId,
                    bridgeBarWays = 10,
                    endStopDeviceTypeId = TestSeed.EndStopId,
                    endStopsPerBank = 2,
                },
                packing = "firstFit",
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("derating", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_new_device_type_is_usable_on_the_next_design_without_a_deploy()
    {
        var client = factory.CreateClient();
        await Seed();

        // Add a wider, four-channel dimmer.
        var part = $"WIDE-{Guid.NewGuid():N}";
        var wide = await (await client.PostAsJsonAsync("/catalogue/device-types",
                DeviceType(part, moduleWidth: 6, channelCount: 4)))
            .Content.ReadFromJsonAsync<DeviceTypeDto>();

        // Its own ruleset, not an edit of the shared seed: these tests share one
        // database, so mutating seeded rows breaks whatever runs next.
        var ruleSet = await (await client.PostAsJsonAsync("/catalogue/rulesets", new
        {
            name = $"Wide {Guid.NewGuid():N}",
            version = 1,
            isDefault = false,
            payload = new
            {
                bandOrder = new[] { "Isolator", "Terminal240", "Dimmer240", "Relay" },
                bandStartsNewRow = true,
                psuDeratingFactor = 0.8,
                preferredDevice = new
                {
                    isolator = TestSeed.IsolatorId,
                    dimmer240 = wide!.Id,
                    dimmer0_10V = TestSeed.TapeDimId,
                    relay = TestSeed.RelayId,
                    dc24VPositive = TestSeed.DcPosId,
                    dc24VNegative = TestSeed.DcNegId,
                    externalDriver = new[] { TestSeed.Psu240Id },
                },
                terminals = new
                {
                    deviceTypeId = TestSeed.TerminalId,
                    blocksPerCircuit = 1,
                    bridgeBarDeviceTypeId = TestSeed.BridgeId,
                    bridgeBarWays = 10,
                    endStopDeviceTypeId = TestSeed.EndStopId,
                    endStopsPerBank = 2,
                },
                packing = "firstFit",
            },
        })).Content.ReadFromJsonAsync<RuleSetIdDto>();

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Live {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectIdDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Uses the new part",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = ruleSet!.Id,
            circuits = new object[]
            {
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
                new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 },
            },
        })).Content.ReadFromJsonAsync<SubmainIdDto>();

        var design = await (await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignShapeDto>();

        // Three circuits on a 4-channel device is one dimmer, six slots wide.
        var dimmers = design!.Layout.Devices.Where(d => d.Category == "Dimmer240").ToList();
        Assert.Single(dimmers);
        Assert.Equal(6, dimmers[0].ModuleWidth);
    }

    private record RuleSetIdDto(Guid Id);
    private record ProjectIdDto(Guid Id);
    private record SubmainIdDto(Guid Id);
    private record DeviceShapeDto(string Category, int ModuleWidth);
    private record LayoutShapeDto(DeviceShapeDto[] Devices);
    private record DesignShapeDto(LayoutShapeDto Layout);
}
