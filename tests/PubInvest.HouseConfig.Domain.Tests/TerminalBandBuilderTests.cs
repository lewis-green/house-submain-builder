using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class TerminalBandBuilderTests
{
    private static IReadOnlyList<Circuit> Circuits(int count) =>
        Enumerable.Range(1, count)
            .Select(n => new Circuit(Guid.NewGuid(), CircuitType.Switched, $"Switched {n}", null, n, null, null))
            .ToList();

    [Fact]
    public void One_block_serves_one_circuit_across_all_three_conductors()
    {
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(9, band.Devices.Count);
        Assert.All(band.Devices, d => Assert.Equal(TerminalRole.All, d.TerminalRole));
        Assert.All(band.Devices, d => Assert.Equal(CatalogueFixture.TerminalId, d.DeviceTypeId));
    }

    [Fact]
    public void The_incoming_feed_does_not_take_a_terminal_block()
    {
        // It lands on the isolator instead.
        var band = TerminalBandBuilder.Build(Circuits(4), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(4, band.Devices.Count);
    }

    [Fact]
    public void Blocks_are_labelled_for_the_circuit_they_serve()
    {
        var band = TerminalBandBuilder.Build(Circuits(3), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(["C1", "C2", "C3"], band.Devices.Select(d => d.Label));
    }

    [Fact]
    public void Only_the_neutral_tier_needs_bars_and_the_earth_needs_none()
    {
        // Nine blocks in one bank, ten-way bars: one bar, one set of end stops.
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(1, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.EndStopId).Quantity);
    }

    [Fact]
    public void Bars_round_up_once_a_bank_runs_past_one_bar()
    {
        var band = TerminalBandBuilder.Build(Circuits(11), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
    }

    [Fact]
    public void A_submain_with_no_circuits_needs_no_terminals()
    {
        var band = TerminalBandBuilder.Build([], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Empty(band.Devices);
        Assert.Empty(band.Accessories);
    }

    [Fact]
    public void A_missing_terminal_part_produces_an_error_diagnostic()
    {
        var catalogue = new DeviceCatalogue(CatalogueFixture.Catalogue().All
            .Where(d => d.Id != CatalogueFixture.TerminalId));

        var band = TerminalBandBuilder.Build(Circuits(1), CatalogueFixture.Rules(), catalogue);

        Assert.Empty(band.Devices);
        var diagnostic = Assert.Single(band.Diagnostics);
        Assert.Equal(DiagnosticCodes.NoPreferredDevice, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
