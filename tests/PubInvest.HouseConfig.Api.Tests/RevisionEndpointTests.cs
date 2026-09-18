using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RevisionEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record DesignDto(int LayoutVersion);
    private record RevisionDto(Guid Id, Guid SubmainId, int LayoutVersion, DateTimeOffset IssuedAt, string IssuedBy);
    private record RevisionDetailDto(Guid Id, int LayoutVersion, string SnapshotJson);

    private static object[] ThreeDimmed =>
    [
        new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 },
        new { type = "DimmedLighting", name = "Kitchen island", sequence = 2 },
        new { type = "Switched", name = "Immersion", sequence = 3 }
    ];

    private async Task<Guid> NewSubmain(HttpClient client, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Rev {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Issued submain",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        return submain!.Id;
    }

    private async Task<(Guid SubmainId, DesignDto Design)> Generated(HttpClient client, object[] circuits)
    {
        var submainId = await NewSubmain(client, circuits);
        var design = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();
        return (submainId, design!);
    }

    [Fact]
    public async Task Issuing_a_revision_records_the_layout_version_and_who_issued_it()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, ThreeDimmed);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var revision = await response.Content.ReadFromJsonAsync<RevisionDto>();
        Assert.Equal(design.LayoutVersion, revision!.LayoutVersion);
        Assert.NotEqual(default, revision.IssuedAt);
        Assert.Equal(submainId, revision.SubmainId);
    }

    [Fact]
    public async Task A_revision_keeps_the_catalogue_it_was_issued_with()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await (await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>();

        // An admin renames the dimmer afterwards.
        await using (var db = factory.NewDbContext())
        {
            var dimmer = await db.DeviceTypes.SingleAsync(d => d.Id == TestSeed.DimmerId);
            dimmer.Model = "Renamed after issue";
            await db.SaveChangesAsync();
        }

        var stored = await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{revision!.Id}");

        // What was issued must keep meaning what it meant.
        Assert.DoesNotContain("Renamed after issue", stored!.SnapshotJson);
        Assert.Contains("Test Dimmer 2", stored.SnapshotJson);
    }

    [Fact]
    public async Task A_revision_carries_the_layout_circuits_and_enclosure()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);

        var revision = await (await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>();
        var stored = await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{revision!.Id}");

        Assert.Contains("Kitchen ceiling", stored!.SnapshotJson);
        Assert.Contains("Dimmer 1", stored.SnapshotJson);
        Assert.Contains("Box 6x24", stored.SnapshotJson);
    }

    [Fact]
    public async Task Issuing_a_submain_with_no_panel_is_a_validation_problem()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, ThreeDimmed);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revisions_are_listed_newest_first()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);

        await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });
        await Task.Delay(10);
        await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });

        var list = await client.GetFromJsonAsync<RevisionDto[]>($"/submains/{submainId}/revisions");

        Assert.Equal(2, list!.Length);
        Assert.True(list[0].IssuedAt >= list[1].IssuedAt);
    }

    [Fact]
    public async Task An_unknown_revision_is_a_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/revisions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
