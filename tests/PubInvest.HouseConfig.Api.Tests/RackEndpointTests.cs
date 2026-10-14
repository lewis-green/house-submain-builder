using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record RackDto(
        Guid Id, string Name, Guid RackTypeId, string Cabinet, int HeightU, int UsedU,
        Guid? FedFromSubmainId, string? FedFromSubmainName, int LayoutVersion);
    private record LayoutDto(Guid RackId, int HeightU, int FreeU, int LayoutVersion, object[] Items);
    private record FedDto(Guid Id, string Name);
    private record ProblemDto(Dictionary<string, string[]> Errors);

    private async Task<(HttpClient Client, Guid ProjectId)> House()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Racks {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        return (client, project!.Id);
    }

    private static async Task<Guid> Submain(HttpClient client, Guid projectId, string name) =>
        (await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name, enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId, circuits = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<IdDto>())!.Id;

    private static Task<HttpResponseMessage> AddRack(HttpClient client, Guid projectId, object body) =>
        client.PostAsJsonAsync($"/projects/{projectId}/racks", body);

    [Fact]
    public async Task A_rack_is_added_to_a_house_and_listed_with_its_cabinet_and_feed()
    {
        var (client, projectId) = await House();
        var ground = await Submain(client, projectId, "Ground floor");

        var response = await AddRack(client, projectId,
            new { name = "  Comms cupboard ", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = ground });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rack = await response.Content.ReadFromJsonAsync<RackDto>();
        Assert.Equal("Comms cupboard", rack!.Name);
        Assert.Equal("Test 4U cabinet", rack.Cabinet);
        Assert.Equal(4, rack.HeightU);
        Assert.Equal(0, rack.UsedU);
        Assert.Equal("Ground floor", rack.FedFromSubmainName);

        var listed = await client.GetFromJsonAsync<RackDto[]>($"/projects/{projectId}/racks");
        Assert.Equal(rack.Id, Assert.Single(listed!).Id);
    }

    [Fact]
    public async Task A_rack_can_be_fed_from_no_submain()
    {
        var (client, projectId) = await House();

        var rack = await (await AddRack(client, projectId,
            new { name = "Loft", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = (Guid?)null }))
            .Content.ReadFromJsonAsync<RackDto>();

        Assert.Null(rack!.FedFromSubmainId);
        Assert.Null(rack.FedFromSubmainName);
    }

    [Fact]
    public async Task A_feed_from_another_house_is_refused()
    {
        var (client, projectId) = await House();
        var (_, otherProject) = await House();
        var elsewhere = await Submain(client, otherProject, "Next door");

        var response = await AddRack(client, projectId,
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = elsewhere });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Contains("fedFromSubmainId", problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_rack_needs_a_name_and_a_cabinet_from_the_catalogue()
    {
        var (client, projectId) = await House();

        var response = await AddRack(client, projectId, new { name = " ", rackTypeId = Guid.NewGuid() });

        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", problem!.Errors.Keys);
        Assert.Contains("rackTypeId", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_new_rack_has_an_empty_layout_with_every_u_free()
    {
        var (client, projectId) = await House();
        var rack = await (await AddRack(client, projectId, new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId }))
            .Content.ReadFromJsonAsync<RackDto>();

        var layout = await client.GetFromJsonAsync<LayoutDto>($"/racks/{rack!.Id}/layout");

        Assert.Equal(4, layout!.HeightU);
        Assert.Equal(4, layout.FreeU);
        Assert.Empty(layout.Items);
    }

    [Fact]
    public async Task The_feed_can_be_changed_and_cleared_and_the_submain_lists_what_it_feeds()
    {
        var (client, projectId) = await House();
        var ground = await Submain(client, projectId, "Ground floor");
        var first = await Submain(client, projectId, "First floor");
        var rack = await (await AddRack(client, projectId,
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = ground }))
            .Content.ReadFromJsonAsync<RackDto>();

        Assert.Equal("Comms", Assert.Single((await client.GetFromJsonAsync<FedDto[]>($"/submains/{ground}/racks"))!).Name);

        var moved = await (await client.PatchAsJsonAsync($"/racks/{rack!.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = first }))
            .Content.ReadFromJsonAsync<RackDto>();
        Assert.Equal("First floor", moved!.FedFromSubmainName);
        Assert.Empty((await client.GetFromJsonAsync<FedDto[]>($"/submains/{ground}/racks"))!);

        var cleared = await (await client.PatchAsJsonAsync($"/racks/{rack.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = (Guid?)null }))
            .Content.ReadFromJsonAsync<RackDto>();
        Assert.Null(cleared!.FedFromSubmainId);
    }

    [Fact]
    public async Task An_unknown_rack_or_house_is_not_found()
    {
        var (client, _) = await House();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/racks/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/racks/{Guid.NewGuid()}/layout")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/projects/{Guid.NewGuid()}/racks")).StatusCode);
    }
}
