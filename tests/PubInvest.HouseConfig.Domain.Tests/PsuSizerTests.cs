using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PsuSizerTests
{
    private static Circuit Tape(int n, decimal wPerM, decimal metres) =>
        new(Guid.NewGuid(), CircuitType.LedTape, $"Tape {n}", null, n, wPerM, metres);

    [Fact]
    public void No_tape_circuits_means_no_psus()
    {
        var lighting = new Circuit(Guid.NewGuid(), CircuitType.DimmedLighting, "Lighting 1", null, 1, null, null);

        var sizing = PsuSizer.Size([lighting], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Empty(sizing.Devices);
        Assert.Empty(sizing.Diagnostics);
    }

    [Fact]
    public void Load_is_derated_before_psus_are_chosen()
    {
        // 100W of tape derated by 0.8 needs 125W, which the 100W unit cannot cover alone.
        var sizing = PsuSizer.Size([Tape(1, 10m, 10m)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var psu = Assert.Single(sizing.Devices);
        Assert.Equal(CatalogueFixture.Psu240Id, psu.DeviceTypeId);
    }

    [Fact]
    public void Large_loads_are_covered_by_several_psus_largest_first()
    {
        // 400W derated by 0.8 = 500W required: 240 + 240 + 100.
        var sizing = PsuSizer.Size([Tape(1, 20m, 20m)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(
            [CatalogueFixture.Psu240Id, CatalogueFixture.Psu240Id, CatalogueFixture.Psu100Id],
            sizing.Devices.Select(d => d.DeviceTypeId));
        Assert.Equal(["PSU 1", "PSU 2", "PSU 3"], sizing.Devices.Select(d => d.Label));
    }

    [Fact]
    public void A_tape_circuit_without_dimensions_warns_but_does_not_block()
    {
        var unmeasured = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Tape 1", null, 1, null, null);

        var sizing = PsuSizer.Size([unmeasured], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var diagnostic = Assert.Single(sizing.Diagnostics);
        Assert.Equal(DiagnosticCodes.TapeLoadMissing, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void An_empty_psu_list_produces_an_error()
    {
        var rules = CatalogueFixture.Rules();
        var noPsus = rules with
        {
            PreferredDevice = rules.PreferredDevice with { Psu24V = [] }
        };

        var sizing = PsuSizer.Size([Tape(1, 10m, 10m)], noPsus, CatalogueFixture.Catalogue());

        Assert.Empty(sizing.Devices);
        var diagnostic = Assert.Single(sizing.Diagnostics);
        Assert.Equal(DiagnosticCodes.PsuUnsized, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
