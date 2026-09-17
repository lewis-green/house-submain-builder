using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Rules;

public sealed record TerminalConductorRule(Guid DeviceTypeId, int BlocksPerCircuit, bool Bridged);

public sealed record TerminalRules(
    TerminalConductorRule Line,
    TerminalConductorRule Neutral,
    TerminalConductorRule Earth,
    Guid BridgeBarDeviceTypeId,
    int BridgeBarWays,
    Guid EndStopDeviceTypeId,
    int EndStopsPerBank);

public sealed record PreferredDevices(
    Guid Dimmer240,
    Guid Dimmer0_10V,
    Guid Relay,
    IReadOnlyList<Guid> Psu24V);

public sealed record RuleSetPayload(
    IReadOnlyList<DeviceCategory> BandOrder,
    bool BandStartsNewRow,
    decimal PsuDeratingFactor,
    PreferredDevices PreferredDevice,
    TerminalRules Terminals,
    string Packing);
