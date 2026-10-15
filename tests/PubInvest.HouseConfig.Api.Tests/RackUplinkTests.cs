using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// A switch's uplink: set, refused, shown at both ends, and cleared when the
/// switch it goes to is removed. Kept apart from the other rack tests so the
/// two features' files change independently.
[Collection("api")]
public class RackUplinkTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record UplinkDto(
        int Port, Guid? ToItemId, int? ToPort, string? ToText, string? ToLabel, Guid? ToRackId, string? ToRackName,
        string? ToLocationName);
    private record DownlinkDto(int Port, Guid FromItemId, string FromLabel, Guid FromRackId, string FromRackName);
    private record ItemDto(Guid Id, string Kind, string Label, UplinkDto? Uplink, DownlinkDto[] Downlinks);
    private record LayoutDto(Guid RackId, int LayoutVersion, ItemDto[] Items);
    private record GroupDto(string Name, int From, int To);
    private record SwitchDto(
        Guid Id, string Label, string Model, Guid RackId, string RackName, Guid? LocationId, string? LocationName,
        GroupDto[] PortGroups, int? UplinkPort, DownlinkDto[] Downlinks, Guid? UplinkToItemId);
    private record ProblemDto(Dictionary<string, string[]> Errors);
    private record RevisionDto(Guid Id, int Number, bool Current);

    /// A house with a "Comms" rack in the plant room holding a 48-port switch
    /// and a patch panel, and a "Garage" rack holding a 16-port switch.
    private async Task<House> NewHouse()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Uplinks {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var plantRoom = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/locations", new { name = "Plant room" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var comms = await (await client.PostAsJsonAsync($"/projects/{project.Id}/racks",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, locationId = plantRoom!.Id })).Content.ReadFromJsonAsync<IdDto>();
        var garage = await (await client.PostAsJsonAsync($"/projects/{project.Id}/racks",
            new { name = "Garage", rackTypeId = TestSeed.Cabinet4UId })).Content.ReadFromJsonAsync<IdDto>();

        await client.PostAsJsonAsync($"/racks/{comms!.Id}/items", new { rackDeviceTypeId = TestSeed.Switch48Id });
        var commsLayout = await (await client.PostAsJsonAsync($"/racks/{comms.Id}/items", new { rackDeviceTypeId = TestSeed.PatchPanelId }))
            .Content.ReadFromJsonAsync<LayoutDto>();
        var garageLayout = await (await client.PostAsJsonAsync($"/racks/{garage!.Id}/items", new { rackDeviceTypeId = TestSeed.Switch16Id }))
            .Content.ReadFromJsonAsync<LayoutDto>();

        return new House(
            client, project.Id, comms.Id, garage.Id,
            commsLayout!.Items.Single(i => i.Kind == "Switch").Id,
            commsLayout.Items.Single(i => i.Kind == "PatchPanel").Id,
            garageLayout!.Items.Single().Id);
    }

    private sealed record House(
        HttpClient Client, Guid ProjectId, Guid CommsId, Guid GarageId, Guid CommsSwitch, Guid PatchPanel, Guid GarageSwitch);

    private static Task<HttpResponseMessage> Uplink(HttpClient client, Guid itemId, object body) =>
        client.PutAsJsonAsync($"/rack-items/{itemId}/uplink", body);

    private static async Task<LayoutDto> Layout(HttpClient client, Guid rackId) =>
        (await client.GetFromJsonAsync<LayoutDto>($"/racks/{rackId}/layout"))!;

    private static async Task<ItemDto> Item(HttpClient client, Guid rackId, Guid itemId) =>
        (await Layout(client, rackId)).Items.Single(i => i.Id == itemId);

    private static async Task<string> Problem(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDto>())!;
        Assert.True(problem.Errors.ContainsKey(key), $"Expected a problem under '{key}', got: {string.Join(", ", problem.Errors.Keys)}");
        return problem.Errors[key][0];
    }

    /// A third switch in the garage rack, below the first.
    private static async Task<Guid> AnotherGarageSwitch(House house)
    {
        var layout = await (await house.Client.PostAsJsonAsync($"/racks/{house.GarageId}/items", new { rackDeviceTypeId = TestSeed.Switch48Id }))
            .Content.ReadFromJsonAsync<LayoutDto>();
        return layout!.Items.Single(i => i.Label == "Switch 2").Id;
    }

    [Fact]
    public async Task A_switch_uplinks_into_a_switch_in_another_rack_and_both_ends_say_so()
    {
        var house = await NewHouse();

        var response = await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var garage = (await response.Content.ReadFromJsonAsync<LayoutDto>())!;
        Assert.Equal(house.GarageId, garage.RackId);
        Assert.Equal(
            new UplinkDto(17, house.CommsSwitch, 49, null, "Switch 1", house.CommsId, "Comms", "Plant room"),
            garage.Items.Single().Uplink);

        var down = Assert.Single((await Item(house.Client, house.CommsId, house.CommsSwitch)).Downlinks);
        Assert.Equal(new DownlinkDto(49, house.GarageSwitch, "Switch 1", house.GarageId, "Garage"), down);
    }

    [Fact]
    public async Task An_uplink_to_something_in_words_is_saved_trimmed_and_replaces_the_one_before()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var response = await Uplink(house.Client, house.GarageSwitch, new { port = 18, toText = "  UDM Pro  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new UplinkDto(18, null, null, "UDM Pro", null, null, null, null),
            (await Item(house.Client, house.GarageId, house.GarageSwitch)).Uplink);
        Assert.Empty((await Item(house.Client, house.CommsId, house.CommsSwitch)).Downlinks);
    }

    [Fact]
    public async Task Saving_an_uplink_moves_nothing_so_the_layout_version_stays()
    {
        var house = await NewHouse();
        var before = (await Layout(house.Client, house.GarageId)).LayoutVersion;

        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toText = "ISP fibre" });

        Assert.Equal(before, (await Layout(house.Client, house.GarageId)).LayoutVersion);
    }

    [Fact]
    public async Task Saving_the_same_uplink_again_is_not_refused_as_taken()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var again = await Uplink(house.Client, house.GarageSwitch, new { port = 18, toItemId = house.CommsSwitch, toPort = 49 });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(18, (await Item(house.Client, house.GarageId, house.GarageSwitch)).Uplink!.Port);
    }

    [Fact]
    public async Task Only_a_switch_has_an_uplink()
    {
        var house = await NewHouse();

        var message = await Problem(await Uplink(house.Client, house.PatchPanel, new { port = 1, toText = "UDM Pro" }), "item");

        Assert.Equal("Patch panel 1 is not a switch. Only a switch has an uplink.", message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    public async Task The_uplink_port_must_be_one_the_switch_has(int port)
    {
        var house = await NewHouse();

        var message = await Problem(await Uplink(house.Client, house.GarageSwitch, new { port, toText = "UDM Pro" }), "port");

        Assert.Equal($"Switch 1 has no port {port}; its ports are 1–18.", message);
    }

    [Fact]
    public async Task An_uplink_goes_somewhere_and_only_one_somewhere()
    {
        var house = await NewHouse();

        var nowhere = await Problem(await Uplink(house.Client, house.GarageSwitch, new { port = 17, toText = "   " }), "toItemId");
        var both = await Problem(await Uplink(house.Client, house.GarageSwitch,
            new { port = 17, toItemId = house.CommsSwitch, toPort = 49, toText = "UDM Pro" }), "toText");
        var portless = await Problem(await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch }), "toPort");
        var strayPort = await Problem(await Uplink(house.Client, house.GarageSwitch, new { port = 17, toPort = 3, toText = "UDM Pro" }), "toPort");

        Assert.Equal("Say where the uplink goes: another switch's port, or in words.", nowhere);
        Assert.Equal("Choose another switch or say where it goes in words, not both.", both);
        Assert.Equal("Choose the port on Switch 1 in Comms it plugs into.", portless);
        Assert.Equal("A port is only chosen on another switch.", strayPort);
    }

    [Fact]
    public async Task Words_are_kept_to_the_limit()
    {
        var house = await NewHouse();

        var message = await Problem(await Uplink(house.Client, house.GarageSwitch, new { port = 17, toText = new string('x', 81) }), "toText");

        Assert.Equal("Keep it to 80 characters; this is 81.", message);
        Assert.Equal(HttpStatusCode.OK,
            (await Uplink(house.Client, house.GarageSwitch, new { port = 17, toText = new string('x', 80) })).StatusCode);
    }

    [Fact]
    public async Task The_destination_must_be_another_switch_in_this_house()
    {
        var house = await NewHouse();
        var elsewhere = await NewHouse();

        var itself = await Problem(await Uplink(house.Client, house.GarageSwitch,
            new { port = 17, toItemId = house.GarageSwitch, toPort = 1 }), "toItemId");
        var panel = await Problem(await Uplink(house.Client, house.GarageSwitch,
            new { port = 17, toItemId = house.PatchPanel, toPort = 1 }), "toItemId");
        var otherHouse = await Problem(await Uplink(house.Client, house.GarageSwitch,
            new { port = 17, toItemId = elsewhere.CommsSwitch, toPort = 49 }), "toItemId");
        var unknown = await Problem(await Uplink(house.Client, house.GarageSwitch,
            new { port = 17, toItemId = Guid.NewGuid(), toPort = 49 }), "toItemId");

        Assert.Equal("A switch cannot uplink into itself.", itself);
        Assert.Equal("Patch panel 1 in Comms is not a switch. An uplink goes into another switch, or say where it goes in words.", panel);
        Assert.Equal("Choose a switch in this house.", otherHouse);
        Assert.Equal("Choose a switch in this house.", unknown);
    }

    [Fact]
    public async Task The_destination_port_must_be_one_the_other_switch_has()
    {
        var house = await NewHouse();

        var message = await Problem(await Uplink(house.Client, house.GarageSwitch,
            new { port = 17, toItemId = house.CommsSwitch, toPort = 53 }), "toPort");

        Assert.Equal("Switch 1 in Comms has no port 53; its ports are 1–52.", message);
    }

    [Fact]
    public async Task A_port_takes_one_uplink_and_never_its_own_switchs_uplink()
    {
        var house = await NewHouse();
        var second = await AnotherGarageSwitch(house);
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });
        await Uplink(house.Client, house.CommsSwitch, new { port = 52, toText = "UDM Pro" });

        var taken = await Problem(await Uplink(house.Client, second, new { port = 49, toItemId = house.CommsSwitch, toPort = 49 }), "toPort");
        var ownUplink = await Problem(await Uplink(house.Client, second, new { port = 49, toItemId = house.CommsSwitch, toPort = 52 }), "toPort");

        Assert.Equal("Port 49 on Switch 1 in Comms already takes the uplink from Switch 1.", taken);
        Assert.Equal("Port 52 is the uplink of Switch 1 in Comms.", ownUplink);
    }

    [Fact]
    public async Task A_switch_cannot_leave_by_a_port_that_takes_another_switchs_uplink()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var message = await Problem(await Uplink(house.Client, house.CommsSwitch, new { port = 49, toText = "UDM Pro" }), "port");

        Assert.Equal("Port 49 takes the uplink from Switch 1 in Garage. Choose another.", message);
    }

    [Fact]
    public async Task Two_switches_cannot_uplink_into_each_other()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var message = await Problem(await Uplink(house.Client, house.CommsSwitch,
            new { port = 52, toItemId = house.GarageSwitch, toPort = 1 }), "toItemId");

        Assert.Equal("Switch 1 in Garage already uplinks into Switch 1; that would make a loop.", message);
    }

    [Fact]
    public async Task Three_switches_cannot_uplink_round_in_a_ring()
    {
        var house = await NewHouse();
        var second = await AnotherGarageSwitch(house);
        await Uplink(house.Client, house.CommsSwitch, new { port = 52, toItemId = house.GarageSwitch, toPort = 1 });
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = second, toPort = 1 });

        var message = await Problem(await Uplink(house.Client, second,
            new { port = 49, toItemId = house.CommsSwitch, toPort = 49 }), "toItemId");

        Assert.Equal("Switch 1 in Comms already uplinks (through Switch 1) into Switch 2; that would make a loop.", message);
    }

    [Fact]
    public async Task A_chain_of_uplinks_is_not_a_loop()
    {
        var house = await NewHouse();
        var second = await AnotherGarageSwitch(house);
        await Uplink(house.Client, second, new { port = 49, toItemId = house.GarageSwitch, toPort = 1 });

        var response = await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// A switch removed without the endpoint (the database's set-null) leaves
    /// its uplinker's port pointing at nothing. That port holds nothing, to the
    /// check and to the picker alike.
    [Fact]
    public async Task An_uplink_left_pointing_at_nothing_holds_no_port_for_the_check_or_the_picker()
    {
        var house = await NewHouse();
        var second = await AnotherGarageSwitch(house);
        await Uplink(house.Client, house.CommsSwitch, new { port = 52, toItemId = second, toPort = 1 });
        await using (var db = factory.NewDbContext())
        {
            await db.RackItems.Where(i => i.Id == second).ExecuteDeleteAsync();
        }

        var switches = (await house.Client.GetFromJsonAsync<SwitchDto[]>($"/projects/{house.ProjectId}/switches"))!;
        var response = await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 52 });

        Assert.Null(switches.Single(s => s.Id == house.CommsSwitch).UplinkPort);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_item_is_not_found()
    {
        var house = await NewHouse();

        Assert.Equal(HttpStatusCode.NotFound, (await Uplink(house.Client, Guid.NewGuid(), new { port = 1, toText = "UDM Pro" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await house.Client.DeleteAsync($"/rack-items/{Guid.NewGuid()}/uplink")).StatusCode);
    }

    [Fact]
    public async Task Removing_an_uplink_clears_both_ends_and_removing_it_twice_is_no_error()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var first = await house.Client.DeleteAsync($"/rack-items/{house.GarageSwitch}/uplink");
        var second = await house.Client.DeleteAsync($"/rack-items/{house.GarageSwitch}/uplink");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Null((await first.Content.ReadFromJsonAsync<LayoutDto>())!.Items.Single().Uplink);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Empty((await Item(house.Client, house.CommsId, house.CommsSwitch)).Downlinks);
    }

    [Fact]
    public async Task Removing_a_switch_clears_the_uplinks_into_it_from_any_rack()
    {
        var house = await NewHouse();
        var second = await AnotherGarageSwitch(house);
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });
        await Uplink(house.Client, second, new { port = 49, toItemId = house.CommsSwitch, toPort = 50 });
        var comms = await Layout(house.Client, house.CommsId);

        var response = await house.Client.DeleteAsync($"/rack-items/{house.CommsSwitch}?basedOnLayoutVersion={comms.LayoutVersion}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var garage = await Layout(house.Client, house.GarageId);
        Assert.All(garage.Items, i => Assert.Null(i.Uplink));
        await using var db = factory.NewDbContext();
        Assert.All(db.RackItems.Where(i => i.RackId == house.GarageId).ToList(), i =>
        {
            Assert.Null(i.UplinkPort);
            Assert.Null(i.UplinkToPort);
        });
    }

    [Fact]
    public async Task Removing_a_switch_with_an_uplink_takes_its_downlink_off_the_other_end()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });
        var garage = await Layout(house.Client, house.GarageId);

        await house.Client.DeleteAsync($"/rack-items/{house.GarageSwitch}?basedOnLayoutVersion={garage.LayoutVersion}");

        Assert.Empty((await Item(house.Client, house.CommsId, house.CommsSwitch)).Downlinks);
        Assert.Equal(HttpStatusCode.OK,
            (await Uplink(house.Client, house.CommsSwitch, new { port = 52, toText = "UDM Pro" })).StatusCode);
    }

    /// Several switches racing for one port: the index lets one in, and every
    /// other is told who has it, as a later save would be. None may be a 500.
    [Fact]
    public async Task Switches_racing_for_one_port_get_one_uplink_in_and_the_rest_a_clear_refusal()
    {
        var house = await NewHouse();
        var racers = new List<Guid> { house.GarageSwitch, await AnotherGarageSwitch(house) };
        var layout = await (await house.Client.PostAsJsonAsync($"/racks/{house.GarageId}/items", new { rackDeviceTypeId = TestSeed.Switch48Id }))
            .Content.ReadFromJsonAsync<LayoutDto>();
        racers.Add(layout!.Items.Single(i => i.Label == "Switch 3").Id);

        var responses = await Task.WhenAll(racers.Select(id =>
            Uplink(house.Client, id, new { port = id == house.GarageSwitch ? 17 : 49, toItemId = house.CommsSwitch, toPort = 50 })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Contains(r.StatusCode,
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        Assert.Single((await Item(house.Client, house.CommsId, house.CommsSwitch)).Downlinks);
    }

    /// Rules that span two switches — a port that takes another's uplink
    /// cannot be one's own, and no loops — have no index to hold them, so the
    /// saves of one house take turns. Two saves that each pass alone but break
    /// a rule together are sent at once, over several houses: never may both
    /// land. A race that happens not to interleave passes too, so this can
    /// only catch the lock missing, not prove it present.
    [Fact]
    public async Task Saves_that_together_would_break_a_rule_never_both_land()
    {
        for (var round = 0; round < 8; round++)
        {
            var house = await NewHouse();

            var loop = await Task.WhenAll(
                Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 }),
                Uplink(house.Client, house.CommsSwitch, new { port = 52, toItemId = house.GarageSwitch, toPort = 1 }));
            Assert.Single(loop, r => r.StatusCode == HttpStatusCode.OK);

            var other = await NewHouse();
            var ownPort = await Task.WhenAll(
                Uplink(other.Client, other.GarageSwitch, new { port = 17, toItemId = other.CommsSwitch, toPort = 49 }),
                Uplink(other.Client, other.CommsSwitch, new { port = 49, toText = "UDM Pro" }));
            Assert.Single(ownPort, r => r.StatusCode == HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_house_lists_every_switch_with_its_rack_place_ports_and_links()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var switches = (await house.Client.GetFromJsonAsync<SwitchDto[]>($"/projects/{house.ProjectId}/switches"))!;

        Assert.Equal(new[] { house.CommsSwitch, house.GarageSwitch }, switches.Select(s => s.Id));
        var comms = switches[0];
        Assert.Equal(("Switch 1", "Ubiquiti Test 48 PoE", "Comms", "Plant room"), (comms.Label, comms.Model, comms.RackName, comms.LocationName));
        Assert.Equal(new[] { "1G", "2.5G", "SFP+" }, comms.PortGroups.Select(g => g.Name));
        Assert.Null(comms.UplinkPort);
        Assert.Equal(49, Assert.Single(comms.Downlinks).Port);
        Assert.Equal((17, (string?)null), (switches[1].UplinkPort, switches[1].LocationName));
        Assert.Equal(house.CommsSwitch, switches[1].UplinkToItemId);
    }

    [Fact]
    public async Task An_unknown_house_has_no_switches_to_list()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/projects/{Guid.NewGuid()}/switches")).StatusCode);
    }
    private static async Task<RevisionDto> Issue(HttpClient client, Guid rackId) =>
        (await (await client.PostAsJsonAsync($"/racks/{rackId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>())!;

    private static async Task<bool> IsCurrent(HttpClient client, Guid rackId, Guid revisionId) =>
        (await client.GetFromJsonAsync<RevisionDto[]>($"/racks/{rackId}/revisions"))!.Single(r => r.Id == revisionId).Current;

    private static async Task<string> PdfText(HttpClient client, Guid revisionId)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(await client.GetByteArrayAsync($"/rack-revisions/{revisionId}/export/pdf"));
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public async Task Each_racks_pdf_prints_its_end_of_the_link_and_names_the_other_rack()
    {
        var house = await NewHouse();
        await house.Client.PutAsJsonAsync($"/rack-items/{house.GarageSwitch}/ports/17", new { label = "To comms", poe = false });
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });

        var garage = await PdfText(house.Client, (await Issue(house.Client, house.GarageId)).Id);
        var comms = await PdfText(house.Client, (await Issue(house.Client, house.CommsId)).Id);

        Assert.Contains("To comms", garage);
        Assert.Contains("Uplink to Switch 1  ·  Comms  ·  Plant room  ·  port 49", garage);
        Assert.Contains("Downlink from Switch 1  ·  Garage", comms);
    }

    [Fact]
    public async Task Within_one_rack_the_pdf_names_the_switch_alone_and_words_print_as_written()
    {
        var house = await NewHouse();
        var second = await AnotherGarageSwitch(house);
        await Uplink(house.Client, second, new { port = 49, toItemId = house.GarageSwitch, toPort = 18 });
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toText = "UDM Pro" });

        var garage = await PdfText(house.Client, (await Issue(house.Client, house.GarageId)).Id);

        Assert.Contains("Uplink to Switch 1  ·  port 18", garage);
        Assert.Contains("Downlink from Switch 2", garage);
        Assert.DoesNotContain("Downlink from Switch 2  ·", garage);
        Assert.Contains("Uplink to UDM Pro", garage);
    }

    /// The downlink is printed on this rack's schedule, so a switch in another
    /// rack uplinking into this one dates this rack's PDF too.
    [Fact]
    public async Task An_uplink_from_another_rack_dates_this_racks_pdf_and_removing_it_makes_it_current_again()
    {
        var house = await NewHouse();
        var revision = await Issue(house.Client, house.CommsId);

        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });
        Assert.False(await IsCurrent(house.Client, house.CommsId, revision.Id));

        await house.Client.DeleteAsync($"/rack-items/{house.GarageSwitch}/uplink");
        Assert.True(await IsCurrent(house.Client, house.CommsId, revision.Id));
    }

    [Fact]
    public async Task Renaming_the_rack_an_uplink_goes_to_dates_the_pdf_that_names_it()
    {
        var house = await NewHouse();
        await Uplink(house.Client, house.GarageSwitch, new { port = 17, toItemId = house.CommsSwitch, toPort = 49 });
        var revision = await Issue(house.Client, house.GarageId);

        await house.Client.PatchAsJsonAsync($"/racks/{house.CommsId}", new { name = "Comms cupboard", rackTypeId = TestSeed.Cabinet4UId });

        Assert.False(await IsCurrent(house.Client, house.GarageId, revision.Id));
    }

    [Fact]
    public async Task A_rack_revision_issued_before_uplinks_were_recorded_is_still_current()
    {
        var house = await NewHouse();
        var revision = await Issue(house.Client, house.CommsId);

        await using (var db = factory.NewDbContext())
        {
            var stored = await db.RackRevisions.SingleAsync(r => r.Id == revision.Id);
            Assert.Contains("\"uplink\":null,\"downlinks\":null", stored.SnapshotJson);
            stored.SnapshotJson = stored.SnapshotJson.Replace(",\"uplink\":null,\"downlinks\":null", "");
            Assert.DoesNotContain("uplink", stored.SnapshotJson);
            await db.SaveChangesAsync();
        }

        Assert.True(await IsCurrent(house.Client, house.CommsId, revision.Id));
    }
}
