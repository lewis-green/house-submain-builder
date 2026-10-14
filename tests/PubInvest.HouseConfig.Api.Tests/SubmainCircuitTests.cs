using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// Editing a built panel: the build form reads the circuits back, the user
/// changes the counts, and the whole list goes back by id. What the engineer
/// did to a circuit since it was built — its name, its room, its place on the
/// terminal rail — must survive that round trip.
[Collection("api")]
public class SubmainCircuitTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id, int CircuitCount);
    private record CircuitDto(
        Guid Id, string Type, string Name, string? Room, int Sequence, decimal? WattsPerMetre, decimal? LengthMetres);
    private record TerminalCircuitDto(Guid CircuitId, string Name, string? Room, string Type, int TerminalCount);
    private record TerminalsDto(int LayoutVersion, TerminalCircuitDto[] Circuits);

    private async Task<Guid> Generated(HttpClient client, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Circuits {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var created = await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Circuits",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits,
            generate = true
        });
        created.EnsureSuccessStatusCode();

        return (await created.Content.ReadFromJsonAsync<SubmainDto>())!.Id;
    }

    [Fact]
    public async Task Get_lists_the_circuits_in_sequence_order_with_their_tape()
    {
        var client = factory.CreateClient();
        // Sent out of order: the list comes back by Sequence, not by insertion.
        var submainId = await Generated(client, [
            new { type = "LedTape", name = "Plinth", sequence = 3, wattsPerMetre = 9.6m, lengthMetres = 4m },
            new { type = "DimmedLighting", name = "Lighting 1", room = "Kitchen", sequence = 1 },
            new { type = "Switched", name = "Switched 1", sequence = 2 },
        ]);

        var circuits = (await client.GetFromJsonAsync<CircuitDto[]>($"/submains/{submainId}/circuits"))!;

        Assert.Equal(["Lighting 1", "Switched 1", "Plinth"], circuits.Select(c => c.Name));
        Assert.Equal([1, 2, 3], circuits.Select(c => c.Sequence));
        Assert.Equal("DimmedLighting", circuits[0].Type);
        Assert.Equal("Kitchen", circuits[0].Room);
        Assert.Null(circuits[1].WattsPerMetre);
        Assert.Equal(9.6m, circuits[2].WattsPerMetre);
        Assert.Equal(4m, circuits[2].LengthMetres);
        Assert.All(circuits, c => Assert.NotEqual(Guid.Empty, c.Id));
    }

    [Fact]
    public async Task Get_for_an_unknown_submain_is_not_found()
    {
        var response = await factory.CreateClient().GetAsync($"/submains/{Guid.NewGuid()}/circuits");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Editing_the_counts_keeps_the_name_room_and_terminal_place_of_every_circuit_it_keeps()
    {
        var client = factory.CreateClient();
        var submainId = await Generated(client, [
            new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
            new { type = "Switched", name = "Switched 1", sequence = 2 },
            new { type = "Switched", name = "Switched 2", sequence = 3 },
        ]);
        var circuits = (await client.GetFromJsonAsync<CircuitDto[]>($"/submains/{submainId}/circuits"))!;
        var lighting = circuits[0];

        // What the engineer does on the panel screen after the build.
        (await client.PatchAsJsonAsync($"/circuits/{lighting.Id}", new { name = "Island pendants", room = "Kitchen" }))
            .EnsureSuccessStatusCode();
        var terminals = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        (await client.PutAsJsonAsync($"/submains/{submainId}/terminals", new
        {
            terminals!.LayoutVersion,
            circuits = new[] { circuits[1].Id, circuits[2].Id, lighting.Id }
                .Select(id => new { circuitId = id, terminalCount = 1 }).ToArray()
        })).EnsureSuccessStatusCode();

        // The edit form's save: one more dimmed circuit, the last switched one
        // dropped, every kept circuit sent back by id with its room.
        var saved = await client.PatchAsJsonAsync($"/submains/{submainId}", new
        {
            name = "Circuits",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits = new object[]
            {
                new { id = lighting.Id, type = "DimmedLighting", name = "Island pendants", room = "Kitchen", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
                new { id = circuits[1].Id, type = "Switched", name = "Switched 1", sequence = 3 },
            },
            rebuild = true
        });
        saved.EnsureSuccessStatusCode();
        Assert.Equal(3, (await saved.Content.ReadFromJsonAsync<SubmainDto>())!.CircuitCount);

        var after = (await client.GetFromJsonAsync<CircuitDto[]>($"/submains/{submainId}/circuits"))!;
        Assert.Equal(["Island pendants", "Lighting 2", "Switched 1"], after.Select(c => c.Name));
        Assert.Equal(lighting.Id, after[0].Id);
        Assert.Equal("Kitchen", after[0].Room);
        Assert.DoesNotContain(after, c => c.Id == circuits[2].Id);

        // Placed circuits keep their places; the new one, unplaced, follows them.
        var order = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.Equal(["Switched 1", "Island pendants", "Lighting 2"], order!.Circuits.Select(c => c.Name));
    }
}
