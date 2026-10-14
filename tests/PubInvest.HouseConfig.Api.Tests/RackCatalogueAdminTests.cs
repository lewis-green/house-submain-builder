using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackCatalogueAdminTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record ProblemDto(Dictionary<string, string[]> Errors);
    private record RackDeviceTypeDto(Guid Id, string PartNumber, bool Active);

    private static readonly PortGroup[] Ports = [new("1G", 1, 8, true), new("SFP+", 9, 10, false)];

    private async Task<(HttpClient Client, Guid SwitchId, Guid CabinetId)> OwnRows()
    {
        await using var db = factory.NewDbContext();
        await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);

        var sw = new RackDeviceTypeRow
        {
            Id = Guid.NewGuid(), Manufacturer = "Ubiquiti", Model = "Admin switch", PartNumber = $"ADM-{Guid.NewGuid():N}",
            Kind = "Switch", HeightU = 1, PortGroupsJson = JsonSerializer.Serialize(Ports, DomainMapper.Json), Active = true,
        };
        var cabinet = new RackTypeRow
        {
            Id = Guid.NewGuid(), Manufacturer = "Generic", Model = "Admin cabinet", PartNumber = $"CAB-{Guid.NewGuid():N}",
            HeightU = 6, Mounting = "Wall",
        };
        db.RackDeviceTypes.Add(sw);
        db.RackTypes.Add(cabinet);
        await db.SaveChangesAsync();
        return (factory.CreateClient(), sw.Id, cabinet.Id);
    }

    private static object SwitchBody(string partNumber, PortGroup[]? ports = null, int heightU = 1, bool active = true) => new
    {
        manufacturer = "Ubiquiti", model = "Admin switch", partNumber, kind = "Switch", heightU,
        portGroups = ports ?? Ports, poeBudgetW = 60, requiredAccessoryId = (Guid?)null, active,
    };

    [Fact]
    public async Task A_confirmed_part_number_is_saved()
    {
        var (client, switchId, _) = await OwnRows();
        var confirmed = $"USW-CONFIRMED-{Guid.NewGuid():N}";

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", SwitchBody(confirmed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(confirmed, (await response.Content.ReadFromJsonAsync<RackDeviceTypeDto>())!.PartNumber);
    }

    [Fact]
    public async Task Port_groups_with_a_gap_are_refused()
    {
        var (client, switchId, _) = await OwnRows();

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}",
            SwitchBody("ADM-GAP", [new PortGroup("1G", 1, 8, true), new PortGroup("SFP+", 10, 11, false)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("portGroups", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
    }

    [Fact]
    public async Task A_body_without_port_groups_is_a_field_error_not_a_crash()
    {
        var (client, switchId, _) = await OwnRows();

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", new
        {
            manufacturer = "Ubiquiti", model = "Admin switch", partNumber = "ADM-NOPORTS", kind = "Switch", heightU = 1,
            poeBudgetW = 60, requiredAccessoryId = (Guid?)null, active = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("portGroups", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
    }

    [Fact]
    public async Task A_placed_items_height_and_ports_cannot_change_but_its_part_number_can()
    {
        var (client, switchId, cabinetId) = await OwnRows();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Admin house" })).Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = cabinetId })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/racks/{rack!.Id}/items", new { rackDeviceTypeId = switchId });

        var taller = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", SwitchBody("ADM-TALL", heightU: 2));
        var renamed = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", SwitchBody($"ADM-OK-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.BadRequest, taller.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    [Fact]
    public async Task A_cabinet_cannot_be_made_shorter_than_an_item_placed_in_it()
    {
        var (client, switchId, cabinetId) = await OwnRows();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Admin house 2" })).Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = cabinetId })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/racks/{rack!.Id}/items", new { rackDeviceTypeId = switchId });

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-types/{cabinetId}", new
        {
            manufacturer = "Generic", model = "Admin cabinet", partNumber = "CAB-SHORT", heightU = 4, mounting = "Wall",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("U6", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["heightU"][0]);
    }

    [Fact]
    public async Task A_required_accessory_must_be_an_accessory()
    {
        var (client, switchId, _) = await OwnRows();

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", new
        {
            manufacturer = "Ubiquiti", model = "Admin switch", partNumber = "ADM-ACC", kind = "Switch", heightU = 1,
            portGroups = Ports, poeBudgetW = 60, requiredAccessoryId = TestSeed.PatchPanelId, active = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("requiredAccessoryId", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
    }
}
