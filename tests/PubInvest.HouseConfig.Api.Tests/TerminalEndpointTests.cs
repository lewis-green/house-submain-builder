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

    private static object Put(int layoutVersion, params (Guid Id, int Count)[] circuits) => new
    {
        layoutVersion,
        circuits = circuits.Select(c => new { circuitId = c.Id, terminalCount = c.Count }).ToArray()
    };

    private async Task<(Guid SubmainId, DesignDto Design, TerminalsDto Terminals)> WithTerminals(HttpClient client)
    {
        var (submainId, design) = await Generated(client);
        var terminals = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        return (submainId, design, terminals!);
    }

    [Fact]
    public async Task Get_lists_the_circuits_in_terminal_order()
    {
        var client = factory.CreateClient();
        var (_, design, terminals) = await WithTerminals(client);

        Assert.Equal(["Kitchen", "Hall", "Landing", "Porch"], terminals.Circuits.Select(c => c.Name));
        Assert.All(terminals.Circuits, c => Assert.Equal(1, c.TerminalCount));
        Assert.Equal(design.LayoutVersion, terminals.LayoutVersion);
    }

    [Fact]
    public async Task Get_for_an_unknown_submain_is_not_found()
    {
        var response = await factory.CreateClient().GetAsync($"/submains/{Guid.NewGuid()}/terminals");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reordering_relabels_the_terminals_and_leaves_every_channel_alone()
    {
        var client = factory.CreateClient();
        var (submainId, design, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();
        var channelsBefore = design.Layout.Devices.Where(d => d.Category != "Terminal240")
            .Select(d => (d.Label, string.Join(",", d.Channels.Select(c => c.CircuitId))));

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[3], 1), (ids[0], 1), (ids[1], 1), (ids[2], 1)));
        response.EnsureSuccessStatusCode();

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C1"], TerminalLabels(stored!, ids[3]));
        Assert.Equal(["C2"], TerminalLabels(stored!, ids[0]));
        Assert.Equal(channelsBefore, stored!.Layout.Devices.Where(d => d.Category != "Terminal240")
            .Select(d => (d.Label, string.Join(",", d.Channels.Select(c => c.CircuitId)))));

        var after = await response.Content.ReadFromJsonAsync<TerminalsDto>();
        Assert.Equal(["Porch", "Kitchen", "Hall", "Landing"], after!.Circuits.Select(c => c.Name));
        Assert.True(after.LayoutVersion > terminals.LayoutVersion);
    }

    [Fact]
    public async Task Six_cables_on_one_circuit_gives_six_bridged_terminals_and_a_line_bar()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[0], 1), (ids[1], 6), (ids[2], 1), (ids[3], 1)));
        response.EnsureSuccessStatusCode();

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C2.1", "C2.2", "C2.3", "C2.4", "C2.5", "C2.6"], TerminalLabels(stored!, ids[1]));
        Assert.Equal(["C3"], TerminalLabels(stored!, ids[2]));

        // The BOM comes from generating, so ask for one.
        var design = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();
        Assert.Equal(1, design!.Bom.Single(l => l.CatalogueId == TestSeed.LineBridgeId).Quantity);
    }

    [Fact]
    public async Task A_stale_layout_version_is_a_conflict()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion - 1, ids.Select(id => (id, 1)).ToArray()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    public static TheoryData<string> BadLists => ["missing", "duplicate", "foreign", "zero", "thirteen"];

    [Theory]
    [MemberData(nameof(BadLists))]
    public async Task A_list_that_is_not_every_circuit_once_with_a_sane_count_is_refused(string fault)
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var circuits = fault switch
        {
            "missing" => ids.Skip(1).Select(id => (id, 1)),
            "duplicate" => ids.Append(ids[0]).Select(id => (id, 1)),
            "foreign" => ids.Skip(1).Append(Guid.NewGuid()).Select(id => (id, 1)),
            "zero" => ids.Select((id, i) => (id, i == 0 ? 0 : 1)),
            _ => ids.Select((id, i) => (id, i == 0 ? 13 : 1)),
        };

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, circuits.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.Equal(terminals.LayoutVersion, after!.LayoutVersion);
    }

    [Fact]
    public async Task A_count_that_overfills_the_panel_saves_nothing()
    {
        // The test box is 6 rows of 24 slot units: 144 in all, and a terminal is
        // one. Twelve circuits on twelve terminals each is 144 terminals before
        // the relays and isolator, so it cannot fit.
        var client = factory.CreateClient();
        var twelve = Enumerable.Range(1, 12)
            .Select(n => (object)new { type = "Switched", name = $"S{n}", sequence = n }).ToArray();
        var (submainId, _) = await Generated(client, twelve);
        var terminals = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals!.LayoutVersion, terminals.Circuits.Select(c => (c.CircuitId, 12)).ToArray()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.All(after!.Circuits, c => Assert.Equal(1, c.TerminalCount));
        Assert.Equal(terminals.LayoutVersion, after.LayoutVersion);
    }

    [Fact]
    public async Task The_chosen_order_survives_an_edit_to_the_circuits_and_a_rebuild()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        (await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[3], 2), (ids[0], 1), (ids[1], 1), (ids[2], 1)))).EnsureSuccessStatusCode();

        // Rename one circuit through the submain edit, which resends the whole list, and rebuild.
        (await client.PatchAsJsonAsync($"/submains/{submainId}", new
        {
            name = "Terminals",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits = terminals.Circuits.Select((c, i) => new
            {
                id = c.CircuitId, type = c.Type, name = c.Name == "Kitchen" ? "Kitchen spots" : c.Name, sequence = i + 1
            }).ToArray(),
            rebuild = true
        })).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.Equal(["Porch", "Kitchen spots", "Hall", "Landing"], after!.Circuits.Select(c => c.Name));
        Assert.Equal(2, after.Circuits[0].TerminalCount);

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C1.1", "C1.2"], TerminalLabels(stored!, ids[3]));
    }

    [Fact]
    public async Task Generating_with_a_circuit_list_keeps_the_stored_order_and_counts()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var put = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[3], 2), (ids[0], 1), (ids[1], 1), (ids[2], 1)));
        put.EnsureSuccessStatusCode();

        // The wizard resends every circuit by id in its original sequence order
        // and knows nothing of terminal places.
        var design = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new
        {
            circuits = terminals.Circuits.Select((c, i) => new
            {
                id = c.CircuitId, type = c.Type, name = c.Name, sequence = i + 1
            }).ToArray()
        })).Content.ReadFromJsonAsync<DesignDto>();

        Assert.Equal(["C1.1", "C1.2"], TerminalLabels(design!, ids[3]));
        Assert.Equal(["C2"], TerminalLabels(design!, ids[0]));
        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.Equal(["Porch", "Kitchen", "Hall", "Landing"], after!.Circuits.Select(c => c.Name));
        Assert.Equal(2, after.Circuits[0].TerminalCount);
    }
}
