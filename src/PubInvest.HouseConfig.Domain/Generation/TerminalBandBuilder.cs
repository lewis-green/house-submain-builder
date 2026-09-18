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

/// One 3-tier terminal block per circuit.
///
/// The WAGO 2003-7646 carries line, neutral and earth on a single slice, so a
/// circuit needs one block rather than one in each of three banks. The neutral
/// tier is commoned with a jumper bar; earth commons through the DIN rail and
/// needs no bar at all; line is per-circuit and loops out to its Shelly channel.
///
/// The incoming feed lands on the isolator, so no block is spent on it.
public static class TerminalBandBuilder
{
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

        var perCircuit = Math.Max(terminals.BlocksPerCircuit, 1);
        var ordered = circuits.OrderBy(c => c.Sequence).ToList();
        var devices = new List<RequiredDevice>();

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var n = 0; n < perCircuit; n++)
            {
                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    // Labelled for the circuit it serves, so the drawing and the
                    // schedule name the same thing.
                    perCircuit == 1 ? $"C{i + 1}" : $"C{i + 1}.{n + 1}",
                    [],
                    TerminalRole.All));
            }
        }

        return new TerminalBand(devices, BuildAccessories(terminals, devices.Count), []);
    }

    /// One bank, so one set of bars and stops — for the neutral tier only.
    private static IReadOnlyList<AccessoryLine> BuildAccessories(TerminalRules terminals, int blocks)
    {
        if (blocks == 0) return [];

        var accessories = new List<AccessoryLine>();

        var bars = terminals.BridgeBarWays <= 0
            ? 0
            : (int)Math.Ceiling(blocks / (double)terminals.BridgeBarWays);

        if (bars > 0) accessories.Add(new AccessoryLine(terminals.BridgeBarDeviceTypeId, bars));
        if (terminals.EndStopsPerBank > 0)
        {
            accessories.Add(new AccessoryLine(terminals.EndStopDeviceTypeId, terminals.EndStopsPerBank));
        }

        return accessories;
    }
}
