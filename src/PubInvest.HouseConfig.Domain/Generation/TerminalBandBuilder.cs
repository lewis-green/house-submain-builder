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

public static class TerminalBandBuilder
{
    public static TerminalBand Build(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var devices = new List<RequiredDevice>();
        var diagnostics = new List<Diagnostic>();
        var bridgedBankBlocks = new List<int>();

        AddBank(TerminalRole.Line, rules.Terminals.Line, "L");
        AddBank(TerminalRole.Neutral, rules.Terminals.Neutral, "N");
        AddBank(TerminalRole.Earth, rules.Terminals.Earth, "E");

        var accessories = BuildAccessories(rules.Terminals, bridgedBankBlocks);

        return new TerminalBand(devices, accessories, diagnostics);

        void AddBank(TerminalRole role, TerminalConductorRule rule, string labelPrefix)
        {
            var deviceType = catalogue.FindActive(rule.DeviceTypeId);
            if (deviceType is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for the {role} terminal bank (id {rule.DeviceTypeId}).",
                    "Point the ruleset's terminal rules at an active catalogue entry."));
                return;
            }

            // + 1 block per conductor for the incoming twin-and-earth.
            var blocks = circuits.Count * Math.Max(rule.BlocksPerCircuit, 1) + 1;

            for (var i = 0; i < blocks; i++)
            {
                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    $"{labelPrefix}{i + 1}",
                    [],
                    role));
            }

            if (rule.Bridged) bridgedBankBlocks.Add(blocks);
        }
    }

    private static IReadOnlyList<AccessoryLine> BuildAccessories(
        TerminalRules terminals,
        IReadOnlyList<int> bridgedBankBlocks)
    {
        if (bridgedBankBlocks.Count == 0) return [];

        var bars = terminals.BridgeBarWays <= 0
            ? 0
            : bridgedBankBlocks.Sum(b => (int)Math.Ceiling(b / (double)terminals.BridgeBarWays));

        var endStops = bridgedBankBlocks.Count * terminals.EndStopsPerBank;

        var accessories = new List<AccessoryLine>();
        if (bars > 0) accessories.Add(new AccessoryLine(terminals.BridgeBarDeviceTypeId, bars));
        if (endStops > 0) accessories.Add(new AccessoryLine(terminals.EndStopDeviceTypeId, endStops));
        return accessories;
    }
}
