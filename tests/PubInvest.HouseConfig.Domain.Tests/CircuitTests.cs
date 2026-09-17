using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;

namespace PubInvest.HouseConfig.Domain.Tests;

public class CircuitTests
{
    [Fact]
    public void LoadWatts_multiplies_tape_watts_per_metre_by_length()
    {
        var circuit = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Kitchen plinth", "Kitchen", 1,
            WattsPerMetre: 14.4m, LengthMetres: 6.5m);

        Assert.Equal(93.6m, circuit.LoadWatts);
    }

    [Fact]
    public void LoadWatts_is_zero_when_tape_dimensions_are_missing()
    {
        var circuit = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Unmeasured", null, 2,
            WattsPerMetre: null, LengthMetres: null);

        Assert.Equal(0m, circuit.LoadWatts);
    }

    [Fact]
    public void DeviceCatalogue_returns_null_for_an_unknown_device_type()
    {
        var catalogue = new DeviceCatalogue([]);

        Assert.Null(catalogue.Find(Guid.NewGuid()));
    }
}
