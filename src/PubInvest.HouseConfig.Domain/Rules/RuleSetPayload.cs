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

/// A horizontal zone of the panel. Every zone starts on a fresh row; within it,
/// `FromLeft` categories grow rightwards from the left-hand end and `FromRight`
/// ones grow leftwards from the right, meeting in the middle.
public sealed record PackingZone(
    IReadOnlyList<DeviceCategory> FromLeft,
    IReadOnlyList<DeviceCategory> FromRight);

public sealed record RuleSetPayload(
    /// Top zone first. The house default is termination on top — circuit
    /// terminals then the 24V pair from the left, isolator from the right — and
    /// the Shelly kit below it.
    IReadOnlyList<PackingZone> Zones,
    decimal PsuDeratingFactor,
    PreferredDevices PreferredDevice,
    TerminalRules Terminals);
