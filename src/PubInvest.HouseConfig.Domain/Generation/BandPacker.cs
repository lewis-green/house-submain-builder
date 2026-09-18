using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record PackResult(PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics);

/// Packs devices into rows, preferring one band per row.
///
/// A banded layout reads better — you can see at a glance what is termination,
/// what is control — but it costs a whole row whenever a band is small. So: pack
/// banded; if that fits, keep it. If it does not, re-pack merged, with dimmers
/// running from the left and relays from the right, and say so in a diagnostic
/// rather than leaving the engineer wondering why the drawing looks dense.
public static class BandPacker
{
    /// Categories packed from the right-hand end of a shared row.
    private static readonly HashSet<DeviceCategory> FromRight =
    [
        DeviceCategory.Relay,
        DeviceCategory.Dc24VPositive,
        DeviceCategory.Dc24VNegative,
    ];

    public static PackResult Pack(
        IReadOnlyList<RequiredDevice> devices,
        EnclosureType enclosure,
        RuleSetPayload rules,
        IReadOnlyList<EnclosureType> allEnclosures)
    {
        var diagnostics = new List<Diagnostic>();
        var placeable = devices.Where(d => d.ModuleWidth > 0).ToList();

        foreach (var tooWide in placeable.Where(d => d.ModuleWidth > enclosure.SlotsPerRow))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.DeviceWiderThanRow,
                $"'{tooWide.Label}' is {DinUnits.ToModules(tooWide.ModuleWidth):0.#} modules wide " +
                $"but a row holds {DinUnits.ToModules(enclosure.SlotsPerRow):0.#}.",
                "Choose a wider enclosure or a narrower device."));
        }

        var fits = placeable.Where(d => d.ModuleWidth <= enclosure.SlotsPerRow).ToList();

        var banded = PackBanded(fits, enclosure.SlotsPerRow, rules);
        var placed = banded;
        var merged = false;

        var mustMerge = rules.Packing == "dense" || RowsUsed(banded) > enclosure.Rows;

        if (mustMerge)
        {
            var dense = PackMerged(fits, enclosure.SlotsPerRow, enclosure.Rows, rules);
            if (rules.Packing == "dense" || RowsUsed(dense) < RowsUsed(banded))
            {
                placed = dense;
                merged = true;
            }
        }

        var rowsUsed = RowsUsed(placed);

        if (merged && rowsUsed <= enclosure.Rows)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Info,
                DiagnosticCodes.BandsMerged,
                "Some rows carry more than one kind of device, packed from both ends, " +
                "because a row each would not fit this enclosure.",
                "Use a larger enclosure if you would rather keep one kind per row."));
        }

        if (rowsUsed > enclosure.Rows)
        {
            var suggestion = SuggestEnclosure(fits, rules, allEnclosures, enclosure);
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.EnclosureTooSmall,
                $"This design needs {rowsUsed} rows of {DinUnits.ToModules(enclosure.SlotsPerRow):0.#} modules; " +
                $"{enclosure.Description} has {enclosure.Rows}.",
                suggestion is null
                    ? "No catalogue enclosure is large enough; split the submain."
                    : $"Use {suggestion.Description} " +
                      $"({suggestion.Rows} rows of {DinUnits.ToModules(suggestion.SlotsPerRow):0.#} modules)."));
        }

        return new PackResult(new PanelLayout(enclosure.Rows, enclosure.SlotsPerRow, placed), diagnostics);
    }

    /// One band per row: each band starts on a fresh rail.
    private static List<PlacedDevice> PackBanded(
        IReadOnlyList<RequiredDevice> devices,
        int slotsPerRow,
        RuleSetPayload rules)
    {
        var placed = new List<PlacedDevice>();
        var row = 0;
        var slot = 0;

        foreach (var band in BandsInOrder(devices, rules))
        {
            var inBand = devices.Where(d => BandOf(d.Category) == band).ToList();
            if (inBand.Count == 0) continue;

            if (slot > 0)
            {
                row++;
                slot = 0;
            }

            foreach (var device in inBand)
            {
                if (slot + device.ModuleWidth > slotsPerRow)
                {
                    row++;
                    slot = 0;
                }

                placed.Add(Place(device, row, slot));
                slot += device.ModuleWidth;
            }
        }

        return placed;
    }

    /// Shared rows, but still one band per row for as long as rows remain: only
    /// the bands that will not fit that way get merged into rows already in use.
    /// Left-packed categories grow rightwards, right-packed ones grow leftwards,
    /// and a row is full when the two fronts would meet.
    private static List<PlacedDevice> PackMerged(
        IReadOnlyList<RequiredDevice> devices,
        int slotsPerRow,
        int maxRows,
        RuleSetPayload rules)
    {
        var placed = new List<PlacedDevice>();
        var left = new List<int>();   // next free slot from the left, per row
        var right = new List<int>();  // next free boundary from the right, per row
        var used = new List<bool>();  // has any band started on this row?

        foreach (var band in BandsInOrder(devices, rules))
        {
            var inBand = devices.Where(d => BandOf(d.Category) == band).ToList();
            if (inBand.Count == 0) continue;

            // Take a fresh row if one is still going spare, so merging is the
            // exception rather than the habit.
            var startRow = used.Count(u => u) < maxRows ? FirstUnusedRow() : 0;

            foreach (var device in inBand)
            {
                var row = startRow;

                while (true)
                {
                    EnsureRow(row);
                    if (right[row] - left[row] >= device.ModuleWidth) break;
                    row++;
                }

                EnsureRow(row);
                used[row] = true;

                if (FromRight.Contains(device.Category))
                {
                    var start = right[row] - device.ModuleWidth;
                    placed.Add(Place(device, row, start));
                    right[row] = start;
                }
                else
                {
                    placed.Add(Place(device, row, left[row]));
                    left[row] += device.ModuleWidth;
                }
            }
        }

        return placed;

        void EnsureRow(int row)
        {
            while (row >= left.Count)
            {
                left.Add(0);
                right.Add(slotsPerRow);
                used.Add(false);
            }
        }

        int FirstUnusedRow()
        {
            for (var row = 0; row < used.Count; row++)
            {
                if (!used[row]) return row;
            }

            return used.Count;
        }
    }

    private static PlacedDevice Place(RequiredDevice device, int row, int slot) => new(
        device.DeviceTypeId,
        device.Category,
        row,
        slot,
        device.ModuleWidth,
        device.Label,
        device.Channels,
        device.TerminalRole);

    private static int RowsUsed(List<PlacedDevice> placed) =>
        placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;

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
                if (devices.Any(d => d.ModuleWidth > e.SlotsPerRow)) return false;
                if (RowsUsed(PackBanded(devices, e.SlotsPerRow, rules)) <= e.Rows) return true;
                return RowsUsed(PackMerged(devices, e.SlotsPerRow, e.Rows, rules)) <= e.Rows;
            });

    /// Tape dimmers share the mains dimmer band, and the -24V block sits with
    /// the +24V one: they are a pair and belong next to each other.
    private static DeviceCategory BandOf(DeviceCategory category) => category switch
    {
        DeviceCategory.Dimmer0_10V => DeviceCategory.Dimmer240,
        DeviceCategory.Dc24VNegative => DeviceCategory.Dc24VPositive,
        _ => category,
    };

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
