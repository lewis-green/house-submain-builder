namespace PubInvest.HouseConfig.Api.Contracts;

/// One circuit as the terminal sheet shows it, in terminal order.
public sealed record TerminalCircuitResponse(Guid CircuitId, string Name, string? Room, string Type, int TerminalCount);

public sealed record TerminalsResponse(int LayoutVersion, IReadOnlyList<TerminalCircuitResponse> Circuits);

public sealed record TerminalCircuitRequest(Guid CircuitId, int TerminalCount);

/// Every circuit of the submain, once, in the order they should land on the rail.
public sealed record UpdateTerminalsRequest(int LayoutVersion, IReadOnlyList<TerminalCircuitRequest>? Circuits);
