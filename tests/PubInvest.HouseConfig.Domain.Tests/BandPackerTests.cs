using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class BandPackerTests
{
    private static RequiredDevice Device(DeviceCategory category, int width, string label) =>
        new(Guid.NewGuid(), category, width, label, []);

    /// Three small bands: one terminal, one dimmer, one relay.
    private static List<RequiredDevice> SmallBands() =>
    [
        Device(DeviceCategory.Terminal240, 1, "C1"),
        Device(DeviceCategory.Dimmer240, 3, "Dimmer 1"),
        Device(DeviceCategory.Relay, 9, "Relay 1"),
    ];

    private static EnclosureType TwoRows() =>
        new(new Guid("22222222-0000-0000-0000-000000000003"), "Test", "2x12", 2, 36, "IP30", 100m);

    [Fact]
    public void Bands_are_laid_out_in_ruleset_order()
    {
        var result = BandPacker.Pack(SmallBands(), CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal(0, result.Layout.Devices.Single(d => d.Label == "C1").RowIndex);
        Assert.Equal(1, result.Layout.Devices.Single(d => d.Label == "Dimmer 1").RowIndex);
        Assert.Equal(2, result.Layout.Devices.Single(d => d.Label == "Relay 1").RowIndex);
    }

    [Fact]
    public void A_design_that_fits_banded_keeps_one_band_per_row()
    {
        var result = BandPacker.Pack(SmallBands(), CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal(3, result.Layout.Devices.Select(d => d.RowIndex).Distinct().Count());
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BandsMerged);
    }

    [Fact]
    public void A_design_that_will_not_fit_banded_merges_rather_than_failing()
    {
        // Banded needs three rows; this enclosure has two.
        var result = BandPacker.Pack(SmallBands(), TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BandsMerged);
    }

    [Fact]
    public void Merged_rows_put_relays_at_the_right_hand_end()
    {
        var result = BandPacker.Pack(SmallBands(), TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var relay = result.Layout.Devices.Single(d => d.Label == "Relay 1");
        Assert.Equal(result.Layout.SlotsPerRow, relay.EndSlotExclusive);
    }

    [Fact]
    public void Merged_rows_put_dimmers_at_the_left_hand_end()
    {
        var result = BandPacker.Pack(SmallBands(), TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var dimmer = result.Layout.Devices.Single(d => d.Label == "Dimmer 1");
        var relay = result.Layout.Devices.Single(d => d.Label == "Relay 1");

        Assert.True(dimmer.StartSlot < relay.StartSlot);
    }

    [Fact]
    public void Devices_never_overlap_even_when_packed_from_both_ends()
    {
        // Packing toward each other from two ends is exactly where an off-by-one
        // puts two devices in the same space, and the database's unique
        // (SubmainId, RowIndex, StartSlot) index would only catch some of those:
        // two devices can overlap without sharing a start slot.
        var devices = new List<RequiredDevice>();
        for (var n = 1; n <= 12; n++) devices.Add(Device(DeviceCategory.Terminal240, 1, $"C{n}"));
        for (var n = 1; n <= 5; n++) devices.Add(Device(DeviceCategory.Dimmer240, 3, $"Dimmer {n}"));
        for (var n = 1; n <= 3; n++) devices.Add(Device(DeviceCategory.Relay, 9, $"Relay {n}"));
        devices.Add(Device(DeviceCategory.Dc24VPositive, 4, "+24V"));
        devices.Add(Device(DeviceCategory.Dc24VNegative, 4, "-24V"));

        var result = BandPacker.Pack(devices, TwoRows(),
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
        var devices = Enumerable.Range(1, 9)
            .Select(n => Device(DeviceCategory.Relay, 9, $"Relay {n}"))
            .ToList();

        var result = BandPacker.Pack(devices, TwoRows(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.All(result.Layout.Devices, d =>
        {
            Assert.True(d.StartSlot >= 0, $"'{d.Label}' starts at {d.StartSlot}");
            Assert.True(d.EndSlotExclusive <= result.Layout.SlotsPerRow,
                $"'{d.Label}' ends at {d.EndSlotExclusive}, past {result.Layout.SlotsPerRow}");
        });
    }

    [Fact]
    public void Devices_in_a_band_are_placed_left_to_right_without_gaps()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 3, "Dimmer 1"),
            Device(DeviceCategory.Dimmer240, 3, "Dimmer 2"),
            Device(DeviceCategory.Dimmer240, 3, "Dimmer 3"),
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal([0, 3, 6], result.Layout.DevicesInRow(0).Select(d => d.StartSlot));
    }

    [Fact]
    public void A_device_that_would_straddle_the_row_end_moves_to_the_next_row()
    {
        var devices = Enumerable.Range(1, 5)
            .Select(n => Device(DeviceCategory.Dimmer240, 3, $"Dimmer {n}"))
            .ToList();

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var fifth = result.Layout.Devices.Single(d => d.Label == "Dimmer 5");
        Assert.Equal(1, fifth.RowIndex);
        Assert.Equal(0, fifth.StartSlot);
    }

    [Fact]
    public void Tape_dimmers_share_the_mains_dimmer_band()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 3, "Dimmer 1"),
            Device(DeviceCategory.Dimmer0_10V, 3, "Tape Dimmer 1"),
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.All(result.Layout.Devices, d => Assert.Equal(0, d.RowIndex));
    }

    [Fact]
    public void Overflowing_every_layout_raises_an_error_and_suggests_a_bigger_enclosure()
    {
        var devices = Enumerable.Range(1, 12)
            .Select(n => Device(DeviceCategory.Relay, 9, $"Relay {n}"))
            .ToList();

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
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

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Empty(result.Layout.Devices);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DeviceWiderThanRow);
    }
}
