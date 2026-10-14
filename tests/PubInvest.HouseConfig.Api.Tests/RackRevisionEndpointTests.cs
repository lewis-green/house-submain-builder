using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackRevisionEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record ItemDto(Guid Id, string Label, int TopU);
    private record LayoutDto(int LayoutVersion, ItemDto[] Items);
    private record RevisionDto(Guid Id, Guid RackId, int Number, bool Current);
    private record BomLineDto(string PartNumber, int Quantity);
    private record BomDto(BomLineDto[] Lines);
    private record RefusalDto(string Message);

    private async Task<(HttpClient Client, Guid ProjectId, Guid RackId)> Rack()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Rack revs {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId })).Content.ReadFromJsonAsync<IdDto>();
        return (client, project.Id, rack!.Id);
    }

    private static async Task<LayoutDto> Add(HttpClient client, Guid rackId, Guid typeId) =>
        (await (await client.PostAsJsonAsync($"/racks/{rackId}/items", new { rackDeviceTypeId = typeId }))
            .Content.ReadFromJsonAsync<LayoutDto>())!;

    private static async Task<RevisionDto> Issue(HttpClient client, Guid rackId) =>
        (await (await client.PostAsJsonAsync($"/racks/{rackId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>())!;

    private static async Task<bool> LatestIsCurrent(HttpClient client, Guid rackId) =>
        (await client.GetFromJsonAsync<RevisionDto[]>($"/racks/{rackId}/revisions"))![0].Current;

    [Fact]
    public async Task Revisions_are_numbered_from_one_and_the_newest_is_current()
    {
        var (client, _, rackId) = await Rack();
        await Add(client, rackId, TestSeed.Switch48Id);

        var first = await Issue(client, rackId);
        var second = await Issue(client, rackId);

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        var listed = await client.GetFromJsonAsync<RevisionDto[]>($"/racks/{rackId}/revisions");
        Assert.Equal(new[] { 2, 1 }, listed!.Select(r => r.Number));
        Assert.True(listed[0].Current);
    }

    [Fact]
    public async Task An_empty_rack_cannot_be_issued()
    {
        var (client, _, rackId) = await Rack();

        var response = await client.PostAsJsonAsync($"/racks/{rackId}/revisions", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("empty", (await response.Content.ReadFromJsonAsync<RefusalDto>())!.Message);
    }

    [Fact]
    public async Task A_label_edit_makes_the_pdf_out_of_date_and_putting_it_back_makes_it_current()
    {
        var (client, _, rackId) = await Rack();
        var item = (await Add(client, rackId, TestSeed.Switch48Id)).Items[0];
        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = "Kitchen AP", poe = true });
        await Issue(client, rackId);

        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = "Kitchen AP 2", poe = true });
        Assert.False(await LatestIsCurrent(client, rackId));

        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = "Kitchen AP", poe = true });
        Assert.True(await LatestIsCurrent(client, rackId));
    }

    [Fact]
    public async Task A_move_or_a_feed_change_makes_the_pdf_out_of_date()
    {
        var (client, projectId, rackId) = await Rack();
        var layout = await Add(client, rackId, TestSeed.Switch48Id);
        await Issue(client, rackId);

        await client.PatchAsJsonAsync($"/rack-items/{layout.Items[0].Id}/position",
            new { topU = 1, basedOnLayoutVersion = layout.LayoutVersion });
        Assert.False(await LatestIsCurrent(client, rackId));

        await Issue(client, rackId);
        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Ground floor", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<IdDto>();
        await client.PatchAsJsonAsync($"/racks/{rackId}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = submain!.Id });
        Assert.False(await LatestIsCurrent(client, rackId));
    }

    [Fact]
    public async Task The_rack_bom_lists_the_cabinet_the_items_and_the_kit_a_half_width_switch_needs()
    {
        var (client, _, rackId) = await Rack();
        await Add(client, rackId, TestSeed.Switch16Id);
        await Add(client, rackId, TestSeed.PatchPanelId);
        await Add(client, rackId, TestSeed.PatchPanelId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/racks/{rackId}/bom")).StatusCode);
        await Issue(client, rackId);
        var bom = await client.GetFromJsonAsync<BomDto>($"/racks/{rackId}/bom");

        Assert.Equal(1, bom!.Lines.Single(l => l.PartNumber == "T-CAB4").Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "T-SW16").Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "T-KIT").Quantity);
        Assert.Equal(2, bom.Lines.Single(l => l.PartNumber == "T-PP24").Quantity);

        var csv = await client.GetStringAsync($"/racks/{rackId}/bom.csv");
        Assert.Contains("T-KIT", csv);
    }

    [Fact]
    public async Task The_house_bom_adds_rack_lines_to_panel_lines()
    {
        var (client, projectId, rackId) = await Rack();
        await Add(client, rackId, TestSeed.Switch48Id);
        await Issue(client, rackId);
        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Ground floor", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new object[] { new { type = "DimmedLighting", name = "Kitchen", sequence = 1 } },
            extraFixtures = Array.Empty<object>(), generate = true,
        })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/submains/{submain!.Id}/revisions", new { });

        var bom = await client.GetFromJsonAsync<BomDto>($"/projects/{projectId}/bom");

        Assert.Contains(bom!.Lines, l => l.PartNumber == "T-SW48");
        Assert.Contains(bom.Lines, l => l.PartNumber == "T-CAB4");
        Assert.Contains(bom.Lines, l => l.PartNumber == "T-DIM2");
    }

    /// Review focus: an item retired after it was placed is still in the rack.
    [Fact]
    public async Task A_deactivated_item_still_appears_in_a_new_revision()
    {
        var (client, _, rackId) = await Rack();
        var item = (await Add(client, rackId, TestSeed.PatchPanelId)).Items[0];
        await using (var db = factory.NewDbContext())
        {
            var row = db.RackDeviceTypes.Single(t => t.Id == TestSeed.PatchPanelId);
            row.Active = false;
            await db.SaveChangesAsync();
        }

        try
        {
            await Issue(client, rackId);
            var bom = await client.GetFromJsonAsync<BomDto>($"/racks/{rackId}/bom");
            Assert.Contains(bom!.Lines, l => l.PartNumber == "T-PP24");
            Assert.Contains((await client.GetFromJsonAsync<LayoutDto>($"/racks/{rackId}/layout"))!.Items, i => i.Id == item.Id);
        }
        finally
        {
            await using var db = factory.NewDbContext();
            db.RackDeviceTypes.Single(t => t.Id == TestSeed.PatchPanelId).Active = true;
            await db.SaveChangesAsync();
        }
    }
}
