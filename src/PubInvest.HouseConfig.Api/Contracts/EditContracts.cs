namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record UpdateCircuitRequest(string Name, string? Room);

public sealed record UpdateChannelRequest(Guid? CircuitId, bool IsSpare, int BasedOnLayoutVersion);

public sealed record UpdatePositionRequest(int RowIndex, int StartSlot, int BasedOnLayoutVersion);

/// Returned with 409 so the client can re-render from current state
/// instead of guessing what changed.
public sealed record ConflictResponse(string Message, int CurrentLayoutVersion);
