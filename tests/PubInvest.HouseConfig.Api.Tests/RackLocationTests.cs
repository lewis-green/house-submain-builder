using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// Where a rack is, as its issued drawing prints it. Kept apart from the other
/// rack tests so the two features' files change independently.
[Collection("api")]
public class RackLocationTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record RevisionDto(Guid Id, int Number, bool Current);

    private static string PdfText(byte[] bytes)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private async Task<(HttpClient Client, Guid ProjectId, Guid RackId)> RackWithASwitch()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Rack places {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/racks/{rack!.Id}/items", new { rackDeviceTypeId = TestSeed.Switch48Id });
        return (client, project.Id, rack.Id);
    }

    private static async Task<RevisionDto> Issue(HttpClient client, Guid rackId) =>
        (await (await client.PostAsJsonAsync($"/racks/{rackId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>())!;

    private static async Task<bool> IsCurrent(HttpClient client, Guid rackId, Guid revisionId) =>
        (await client.GetFromJsonAsync<RevisionDto[]>($"/racks/{rackId}/revisions"))!.Single(r => r.Id == revisionId).Current;

    [Fact]
    public async Task Placing_a_rack_dates_its_revision_and_the_next_pdf_says_where_it_is()
    {
        var (client, projectId, rackId) = await RackWithASwitch();
        var before = await Issue(client, rackId);
        var garage = await (await client.PostAsJsonAsync($"/projects/{projectId}/locations", new { name = "Garage" }))
            .Content.ReadFromJsonAsync<IdDto>();

        await client.PatchAsJsonAsync($"/racks/{rackId}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, locationId = garage!.Id });

        Assert.False(await IsCurrent(client, rackId, before.Id));
        var after = await Issue(client, rackId);
        var text = PdfText(await client.GetByteArrayAsync($"/rack-revisions/{after.Id}/export/pdf"));
        Assert.Contains("Comms  ·  Garage", text);
    }

    [Fact]
    public async Task An_unplaced_rack_has_nothing_after_its_name()
    {
        var (client, _, rackId) = await RackWithASwitch();
        var revision = await Issue(client, rackId);

        var text = PdfText(await client.GetByteArrayAsync($"/rack-revisions/{revision.Id}/export/pdf"));

        Assert.DoesNotContain("Comms  ·", text);
    }

    [Fact]
    public async Task A_rack_revision_issued_before_locations_were_recorded_is_still_current()
    {
        var (client, _, rackId) = await RackWithASwitch();
        var revision = await Issue(client, rackId);

        await using (var db = factory.NewDbContext())
        {
            var stored = await db.RackRevisions.SingleAsync(r => r.Id == revision.Id);
            Assert.Contains("\"locationName\":null", stored.SnapshotJson);
            stored.SnapshotJson = stored.SnapshotJson.Replace(",\"locationName\":null", "");
            Assert.DoesNotContain("locationName", stored.SnapshotJson);
            await db.SaveChangesAsync();
        }

        Assert.True(await IsCurrent(client, rackId, revision.Id));
    }
}
