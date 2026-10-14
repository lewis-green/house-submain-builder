using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Domain.Tests;

public class RackBomTests
{
    private static readonly RackType Cabinet = new(Guid.NewGuid(), "Generic", "12U wall cabinet", "CAB-12", 12, "Wall");
    private static readonly RackDeviceType Kit = new(Guid.NewGuid(), "Ubiquiti", "Rack kit", "KIT", RackDeviceKind.Accessory, 0, [], null, null, true);
    private static readonly RackDeviceType Small = new(Guid.NewGuid(), "Ubiquiti", "16 PoE", "SW16", RackDeviceKind.Switch, 1,
        [new PortGroup("1G", 1, 16, true)], 180, Kit.Id, true);
    private static readonly RackDeviceType Panel = new(Guid.NewGuid(), "Generic", "Patch panel", "PP24", RackDeviceKind.PatchPanel, 1,
        [new PortGroup("RJ45", 1, 24, true)], null, null, true);

    private static Dictionary<Guid, RackDeviceType> Catalogue => new[] { Kit, Small, Panel }.ToDictionary(t => t.Id);

    [Fact]
    public void Lists_the_cabinet_once_and_each_item_by_count()
    {
        var bom = RackBom.Build(Cabinet, [Panel, Panel, Small], Catalogue);

        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "CAB-12").Quantity);
        Assert.Equal(2, bom.Lines.Single(l => l.PartNumber == "PP24").Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "SW16").Quantity);
    }

    [Fact]
    public void Adds_one_required_accessory_per_item_that_needs_it()
    {
        var bom = RackBom.Build(Cabinet, [Small, Small, Panel], Catalogue);

        Assert.Equal(2, bom.Lines.Single(l => l.PartNumber == "KIT").Quantity);
    }

    [Fact]
    public void Orders_lines_by_part_number_and_carries_no_prices()
    {
        var bom = RackBom.Build(Cabinet, [Small, Panel], Catalogue);

        Assert.Equal(new[] { "CAB-12", "KIT", "PP24", "SW16" }, bom.Lines.Select(l => l.PartNumber));
        Assert.All(bom.Lines, l => Assert.True(l.PanelMounted));
    }
}
