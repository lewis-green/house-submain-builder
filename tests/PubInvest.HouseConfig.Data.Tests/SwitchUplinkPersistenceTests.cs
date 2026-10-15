using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Data.Tests;

/// What the database itself holds a switch's uplink to, under whatever the
/// endpoints check first.
[Collection("postgres")]
public class SwitchUplinkPersistenceTests(PostgresFixture fixture)
{
    /// A house with two racks, a switch in each and a third in the first.
    private async Task<(Guid HouseId, RackItem First, RackItem Second, RackItem Core)> House()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cabinet = new RackTypeRow { Id = Guid.NewGuid(), Manufacturer = "Test", Model = "Cabinet", PartNumber = $"{tag}-CAB", HeightU = 4 };
        var type = new RackDeviceTypeRow
        {
            Id = Guid.NewGuid(), Manufacturer = "Test", Model = "Switch", PartNumber = $"{tag}-SW", Kind = "Switch", HeightU = 1,
            PortGroupsJson = """[{"name":"SFP+","from":1,"to":8,"poeTick":false}]""", Active = true,
        };
        var house = new Project { Id = Guid.NewGuid(), Name = $"Uplinks {tag}", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test" };
        var comms = new Rack { Id = Guid.NewGuid(), ProjectId = house.Id, Name = "Comms", RackTypeId = cabinet.Id, CreatedAt = DateTimeOffset.UtcNow };
        var garage = new Rack { Id = Guid.NewGuid(), ProjectId = house.Id, Name = "Garage", RackTypeId = cabinet.Id, CreatedAt = DateTimeOffset.UtcNow };

        RackItem Switch(Rack rack, int topU, string label) => new()
        {
            Id = Guid.NewGuid(), RackId = rack.Id, RackDeviceTypeId = type.Id, TopU = topU, Sequence = topU, Label = label,
        };

        var first = Switch(comms, 4, "Switch 1");
        var core = Switch(comms, 3, "Switch 2");
        var second = Switch(garage, 4, "Switch 1");

        await using var db = fixture.CreateContext();
        db.RackTypes.Add(cabinet);
        db.RackDeviceTypes.Add(type);
        db.Projects.Add(house);
        db.Racks.AddRange(comms, garage);
        db.RackItems.AddRange(first, core, second);
        await db.SaveChangesAsync();
        return (house.Id, first, second, core);
    }

    [Fact]
    public async Task Two_switches_cannot_uplink_into_the_same_port()
    {
        var (_, first, second, core) = await House();

        await using var db = fixture.CreateContext();
        await db.RackItems.Where(i => i.Id == first.Id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.UplinkPort, 8).SetProperty(i => i.UplinkToItemId, core.Id).SetProperty(i => i.UplinkToPort, 3));

        await Assert.ThrowsAnyAsync<Exception>(() => db.RackItems.Where(i => i.Id == second.Id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.UplinkPort, 8).SetProperty(i => i.UplinkToItemId, core.Id).SetProperty(i => i.UplinkToPort, 3)));
    }

    [Fact]
    public async Task Any_number_of_switches_can_uplink_to_something_written_in_words()
    {
        var (_, first, second, _) = await House();

        await using var db = fixture.CreateContext();
        foreach (var id in new[] { first.Id, second.Id })
        {
            await db.RackItems.Where(i => i.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UplinkPort, 8).SetProperty(i => i.UplinkToText, "UDM Pro"));
        }

        Assert.Equal(2, await db.RackItems.CountAsync(i => i.UplinkToText == "UDM Pro"));
    }

    /// The endpoint clears the uplinks first; this is what holds if anything
    /// removes a switch without it.
    [Fact]
    public async Task Removing_the_switch_an_uplink_goes_to_leaves_no_link_to_it()
    {
        var (_, first, _, core) = await House();

        await using (var db = fixture.CreateContext())
        {
            await db.RackItems.Where(i => i.Id == first.Id).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UplinkPort, 8).SetProperty(i => i.UplinkToItemId, core.Id).SetProperty(i => i.UplinkToPort, 3));
            await db.RackItems.Where(i => i.Id == core.Id).ExecuteDeleteAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            Assert.Null((await db.RackItems.SingleAsync(i => i.Id == first.Id)).UplinkToItemId);
        }
    }

    [Fact]
    public async Task Removing_a_house_whose_racks_uplink_into_each_other_removes_them_all()
    {
        var (houseId, first, second, core) = await House();

        await using var db = fixture.CreateContext();
        await db.RackItems.Where(i => i.Id == first.Id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.UplinkPort, 8).SetProperty(i => i.UplinkToItemId, second.Id).SetProperty(i => i.UplinkToPort, 1));
        await db.RackItems.Where(i => i.Id == second.Id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.UplinkPort, 8).SetProperty(i => i.UplinkToItemId, core.Id).SetProperty(i => i.UplinkToPort, 1));

        await db.Projects.Where(p => p.Id == houseId).ExecuteDeleteAsync();

        Assert.False(await db.RackItems.AnyAsync(i => i.Id == first.Id || i.Id == second.Id || i.Id == core.Id));
    }
}
