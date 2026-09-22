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
    Guid Cover,
    Guid LedController,
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

/// One way of dividing a panel into zones, top to bottom.
public sealed record PanelLayoutOption(IReadOnlyList<PackingZone> Zones);

public sealed record RuleSetPayload(
    /// Tried in order, finest first: the first that fits the enclosure wins.
    ///
    /// The house default starts with a row for every kind of device, falls back
    /// to putting the two sorts of dimmer together, and finally lets the relays
    /// share the dimmer row from the other end. Termination stays on the top row
    /// throughout — circuit terminals and the 24V pair from the left, the
    /// isolator hard against the right.
    IReadOnlyList<PanelLayoutOption> Layouts,
    decimal PsuDeratingFactor,
    PreferredDevices PreferredDevice,
    TerminalRules Terminals);
