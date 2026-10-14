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
    decimal? LengthMetres,
    /// Where the circuit lands along the terminal rail, counted from 1. Null
    /// for a circuit nobody has placed: it follows the placed ones, in
    /// Sequence order. Separate from Sequence because Sequence decides which
    /// Shelly channel a circuit gets, and moving a terminal must not rewire one.
    int? TerminalPosition = null,
    /// How many terminal blocks the circuit lands on. Above one, their line
    /// tiers are bridged so several cables share one channel.
    int TerminalCount = 1)
{
    public decimal LoadWatts => (WattsPerMetre ?? 0m) * (LengthMetres ?? 0m);

    public bool HasTapeLoad => WattsPerMetre is > 0m && LengthMetres is > 0m;
}
