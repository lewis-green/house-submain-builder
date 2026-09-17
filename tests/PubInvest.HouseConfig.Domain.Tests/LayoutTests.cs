using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class LayoutTests
{
    private static PlacedDevice Device(int row, int startSlot, int width) =>
        new(Guid.NewGuid(), DeviceCategory.Relay, row, startSlot, width, "Relay 1", [], TerminalRole.None);

    [Fact]
    public void DevicesInRow_returns_only_that_row_ordered_by_slot()
    {
        var layout = new PanelLayout(2, 12, [Device(0, 4, 2), Device(1, 0, 4), Device(0, 0, 4)]);

        Assert.Equal([0, 4], layout.DevicesInRow(0).Select(d => d.StartSlot));
    }

    [Fact]
    public void SlotsUsed_sums_module_widths_across_all_rows()
    {
        var layout = new PanelLayout(2, 12, [Device(0, 0, 4), Device(0, 4, 2), Device(1, 0, 4)]);

        Assert.Equal(10, layout.SlotsUsed);
    }

    [Fact]
    public void BillOfMaterials_total_is_the_sum_of_line_totals()
    {
        var bom = new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "SPDM-002PE", "Shelly Pro Dimmer 2PM", 3, 60.00m),
            new BomLine(Guid.NewGuid(), "2003-7646", "WAGO TOPJOB S", 24, 1.50m)
        ]);

        Assert.Equal(216.00m, bom.Total);
    }
}
