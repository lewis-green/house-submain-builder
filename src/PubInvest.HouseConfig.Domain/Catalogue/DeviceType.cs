namespace PubInvest.HouseConfig.Domain.Catalogue;

public enum DeviceCategory
{
    /// A 3-tier block carrying L, N and E for one circuit on a single slice.
    Terminal240,

    /// Two-pole isolator. The incoming submain feed lands here, not on a terminal.
    Isolator,

    Dimmer240,
    Dimmer0_10V,
    Relay,

    /// 12-way +24V distribution block. One way per LED tape circuit.
    Dc24VPositive,

    /// 12-way -24V distribution block.
    Dc24VNegative,

    /// Sized and costed but never placed: LED drivers are not DIN mount and live
    /// outside the panel.
    ExternalDriver,

    Accessory,
}

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
