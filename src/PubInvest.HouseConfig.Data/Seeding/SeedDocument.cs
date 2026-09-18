using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Seeding;

public sealed record SeedDocument(
    int Version,
    IReadOnlyList<SeedDeviceType> DeviceTypes,
    IReadOnlyList<SeedEnclosure> Enclosures,
    IReadOnlyList<SeedRuleSet> RuleSets);

public sealed record SeedDeviceType(
    Guid Id, string Manufacturer, string Model, string PartNumber, string Category,
    int ModuleWidth, int ChannelCount, int? MaxLoadPerChannelW, int? MaxTotalLoadW, bool Active);

public sealed record SeedEnclosure(
    Guid Id, string Manufacturer, string Model, int Rows, int SlotsPerRow, string IpRating);

public sealed record SeedRuleSet(
    Guid Id, string Name, int Version, bool IsDefault, RuleSetPayload Payload);
