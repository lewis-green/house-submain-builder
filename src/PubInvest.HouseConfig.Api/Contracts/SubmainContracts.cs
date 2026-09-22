namespace PubInvest.HouseConfig.Api.Contracts;

/// A device the panel carries that no circuit asks for. Quantity, not one entry
/// per device, because they are ordered and fitted by the handful.
public sealed record ExtraFixtureRequest(Guid DeviceTypeId, int Quantity);

public sealed record ExtraFixtureResponse(Guid DeviceTypeId, int Quantity);

public sealed record CircuitRequest(
    Guid? Id, string Type, string Name, string? Room, int Sequence,
    decimal? WattsPerMetre, decimal? LengthMetres);

// HasIsolator and TerminalsAtBottom are nullable on the wire and left alone when
// absent: the wizard sends a subset of these fields, and a missing bool must not
// read as "no isolator".
public sealed record CreateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    bool? HasIsolator, bool? TerminalsAtBottom,
    IReadOnlyList<CircuitRequest>? Circuits,
    IReadOnlyList<ExtraFixtureRequest>? ExtraFixtures);

public sealed record UpdateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    bool? HasIsolator, bool? TerminalsAtBottom,
    IReadOnlyList<CircuitRequest>? Circuits,
    IReadOnlyList<ExtraFixtureRequest>? ExtraFixtures);

public sealed record SubmainResponse(
    Guid Id, Guid ProjectId, string Name, string? Reference, string? FeedCableSize,
    int? OriginBreakerAmps, string? Phase, Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    bool HasIsolator, bool TerminalsAtBottom,
    int LayoutVersion, int CircuitCount, int DeviceCount,
    IReadOnlyList<ExtraFixtureResponse> ExtraFixtures);
