using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

/// Gear that feeds nothing: a meter, a LAN switch, a relay someone wants on the
/// rail for later. Asked for by device type and quantity, not by circuit.
public class ExtraFixtureTests
{
    private static Circuit Switched(int sequence) =>
        new(Guid.NewGuid(), CircuitType.Switched, $"Switched {sequence}", null, sequence, null, null);

    private static GenerationResult Generate(params ExtraFixture[] extras) =>
        PanelGenerator.Generate(new GenerationRequest(
            [Switched(1)],
            CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(),
            CatalogueFixture.AllEnclosures(),
            ExtraFixtures: extras));

    [Fact]
    public void A_meter_asked_for_by_hand_is_placed()
    {
        var result = Generate(new ExtraFixture(CatalogueFixture.MeterId, 1));

        var meter = Assert.Single(result.Layout.Devices.Where(d => d.Category == DeviceCategory.EnergyMeter));
        Assert.Equal("Meter 1", meter.Label);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void A_quantity_of_more_than_one_gives_that_many_numbered_devices()
    {
        var result = Generate(new ExtraFixture(CatalogueFixture.MeterId, 3));

        Assert.Equal(
            ["Meter 1", "Meter 2", "Meter 3"],
            result.Layout.Devices.Where(d => d.Category == DeviceCategory.EnergyMeter)
                .OrderBy(d => d.Label, StringComparer.Ordinal).Select(d => d.Label));
    }

    [Fact]
    public void A_fixture_carries_no_channels_to_name()
    {
        // A meter measures and a LAN switch has ports; neither feeds a circuit
        // anyone would put a room against.
        var result = Generate(new ExtraFixture(CatalogueFixture.LanId, 1));

        var lan = Assert.Single(result.Layout.Devices.Where(d => d.Category == DeviceCategory.Network));
        Assert.Empty(lan.Channels);
    }

    [Fact]
    public void A_hand_added_relay_continues_the_numbering_rather_than_repeating_it()
    {
        // Labels are what dragged positions are keyed on, so two devices sharing
        // one would make a drag ambiguous.
        var result = Generate(new ExtraFixture(CatalogueFixture.RelayId, 1));

        var relays = result.Layout.Devices
            .Where(d => d.Category == DeviceCategory.Relay)
            .Select(d => d.Label)
            .ToList();

        Assert.Equal(["Relay 1", "Relay 2"], relays.OrderBy(l => l, StringComparer.Ordinal));
        Assert.Equal(relays.Count, relays.Distinct().Count());
    }

    [Fact]
    public void Meters_and_network_gear_fill_from_the_right_of_their_rail()
    {
        var result = Generate(
            new ExtraFixture(CatalogueFixture.MeterId, 1),
            new ExtraFixture(CatalogueFixture.LanId, 1));

        var meter = result.Layout.Devices.Single(d => d.Category == DeviceCategory.EnergyMeter);
        var lan = result.Layout.Devices.Single(d => d.Category == DeviceCategory.Network);

        Assert.Equal(meter.RowIndex, lan.RowIndex);
        Assert.Equal(result.Layout.SlotsPerRow, meter.EndSlotExclusive);
        Assert.Equal(meter.StartSlot, lan.EndSlotExclusive);
    }

    [Fact]
    public void A_fixture_reaches_the_bill_of_materials()
    {
        var result = Generate(new ExtraFixture(CatalogueFixture.MeterId, 2));

        var line = Assert.Single(result.Bom.Lines.Where(l => l.CatalogueId == CatalogueFixture.MeterId));
        Assert.Equal(2, line.Quantity);
        Assert.True(line.PanelMounted);
    }

    [Fact]
    public void A_quantity_of_zero_asks_for_nothing()
    {
        var result = Generate(new ExtraFixture(CatalogueFixture.MeterId, 0));

        Assert.DoesNotContain(result.Layout.Devices, d => d.Category == DeviceCategory.EnergyMeter);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void A_fixture_whose_catalogue_entry_is_gone_is_an_error_naming_it()
    {
        var missing = new Guid("11111111-0000-0000-0000-0000000000ff");

        var result = Generate(new ExtraFixture(missing, 1));

        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Code == DiagnosticCodes.NoPreferredDevice
            && d.Message.Contains(missing.ToString()));
    }

    [Fact]
    public void Asking_for_nothing_changes_nothing()
    {
        var plain = PanelGenerator.Generate(new GenerationRequest(
            [Switched(1)], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(), CatalogueFixture.AllEnclosures()));

        var withEmpty = Generate();

        Assert.Equal(
            plain.Layout.Devices.Select(d => (d.Label, d.RowIndex, d.StartSlot)),
            withEmpty.Layout.Devices.Select(d => (d.Label, d.RowIndex, d.StartSlot)));
    }
}
