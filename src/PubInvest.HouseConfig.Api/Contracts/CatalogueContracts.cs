using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record SaveDeviceTypeRequest(
    string Manufacturer,
    string Model,
    string PartNumber,
    string Category,
    int ModuleWidth,
    int ChannelCount,
    int? MaxLoadPerChannelW,
    int? MaxTotalLoadW,
    bool Active);

public sealed record SaveEnclosureRequest(
    string Manufacturer,
    string Model,
    int Rows,
    int SlotsPerRow,
    string IpRating);

public sealed record SaveRuleSetRequest(
    string Name,
    int Version,
    bool IsDefault,
    RuleSetPayload Payload);
