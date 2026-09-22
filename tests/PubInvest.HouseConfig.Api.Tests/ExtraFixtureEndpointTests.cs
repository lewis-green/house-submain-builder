using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// Blinds and colour tape as circuits; meters and network gear as fixtures.
[Collection("api")]
public class ExtraFixtureEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record FixtureDto(Guid DeviceTypeId, int Quantity);
    private record SubmainDto(Guid Id, int LayoutVersion, FixtureDto[] ExtraFixtures);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, string? CircuitRoom, bool IsSpare);
    private record DeviceDto(string Category, int RowIndex, int StartSlot, string Label, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record BomDto(Guid CatalogueId, string PartNumber, int Quantity, bool PanelMounted);
    private record DesignDto(int LayoutVersion, LayoutDto Layout, BomDto[] Bom);

    private async Task<Guid> SeededProject(HttpClient client)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Fixtures {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        return project!.Id;
    }

    private async Task<SubmainDto> Submain(HttpClient client, object body)
    {
        var projectId = await SeededProject(client);
        return (await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", body))
            .Content.ReadFromJsonAsync<SubmainDto>())!;
    }

    private static async Task<DesignDto> Generate(HttpClient client, Guid id) =>
        (await (await client.PostAsJsonAsync($"/submains/{id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>())!;

    [Fact]
    public async Task A_blind_becomes_a_cover_controller_with_a_nameable_channel()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Blinds", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[] { new { type = "Cover", name = "Bay window", room = "Snug", sequence = 1 } },
        });

        var design = await Generate(client, submain.Id);
        var cover = Assert.Single(design.Layout.Devices.Where(d => d.Category == "Cover"));
        var channel = Assert.Single(cover.Channels.Where(c => !c.IsSpare));

        Assert.Equal("Bay window", channel.CircuitName);
        Assert.Equal("Snug", channel.CircuitRoom);
    }

    [Fact]
    public async Task A_colour_run_takes_one_controller_and_shows_one_channel()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Colour", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[]
            {
                new { type = "RgbwTape", name = "Cove", room = "Lounge", sequence = 1,
                      wattsPerMetre = 14.4m, lengthMetres = 5m },
            },
        });

        var design = await Generate(client, submain.Id);
        var led = Assert.Single(design.Layout.Devices.Where(d => d.Category == "LedController"));

        Assert.Single(led.Channels);
        Assert.Equal("Cove", led.Channels[0].CircuitName);
        Assert.Contains(design.Layout.Devices, d => d.Category == "Dc24VPositive");
    }

    [Fact]
    public async Task A_meter_asked_for_on_the_submain_is_placed_and_ordered()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Metered", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[] { new { type = "Switched", name = "Immersion", sequence = 1 } },
            extraFixtures = new[] { new { deviceTypeId = TestSeed.MeterId, quantity = 2 } },
        });

        Assert.Equal(2, submain.ExtraFixtures.Single().Quantity);

        var design = await Generate(client, submain.Id);

        Assert.Equal(2, design.Layout.Devices.Count(d => d.Category == "EnergyMeter"));
        Assert.Equal(2, design.Bom.Single(l => l.CatalogueId == TestSeed.MeterId).Quantity);
    }

    [Fact]
    public async Task Fixtures_survive_a_reload_and_a_regeneration()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Keeps", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[] { new { type = "Switched", name = "Immersion", sequence = 1 } },
            extraFixtures = new[] { new { deviceTypeId = TestSeed.LanId, quantity = 1 } },
        });

        await Generate(client, submain.Id);
        await Generate(client, submain.Id);

        var reloaded = await client.GetFromJsonAsync<SubmainDto>($"/submains/{submain.Id}");
        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submain.Id}/layout");

        Assert.Equal(TestSeed.LanId, reloaded!.ExtraFixtures.Single().DeviceTypeId);
        Assert.Single(stored!.Layout.Devices.Where(d => d.Category == "Network"));
    }

    [Fact]
    public async Task Updating_a_submain_without_mentioning_fixtures_leaves_them_alone()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Untouched", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[] { new { type = "Switched", name = "Immersion", sequence = 1 } },
            extraFixtures = new[] { new { deviceTypeId = TestSeed.MeterId, quantity = 1 } },
        });

        var patched = await (await client.PatchAsJsonAsync($"/submains/{submain.Id}", new
        {
            name = "Renamed", enclosureTypeId = TestSeed.EnclosureId,
        })).Content.ReadFromJsonAsync<SubmainDto>();

        Assert.Equal(TestSeed.MeterId, patched!.ExtraFixtures.Single().DeviceTypeId);
    }

    [Fact]
    public async Task Sending_an_empty_fixture_list_clears_them()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Cleared", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[] { new { type = "Switched", name = "Immersion", sequence = 1 } },
            extraFixtures = new[] { new { deviceTypeId = TestSeed.MeterId, quantity = 1 } },
        });

        var patched = await (await client.PatchAsJsonAsync($"/submains/{submain.Id}", new
        {
            name = "Cleared", enclosureTypeId = TestSeed.EnclosureId,
            extraFixtures = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<SubmainDto>();

        Assert.Empty(patched!.ExtraFixtures);
    }

    [Fact]
    public async Task The_wizard_can_preview_a_fixture_before_it_is_saved()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Preview", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new[] { new { type = "Switched", name = "Immersion", sequence = 1 } },
        });

        var preview = await (await client.PostAsJsonAsync($"/submains/{submain.Id}/design/preview", new
        {
            enclosureTypeId = TestSeed.EnclosureId,
            circuits = new[] { new { type = "Switched", name = "Immersion", sequence = 1 } },
            extraFixtures = new[] { new { deviceTypeId = TestSeed.LanId, quantity = 1 } },
        })).Content.ReadFromJsonAsync<DesignDto>();

        Assert.Single(preview!.Layout.Devices.Where(d => d.Category == "Network"));

        var stored = await client.GetFromJsonAsync<SubmainDto>($"/submains/{submain.Id}");
        Assert.Empty(stored!.ExtraFixtures);
    }
}
