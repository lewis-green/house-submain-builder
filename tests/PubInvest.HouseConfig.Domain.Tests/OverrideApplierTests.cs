using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class OverrideApplierTests
{
    private static PlacedDevice Device(string label, int row, int slot, int width = 3) =>
        new(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, row, slot, width, label, [], TerminalRole.None);

    [Fact]
    public void An_override_moves_the_device_it_names()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0), Device("Dimmer 2", 1, 3)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 2", 1, 30)]);

        var dimmer2 = moved.Devices.Single(d => d.Label == "Dimmer 2");
        Assert.Equal(1, dimmer2.RowIndex);
        Assert.Equal(30, dimmer2.StartSlot);
        Assert.Equal(0, moved.Devices.Single(d => d.Label == "Dimmer 1").StartSlot);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_override_naming_a_device_that_no_longer_exists_is_reported()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (_, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 9", 1, 30)]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.PositionOverrideDropped, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Dimmer 9", diagnostic.Message);
    }

    [Fact]
    public void An_override_onto_an_occupied_slot_is_dropped_and_reported()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0), Device("Dimmer 2", 1, 3)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 2", 1, 0)]);

        Assert.Equal(3, moved.Devices.Single(d => d.Label == "Dimmer 2").StartSlot);
        Assert.Equal(DiagnosticCodes.PositionOverrideDropped, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void An_override_running_past_the_end_of_a_row_is_dropped()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 1", 1, 52)]);

        Assert.Equal(0, moved.Devices.Single(d => d.Label == "Dimmer 1").StartSlot);
        Assert.Contains("does not fit", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void An_override_onto_a_row_that_does_not_exist_is_dropped()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 1", 9, 0)]);

        Assert.Equal(1, moved.Devices.Single(d => d.Label == "Dimmer 1").RowIndex);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void A_device_can_move_into_the_slot_another_override_vacates()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0), Device("Dimmer 2", 1, 3)]);

        // Dimmer 1 leaves slot 0, so Dimmer 2 may take it.
        var (moved, diagnostics) = OverrideApplier.Apply(layout,
        [
            new PositionOverride("Dimmer 1", 1, 30),
            new PositionOverride("Dimmer 2", 1, 0)
        ]);

        Assert.Equal(30, moved.Devices.Single(d => d.Label == "Dimmer 1").StartSlot);
        Assert.Equal(0, moved.Devices.Single(d => d.Label == "Dimmer 2").StartSlot);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void No_overrides_returns_the_layout_untouched()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, []);

        Assert.Same(layout, moved);
        Assert.Empty(diagnostics);
    }
}
