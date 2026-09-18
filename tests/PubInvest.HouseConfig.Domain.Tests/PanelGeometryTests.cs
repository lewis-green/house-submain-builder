using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PanelGeometryTests
{
    private static PlacedDevice Device(int row, int slot, int width) =>
        new(Guid.NewGuid(), DeviceCategory.Relay, row, slot, width, "Relay 1", [], TerminalRole.None);

    [Fact]
    public void One_din_module_spans_three_slot_units()
    {
        Assert.Equal(PanelGeometry.SlotMm * DinUnits.PerModule, PanelGeometry.SlotToX(DinUnits.PerModule));
    }

    [Fact]
    public void A_device_box_matches_its_slot_span()
    {
        var box = PanelGeometry.DeviceBox(Device(1, 3, 9));

        Assert.Equal(PanelGeometry.SlotToX(3), box.X);
        Assert.Equal(PanelGeometry.RowToY(1), box.Y);
        Assert.Equal(PanelGeometry.SlotToX(9), box.Width);
        Assert.Equal(PanelGeometry.RowMm, box.Height);
    }

    [Fact]
    public void Rows_are_stacked_with_a_gap_between_rails()
    {
        Assert.Equal(0, PanelGeometry.RowToY(0));
        Assert.Equal(2 * (PanelGeometry.RowMm + PanelGeometry.RowGapMm), PanelGeometry.RowToY(2));
    }

    [Fact]
    public void A_four_row_panel_is_four_rails_and_three_gaps_tall()
    {
        var size = PanelGeometry.PanelSize(new PanelLayout(4, 54, []));

        Assert.Equal(4 * PanelGeometry.RowMm + 3 * PanelGeometry.RowGapMm, size.Height);
        Assert.Equal(PanelGeometry.SlotToX(54), size.Width);
    }

    [Fact]
    public void A_panel_with_no_rows_has_no_height()
    {
        Assert.Equal(0, PanelGeometry.PanelSize(new PanelLayout(0, 0, [])).Height);
    }
}
