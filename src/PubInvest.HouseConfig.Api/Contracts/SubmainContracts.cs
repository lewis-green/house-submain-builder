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
// Generate: lay the panel out in the same transaction as the save, so a design
// the generator refuses, or a failure on the way, creates nothing. The wizard
// used to make two calls, and a failed second one left an empty submain in the
// house that nothing could remove.
public sealed record CreateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    bool? HasIsolator, bool? TerminalsAtBottom,
    IReadOnlyList<CircuitRequest>? Circuits,
    IReadOnlyList<ExtraFixtureRequest>? ExtraFixtures,
    bool? Generate = null);

// Rebuild: regenerate the panel in the same transaction as the edit. An install
// option or a fixture only means anything once the panel is built again, so the
// two either both happen or neither does — a saved option over an unchanged
// drawing is a state nobody can see.
public sealed record UpdateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    bool? HasIsolator, bool? TerminalsAtBottom,
    IReadOnlyList<CircuitRequest>? Circuits,
    IReadOnlyList<ExtraFixtureRequest>? ExtraFixtures,
    bool? Rebuild = null);

public sealed record SubmainResponse(
    Guid Id, Guid ProjectId, string Name, string? Reference, string? FeedCableSize,
    int? OriginBreakerAmps, string? Phase, Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    bool HasIsolator, bool TerminalsAtBottom,
    int LayoutVersion, int CircuitCount, int DeviceCount,
    IReadOnlyList<ExtraFixtureResponse> ExtraFixtures);
