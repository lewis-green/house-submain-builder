using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Contracts;

/// Where a switch's uplink goes: another switch's port, named by its label,
/// rack and location so either end can say it without a second request, or
/// something said in words.
public sealed record RackUplinkResponse(
    int Port,
    Guid? ToItemId,
    int? ToPort,
    string? ToText,
    string? ToLabel,
    Guid? ToRackId,
    string? ToRackName,
    string? ToLocationName);

/// Another switch's uplink, seen from the port it arrives at.
public sealed record RackDownlinkResponse(int Port, Guid FromItemId, string FromLabel, Guid FromRackId, string FromRackName);

/// Exactly one of ToItemId (with ToPort) and ToText.
public sealed record SaveRackUplinkRequest(int Port, Guid? ToItemId = null, int? ToPort = null, string? ToText = null);

/// A switch anywhere in the house, as the uplink picker offers it: where it
/// is, its ports, and which of them are already spoken for.
public sealed record HouseSwitchResponse(
    Guid Id,
    string Label,
    string Model,
    Guid RackId,
    string RackName,
    Guid? LocationId,
    string? LocationName,
    IReadOnlyList<PortGroup> PortGroups,
    int? UplinkPort,
    IReadOnlyList<RackDownlinkResponse> Downlinks);
