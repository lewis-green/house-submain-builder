using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record DeviceDemand(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<Diagnostic> Diagnostics);

public static class DeviceDemandCalculator
{
    public static DeviceDemand Calculate(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var devices = new List<RequiredDevice>();
        var diagnostics = new List<Diagnostic>();

        Build(CircuitType.DimmedLighting, rules.PreferredDevice.Dimmer240, "Dimmer");
        Build(CircuitType.Switched, rules.PreferredDevice.Relay, "Relay");
        Build(CircuitType.LedTape, rules.PreferredDevice.Dimmer0_10V, "Tape Dimmer");
        Build(CircuitType.Cover, rules.PreferredDevice.Cover, "Cover");
        Build(CircuitType.RgbwTape, rules.PreferredDevice.LedController, "LED");

        return new DeviceDemand(devices, diagnostics);

        void Build(CircuitType type, Guid preferredId, string labelPrefix)
        {
            var ofType = circuits.Where(c => c.Type == type).OrderBy(c => c.Sequence).ToList();
            if (ofType.Count == 0) return;

            var deviceType = catalogue.FindActive(preferredId);
            if (deviceType is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for {type} circuits (preferred id {preferredId}).",
                    "Choose an active device in the ruleset, or re-activate the catalogue entry."));
                return;
            }

            if (deviceType.CircuitCapacity <= 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"{deviceType.Description} has no channels and cannot carry {type} circuits."));
                return;
            }

            var perDevice = deviceType.CircuitCapacity;
            var deviceCount = (int)Math.Ceiling(ofType.Count / (double)perDevice);

            for (var i = 0; i < deviceCount; i++)
            {
                var channels = new List<ChannelAssignment>(perDevice);
                for (var ch = 0; ch < perDevice; ch++)
                {
                    var circuitIndex = i * perDevice + ch;
                    channels.Add(circuitIndex < ofType.Count
                        ? new ChannelAssignment(ch, ofType[circuitIndex].Id, IsSpare: false)
                        : new ChannelAssignment(ch, null, IsSpare: true));
                }

                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    $"{labelPrefix} {i + 1}",
                    channels));
            }
        }
    }
}
