namespace PubInvest.HouseConfig.Domain.Circuits;

public enum CircuitType { DimmedLighting, Switched, LedTape }

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
