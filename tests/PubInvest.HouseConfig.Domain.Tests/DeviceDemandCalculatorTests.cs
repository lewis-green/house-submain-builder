using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class DeviceDemandCalculatorTests
{
    private static Circuit Lighting(int n) =>
        new(Guid.NewGuid(), CircuitType.DimmedLighting, $"Lighting {n}", null, n, null, null);

    private static Circuit Switched(int n) =>
        new(Guid.NewGuid(), CircuitType.Switched, $"Switched {n}", null, n, null, null);

    private static Circuit Tape(int n) =>
        new(Guid.NewGuid(), CircuitType.LedTape, $"Tape {n}", null, n, 14.4m, 5m);

    [Fact]
    public void Five_dimmed_circuits_need_three_two_channel_dimmers()
    {
        var circuits = Enumerable.Range(1, 5).Select(Lighting).ToList();

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var dimmers = demand.Devices.Where(d => d.Category == DeviceCategory.Dimmer240).ToList();
        Assert.Equal(3, dimmers.Count);
        Assert.Equal(["Dimmer 1", "Dimmer 2", "Dimmer 3"], dimmers.Select(d => d.Label));
    }

    [Fact]
    public void The_last_device_carries_spare_channels_rather_than_being_dropped()
    {
        var circuits = Enumerable.Range(1, 5).Select(Lighting).ToList();

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var last = demand.Devices.Last(d => d.Category == DeviceCategory.Dimmer240);
        Assert.Equal(2, last.Channels.Count);
        Assert.NotNull(last.Channels[0].CircuitId);
        Assert.True(last.Channels[1].IsSpare);
        Assert.Null(last.Channels[1].CircuitId);
    }

    [Fact]
    public void Circuits_are_assigned_in_sequence_order()
    {
        var circuits = new List<Circuit> { Lighting(3), Lighting(1), Lighting(2) };

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var assigned = demand.Devices
            .Where(d => d.Category == DeviceCategory.Dimmer240)
            .SelectMany(d => d.Channels)
            .Where(c => c.CircuitId is not null)
            .Select(c => circuits.Single(x => x.Id == c.CircuitId).Sequence);

        Assert.Equal([1, 2, 3], assigned);
    }

    [Fact]
    public void Each_circuit_type_gets_its_own_device_category()
    {
        var circuits = new List<Circuit> { Lighting(1), Switched(2), Tape(3) };

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(
            [DeviceCategory.Dimmer240, DeviceCategory.Relay, DeviceCategory.Dimmer0_10V],
            demand.Devices.Select(d => d.Category));
    }

    [Fact]
    public void No_circuits_of_a_type_means_no_devices_of_that_category()
    {
        var demand = DeviceDemandCalculator.Calculate([Lighting(1)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.All(demand.Devices, d => Assert.Equal(DeviceCategory.Dimmer240, d.Category));
    }

    [Fact]
    public void An_inactive_preferred_device_produces_an_error_diagnostic()
    {
        var catalogue = new DeviceCatalogue(CatalogueFixture.Catalogue().All
            .Select(d => d.Id == CatalogueFixture.RelayId ? d with { Active = false } : d));

        var demand = DeviceDemandCalculator.Calculate([Switched(1)], CatalogueFixture.Rules(), catalogue);

        Assert.Empty(demand.Devices);
        var diagnostic = Assert.Single(demand.Diagnostics);
        Assert.Equal(DiagnosticCodes.NoPreferredDevice, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
