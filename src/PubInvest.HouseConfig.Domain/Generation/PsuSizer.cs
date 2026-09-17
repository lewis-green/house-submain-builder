using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record PsuSizing(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<Diagnostic> Diagnostics);

public static class PsuSizer
{
    public static PsuSizing Size(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var diagnostics = new List<Diagnostic>();
        var tape = circuits.Where(c => c.Type == CircuitType.LedTape).OrderBy(c => c.Sequence).ToList();
        if (tape.Count == 0) return new PsuSizing([], diagnostics);

        foreach (var circuit in tape.Where(c => !c.HasTapeLoad))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.TapeLoadMissing,
                $"'{circuit.Name}' has no watts-per-metre and length, so it is excluded from PSU sizing.",
                "Enter the tape wattage and run length."));
        }

        var totalWatts = tape.Sum(c => c.LoadWatts);
        if (totalWatts <= 0m) return new PsuSizing([], diagnostics);

        var factor = rules.PsuDeratingFactor <= 0m ? 1m : rules.PsuDeratingFactor;
        var requiredWatts = totalWatts / factor;

        var candidates = rules.PreferredDevice.Psu24V
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
                $"{requiredWatts:0.#}W of 24V supply is needed but the ruleset lists no usable PSU.",
                "Add at least one active 24V PSU with a rated wattage to the ruleset."));
            return new PsuSizing([], diagnostics);
        }

        var devices = new List<RequiredDevice>();
        var remaining = requiredWatts;

        while (remaining > 0m)
        {
            // Prefer the smallest unit that finishes the job; otherwise take the largest.
            var chosen = candidates.LastOrDefault(d => d.MaxTotalLoadW >= remaining) ?? candidates[0];
            devices.Add(new RequiredDevice(
                chosen.Id,
                chosen.Category,
                chosen.ModuleWidth,
                $"PSU {devices.Count + 1}",
                []));
            remaining -= chosen.MaxTotalLoadW!.Value;
        }

        return new PsuSizing(devices, diagnostics);
    }
}
