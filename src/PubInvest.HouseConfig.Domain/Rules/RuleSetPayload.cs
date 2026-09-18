using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Rules;

/// One 3-tier block per circuit: L, N and E on a single slice.
///
/// Only the neutral tier is bridged with a bar. Earth commons through the DIN
/// rail, so it needs none; line is per-circuit and loops out to the Shelly.
public sealed record TerminalRules(
    Guid DeviceTypeId,
    int BlocksPerCircuit,
    Guid BridgeBarDeviceTypeId,
    int BridgeBarWays,
    Guid EndStopDeviceTypeId,
    int EndStopsPerBank);

public sealed record PreferredDevices(
    Guid Isolator,
    Guid Dimmer240,
    Guid Dimmer0_10V,
    Guid Relay,
    Guid Dc24VPositive,
    Guid Dc24VNegative,
    /// Not panel-mounted. Sized from the tape load and costed, never placed.
    IReadOnlyList<Guid> ExternalDriver);

public sealed record RuleSetPayload(
    IReadOnlyList<DeviceCategory> BandOrder,
    decimal PsuDeratingFactor,
    PreferredDevices PreferredDevice,
    TerminalRules Terminals,
    /// "bandPerRow" tries a row per band and merges only if that will not fit;
    /// "dense" always merges, packing relays from the right.
    string Packing);
