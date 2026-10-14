using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record TerminalBand(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<AccessoryLine> Accessories,
    IReadOnlyList<Diagnostic> Diagnostics);

/// One 3-tier terminal block per circuit, or several bridged on the line tier
/// when a circuit asks for them (Circuit.TerminalCount). Laid in terminal order
/// (InTerminalOrder), not Sequence.
///
/// The WAGO 2003-7646 carries line, neutral and earth on a single slice, so a
/// circuit needs one block rather than one in each of three banks. The neutral
/// tier is commoned with a jumper bar; earth commons through the DIN rail and
/// needs no bar at all; line is per-circuit and loops out to its Shelly channel.
///
/// The incoming feed lands on the isolator, so no block is spent on it.
public static class TerminalBandBuilder
{
    /// The most blocks one circuit may land on.
    public const int MaxTerminalsPerCircuit = 12;

    /// Placed circuits first, in position order, then the rest in Sequence
    /// order. With no positions at all this is plain Sequence order.
    public static IReadOnlyList<Circuit> InTerminalOrder(IEnumerable<Circuit> circuits) =>
        circuits
            .OrderBy(c => c.TerminalPosition is null)
            .ThenBy(c => c.TerminalPosition)
            .ThenBy(c => c.Sequence)
            .ToList();

    public static TerminalBand Build(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var terminals = rules.Terminals;
        var deviceType = catalogue.FindActive(terminals.DeviceTypeId);

        if (deviceType is null)
        {
            return new TerminalBand([], [], [
                new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for the terminal blocks (id {terminals.DeviceTypeId}).",
                    "Point the ruleset's terminal rules at an active catalogue entry.")
            ]);
        }

        if (circuits.Count == 0) return new TerminalBand([], [], []);

        var ordered = InTerminalOrder(circuits);
        var devices = new List<RequiredDevice>();
        var lineBars = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var circuit = ordered[i];
            var count = Math.Clamp(circuit.TerminalCount, 1, MaxTerminalsPerCircuit);

            for (var n = 0; n < count; n++)
            {
                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    // Numbered by place on the rail, so C1 is always the first
                    // terminal. A bridged circuit's blocks share its number.
                    count == 1 ? $"C{i + 1}" : $"C{i + 1}.{n + 1}",
                    [],
                    TerminalRole.All,
                    circuit.Id));
            }

            // Cut to length per circuit. An offcut is not counted as a bar.
            if (count > 1 && terminals.LineBridgeBarWays > 0)
            {
                lineBars += (int)Math.Ceiling(count / (double)terminals.LineBridgeBarWays);
            }
        }

        return new TerminalBand(devices, BuildAccessories(terminals, devices.Count, lineBars), []);
    }

    /// One bank, so one neutral bar run and one set of stops; line bars only
    /// for the circuits that bridge.
    private static IReadOnlyList<AccessoryLine> BuildAccessories(TerminalRules terminals, int blocks, int lineBars)
    {
        if (blocks == 0) return [];

        var accessories = new List<AccessoryLine>();

        var bars = terminals.BridgeBarWays <= 0
            ? 0
            : (int)Math.Ceiling(blocks / (double)terminals.BridgeBarWays);

        if (bars > 0) accessories.Add(new AccessoryLine(terminals.BridgeBarDeviceTypeId, bars));
        if (lineBars > 0) accessories.Add(new AccessoryLine(terminals.LineBridgeBarDeviceTypeId, lineBars));
        if (terminals.EndStopsPerBank > 0)
        {
            accessories.Add(new AccessoryLine(terminals.EndStopDeviceTypeId, terminals.EndStopsPerBank));
        }

        return accessories;
    }
}
