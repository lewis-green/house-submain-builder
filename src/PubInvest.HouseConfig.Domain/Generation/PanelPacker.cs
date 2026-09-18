using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record PackResult(PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics);

/// Lays a panel out in zones, top to bottom.
///
/// The ruleset offers several layouts, finest first, and the packer takes the
/// first that fits the enclosure: a row for every kind of device if the rows are
/// there, then the two sorts of dimmer together, then the relays sharing that row
/// from the other end. A spare row is worth more as separation than as spare.
///
/// Every zone starts on a fresh row, so the Shelly gear never shares a rail with
/// the terminations. Within a zone the two ends grow toward each other and a row
/// is full when they would meet.
public static class PanelPacker
{
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
        var placed = BestFitting(fits, enclosure, rules);
        var rowsUsed = placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;

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

    /// The finest layout that fits, or the densest one if none of them do —
    /// in which case Pack reports the overflow against the best attempt.
    private static List<PlacedDevice> BestFitting(
        IReadOnlyList<RequiredDevice> devices,
        EnclosureType enclosure,
        RuleSetPayload rules)
    {
        List<PlacedDevice>? densest = null;

        foreach (var option in rules.Layouts)
        {
            var placed = Place(devices, enclosure.SlotsPerRow, option.Zones, devices);
            var rows = placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;

            if (rows <= enclosure.Rows) return placed;
            densest = placed;
        }

        return densest ?? [];
    }

    private static List<PlacedDevice> Place(
        IReadOnlyList<RequiredDevice> devices,
        int slotsPerRow,
        IReadOnlyList<PackingZone> zones,
        IReadOnlyList<RequiredDevice> all)
    {
        var placed = new List<PlacedDevice>();
        var nextZoneRow = 0;

        foreach (var zone in ZonesInOrder(all, zones))
        {
            var rows = new Rows(nextZoneRow, slotsPerRow);

            // Right-hand devices first, so the isolator is guaranteed the top-right
            // of its zone even when the terminals run onto a second row.
            foreach (var device in InOrder(devices, zone.FromRight))
            {
                var row = rows.FirstWithRoom(device.ModuleWidth);
                placed.Add(Place(device, row, rows.TakeFromRight(row, device.ModuleWidth)));
            }

            foreach (var device in InOrder(devices, zone.FromLeft))
            {
                var row = rows.FirstWithRoom(device.ModuleWidth);
                placed.Add(Place(device, row, rows.TakeFromLeft(row, device.ModuleWidth)));
            }

            nextZoneRow = rows.NextFreeRow;
        }

        return placed;
    }

    /// Devices belonging to a zone, kept in the order the generator produced
    /// them. The zone's category list says which devices belong to which end,
    /// not what order they go in — that way the 24V joints stay in the pairs the
    /// sizer emitted (+1, -1, +2, -2) instead of being regrouped into all the
    /// positives followed by all the negatives.
    private static IEnumerable<RequiredDevice> InOrder(
        IReadOnlyList<RequiredDevice> devices,
        IReadOnlyList<DeviceCategory> categories)
    {
        var wanted = categories.ToHashSet();
        return devices.Where(d => wanted.Contains(d.Category));
    }

    private static IEnumerable<PackingZone> ZonesInOrder(
        IReadOnlyList<RequiredDevice> devices,
        IReadOnlyList<PackingZone> zones)
    {
        foreach (var zone in zones) yield return zone;

        // Anything the ruleset forgot still has to go somewhere, rather than
        // silently vanishing from the drawing.
        var placedCategories = zones
            .SelectMany(z => z.FromLeft.Concat(z.FromRight))
            .ToHashSet();

        var strays = devices
            .Select(d => d.Category)
            .Distinct()
            .Where(c => !placedCategories.Contains(c))
            .ToList();

        if (strays.Count > 0) yield return new PackingZone(strays, []);
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

    /// The left and right fronts of each row in a zone.
    private sealed class Rows(int firstRow, int slotsPerRow)
    {
        private readonly List<int> _left = [];
        private readonly List<int> _right = [];

        public int NextFreeRow => firstRow + Math.Max(_left.Count, 1);

        public int FirstWithRoom(int width)
        {
            for (var i = 0; ; i++)
            {
                Ensure(i);
                if (_right[i] - _left[i] >= width) return firstRow + i;
            }
        }

        public int TakeFromLeft(int row, int width)
        {
            var i = row - firstRow;
            var start = _left[i];
            _left[i] += width;
            return start;
        }

        public int TakeFromRight(int row, int width)
        {
            var i = row - firstRow;
            var start = _right[i] - width;
            _right[i] = start;
            return start;
        }

        private void Ensure(int index)
        {
            while (index >= _left.Count)
            {
                _left.Add(0);
                _right.Add(slotsPerRow);
            }
        }
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
                if (devices.Any(d => d.ModuleWidth > e.SlotsPerRow)) return false;

                return rules.Layouts.Any(option =>
                {
                    var trial = Place(devices, e.SlotsPerRow, option.Zones, devices);
                    var rows = trial.Count == 0 ? 0 : trial.Max(d => d.RowIndex) + 1;
                    return rows <= e.Rows;
                });
            });
}
