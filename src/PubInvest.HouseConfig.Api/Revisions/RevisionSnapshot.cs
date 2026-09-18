using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Api.Revisions;

/// Everything needed to redraw and re-cost this design later, embedded rather
/// than referenced: an admin editing a rule or a price afterwards must not be
/// able to change what an issued drawing meant.
public sealed record RevisionSnapshot(
    string ProjectName,
    string SubmainName,
    string? Reference,
    PanelLayout Layout,
    IReadOnlyList<CircuitSnapshot> Circuits,
    RuleSetPayload RuleSet,
    IReadOnlyList<DeviceType> Catalogue,
    EnclosureType Enclosure);

public sealed record CircuitSnapshot(Guid Id, string Type, string Name, string? Room, int Sequence);
