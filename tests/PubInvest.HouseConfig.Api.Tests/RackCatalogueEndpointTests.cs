using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackCatalogueEndpointTests(HouseConfigApiFactory factory)
{
    private record PortGroupDto(string Name, int From, int To, bool PoeTick);
    private record RackTypeDto(Guid Id, string Model, int HeightU, string Description);
    private record RackDeviceTypeDto(
        Guid Id, string Model, string Kind, int HeightU, PortGroupDto[] PortGroups, int PortCount,
        int? PoeBudgetW, Guid? RequiredAccessoryId, bool Active);

    private async Task<HttpClient> Seeded()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }
        return factory.CreateClient();
    }

    [Fact]
    public async Task Lists_rack_cabinets_by_height()
    {
        var client = await Seeded();

        var cabinets = await client.GetFromJsonAsync<RackTypeDto[]>("/catalogue/rack-types");

        var four = cabinets!.Single(c => c.Id == TestSeed.Cabinet4UId);
        Assert.Equal(4, four.HeightU);
        Assert.Equal("Test 4U cabinet", four.Description);
        Assert.Equal(cabinets!.OrderBy(c => c.HeightU).Select(c => c.Id), cabinets!.Select(c => c.Id));
    }

    [Fact]
    public async Task Lists_rack_items_with_their_port_groups_and_required_accessory()
    {
        var client = await Seeded();

        var items = await client.GetFromJsonAsync<RackDeviceTypeDto[]>("/catalogue/rack-device-types");

        var sw = items!.Single(i => i.Id == TestSeed.Switch48Id);
        Assert.Equal("Switch", sw.Kind);
        Assert.Equal(52, sw.PortCount);
        Assert.Equal(new[] { "1G", "2.5G", "SFP+" }, sw.PortGroups.Select(g => g.Name));
        Assert.False(sw.PortGroups[2].PoeTick);
        Assert.Equal(TestSeed.RackKitId, items!.Single(i => i.Id == TestSeed.Switch16Id).RequiredAccessoryId);
        Assert.Equal("Accessory", items!.Single(i => i.Id == TestSeed.RackKitId).Kind);
    }
}
