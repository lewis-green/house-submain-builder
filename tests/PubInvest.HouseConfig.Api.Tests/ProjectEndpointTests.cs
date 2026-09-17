using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class ProjectEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id, string Name, string? Address);
    private record SubmainDto(Guid Id, string Name, int LayoutVersion, int CircuitCount);

    [Fact]
    public async Task A_project_can_be_created_and_read_back()
    {
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/projects", new { name = "Willow House", address = "1 Test Lane" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var project = await created.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal("Willow House", project!.Name);

        var fetched = await client.GetFromJsonAsync<ProjectDto>($"/projects/{project.Id}");
        Assert.Equal("1 Test Lane", fetched!.Address);
    }

    [Fact]
    public async Task A_project_without_a_name_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/projects", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_submain_is_created_with_its_circuits_and_reports_the_count()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Circuit House" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var response = await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Ground Floor West",
            circuits = new object[]
            {
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
                new { type = "Switched", name = "Switched 1", sequence = 2 },
                new { type = "LedTape", name = "Tape 1", sequence = 3, wattsPerMetre = 14.4, lengthMetres = 5.0 }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submain = await response.Content.ReadFromJsonAsync<SubmainDto>();
        Assert.Equal(3, submain!.CircuitCount);
        Assert.Equal(0, submain.LayoutVersion);
    }

    [Fact]
    public async Task An_unknown_circuit_type_is_rejected_with_the_offending_index()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Bad Type House" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var response = await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Bad",
            circuits = new object[] { new { type = "Underfloor", name = "Nope", sequence = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("circuits[0].type", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Patching_a_submain_replaces_its_circuits_and_keeps_supplied_ids()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Patch House" }))
            .Content.ReadFromJsonAsync<ProjectDto>();
        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Loft",
            circuits = new object[] { new { type = "Switched", name = "Switched 1", sequence = 1 } }
        })).Content.ReadFromJsonAsync<SubmainDto>();

        Guid keptId;
        await using (var db = factory.NewDbContext())
        {
            keptId = (await db.Circuits.SingleAsync(c => c.SubmainId == submain!.Id)).Id;
        }

        var patched = await client.PatchAsJsonAsync($"/submains/{submain!.Id}", new
        {
            name = "Loft",
            circuits = new object[]
            {
                new { id = keptId, type = "Switched", name = "Immersion", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 2 }
            }
        });

        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        await using var check = factory.NewDbContext();
        var circuits = await check.Circuits.Where(c => c.SubmainId == submain.Id).ToListAsync();
        Assert.Equal(2, circuits.Count);
        Assert.Equal("Immersion", circuits.Single(c => c.Id == keptId).Name);
    }

    [Fact]
    public async Task An_unknown_project_returns_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
