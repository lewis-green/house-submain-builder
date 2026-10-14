using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class LocationEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record LocationDto(Guid Id, string Name, string? Notes, int Sequence);
    private record SubmainDto(Guid Id, string Name, Guid? LocationId);
    private record RackDto(Guid Id, string Name, Guid? LocationId);
    private record ProblemDto(Dictionary<string, string[]> Errors);

    private async Task<(HttpClient Client, Guid ProjectId)> House()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Locations {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        return (client, project!.Id);
    }

    private static async Task<LocationDto> AddLocation(HttpClient client, Guid projectId, string name)
    {
        var response = await client.PostAsJsonAsync($"/projects/{projectId}/locations", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LocationDto>())!;
    }

    private static object SubmainBody(string name, Guid? locationId) => new
    {
        name, enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
        circuits = Array.Empty<object>(), locationId,
    };

    private static Task<HttpResponseMessage> AddSubmain(HttpClient client, Guid projectId, string name, Guid? locationId) =>
        client.PostAsJsonAsync($"/projects/{projectId}/submains", SubmainBody(name, locationId));

    private static Task<HttpResponseMessage> AddRack(HttpClient client, Guid projectId, string name, Guid? locationId) =>
        client.PostAsJsonAsync($"/projects/{projectId}/racks",
            new { name, rackTypeId = TestSeed.Cabinet4UId, locationId });

    private static async Task<string[]> Problems(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        return problem!.Errors[key];
    }

    [Fact]
    public async Task Locations_are_listed_in_the_order_they_were_added()
    {
        var (client, projectId) = await House();

        var plant = await AddLocation(client, projectId, "  Plant room ");
        await AddLocation(client, projectId, "Garage");
        await AddLocation(client, projectId, "Attic");

        Assert.Equal("Plant room", plant.Name);
        var listed = await client.GetFromJsonAsync<LocationDto[]>($"/projects/{projectId}/locations");
        Assert.Equal(["Plant room", "Garage", "Attic"], listed!.Select(l => l.Name).ToArray());
        Assert.Equal(listed.Select(l => l.Sequence).Order().ToArray(), listed.Select(l => l.Sequence).ToArray());
    }

    [Fact]
    public async Task A_location_keeps_its_notes()
    {
        var (client, projectId) = await House();

        var response = await client.PostAsJsonAsync($"/projects/{projectId}/locations",
            new { name = "Garage", notes = "Left of the door" });

        Assert.Equal("Left of the door", (await response.Content.ReadFromJsonAsync<LocationDto>())!.Notes);
    }

    [Fact]
    public async Task An_unknown_house_has_no_locations_to_list_or_add_to()
    {
        var (client, _) = await House();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/projects/{Guid.NewGuid()}/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/projects/{Guid.NewGuid()}/locations", new { name = "Garage" })).StatusCode);
    }

    [Fact]
    public async Task A_location_needs_a_name()
    {
        var (client, projectId) = await House();

        var response = await client.PostAsJsonAsync($"/projects/{projectId}/locations", new { name = "  " });

        Assert.Equal(["A location name is required."], await Problems(response, "name"));
    }

    [Fact]
    public async Task A_house_cannot_have_two_locations_with_the_same_name()
    {
        var (client, projectId) = await House();
        await AddLocation(client, projectId, "Plant room");

        var response = await client.PostAsJsonAsync($"/projects/{projectId}/locations", new { name = "plant ROOM " });

        Assert.Equal(["There is already a location called Plant room in this house."], await Problems(response, "name"));
    }

    [Fact]
    public async Task Another_house_can_use_the_same_name()
    {
        var (client, projectId) = await House();
        var (_, otherProject) = await House();
        await AddLocation(client, projectId, "Plant room");

        await AddLocation(client, otherProject, "Plant room");
    }

    [Fact]
    public async Task A_location_is_renamed_and_its_notes_replaced()
    {
        var (client, projectId) = await House();
        var plant = await AddLocation(client, projectId, "Plant room");

        var response = await client.PatchAsJsonAsync($"/locations/{plant.Id}", new { name = " Boiler room ", notes = "Behind the kitchen" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await response.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Equal("Boiler room", renamed!.Name);
        Assert.Equal("Behind the kitchen", renamed.Notes);
        Assert.Equal(plant.Sequence, renamed.Sequence);
    }

    [Fact]
    public async Task A_location_can_change_the_case_of_its_own_name()
    {
        var (client, projectId) = await House();
        var plant = await AddLocation(client, projectId, "plant room");

        var response = await client.PatchAsJsonAsync($"/locations/{plant.Id}", new { name = "Plant room" });

        Assert.Equal("Plant room", (await response.Content.ReadFromJsonAsync<LocationDto>())!.Name);
    }

    [Fact]
    public async Task A_rename_onto_another_locations_name_or_to_nothing_is_refused()
    {
        var (client, projectId) = await House();
        await AddLocation(client, projectId, "Plant room");
        var garage = await AddLocation(client, projectId, "Garage");

        Assert.Equal(["There is already a location called Plant room in this house."],
            await Problems(await client.PatchAsJsonAsync($"/locations/{garage.Id}", new { name = "Plant Room" }), "name"));
        Assert.Equal(["A location name is required."],
            await Problems(await client.PatchAsJsonAsync($"/locations/{garage.Id}", new { name = "" }), "name"));
    }

    [Fact]
    public async Task An_unknown_location_cannot_be_renamed_or_removed()
    {
        var (client, _) = await House();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PatchAsJsonAsync($"/locations/{Guid.NewGuid()}", new { name = "Garage" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/locations/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_location_keeps_its_panels_and_racks_unplaced()
    {
        var (client, projectId) = await House();
        var plant = await AddLocation(client, projectId, "Plant room");
        var submain = await (await AddSubmain(client, projectId, "Ground floor", plant.Id)).Content.ReadFromJsonAsync<SubmainDto>();
        var rack = await (await AddRack(client, projectId, "Comms", plant.Id)).Content.ReadFromJsonAsync<RackDto>();
        Assert.Equal(plant.Id, submain!.LocationId);
        Assert.Equal(plant.Id, rack!.LocationId);

        var response = await client.DeleteAsync($"/locations/{plant.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<LocationDto[]>($"/projects/{projectId}/locations"))!);
        Assert.Null((await client.GetFromJsonAsync<SubmainDto>($"/submains/{submain.Id}"))!.LocationId);
        Assert.Null((await client.GetFromJsonAsync<RackDto>($"/racks/{rack.Id}"))!.LocationId);
    }

    [Fact]
    public async Task A_panel_is_moved_between_locations_and_unplaced_by_its_edit()
    {
        var (client, projectId) = await House();
        var plant = await AddLocation(client, projectId, "Plant room");
        var garage = await AddLocation(client, projectId, "Garage");
        var submain = await (await AddSubmain(client, projectId, "Ground floor", plant.Id)).Content.ReadFromJsonAsync<SubmainDto>();

        var moved = await (await client.PatchAsJsonAsync($"/submains/{submain!.Id}", SubmainBody("Ground floor", garage.Id)))
            .Content.ReadFromJsonAsync<SubmainDto>();
        Assert.Equal(garage.Id, moved!.LocationId);

        // The edit replaces every detail with what it is sent, so an edit that
        // leaves the location out unplaces the panel. The UI always sends it.
        var unplaced = await (await client.PatchAsJsonAsync($"/submains/{submain.Id}", new { name = "Ground floor" }))
            .Content.ReadFromJsonAsync<SubmainDto>();
        Assert.Null(unplaced!.LocationId);
    }

    [Fact]
    public async Task A_panel_cannot_be_placed_in_another_houses_location()
    {
        var (client, projectId) = await House();
        var (_, otherProject) = await House();
        var elsewhere = await AddLocation(client, otherProject, "Next door's garage");
        var mine = await (await AddSubmain(client, projectId, "Ground floor", null)).Content.ReadFromJsonAsync<SubmainDto>();

        const string refusal = "Choose a location in this house, or none.";
        Assert.Equal([refusal], await Problems(await AddSubmain(client, projectId, "First floor", elsewhere.Id), "locationId"));
        Assert.Equal([refusal], await Problems(
            await client.PatchAsJsonAsync($"/submains/{mine!.Id}", SubmainBody("Ground floor", elsewhere.Id)), "locationId"));
        Assert.Equal([refusal], await Problems(await AddSubmain(client, projectId, "Loft", Guid.NewGuid()), "locationId"));
    }

    [Fact]
    public async Task A_rack_is_moved_between_locations_and_unplaced_by_its_settings()
    {
        var (client, projectId) = await House();
        var plant = await AddLocation(client, projectId, "Plant room");
        var garage = await AddLocation(client, projectId, "Garage");
        var rack = await (await AddRack(client, projectId, "Comms", plant.Id)).Content.ReadFromJsonAsync<RackDto>();

        var moved = await (await client.PatchAsJsonAsync($"/racks/{rack!.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, locationId = garage.Id }))
            .Content.ReadFromJsonAsync<RackDto>();
        Assert.Equal(garage.Id, moved!.LocationId);

        var unplaced = await (await client.PatchAsJsonAsync($"/racks/{rack.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId }))
            .Content.ReadFromJsonAsync<RackDto>();
        Assert.Null(unplaced!.LocationId);
    }

    [Fact]
    public async Task A_rack_cannot_be_placed_in_another_houses_location()
    {
        var (client, projectId) = await House();
        var (_, otherProject) = await House();
        var elsewhere = await AddLocation(client, otherProject, "Next door's garage");
        var mine = await (await AddRack(client, projectId, "Comms", null)).Content.ReadFromJsonAsync<RackDto>();

        const string refusal = "Choose a location in this house, or none.";
        Assert.Equal([refusal], await Problems(await AddRack(client, projectId, "Loft", elsewhere.Id), "locationId"));
        Assert.Equal([refusal], await Problems(await client.PatchAsJsonAsync($"/racks/{mine!.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, locationId = elsewhere.Id }), "locationId"));
    }
}
