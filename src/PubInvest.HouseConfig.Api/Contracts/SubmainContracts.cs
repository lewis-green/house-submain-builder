namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record CircuitRequest(
    Guid? Id, string Type, string Name, string? Room, int Sequence,
    decimal? WattsPerMetre, decimal? LengthMetres);

public sealed record CreateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    IReadOnlyList<CircuitRequest>? Circuits);

public sealed record UpdateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    IReadOnlyList<CircuitRequest>? Circuits);

public sealed record SubmainResponse(
    Guid Id, Guid ProjectId, string Name, string? Reference, string? FeedCableSize,
    int? OriginBreakerAmps, string? Phase, Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    int LayoutVersion, int CircuitCount, int DeviceCount);
