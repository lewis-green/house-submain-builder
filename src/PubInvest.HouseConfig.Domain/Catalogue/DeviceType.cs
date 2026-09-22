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

    /// Roller shutter or blind controller: two channels drive one cover, up and
    /// down, so its channel count is not a count of circuits.
    Cover,

    /// Multi-channel constant-voltage LED controller (RGB plus white).
    LedController,

    /// Energy meter. Measures; switches nothing and feeds no circuit.
    EnergyMeter,

    /// Network gear in the panel, such as a DIN-mount LAN switch.
    Network,

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
    bool Active)
{
    public string Description => $"{Manufacturer} {Model}";

    /// How many circuits this device can carry, which is not always its channel
    /// count. An RGBWW controller's five channels are the colour outputs of one
    /// run, not five runs, so it carries one circuit and shows one channel to
    /// name.
    public int CircuitCapacity => Category == DeviceCategory.LedController ? 1 : ChannelCount;
}
