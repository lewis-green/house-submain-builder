using PubInvest.HouseConfig.Domain.Bom;
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
    EnclosureType Enclosure,
    /// Costed at issue time. Accessories (jumper bars, end stops) occupy no
    /// slots, so they are not placed devices and cannot be recovered from the
    /// layout alone — storing the priced BOM is what makes a revision able to
    /// re-cost itself years later.
    BillOfMaterials Bom);

public sealed record CircuitSnapshot(Guid Id, string Type, string Name, string? Room, int Sequence);
