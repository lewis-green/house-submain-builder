using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Domain.Tests;

public class BandPackerTests
{
    private static RequiredDevice Device(DeviceCategory category, int width, string label) =>
        new(Guid.NewGuid(), category, width, label, []);

    [Fact]
    public void Bands_are_laid_out_in_ruleset_order_each_starting_a_new_row()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Relay, 4, "Relay 1"),
            Device(DeviceCategory.Terminal240, 1, "L1"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal(0, result.Layout.Devices.Single(d => d.Label == "L1").RowIndex);
        Assert.Equal(1, result.Layout.Devices.Single(d => d.Label == "Dimmer 1").RowIndex);
        Assert.Equal(2, result.Layout.Devices.Single(d => d.Label == "Relay 1").RowIndex);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Devices_in_a_band_are_placed_left_to_right_without_gaps()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 2"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 3")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal([0, 2, 4], result.Layout.DevicesInRow(0).Select(d => d.StartSlot));
    }

    [Fact]
    public void A_device_that_would_straddle_the_row_end_moves_to_the_next_row()
    {
        // Small enclosure is 12 slots per row; five 3-slot PSUs cannot share one row.
        var devices = Enumerable.Range(1, 5)
            .Select(n => Device(DeviceCategory.Psu24V, 3, $"PSU {n}"))
            .ToList();

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var fifth = result.Layout.Devices.Single(d => d.Label == "PSU 5");
        Assert.Equal(1, fifth.RowIndex);
        Assert.Equal(0, fifth.StartSlot);
    }

    [Fact]
    public void Tape_dimmers_share_the_mains_dimmer_band()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1"),
            Device(DeviceCategory.Dimmer0_10V, 2, "Tape Dimmer 1")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.All(result.Layout.Devices, d => Assert.Equal(0, d.RowIndex));
    }

    [Fact]
    public void Overflowing_the_enclosure_raises_an_error_and_suggests_a_bigger_one()
    {
        // Small enclosure has 2 rows; three bands alone need three rows.
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Terminal240, 1, "L1"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1"),
            Device(DeviceCategory.Relay, 4, "Relay 1")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal(2, result.Layout.Rows);
        Assert.Contains(result.Layout.Devices, d => d.RowIndex >= result.Layout.Rows);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.EnclosureTooSmall, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Test 6x24", diagnostic.Suggestion);
        // Slot units are internal; the message an engineer reads is in modules.
        Assert.Contains("modules", diagnostic.Message);
        Assert.DoesNotContain("slots", diagnostic.Message);
    }

    [Fact]
    public void A_device_wider_than_a_row_is_reported_and_skipped()
    {
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Relay, 30, "Huge") };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Empty(result.Layout.Devices);
        Assert.Equal(DiagnosticCodes.DeviceWiderThanRow, Assert.Single(result.Diagnostics).Code);
    }
}
