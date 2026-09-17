using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class OrphanReporterTests
{
    private static readonly Guid CircuitA = new("44444444-0000-0000-0000-000000000001");
    private static readonly Guid CircuitB = new("44444444-0000-0000-0000-000000000002");

    private static PlacedDevice Dimmer(string label, params Guid?[] circuits) =>
        new(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 1, 0, 2, label,
            circuits.Select((c, i) => new ChannelAssignment(i, c, c is null)).ToList(),
            TerminalRole.None);

    private static ExistingAssignment Existing(string label, params Guid?[] circuits) =>
        new(Guid.NewGuid(), label,
            circuits.Select((c, i) => new ChannelAssignment(i, c, c is null)).ToList());

    [Fact]
    public void Nothing_is_reported_when_every_circuit_still_has_a_channel()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, CircuitB)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Nothing_is_reported_when_a_circuit_simply_moves_to_another_device()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, null), Dimmer("Dimmer 2", CircuitB, null)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_circuit_that_loses_its_channel_is_reported_as_a_warning()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, null)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.OrphanedAssignment, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(CircuitB.ToString(), diagnostic.Message);
    }

    [Fact]
    public void Several_orphans_are_reported_in_a_stable_order()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", null, null)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(CircuitA.ToString(), diagnostics[0].Message);
        Assert.Contains(CircuitB.ToString(), diagnostics[1].Message);
    }

    [Fact]
    public void A_first_generation_with_no_previous_devices_reports_nothing()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, null)]);

        Assert.Empty(OrphanReporter.Report(layout, []));
    }
}
