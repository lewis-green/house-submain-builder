using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PanelPackerTests
{
    private static RequiredDevice Device(DeviceCategory category, int width, string label) =>
        new(Guid.NewGuid(), category, width, label, []);

    /// A realistic small panel: isolator, seven circuit terminals, a 24V pair,
    /// two dimmers and a relay.
    private static List<RequiredDevice> Panel()
    {
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Isolator, 6, "Isolator") };
        for (var n = 1; n <= 7; n++) devices.Add(Device(DeviceCategory.Terminal240, 1, $"C{n}"));
        devices.Add(Device(DeviceCategory.Dc24VPositive, 4, "+24V"));
        devices.Add(Device(DeviceCategory.Dc24VNegative, 4, "-24V"));
        devices.Add(Device(DeviceCategory.Dimmer240, 3, "Dimmer 1"));
        devices.Add(Device(DeviceCategory.Dimmer240, 3, "Dimmer 2"));
        devices.Add(Device(DeviceCategory.Relay, 9, "Relay 1"));
        return devices;
    }

    /// Termination takes one row, leaving one for three kinds of device, so the
    /// ladder has to fall to its last rung.
    private static EnclosureType TwoRows() =>
        new(new Guid("22222222-0000-0000-0000-000000000004"), "Test", "2x8", 2, 24, "IP30", 100m);

    private static EnclosureType OneRow() =>
        new(new Guid("22222222-0000-0000-0000-000000000005"), "Test", "1x8", 1, 24, "IP30", 100m);

    private static PackResult Packed() =>
        PanelPacker.Pack(Panel(), CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

    [Fact]
    public void The_24V_joints_stay_in_their_pairs_along_the_rail()
    {
        // +1, -1, +2, -2 — each run's two joints side by side, not all the
        // positives followed by all the negatives.
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Isolator, 6, "Isolator") };
        for (var n = 1; n <= 2; n++)
        {
            devices.Add(Device(DeviceCategory.Dc24VPositive, 1, $"+24V {n}"));
            devices.Add(Device(DeviceCategory.Dc24VNegative, 1, $"-24V {n}"));
        }

        var layout = PanelPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures()).Layout;

        Assert.Equal(
            ["+24V 1", "-24V 1", "+24V 2", "-24V 2"],
            layout.DevicesInRow(0).Where(d => d.Label.Contains("24V")).Select(d => d.Label));
    }

    [Fact]
    public void Terminals_are_on_the_top_row_starting_at_the_left()
    {
        var layout = Packed().Layout;

        var terminals = layout.Devices.Where(d => d.Category == DeviceCategory.Terminal240).ToList();

        Assert.All(terminals, d => Assert.Equal(0, d.RowIndex));
        Assert.Equal(0, terminals.Min(d => d.StartSlot));
    }

    [Fact]
    public void The_isolator_is_on_the_top_row_hard_against_the_right()
    {
        var layout = Packed().Layout;

        var isolator = layout.Devices.Single(d => d.Category == DeviceCategory.Isolator);

        Assert.Equal(0, isolator.RowIndex);
        Assert.Equal(layout.SlotsPerRow, isolator.EndSlotExclusive);
    }

    [Fact]
    public void The_24V_pair_is_on_the_top_row_straight_after_the_terminals()
    {
        var layout = Packed().Layout;

        var lastTerminal = layout.Devices
            .Where(d => d.Category == DeviceCategory.Terminal240)
            .Max(d => d.EndSlotExclusive);
        var positive = layout.Devices.Single(d => d.Category == DeviceCategory.Dc24VPositive);
        var negative = layout.Devices.Single(d => d.Category == DeviceCategory.Dc24VNegative);

        Assert.Equal(0, positive.RowIndex);
        Assert.Equal(0, negative.RowIndex);
        Assert.Equal(lastTerminal, positive.StartSlot);
        Assert.Equal(positive.EndSlotExclusive, negative.StartSlot);
    }

    [Fact]
    public void Every_shelly_device_is_below_the_termination_row()
    {
        var layout = Packed().Layout;

        var shelly = layout.Devices.Where(d =>
            d.Category is DeviceCategory.Dimmer240 or DeviceCategory.Dimmer0_10V or DeviceCategory.Relay);

        Assert.All(shelly, d => Assert.True(d.RowIndex >= 1, $"'{d.Label}' is on row {d.RowIndex}"));
    }

    [Fact]
    public void With_rows_to_spare_each_kind_of_device_gets_its_own()
    {
        // The finest layout in the ladder: dimmers, tape dimmers and relays apart.
        var layout = Packed().Layout;

        var dimmerRow = layout.Devices.First(d => d.Category == DeviceCategory.Dimmer240).RowIndex;
        var relayRow = layout.Devices.First(d => d.Category == DeviceCategory.Relay).RowIndex;

        Assert.NotEqual(dimmerRow, relayRow);
    }

    [Fact]
    public void Dimmers_fill_from_the_left_and_relays_from_the_right_once_they_share()
    {
        var layout = PanelPacker.Pack(Panel(), TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures()).Layout;

        var dimmer = layout.Devices.Single(d => d.Label == "Dimmer 1");
        var relay = layout.Devices.Single(d => d.Label == "Relay 1");

        Assert.Equal(dimmer.RowIndex, relay.RowIndex);
        Assert.Equal(0, dimmer.StartSlot);
        Assert.Equal(layout.SlotsPerRow, relay.EndSlotExclusive);
    }

    [Fact]
    public void The_ladder_prefers_separation_over_leaving_a_row_spare()
    {
        var roomy = Packed().Layout;
        var cramped = PanelPacker.Pack(Panel(), TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures()).Layout;

        var roomyRows = roomy.Devices.Select(d => d.RowIndex).Distinct().Count();
        var crampedRows = cramped.Devices.Select(d => d.RowIndex).Distinct().Count();

        // A spare row is worth more as separation than as spare.
        Assert.True(roomyRows > crampedRows,
            $"roomy used {roomyRows} rows, cramped used {crampedRows}");
    }

    [Fact]
    public void A_zone_always_starts_on_a_fresh_row()
    {
        // Even with acres of room on the termination row, no Shelly device joins it.
        var layout = Packed().Layout;

        Assert.DoesNotContain(layout.Devices.Where(d => d.RowIndex == 0),
            d => d.Category is DeviceCategory.Dimmer240 or DeviceCategory.Relay);
    }

    [Fact]
    public void Devices_never_overlap_even_when_packed_from_both_ends()
    {
        // Packing toward each other from two ends is exactly where an off-by-one
        // puts two devices in the same space, and the database's unique
        // (SubmainId, RowIndex, StartSlot) index would only catch some of those:
        // two devices can overlap without sharing a start slot.
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Isolator, 6, "Isolator") };
        for (var n = 1; n <= 30; n++) devices.Add(Device(DeviceCategory.Terminal240, 1, $"C{n}"));
        devices.Add(Device(DeviceCategory.Dc24VPositive, 4, "+24V"));
        devices.Add(Device(DeviceCategory.Dc24VNegative, 4, "-24V"));
        for (var n = 1; n <= 6; n++) devices.Add(Device(DeviceCategory.Dimmer240, 3, $"Dimmer {n}"));
        for (var n = 1; n <= 4; n++) devices.Add(Device(DeviceCategory.Relay, 9, $"Relay {n}"));

        var result = PanelPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        foreach (var row in result.Layout.Devices.GroupBy(d => d.RowIndex))
        {
            var ordered = row.OrderBy(d => d.StartSlot).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.True(
                    ordered[i].StartSlot >= ordered[i - 1].EndSlotExclusive,
                    $"'{ordered[i].Label}' at {ordered[i].StartSlot} overlaps " +
                    $"'{ordered[i - 1].Label}' ending at {ordered[i - 1].EndSlotExclusive} on row {row.Key}");
            }
        }
    }

    [Fact]
    public void Nothing_is_ever_placed_outside_the_row()
    {
        var devices = new List<RequiredDevice>();
        for (var n = 1; n <= 40; n++) devices.Add(Device(DeviceCategory.Terminal240, 1, $"C{n}"));
        for (var n = 1; n <= 6; n++) devices.Add(Device(DeviceCategory.Relay, 9, $"Relay {n}"));

        var result = PanelPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.All(result.Layout.Devices, d =>
        {
            Assert.True(d.StartSlot >= 0, $"'{d.Label}' starts at {d.StartSlot}");
            Assert.True(d.EndSlotExclusive <= result.Layout.SlotsPerRow,
                $"'{d.Label}' ends at {d.EndSlotExclusive}, past {result.Layout.SlotsPerRow}");
        });
    }

    [Fact]
    public void Terminals_spill_onto_a_second_row_without_disturbing_the_isolator()
    {
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Isolator, 6, "Isolator") };
        for (var n = 1; n <= 30; n++) devices.Add(Device(DeviceCategory.Terminal240, 1, $"C{n}"));

        var result = PanelPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var isolator = result.Layout.Devices.Single(d => d.Category == DeviceCategory.Isolator);
        Assert.Equal(0, isolator.RowIndex);
        Assert.Equal(result.Layout.SlotsPerRow, isolator.EndSlotExclusive);

        Assert.Contains(result.Layout.Devices, d => d.Category == DeviceCategory.Terminal240 && d.RowIndex == 1);
    }

    [Fact]
    public void Two_kinds_sharing_a_row_each_start_from_an_end()
    {
        // Never one type running straight on from another: each works inwards
        // from its own end of the rail.
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 3, "Dimmer 1"),
            Device(DeviceCategory.Dimmer0_10V, 3, "Tape Dimmer 1"),
        };

        // Two rows: one for termination, one shared by the two sorts of dimmer.
        var layout = PanelPacker.Pack(devices, TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures()).Layout;

        var mains = layout.Devices.Single(d => d.Category == DeviceCategory.Dimmer240);
        var tape = layout.Devices.Single(d => d.Category == DeviceCategory.Dimmer0_10V);

        Assert.Equal(mains.RowIndex, tape.RowIndex);
        Assert.Equal(0, mains.StartSlot);
        Assert.Equal(layout.SlotsPerRow, tape.EndSlotExclusive);
    }

    [Fact]
    public void Tape_dimmers_join_the_mains_dimmers_only_when_rows_run_short()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 3, "Dimmer 1"),
            Device(DeviceCategory.Dimmer0_10V, 3, "Tape Dimmer 1"),
        };

        var roomy = PanelPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures()).Layout;
        var cramped = PanelPacker.Pack(devices, OneRow(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures()).Layout;

        Assert.Equal(2, roomy.Devices.Select(d => d.RowIndex).Distinct().Count());
        Assert.Equal(1, cramped.Devices.Select(d => d.RowIndex).Distinct().Count());
    }

    [Fact]
    public void Overflowing_the_enclosure_raises_an_error_and_suggests_a_bigger_one()
    {
        var devices = Enumerable.Range(1, 12)
            .Select(n => Device(DeviceCategory.Relay, 9, $"Relay {n}"))
            .ToList();

        var result = PanelPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var diagnostic = result.Diagnostics.Single(d => d.Code == DiagnosticCodes.EnclosureTooSmall);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("modules", diagnostic.Message);
        Assert.DoesNotContain("slots", diagnostic.Message);
    }

    [Fact]
    public void A_device_wider_than_a_row_is_reported_and_skipped()
    {
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Relay, 30, "Huge") };

        var result = PanelPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Empty(result.Layout.Devices);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DeviceWiderThanRow);
    }

    [Fact]
    public void A_category_no_zone_mentions_is_still_placed_rather_than_dropped()
    {
        var rules = CatalogueFixture.Rules();
        var withoutRelays = rules with
        {
            Layouts = [new PanelLayoutOption(
                [new PackingZone([DeviceCategory.Terminal240], [DeviceCategory.Isolator])])],
        };

        var result = PanelPacker.Pack(
            [Device(DeviceCategory.Relay, 9, "Relay 1")],
            CatalogueFixture.LargeEnclosure(), withoutRelays, CatalogueFixture.AllEnclosures());

        Assert.Single(result.Layout.Devices);
    }
}
