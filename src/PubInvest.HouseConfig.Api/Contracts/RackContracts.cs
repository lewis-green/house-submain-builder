using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record SaveRackRequest(string Name, Guid RackTypeId, Guid? FedFromSubmainId, string? Notes = null);

public sealed record RackResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    Guid RackTypeId,
    string Cabinet,
    int HeightU,
    int UsedU,
    Guid? FedFromSubmainId,
    string? FedFromSubmainName,
    string? Notes,
    int LayoutVersion);

public sealed record RackPortResponse(int Number, string? Label, bool Poe);

public sealed record RackItemResponse(
    Guid Id,
    Guid RackDeviceTypeId,
    RackDeviceKind Kind,
    string Model,
    string PartNumber,
    string Label,
    int TopU,
    int HeightU,
    IReadOnlyList<PortGroup> PortGroups,
    int? PoeBudgetW,
    /// Only the ports that have a label or a tick; the rest are implied by the groups.
    IReadOnlyList<RackPortResponse> Ports);

/// Items top U first, the order a person reads a rack in.
public sealed record RackLayoutResponse(
    Guid RackId,
    int HeightU,
    int FreeU,
    int LayoutVersion,
    IReadOnlyList<RackItemResponse> Items);

public sealed record AddRackItemRequest(Guid RackDeviceTypeId);

public sealed record MoveRackItemRequest(int TopU, int BasedOnLayoutVersion);

public sealed record SaveRackPortRequest(string? Label, bool Poe);

public sealed record FedRackResponse(Guid Id, string Name);

/// A request the rack cannot take, said in words: "No room in this rack: 0U free".
public sealed record RefusalResponse(string Message);
