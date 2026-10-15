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
    IReadOnlyList<RackPortSnapshot> Ports,
    /// A switch's uplink, or null when it has none — as every item issued
    /// before uplinks existed had, so those still compare equal.
    RackUplinkSnapshot? Uplink = null,
    /// Other switches' uplinks arriving here, from this rack or another. Null
    /// rather than empty when there are none, for the same reason.
    IReadOnlyList<RackDownlinkSnapshot>? Downlinks = null);

public sealed record RackPortSnapshot(int Number, string? Label, bool Poe);

/// Either the words, or the other switch with its port. The other switch's
/// rack and location are null when it is in this rack: the drawing's own
/// header already says where that is.
public sealed record RackUplinkSnapshot(
    int Port,
    string? ToText,
    string? ToLabel,
    string? ToRackName,
    string? ToLocationName,
    int? ToPort);

/// The rack is null when the switch it comes from is in this one.
public sealed record RackDownlinkSnapshot(int Port, string FromLabel, string? FromRackName);
