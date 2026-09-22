using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record TapeSupply(
    /// +24V and -24V distribution blocks, which do go on the rail.
    IReadOnlyList<RequiredDevice> Blocks,
    /// The driver itself, which does not. Costed on the BOM, never placed.
    IReadOnlyList<AccessoryLine> ExternalParts,
    IReadOnlyList<Diagnostic> Diagnostics);

/// LED drivers are not DIN mount: they sit outside the panel. What the panel
/// carries is a +24V and a -24V block for each tape circuit — they are the
/// joints where that tape's output is made off, so every run needs its own pair.
public static class TapeSupplySizer
{
    public static TapeSupply Size(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var diagnostics = new List<Diagnostic>();
        // Colour tape runs off the same 24V supply as plain tape: its controller
        // is fed from the driver and its output is made off at the same joints.
        var tape = circuits
            .Where(c => c.Type is CircuitType.LedTape or CircuitType.RgbwTape)
            .OrderBy(c => c.Sequence)
            .ToList();

        if (tape.Count == 0) return new TapeSupply([], [], []);

        foreach (var circuit in tape.Where(c => !c.HasTapeLoad))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.TapeLoadMissing,
                $"'{circuit.Name}' has no watts-per-metre and length, so it is excluded from driver sizing.",
                "Enter the tape wattage and run length."));
        }

        var blocks = BuildBlocks(tape.Count, rules, catalogue, diagnostics);
        var drivers = SizeDrivers(tape, rules, catalogue, diagnostics);

        return new TapeSupply(blocks, drivers, diagnostics);
    }

    /// Emitted as pairs — +1, -1, +2, -2 — so each run's two joints end up side
    /// by side on the rail rather than all the positives followed by all the
    /// negatives.
    private static List<RequiredDevice> BuildBlocks(
        int tapeCircuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue,
        List<Diagnostic> diagnostics)
    {
        var positive = Resolve(rules.PreferredDevice.Dc24VPositive, "+24V");
        var negative = Resolve(rules.PreferredDevice.Dc24VNegative, "-24V");

        var devices = new List<RequiredDevice>();

        for (var i = 0; i < tapeCircuits; i++)
        {
            var suffix = tapeCircuits == 1 ? "" : $" {i + 1}";
            if (positive is not null) devices.Add(Block(positive, $"+24V{suffix}"));
            if (negative is not null) devices.Add(Block(negative, $"-24V{suffix}"));
        }

        return devices;

        DeviceType? Resolve(Guid deviceTypeId, string what)
        {
            var deviceType = catalogue.FindActive(deviceTypeId);
            if (deviceType is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for the {what} block (id {deviceTypeId}).",
                    "Choose an active distribution block in the ruleset."));
            }

            return deviceType;
        }

        static RequiredDevice Block(DeviceType deviceType, string label) =>
            new(deviceType.Id, deviceType.Category, deviceType.ModuleWidth, label, []);
    }

    private static List<AccessoryLine> SizeDrivers(
        IReadOnlyList<Circuit> tape,
        RuleSetPayload rules,
        DeviceCatalogue catalogue,
        List<Diagnostic> diagnostics)
    {
        var totalWatts = tape.Sum(c => c.LoadWatts);
        if (totalWatts <= 0m) return [];

        var factor = rules.PsuDeratingFactor <= 0m ? 1m : rules.PsuDeratingFactor;
        var requiredWatts = totalWatts / factor;

        var candidates = rules.PreferredDevice.ExternalDriver
            .Select(catalogue.FindActive)
            .OfType<DeviceType>()
            .Where(d => d.MaxTotalLoadW is > 0)
            .OrderByDescending(d => d.MaxTotalLoadW)
            .ThenBy(d => d.PartNumber, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.PsuUnsized,
                $"{requiredWatts:0.#}W of 24V supply is needed but the ruleset lists no usable driver.",
                "Add at least one active 24V driver with a rated wattage to the ruleset."));
            return [];
        }

        var chosen = new Dictionary<Guid, int>();
        var remaining = requiredWatts;

        while (remaining > 0m)
        {
            var driver = candidates.LastOrDefault(d => d.MaxTotalLoadW >= remaining) ?? candidates[0];
            chosen[driver.Id] = chosen.GetValueOrDefault(driver.Id) + 1;
            remaining -= driver.MaxTotalLoadW!.Value;
        }

        return chosen
            .OrderBy(pair => catalogue.Find(pair.Key)!.PartNumber, StringComparer.Ordinal)
            .Select(pair => new AccessoryLine(pair.Key, pair.Value))
            .ToList();
    }
}
