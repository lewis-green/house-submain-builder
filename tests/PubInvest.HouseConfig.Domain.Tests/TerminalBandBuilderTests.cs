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

    private static Circuit Switched(int sequence, int? position = null, int count = 1) =>
        new(Guid.NewGuid(), CircuitType.Switched, $"Switched {sequence}", null, sequence, null, null,
            position, count);

    private static TerminalBand Build(params Circuit[] circuits) =>
        TerminalBandBuilder.Build(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

    [Fact]
    public void Circuits_are_laid_in_their_terminal_position_not_their_sequence()
    {
        var first = Switched(1, position: 3);
        var second = Switched(2, position: 1);
        var third = Switched(3, position: 2);

        var band = Build(first, second, third);

        Assert.Equal([second.Id, third.Id, first.Id], band.Devices.Select(d => d.CircuitId!.Value));
        Assert.Equal(["C1", "C2", "C3"], band.Devices.Select(d => d.Label));
    }

    [Fact]
    public void Circuits_nobody_has_placed_follow_the_placed_ones_in_sequence_order()
    {
        // Added after the order was chosen: they go on the end, not in the middle.
        var placed = Switched(3, position: 1);
        var newA = Switched(1);
        var newB = Switched(2);

        var band = Build(newA, placed, newB);

        Assert.Equal([placed.Id, newA.Id, newB.Id], band.Devices.Select(d => d.CircuitId!.Value));
    }

    [Fact]
    public void With_no_positions_at_all_the_order_is_the_sequence()
    {
        var a = Switched(2);
        var b = Switched(1);

        var band = Build(a, b);

        Assert.Equal([b.Id, a.Id], band.Devices.Select(d => d.CircuitId!.Value));
    }

    [Fact]
    public void A_circuit_on_several_terminals_gets_adjacent_blocks_and_the_next_keeps_its_number()
    {
        var a = Switched(1);
        var bridged = Switched(2, count: 6);
        var c = Switched(3);

        var band = Build(a, bridged, c);

        Assert.Equal(
            ["C1", "C2.1", "C2.2", "C2.3", "C2.4", "C2.5", "C2.6", "C3"],
            band.Devices.Select(d => d.Label));
        Assert.All(band.Devices.Skip(1).Take(6), d => Assert.Equal(bridged.Id, d.CircuitId));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(12, 2)]
    public void A_bridged_circuit_gets_line_bars_for_its_own_blocks(int count, int bars)
    {
        var band = Build(Switched(1, count: count));

        var line = band.Accessories.SingleOrDefault(a => a.DeviceTypeId == CatalogueFixture.LineBridgeId);
        Assert.Equal(bars, line?.Quantity ?? 0);
    }

    [Fact]
    public void Line_bars_are_not_shared_between_circuits()
    {
        // Two groups of six would fit one 10-way bar's length between them, but
        // an offcut is not a bar: each group gets its own.
        var band = Build(Switched(1, count: 6), Switched(2, count: 6));

        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.LineBridgeId).Quantity);
    }

    [Fact]
    public void The_neutral_bar_spans_every_block_including_bridged_ones()
    {
        // 6 + 5 = 11 blocks: two 10-way neutral bars.
        var band = Build(Switched(1, count: 6), Switched(2), Switched(3), Switched(4), Switched(5), Switched(6));

        Assert.Equal(11, band.Devices.Count);
        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
    }

    [Fact]
    public void A_ruleset_without_line_bars_counts_none()
    {
        var rules = CatalogueFixture.Rules() with
        {
            Terminals = CatalogueFixture.Rules().Terminals with { LineBridgeBarWays = 0 }
        };

        var band = TerminalBandBuilder.Build([Switched(1, count: 4)], rules, CatalogueFixture.Catalogue());

        Assert.DoesNotContain(band.Accessories, a => a.DeviceTypeId == CatalogueFixture.LineBridgeId);
    }

    [Fact]
    public void A_count_beyond_the_limit_is_held_to_it()
    {
        var band = Build(Switched(1, count: 40));

        Assert.Equal(TerminalBandBuilder.MaxTerminalsPerCircuit, band.Devices.Count);
    }
}
