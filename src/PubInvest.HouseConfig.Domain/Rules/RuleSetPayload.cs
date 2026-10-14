using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Rules;

/// One 3-tier block per terminal: L, N and E on a single slice. A circuit has
/// one block unless it asks for more (Circuit.TerminalCount).
///
/// The neutral tier is bridged across the whole bank with a bar. Earth commons
/// through the DIN rail, so it needs none. Line is per-circuit and loops out to
/// the Shelly. Only a circuit with several blocks bridges its line tier, across
/// its own blocks, with a cut-to-length line bar.
///
/// The line bar fields are defaulted so a ruleset stored before they existed
/// still reads. With no line bar ways, no line bars are counted.
public sealed record TerminalRules(
    Guid DeviceTypeId,
    Guid BridgeBarDeviceTypeId,
    int BridgeBarWays,
    Guid EndStopDeviceTypeId,
    int EndStopsPerBank,
    Guid LineBridgeBarDeviceTypeId = default,
    int LineBridgeBarWays = 0);

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
