using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class TapeSupplySizerTests
{
    private static Circuit Tape(int n, decimal wPerM = 14.4m, decimal metres = 5m) =>
        new(Guid.NewGuid(), CircuitType.LedTape, $"Tape {n}", null, n, wPerM, metres);

    [Fact]
    public void No_tape_means_no_blocks_and_no_driver()
    {
        var lighting = new Circuit(Guid.NewGuid(), CircuitType.DimmedLighting, "Lighting 1", null, 1, null, null);

        var supply = TapeSupplySizer.Size([lighting], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Empty(supply.Blocks);
        Assert.Empty(supply.ExternalParts);
        Assert.Empty(supply.Diagnostics);
    }

    [Fact]
    public void Tape_puts_a_plus_and_a_minus_block_on_the_rail()
    {
        var supply = TapeSupplySizer.Size([Tape(1)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Contains(supply.Blocks, b => b.Category == DeviceCategory.Dc24VPositive);
        Assert.Contains(supply.Blocks, b => b.Category == DeviceCategory.Dc24VNegative);
        Assert.Equal(2, supply.Blocks.Count);
    }

    [Fact]
    public void Every_tape_run_gets_its_own_pair_of_joints()
    {
        // Each block is where that tape's output is made off, so three runs need
        // three + blocks and three - blocks, not three ways on one pair.
        var circuits = Enumerable.Range(1, 3).Select(n => Tape(n)).ToList();

        var supply = TapeSupplySizer.Size(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(3, supply.Blocks.Count(b => b.Category == DeviceCategory.Dc24VPositive));
        Assert.Equal(3, supply.Blocks.Count(b => b.Category == DeviceCategory.Dc24VNegative));
    }

    [Fact]
    public void Joints_are_numbered_so_a_run_can_be_matched_to_its_pair()
    {
        var circuits = Enumerable.Range(1, 3).Select(n => Tape(n)).ToList();

        var supply = TapeSupplySizer.Size(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(["+24V 1", "+24V 2", "+24V 3"],
            supply.Blocks.Where(b => b.Category == DeviceCategory.Dc24VPositive).Select(b => b.Label));
        Assert.Equal(["-24V 1", "-24V 2", "-24V 3"],
            supply.Blocks.Where(b => b.Category == DeviceCategory.Dc24VNegative).Select(b => b.Label));
    }

    [Fact]
    public void A_single_tape_run_needs_no_numbering()
    {
        var supply = TapeSupplySizer.Size([Tape(1)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(["+24V", "-24V"], supply.Blocks.Select(b => b.Label));
    }

    [Fact]
    public void The_driver_is_sized_but_never_placed_on_the_rail()
    {
        // 72W of tape derated by 0.8 needs 90W, so the 100W driver covers it.
        var supply = TapeSupplySizer.Size([Tape(1)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Contains(supply.ExternalParts, p => p.DeviceTypeId == CatalogueFixture.Psu100Id);
        Assert.DoesNotContain(supply.Blocks, b => b.Category == DeviceCategory.ExternalDriver);
    }

    [Fact]
    public void A_larger_load_takes_a_larger_driver()
    {
        // 20 W/m over 20m is 400W; derated that is 500W, so 240 + 240 + 100.
        var supply = TapeSupplySizer.Size([Tape(1, 20m, 20m)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(2, supply.ExternalParts.Single(p => p.DeviceTypeId == CatalogueFixture.Psu240Id).Quantity);
        Assert.Equal(1, supply.ExternalParts.Single(p => p.DeviceTypeId == CatalogueFixture.Psu100Id).Quantity);
    }

    [Fact]
    public void Tape_with_no_dimensions_warns_but_still_gets_its_blocks()
    {
        var unmeasured = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Tape 1", null, 1, null, null);

        var supply = TapeSupplySizer.Size([unmeasured], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(DiagnosticCodes.TapeLoadMissing, Assert.Single(supply.Diagnostics).Code);
        Assert.Equal(2, supply.Blocks.Count);
    }

    [Fact]
    public void An_empty_driver_list_is_an_error()
    {
        var rules = CatalogueFixture.Rules();
        var noDrivers = rules with { PreferredDevice = rules.PreferredDevice with { ExternalDriver = [] } };

        var supply = TapeSupplySizer.Size([Tape(1)], noDrivers, CatalogueFixture.Catalogue());

        Assert.Contains(supply.Diagnostics, d => d.Code == DiagnosticCodes.PsuUnsized);
    }
}
