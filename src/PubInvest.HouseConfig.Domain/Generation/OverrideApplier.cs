using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

public static class OverrideApplier
{
    public static (PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics) Apply(
        PanelLayout layout,
        IReadOnlyList<PositionOverride> overrides)
    {
        if (overrides.Count == 0) return (layout, []);

        var devices = layout.Devices.ToList();
        var diagnostics = new List<Diagnostic>();

        // Stable order so a set of overrides always resolves the same way.
        var pending = overrides
            .OrderBy(o => o.RowIndex)
            .ThenBy(o => o.StartSlot)
            .ThenBy(o => o.Label, StringComparer.Ordinal)
            .ToList();

        foreach (var missing in pending.Where(o => devices.All(d => d.Label != o.Label)).ToList())
        {
            diagnostics.Add(Dropped(missing, "it is not in this design any more"));
            pending.Remove(missing);
        }

        // Repeat until a pass moves nothing. One override can be blocked only by a
        // device that is itself about to move, so a later pass frees it; without
        // this, two devices swapping places would both be dropped.
        bool movedSomething;
        do
        {
            movedSomething = false;

            foreach (var request in pending.ToList())
            {
                var index = devices.FindIndex(d => d.Label == request.Label);
                var device = devices[index];

                if (!Fits(request, device, layout, devices)) continue;

                devices[index] = device with { RowIndex = request.RowIndex, StartSlot = request.StartSlot };
                pending.Remove(request);
                movedSomething = true;
            }
        }
        while (movedSomething && pending.Count > 0);

        foreach (var stuck in pending)
        {
            var device = devices.Single(d => d.Label == stuck.Label);
            diagnostics.Add(Dropped(stuck, WhyNot(stuck, device, layout)));
        }

        return (layout with { Devices = devices }, diagnostics);
    }

    private static bool Fits(
        PositionOverride request,
        PlacedDevice device,
        PanelLayout layout,
        IReadOnlyList<PlacedDevice> devices)
    {
        var end = request.StartSlot + device.ModuleWidth;

        if (request.RowIndex < 0 || request.RowIndex >= layout.Rows) return false;
        if (request.StartSlot < 0 || end > layout.SlotsPerRow) return false;

        return !devices.Any(other =>
            !ReferenceEquals(other, device)
            && other.RowIndex == request.RowIndex
            && other.StartSlot < end
            && request.StartSlot < other.EndSlotExclusive);
    }

    private static string WhyNot(PositionOverride request, PlacedDevice device, PanelLayout layout)
    {
        if (request.RowIndex < 0 || request.RowIndex >= layout.Rows)
            return $"row {request.RowIndex + 1} does not exist";

        if (request.StartSlot < 0 || request.StartSlot + device.ModuleWidth > layout.SlotsPerRow)
            return "it does not fit in that row";

        return "another device is already there";
    }

    private static Diagnostic Dropped(PositionOverride request, string why) => new(
        DiagnosticSeverity.Warning,
        DiagnosticCodes.PositionOverrideDropped,
        $"'{request.Label}' could not be kept where you put it because {why}; it has been placed automatically.",
        "Drag it again if the new position is not what you want.");
}
