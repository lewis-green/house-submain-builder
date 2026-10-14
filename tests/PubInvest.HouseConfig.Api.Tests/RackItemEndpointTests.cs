using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackItemEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record PortDto(int Number, string? Label, bool Poe);
    private record ItemDto(Guid Id, string Kind, string Label, int TopU, int HeightU, PortDto[] Ports);
    private record LayoutDto(Guid RackId, int HeightU, int FreeU, int LayoutVersion, ItemDto[] Items);
    private record VersionDto(int LayoutVersion);
    private record RefusalDto(string Message);
    private record ConflictDto(string Message, int CurrentLayoutVersion);
    private record ProblemDto(Dictionary<string, string[]> Errors);

    private async Task<(HttpClient Client, Guid RackId)> Rack(Guid cabinetId)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Items {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = cabinetId })).Content.ReadFromJsonAsync<IdDto>();
        return (client, rack!.Id);
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, Guid rackId, Guid typeId) =>
        client.PostAsJsonAsync($"/racks/{rackId}/items", new { rackDeviceTypeId = typeId });

    private static async Task<LayoutDto> Added(HttpClient client, Guid rackId, Guid typeId)
    {
        var response = await Add(client, rackId, typeId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LayoutDto>())!;
    }

    private static async Task<LayoutDto> Layout(HttpClient client, Guid rackId) =>
        (await client.GetFromJsonAsync<LayoutDto>($"/racks/{rackId}/layout"))!;

    private static Task<HttpResponseMessage> Port(HttpClient client, Guid itemId, int number, string? label, bool poe) =>
        client.PutAsJsonAsync($"/rack-items/{itemId}/ports/{number}", new { label, poe });

    [Fact]
    public async Task Items_fill_the_rack_from_the_top_down_and_are_named_by_kind()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);

        await Added(client, rackId, TestSeed.Switch48Id);
        await Added(client, rackId, TestSeed.PatchPanelId);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);

        Assert.Equal(new[] { 4, 3, 2 }, layout.Items.Select(i => i.TopU));
        Assert.Equal(new[] { "Switch 1", "Patch panel 1", "Switch 2" }, layout.Items.Select(i => i.Label));
        Assert.Equal(1, layout.FreeU);
    }

    [Fact]
    public async Task A_removed_items_name_is_never_given_to_a_later_one()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);
        var second = layout.Items.Single(i => i.Label == "Switch 2");

        await client.DeleteAsync($"/rack-items/{second.Id}?basedOnLayoutVersion={layout.LayoutVersion}");
        var after = await Added(client, rackId, TestSeed.Switch48Id);

        Assert.Contains(after.Items, i => i.Label == "Switch 3");
        Assert.DoesNotContain(after.Items, i => i.Label == "Switch 2");
    }

    [Fact]
    public async Task A_full_rack_refuses_another_item_in_words_and_saves_nothing()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet2UId);
        await Added(client, rackId, TestSeed.PatchPanel2UId);

        var response = await Add(client, rackId, TestSeed.Switch48Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.StartsWith("No room in this rack: 0U free", (await response.Content.ReadFromJsonAsync<RefusalDto>())!.Message);
        Assert.Single((await Layout(client, rackId)).Items);
    }

    [Fact]
    public async Task An_accessory_or_a_retired_item_is_never_placed()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(client, rackId, TestSeed.RackKitId)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(client, rackId, TestSeed.InactiveSwitchId)).StatusCode);
        Assert.Empty((await Layout(client, rackId)).Items);
    }

    [Fact]
    public async Task A_move_to_a_free_u_is_saved_and_bumps_the_version()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);
        var item = Assert.Single(layout.Items);

        var response = await client.PatchAsJsonAsync($"/rack-items/{item.Id}/position",
            new { topU = 1, basedOnLayoutVersion = layout.LayoutVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(layout.LayoutVersion + 1, (await response.Content.ReadFromJsonAsync<VersionDto>())!.LayoutVersion);
        Assert.Equal(1, Assert.Single((await Layout(client, rackId)).Items).TopU);
    }

    [Fact]
    public async Task A_move_onto_another_item_or_outside_the_cabinet_is_refused()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        var layout = await Added(client, rackId, TestSeed.PatchPanelId);
        var panel = layout.Items.Single(i => i.Label == "Patch panel 1");

        var onto = await client.PatchAsJsonAsync($"/rack-items/{panel.Id}/position",
            new { topU = 4, basedOnLayoutVersion = layout.LayoutVersion });
        var outside = await client.PatchAsJsonAsync($"/rack-items/{panel.Id}/position",
            new { topU = 5, basedOnLayoutVersion = layout.LayoutVersion });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, onto.StatusCode);
        Assert.Contains("U4", (await onto.Content.ReadFromJsonAsync<RefusalDto>())!.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outside.StatusCode);
        Assert.Equal(3, (await Layout(client, rackId)).Items.Single(i => i.Id == panel.Id).TopU);
    }

    [Fact]
    public async Task A_move_based_on_an_old_layout_is_a_conflict()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var first = await Added(client, rackId, TestSeed.Switch48Id);
        await Added(client, rackId, TestSeed.PatchPanelId);

        var response = await client.PatchAsJsonAsync($"/rack-items/{first.Items[0].Id}/position",
            new { topU = 1, basedOnLayoutVersion = first.LayoutVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(first.LayoutVersion + 1, (await response.Content.ReadFromJsonAsync<ConflictDto>())!.CurrentLayoutVersion);
    }

    [Fact]
    public async Task Removing_an_item_frees_its_u_and_drops_its_port_labels()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);
        var item = Assert.Single(layout.Items);
        await Port(client, item.Id, 1, "Kitchen AP", true);

        var response = await client.DeleteAsync($"/rack-items/{item.Id}?basedOnLayoutVersion={layout.LayoutVersion}");

        var after = (await response.Content.ReadFromJsonAsync<LayoutDto>())!;
        Assert.Empty(after.Items);
        Assert.Equal(4, after.FreeU);
        await using var db = factory.NewDbContext();
        Assert.False(db.RackPorts.Any(p => p.RackItemId == item.Id));
    }

    [Fact]
    public async Task A_port_label_and_poe_tick_are_saved_and_shown_in_the_layout()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);

        var response = await Port(client, item.Id, 1, "  Kitchen AP  ", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var port = Assert.Single(Assert.Single((await Layout(client, rackId)).Items).Ports);
        Assert.Equal(new PortDto(1, "Kitchen AP", true), port);
    }

    [Fact]
    public async Task A_label_of_only_spaces_clears_the_port()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);
        await Port(client, item.Id, 2, "Lounge TV", false);

        await Port(client, item.Id, 2, "   ", false);

        Assert.Empty(Assert.Single((await Layout(client, rackId)).Items).Ports);
    }

    [Fact]
    public async Task An_overlong_label_is_refused_with_the_limit()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);

        var response = await Port(client, item.Id, 3, new string('x', 61), false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("60", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["label"][0]);
        Assert.Equal(HttpStatusCode.OK, (await Port(client, item.Id, 3, new string('x', 60), false)).StatusCode);
    }

    [Fact]
    public async Task Poe_on_an_sfp_port_is_refused_but_a_patch_panel_port_takes_it()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        var layout = await Added(client, rackId, TestSeed.PatchPanelId);
        var sw = layout.Items.Single(i => i.Kind == "Switch");
        var panel = layout.Items.Single(i => i.Kind == "PatchPanel");

        var sfp = await Port(client, sw.Id, 49, "Uplink", true);
        var outlet = await Port(client, panel.Id, 5, "Office AP", true);

        Assert.Equal(HttpStatusCode.BadRequest, sfp.StatusCode);
        Assert.Contains("poe", (await sfp.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
        Assert.Equal(HttpStatusCode.OK, (await Port(client, sw.Id, 49, "Uplink", false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, outlet.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public async Task A_port_outside_the_item_is_not_found(int number)
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);

        Assert.Equal(HttpStatusCode.NotFound, (await Port(client, item.Id, number, "x", false)).StatusCode);
    }

    [Fact]
    public async Task Changing_to_a_cabinet_too_short_is_refused_with_the_u_to_clear()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        await Added(client, rackId, TestSeed.PatchPanelId);

        var response = await client.PatchAsJsonAsync($"/racks/{rackId}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet2UId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("U3–U4", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["rackTypeId"][0]);
    }

    /// Several saves of a port not yet stored, at once: two can both find no
    /// row and both insert it. None may fail with a 500, and the port ends up
    /// with one of the labels sent.
    [Fact]
    public async Task Concurrent_saves_of_a_new_port_never_fail_and_one_of_them_sticks()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);
        var labels = Enumerable.Range(1, 6).Select(n => $"Racer {n}").ToArray();

        var responses = await Task.WhenAll(labels.Select(label => Port(client, item.Id, 7, label, false)));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var port = Assert.Single(Assert.Single((await Layout(client, rackId)).Items).Ports);
        Assert.Equal(7, port.Number);
        Assert.Contains(port.Label, labels);
    }

    /// Review focus: two adds racing for the same U. However they interleave,
    /// no request may fail with a 500, and no two items may share a U.
    [Fact]
    public async Task Concurrent_adds_never_share_a_u()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Add(client, rackId, TestSeed.Switch48Id)));

        Assert.All(responses, r => Assert.Contains(r.StatusCode,
            new[] { HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict }));
        var layout = await Layout(client, rackId);
        Assert.Equal(layout.Items.Length, layout.Items.Select(i => i.TopU).Distinct().Count());
        Assert.Equal(responses.Count(r => r.StatusCode == HttpStatusCode.Created), layout.Items.Length);
    }
}
