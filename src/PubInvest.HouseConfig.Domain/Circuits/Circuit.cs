namespace PubInvest.HouseConfig.Domain.Circuits;

public enum CircuitType
{
    DimmedLighting,
    Switched,

    /// Constant-voltage 24V tape on a 0-10V dimmer, fed by an external driver.
    LedTape,

    /// A blind or roller shutter.
    Cover,

    /// Colour tape on an RGBWW controller. Fed from the same 24V supply as
    /// LedTape, so it is sized with it.
    RgbwTape,
}

public sealed record Circuit(
    Guid Id,
    CircuitType Type,
    string Name,
    string? Room,
    int Sequence,
    decimal? WattsPerMetre,
    decimal? LengthMetres)
{
    public decimal LoadWatts => (WattsPerMetre ?? 0m) * (LengthMetres ?? 0m);

    public bool HasTapeLoad => WattsPerMetre is > 0m && LengthMetres is > 0m;
}
