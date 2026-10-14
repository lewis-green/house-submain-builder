using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// Everything an issued rack prints, embedded: an admin editing the catalogue
/// later must not be able to change what an issued drawing meant.
public sealed record RackSnapshot(
    string ProjectName,
    string RackName,
    RackType Cabinet,
    string? FedFromSubmainName,
    /// Top U first.
    IReadOnlyList<RackItemSnapshot> Items,
    /// The item types placed and the accessories they need, as they were.
    IReadOnlyList<RackDeviceType> Catalogue,
    BillOfMaterials Bom,
    /// Where in the house the rack is, or null when it is not placed — as every
    /// rack issued before locations existed was, so those still compare equal.
    string? LocationName = null);

public sealed record RackItemSnapshot(
    Guid RackDeviceTypeId,
    string Label,
    int TopU,
    int HeightU,
    IReadOnlyList<RackPortSnapshot> Ports);

public sealed record RackPortSnapshot(int Number, string? Label, bool Poe);
