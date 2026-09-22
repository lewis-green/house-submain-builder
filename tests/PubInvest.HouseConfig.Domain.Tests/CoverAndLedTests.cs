using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

/// Blinds and colour tape are circuits like any other: they have a room and a
/// name, and the device that drives them is chosen from the ruleset.
public class CoverAndLedTests
{
    private static Circuit Circuit(CircuitType type, int sequence, decimal? w = null, decimal? m = null) =>
        new(Guid.NewGuid(), type, $"{type} {sequence}", null, sequence, w, m);

    private static DeviceDemand Demand(params Circuit[] circuits) =>
        DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

    [Fact]
    public void A_dual_cover_controller_carries_two_blinds()
    {
        // Its channel count is the number of covers it drives, which is what the
        // 'Dual' in Shelly's name means and what the catalogue records.
        var demand = Demand(
            Circuit(CircuitType.Cover, 1),
            Circuit(CircuitType.Cover, 2));

        Assert.Single(demand.Devices);
        Assert.Equal(DeviceCategory.Cover, demand.Devices[0].Category);
        Assert.Equal(2, demand.Devices[0].Channels.Count(c => !c.IsSpare));
    }

    [Fact]
    public void A_third_blind_needs_a_second_controller()
    {
        var demand = Demand(
            Circuit(CircuitType.Cover, 1),
            Circuit(CircuitType.Cover, 2),
            Circuit(CircuitType.Cover, 3));

        Assert.Equal(2, demand.Devices.Count);
        Assert.Equal(["Cover 1", "Cover 2"], demand.Devices.Select(d => d.Label));
    }

    [Fact]
    public void One_colour_controller_serves_one_run_however_many_outputs_it_has()
    {
        // Five channels are red, green, blue and the two whites of a single run,
        // not five runs.
        var demand = Demand(Circuit(CircuitType.RgbwTape, 1, 14.4m, 5m));

        var controller = Assert.Single(demand.Devices);
        Assert.Equal(DeviceCategory.LedController, controller.Category);
        Assert.Single(controller.Channels);
        Assert.Empty(controller.Channels.Where(c => c.IsSpare));
    }

    [Fact]
    public void Two_colour_runs_need_two_controllers()
    {
        var demand = Demand(
            Circuit(CircuitType.RgbwTape, 1, 14.4m, 5m),
            Circuit(CircuitType.RgbwTape, 2, 14.4m, 5m));

        Assert.Equal(["LED 1", "LED 2"], demand.Devices.Select(d => d.Label));
    }

    [Fact]
    public void Colour_tape_gets_its_own_pair_of_24V_joints()
    {
        var supply = TapeSupplySizer.Size(
            [Circuit(CircuitType.RgbwTape, 1, 14.4m, 5m)],
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue());

        Assert.Contains(supply.Blocks, b => b.Category == DeviceCategory.Dc24VPositive);
        Assert.Contains(supply.Blocks, b => b.Category == DeviceCategory.Dc24VNegative);
    }

    [Fact]
    public void Colour_tape_is_sized_into_the_same_driver_as_plain_tape()
    {
        var supply = TapeSupplySizer.Size(
            [
                Circuit(CircuitType.LedTape, 1, 14.4m, 5m),
                Circuit(CircuitType.RgbwTape, 2, 14.4m, 5m),
            ],
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue());

        // 144W of tape at 0.8 derating needs 180W, so one 240W driver covers both.
        var driver = Assert.Single(supply.ExternalParts);
        Assert.Equal(1, driver.Quantity);
        Assert.Equal(4, supply.Blocks.Count);
    }

    [Fact]
    public void Blinds_and_colour_tape_reach_the_panel()
    {
        var result = PanelGenerator.Generate(new GenerationRequest(
            [
                Circuit(CircuitType.DimmedLighting, 1),
                Circuit(CircuitType.Cover, 2),
                Circuit(CircuitType.RgbwTape, 3, 14.4m, 5m),
            ],
            CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(),
            CatalogueFixture.AllEnclosures()));

        Assert.False(result.HasErrors);
        Assert.Contains(result.Layout.Devices, d => d.Category == DeviceCategory.Cover);
        Assert.Contains(result.Layout.Devices, d => d.Category == DeviceCategory.LedController);
    }

    [Fact]
    public void Covers_and_colour_controllers_share_a_rail_below_the_relays()
    {
        var result = PanelGenerator.Generate(new GenerationRequest(
            [
                Circuit(CircuitType.Switched, 1),
                Circuit(CircuitType.Cover, 2),
                Circuit(CircuitType.RgbwTape, 3, 14.4m, 5m),
            ],
            CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(),
            CatalogueFixture.AllEnclosures()));

        var relay = result.Layout.Devices.Single(d => d.Category == DeviceCategory.Relay);
        var cover = result.Layout.Devices.Single(d => d.Category == DeviceCategory.Cover);
        var led = result.Layout.Devices.Single(d => d.Category == DeviceCategory.LedController);

        Assert.Equal(cover.RowIndex, led.RowIndex);
        Assert.True(cover.RowIndex > relay.RowIndex);
        Assert.Equal(0, cover.StartSlot);
    }

    [Fact]
    public void A_panel_with_no_blinds_does_not_ask_for_a_cover_controller()
    {
        var demand = Demand(Circuit(CircuitType.DimmedLighting, 1));

        Assert.DoesNotContain(demand.Devices, d => d.Category == DeviceCategory.Cover);
        Assert.Empty(demand.Diagnostics);
    }
}
