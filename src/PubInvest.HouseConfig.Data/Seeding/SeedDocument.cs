using PubInvest.HouseConfig.Domain.Racks;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Seeding;

/// Rack sections default to null so a seed written before racks existed, and
/// every test that builds one in code, still reads.
public sealed record SeedDocument(
    int Version,
    IReadOnlyList<SeedDeviceType> DeviceTypes,
    IReadOnlyList<SeedEnclosure> Enclosures,
    IReadOnlyList<SeedRuleSet> RuleSets,
    IReadOnlyList<SeedRackType>? RackTypes = null,
    IReadOnlyList<SeedRackDeviceType>? RackDeviceTypes = null);

public sealed record SeedDeviceType(
    Guid Id, string Manufacturer, string Model, string PartNumber, string Category,
    int ModuleWidth, int ChannelCount, int? MaxLoadPerChannelW, int? MaxTotalLoadW, bool Active);

public sealed record SeedEnclosure(
    Guid Id, string Manufacturer, string Model, int Rows, int SlotsPerRow, string IpRating);

public sealed record SeedRuleSet(
    Guid Id, string Name, int Version, bool IsDefault, RuleSetPayload Payload);

public sealed record SeedRackType(
    Guid Id, string Manufacturer, string Model, string PartNumber, int HeightU, string Mounting);

public sealed record SeedRackDeviceType(
    Guid Id, string Manufacturer, string Model, string PartNumber, string Kind, int HeightU,
    IReadOnlyList<PortGroup> PortGroups, int? PoeBudgetW, Guid? RequiredAccessoryId, bool Active);
