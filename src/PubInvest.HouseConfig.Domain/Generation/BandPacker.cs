using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record PackResult(PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics);

public static class BandPacker
{
    public static PackResult Pack(
        IReadOnlyList<RequiredDevice> devices,
        EnclosureType enclosure,
        RuleSetPayload rules,
        IReadOnlyList<EnclosureType> allEnclosures)
    {
        var diagnostics = new List<Diagnostic>();
        var placed = Place(devices, enclosure.SlotsPerRow, rules, diagnostics);
        var rowsUsed = placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;

        if (rowsUsed > enclosure.Rows)
        {
            var suggestion = SuggestEnclosure(devices, rules, allEnclosures, enclosure);
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.EnclosureTooSmall,
                // Slot units are an internal unit; a person reads DIN modules.
                $"This design needs {rowsUsed} rows of {DinUnits.ToModules(enclosure.SlotsPerRow):0.#} modules; " +
                $"{enclosure.Description} has {enclosure.Rows}.",
                suggestion is null
                    ? "No catalogue enclosure is large enough; split the submain."
                    : $"Use {suggestion.Description} " +
                      $"({suggestion.Rows} rows of {DinUnits.ToModules(suggestion.SlotsPerRow):0.#} modules)."));
        }

        return new PackResult(new PanelLayout(enclosure.Rows, enclosure.SlotsPerRow, placed), diagnostics);
    }

    private static List<PlacedDevice> Place(
        IReadOnlyList<RequiredDevice> devices,
        int slotsPerRow,
        RuleSetPayload rules,
        List<Diagnostic>? diagnostics)
    {
        var placed = new List<PlacedDevice>();
        var row = 0;
        var slot = 0;

        foreach (var band in BandsInOrder(devices, rules))
        {
            var inBand = devices.Where(d => BandOf(d.Category) == band).ToList();
            if (inBand.Count == 0) continue;

            if (rules.BandStartsNewRow && slot > 0)
            {
                row++;
                slot = 0;
            }

            foreach (var device in inBand)
            {
                if (device.ModuleWidth <= 0) continue;

                if (device.ModuleWidth > slotsPerRow)
                {
                    diagnostics?.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.DeviceWiderThanRow,
                        $"'{device.Label}' is {DinUnits.ToModules(device.ModuleWidth):0.#} modules wide " +
                        $"but a row holds {DinUnits.ToModules(slotsPerRow):0.#}.",
                        "Choose a wider enclosure or a narrower device."));
                    continue;
                }

                if (slot + device.ModuleWidth > slotsPerRow)
                {
                    row++;
                    slot = 0;
                }

                placed.Add(new PlacedDevice(
                    device.DeviceTypeId,
                    device.Category,
                    row,
                    slot,
                    device.ModuleWidth,
                    device.Label,
                    device.Channels,
                    device.TerminalRole));

                slot += device.ModuleWidth;
            }
        }

        return placed;
    }

    private static EnclosureType? SuggestEnclosure(
        IReadOnlyList<RequiredDevice> devices,
        RuleSetPayload rules,
        IReadOnlyList<EnclosureType> allEnclosures,
        EnclosureType current)
        => allEnclosures
            .Where(e => e.Id != current.Id)
            .OrderBy(e => e.TotalSlots)
            .ThenBy(e => e.Description, StringComparer.Ordinal)
            .FirstOrDefault(e =>
            {
                var trial = Place(devices, e.SlotsPerRow, rules, diagnostics: null);
                var rows = trial.Count == 0 ? 0 : trial.Max(d => d.RowIndex) + 1;
                return trial.Count == devices.Count(d => d.ModuleWidth > 0) && rows <= e.Rows;
            });

    private static DeviceCategory BandOf(DeviceCategory category)
        => category == DeviceCategory.Dimmer0_10V ? DeviceCategory.Dimmer240 : category;

    private static IEnumerable<DeviceCategory> BandsInOrder(
        IReadOnlyList<RequiredDevice> devices,
        RuleSetPayload rules)
    {
        var seen = new HashSet<DeviceCategory>();
        foreach (var band in rules.BandOrder)
        {
            if (seen.Add(BandOf(band))) yield return BandOf(band);
        }

        foreach (var device in devices)
        {
            var band = BandOf(device.Category);
            if (seen.Add(band)) yield return band;
        }
    }
}
