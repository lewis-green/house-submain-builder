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
    public void Each_conductor_gets_one_block_per_circuit_plus_one_for_the_incomer()
    {
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(10, band.Devices.Count(d => d.TerminalRole == TerminalRole.Line));
        Assert.Equal(10, band.Devices.Count(d => d.TerminalRole == TerminalRole.Neutral));
        Assert.Equal(10, band.Devices.Count(d => d.TerminalRole == TerminalRole.Earth));
    }

    [Fact]
    public void Earth_blocks_use_the_pe_part_and_line_blocks_the_standard_part()
    {
        var band = TerminalBandBuilder.Build(Circuits(2), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.All(
            band.Devices.Where(d => d.TerminalRole == TerminalRole.Earth),
            d => Assert.Equal(CatalogueFixture.EarthId, d.DeviceTypeId));
        Assert.All(
            band.Devices.Where(d => d.TerminalRole == TerminalRole.Line),
            d => Assert.Equal(CatalogueFixture.TerminalId, d.DeviceTypeId));
    }

    [Fact]
    public void Blocks_are_labelled_by_conductor_and_numbered_from_one()
    {
        var band = TerminalBandBuilder.Build(Circuits(2), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(
            ["L1", "L2", "L3"],
            band.Devices.Where(d => d.TerminalRole == TerminalRole.Line).Select(d => d.Label));
    }

    [Fact]
    public void Bridged_banks_contribute_jumper_bars_and_end_stops_but_unbridged_lines_do_not()
    {
        // 9 circuits -> 10 blocks per bank; 10-way bars -> 1 bar per bridged bank; 2 bridged banks.
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
        Assert.Equal(4, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.EndStopId).Quantity);
    }

    [Fact]
    public void Jumper_bars_round_up_when_a_bank_exceeds_one_bar()
    {
        // 11 circuits -> 12 blocks per bank -> ceil(12/10) = 2 bars per bridged bank, 2 banks.
        var band = TerminalBandBuilder.Build(Circuits(11), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(4, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
    }

    [Fact]
    public void A_missing_terminal_part_produces_an_error_diagnostic()
    {
        var catalogue = new DeviceCatalogue(CatalogueFixture.Catalogue().All
            .Where(d => d.Id != CatalogueFixture.EarthId));

        var band = TerminalBandBuilder.Build(Circuits(1), CatalogueFixture.Rules(), catalogue);

        Assert.DoesNotContain(band.Devices, d => d.TerminalRole == TerminalRole.Earth);
        var diagnostic = Assert.Single(band.Diagnostics);
        Assert.Equal(DiagnosticCodes.NoPreferredDevice, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
