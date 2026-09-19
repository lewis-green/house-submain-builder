using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// The room a circuit feeds travels with its name: onto the channel an engineer
/// taps, and from there into the panel schedule.
[Collection("api")]
public class CircuitRoomTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, string? CircuitRoom, bool IsSpare);
    private record DeviceDto(Guid? Id, string Category, string Label, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record DesignDto(int LayoutVersion, LayoutDto Layout);

    private async Task<Guid> SeededProject(HttpClient client)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Rooms {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        return project!.Id;
    }

    private async Task<(Guid ProjectId, Guid SubmainId, DesignDto Design)> Generated(
        HttpClient client, object[] circuits)
    {
        var projectId = await SeededProject(client);

        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Rooms",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var design = await (await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

        return (projectId, submain.Id, design!);
    }

    private static ChannelDto ChannelNamed(DesignDto design, string circuitName) =>
        design.Layout.Devices.SelectMany(d => d.Channels).Single(c => c.CircuitName == circuitName);

    [Fact]
    public async Task A_generated_design_reports_the_room_alongside_the_circuit_name()
    {
        var client = factory.CreateClient();
        var (_, _, design) = await Generated(client,
            [new { type = "DimmedLighting", name = "Island pendants", room = "Kitchen", sequence = 1 }]);

        Assert.Equal("Kitchen", ChannelNamed(design, "Island pendants").CircuitRoom);
    }

    [Fact]
    public async Task The_stored_layout_reports_the_room_too()
    {
        var client = factory.CreateClient();
        var (_, submainId, _) = await Generated(client,
            [new { type = "Switched", name = "Worktop sockets", room = "Utility", sequence = 1 }]);

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");

        Assert.Equal("Utility", ChannelNamed(stored!, "Worktop sockets").CircuitRoom);
    }

    [Fact]
    public async Task Naming_the_room_and_the_circuit_together_reaches_the_channel()
    {
        var client = factory.CreateClient();
        var (_, submainId, design) = await Generated(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var circuitId = ChannelNamed(design, "Lighting 1").CircuitId;
        Assert.Null(ChannelNamed(design, "Lighting 1").CircuitRoom);

        await client.PatchAsJsonAsync($"/circuits/{circuitId}", new { name = "Ceiling spots", room = "Snug" });

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        var channel = ChannelNamed(stored!, "Ceiling spots");

        Assert.Equal("Snug", channel.CircuitRoom);
    }

    [Fact]
    public async Task A_blank_room_is_stored_as_no_room_rather_than_an_empty_string()
    {
        var client = factory.CreateClient();
        var (_, submainId, design) = await Generated(client,
            [new { type = "DimmedLighting", name = "Lighting 1", room = "Hall", sequence = 1 }]);

        var circuitId = ChannelNamed(design, "Lighting 1").CircuitId;

        await client.PatchAsJsonAsync($"/circuits/{circuitId}", new { name = "Lighting 1", room = "   " });

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");

        Assert.Null(ChannelNamed(stored!, "Lighting 1").CircuitRoom);
    }

    [Fact]
    public async Task The_house_offers_the_rooms_it_already_uses_once_each_in_order()
    {
        var client = factory.CreateClient();
        var (projectId, _, _) = await Generated(client, [
            new { type = "DimmedLighting", name = "Lighting 1", room = "Kitchen", sequence = 1 },
            new { type = "DimmedLighting", name = "Lighting 2", room = "Kitchen", sequence = 2 },
            new { type = "Switched", name = "Switched 1", room = "Boot room", sequence = 3 },
            new { type = "Switched", name = "Switched 2", sequence = 4 },
        ]);

        var rooms = await client.GetFromJsonAsync<string[]>($"/projects/{projectId}/rooms");

        Assert.Equal(["Boot room", "Kitchen"], rooms!);
    }

    [Fact]
    public async Task A_house_with_no_rooms_named_yet_offers_an_empty_list()
    {
        var client = factory.CreateClient();
        var projectId = await SeededProject(client);

        var rooms = await client.GetFromJsonAsync<string[]>($"/projects/{projectId}/rooms");

        Assert.Empty(rooms!);
    }
}
