namespace PubInvest.HouseConfig.Domain.Catalogue;

public enum DeviceCategory { Terminal240, Dimmer240, Dimmer0_10V, Relay, Psu24V, Accessory }

public sealed record DeviceType(
    Guid Id,
    string Manufacturer,
    string Model,
    string PartNumber,
    DeviceCategory Category,
    int ModuleWidth,
    int ChannelCount,
    int? MaxLoadPerChannelW,
    int? MaxTotalLoadW,
    decimal Cost,
    bool Active)
{
    public string Description => $"{Manufacturer} {Model}";
}
