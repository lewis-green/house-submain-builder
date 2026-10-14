using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// The order circuits land along the terminal rail, and how many terminals each
/// lands on, are the engineer's to choose — without rewiring any Shelly.
[Collection("api")]
public class TerminalEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, string? CircuitRoom, bool IsSpare);
    private record DeviceDto(Guid? Id, string Category, string Label, Guid? CircuitId, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record BomLineDto(Guid CatalogueId, int Quantity);
    private record DesignDto(int LayoutVersion, LayoutDto Layout, BomLineDto[] Bom);
    private record TerminalCircuitDto(Guid CircuitId, string Name, string? Room, string Type, int TerminalCount);
    private record TerminalsDto(int LayoutVersion, TerminalCircuitDto[] Circuits);

    private static readonly object[] FourCircuits =
    [
        new { type = "DimmedLighting", name = "Kitchen", sequence = 1 },
        new { type = "DimmedLighting", name = "Hall", sequence = 2 },
        new { type = "Switched", name = "Landing", sequence = 3 },
        new { type = "Switched", name = "Porch", sequence = 4 },
    ];

    private async Task<(Guid SubmainId, DesignDto Design)> Generated(HttpClient client, object[]? circuits = null)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Terminals {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Terminals",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits = circuits ?? FourCircuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var design = await (await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

        return (submain.Id, design!);
    }

    private static string[] TerminalLabels(DesignDto design, Guid circuitId) =>
        design.Layout.Devices.Where(d => d.Category == "Terminal240" && d.CircuitId == circuitId)
            .Select(d => d.Label).ToArray();

    private static Guid CircuitOnChannel(DesignDto design, string name) =>
        design.Layout.Devices.SelectMany(d => d.Channels).Single(c => c.CircuitName == name).CircuitId!.Value;

    [Fact]
    public async Task Generate_and_the_stored_layout_name_the_circuit_on_each_terminal()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client);
        var kitchen = CircuitOnChannel(design, "Kitchen");

        Assert.Equal(["C1"], TerminalLabels(design, kitchen));

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C1"], TerminalLabels(stored!, kitchen));
    }

    [Fact]
    public async Task A_terminal_is_not_a_channel_so_a_circuit_can_still_be_moved_between_channels()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client);
        var kitchen = CircuitOnChannel(design, "Kitchen");
        var dimmer = design.Layout.Devices.First(d => d.Channels.Any(c => c.CircuitId == kitchen));
        var index = dimmer.Channels.Single(c => c.CircuitId == kitchen).ChannelIndex;

        // Free it, then wire it back: the terminal must not count as "wired elsewhere".
        var freed = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/channels/{index}",
            new { circuitId = (Guid?)null, isSpare = true, basedOnLayoutVersion = design.LayoutVersion });
        freed.EnsureSuccessStatusCode();
        var version = (await freed.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["layoutVersion"];

        var rewired = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/channels/{index}",
            new { circuitId = kitchen, isSpare = false, basedOnLayoutVersion = version });

        Assert.Equal(HttpStatusCode.OK, rewired.StatusCode);
    }
}
