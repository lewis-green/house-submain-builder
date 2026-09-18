using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class BomBuilderTests
{
    private static PlacedDevice Placed(Guid deviceTypeId, DeviceCategory category, int row, int slot, int width, string label) =>
        new(deviceTypeId, category, row, slot, width, label, [], TerminalRole.None);

    [Fact]
    public void Identical_devices_are_aggregated_into_one_line()
    {
        var layout = new PanelLayout(6, 24, [
            Placed(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 0, 0, 2, "Dimmer 1"),
            Placed(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 0, 2, 2, "Dimmer 2"),
            Placed(CatalogueFixture.RelayId, DeviceCategory.Relay, 1, 0, 4, "Relay 1")
        ]);

        var bom = BomBuilder.Build(layout, [], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        Assert.Equal(2, bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.DimmerId).Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.RelayId).Quantity);
    }

    [Fact]
    public void The_enclosure_appears_exactly_once()
    {
        var layout = new PanelLayout(6, 24, []);

        var bom = BomBuilder.Build(layout, [], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        var line = Assert.Single(bom.Lines);
        Assert.Equal(CatalogueFixture.LargeBoxId, line.CatalogueId);
        Assert.Equal(1, line.Quantity);
    }

    [Fact]
    public void Accessories_are_included_even_though_they_occupy_no_slots()
    {
        var layout = new PanelLayout(6, 24, []);
        var accessories = new[] { new AccessoryLine(CatalogueFixture.BridgeId, 2) };

        var bom = BomBuilder.Build(layout, accessories, CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        Assert.Equal(2, bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.BridgeId).Quantity);
    }

    [Fact]
    public void Lines_are_ordered_by_part_number()
    {
        var layout = new PanelLayout(6, 24, [
            Placed(CatalogueFixture.RelayId, DeviceCategory.Relay, 0, 0, 4, "Relay 1"),
            Placed(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 0, 4, 2, "Dimmer 1")
        ]);

        var bom = BomBuilder.Build(layout, [], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        Assert.Equal(
            bom.Lines.Select(l => l.PartNumber).OrderBy(p => p, StringComparer.Ordinal),
            bom.Lines.Select(l => l.PartNumber));
    }
}
