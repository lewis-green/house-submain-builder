using System.Text.Json;
using System.Text.Json.Serialization;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Mapping;

public static class DomainMapper
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DeviceType ToDomain(DeviceTypeRow row) => new(
        row.Id, row.Manufacturer, row.Model, row.PartNumber,
        Enum.Parse<DeviceCategory>(row.Category),
        row.ModuleWidth, row.ChannelCount, row.MaxLoadPerChannelW, row.MaxTotalLoadW,
        row.Cost, row.Active);

    public static EnclosureType ToDomain(EnclosureTypeRow row) => new(
        row.Id, row.Manufacturer, row.Model, row.Rows, row.SlotsPerRow, row.IpRating, row.Cost);

    public static Circuit ToDomain(CircuitRow row) => new(
        row.Id, Enum.Parse<CircuitType>(row.Type), row.Name, row.Room, row.Sequence,
        row.WattsPerMetre, row.LengthMetres);

    public static RuleSetPayload ToDomain(RuleSetRow row)
        => JsonSerializer.Deserialize<RuleSetPayload>(row.PayloadJson, Json)
           ?? throw new InvalidOperationException($"Ruleset '{row.Name}' v{row.Version} has an unreadable payload.");

    public static ExistingAssignment ToExisting(DeviceInstance row) => new(
        row.Id,
        row.Label,
        row.Channels
            .OrderBy(c => c.ChannelIndex)
            .Select(c => new ChannelAssignment(c.ChannelIndex, c.CircuitId, c.IsSpare))
            .ToList());
}
