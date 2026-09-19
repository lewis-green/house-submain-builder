using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

/// Two facts about the install rather than the rules: which end the cables come
/// in, and whether this submain is isolated upstream.
public class PanelOrientationTests
{
    private static List<Circuit> Circuits(int dimmed = 2, int switched = 1) =>
        [
            ..Enumerable.Range(1, dimmed).Select(n =>
                new Circuit(Guid.NewGuid(), CircuitType.DimmedLighting, $"Lighting {n}", null, n, null, null)),
            ..Enumerable.Range(1, switched).Select(n =>
                new Circuit(Guid.NewGuid(), CircuitType.Switched, $"Switched {n}", null, dimmed + n, null, null)),
        ];

    private static GenerationRequest Request(
        bool terminalsAtBottom = false,
        bool includeIsolator = true,
        EnclosureType? enclosure = null) =>
        new(Circuits(),
            enclosure ?? CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(),
            CatalogueFixture.AllEnclosures(),
            null,
            includeIsolator,
            terminalsAtBottom);

    private static IEnumerable<PlacedDevice> OfCategory(PanelLayout layout, DeviceCategory category) =>
        layout.Devices.Where(d => d.Category == category);

    private static int RowOf(PanelLayout layout, DeviceCategory category) =>
        OfCategory(layout, category).Select(d => d.RowIndex).Distinct().Single();

    [Fact]
    public void Terminations_sit_on_the_top_row_by_default()
    {
        var result = PanelGenerator.Generate(Request());

        Assert.Equal(0, RowOf(result.Layout, DeviceCategory.Terminal240));
        Assert.Equal(0, RowOf(result.Layout, DeviceCategory.Isolator));
    }

    [Fact]
    public void Terminations_sit_on_the_bottom_row_when_the_panel_is_glanded_from_below()
    {
        var enclosure = CatalogueFixture.LargeEnclosure();
        var result = PanelGenerator.Generate(Request(terminalsAtBottom: true, enclosure: enclosure));

        Assert.Equal(enclosure.Rows - 1, RowOf(result.Layout, DeviceCategory.Terminal240));
        Assert.Equal(enclosure.Rows - 1, RowOf(result.Layout, DeviceCategory.Isolator));
    }

    [Fact]
    public void Turning_the_panel_over_keeps_every_device_in_the_enclosure()
    {
        var enclosure = CatalogueFixture.LargeEnclosure();
        var result = PanelGenerator.Generate(Request(terminalsAtBottom: true, enclosure: enclosure));

        Assert.All(result.Layout.Devices, d =>
        {
            Assert.InRange(d.RowIndex, 0, enclosure.Rows - 1);
        });
    }

    [Fact]
    public void The_spare_rows_rise_to_the_top_away_from_the_glands()
    {
        var enclosure = CatalogueFixture.LargeEnclosure();
        var top = PanelGenerator.Generate(Request(enclosure: enclosure));
        var bottom = PanelGenerator.Generate(Request(terminalsAtBottom: true, enclosure: enclosure));

        // The design occupies the same number of rows either way; glanded from
        // below it is pushed against the last rail, leaving the spare rows above.
        var span = top.Layout.Devices.Max(d => d.RowIndex) - top.Layout.Devices.Min(d => d.RowIndex);

        Assert.Equal(0, top.Layout.Devices.Min(d => d.RowIndex));
        Assert.Equal(enclosure.Rows - 1, bottom.Layout.Devices.Max(d => d.RowIndex));
        Assert.Equal(enclosure.Rows - 1 - span, bottom.Layout.Devices.Min(d => d.RowIndex));
    }

    [Fact]
    public void Turning_the_panel_over_does_not_move_anything_sideways()
    {
        var top = PanelGenerator.Generate(Request());
        var bottom = PanelGenerator.Generate(Request(terminalsAtBottom: true));

        var topSlots = top.Layout.Devices.ToDictionary(d => d.Label, d => d.StartSlot);
        var bottomSlots = bottom.Layout.Devices.ToDictionary(d => d.Label, d => d.StartSlot);

        Assert.Equal(topSlots, bottomSlots);
    }

    [Fact]
    public void The_order_of_the_rows_is_reversed_not_just_shifted_down()
    {
        var top = PanelGenerator.Generate(Request());
        var bottom = PanelGenerator.Generate(Request(terminalsAtBottom: true));

        // Terminals lead the dimmers downward in one, upward in the other.
        Assert.True(RowOf(top.Layout, DeviceCategory.Terminal240)
                    < RowOf(top.Layout, DeviceCategory.Dimmer240));
        Assert.True(RowOf(bottom.Layout, DeviceCategory.Terminal240)
                    > RowOf(bottom.Layout, DeviceCategory.Dimmer240));
    }

    [Fact]
    public void A_submain_isolated_upstream_gets_no_isolator()
    {
        var result = PanelGenerator.Generate(Request(includeIsolator: false));

        Assert.Empty(OfCategory(result.Layout, DeviceCategory.Isolator));
    }

    [Fact]
    public void Leaving_the_isolator_out_is_not_an_error()
    {
        var result = PanelGenerator.Generate(Request(includeIsolator: false));

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Leaving_the_isolator_out_spends_no_terminal_block_on_the_feed()
    {
        var withIsolator = PanelGenerator.Generate(Request());
        var without = PanelGenerator.Generate(Request(includeIsolator: false));

        Assert.Equal(
            OfCategory(withIsolator.Layout, DeviceCategory.Terminal240).Count(),
            OfCategory(without.Layout, DeviceCategory.Terminal240).Count());
    }

    [Fact]
    public void An_isolator_that_is_wanted_but_missing_from_the_catalogue_is_still_an_error()
    {
        var rules = CatalogueFixture.Rules();
        var withoutIsolator = new DeviceCatalogue(
            CatalogueFixture.Catalogue().All.Where(d => d.Id != CatalogueFixture.IsolatorId));

        var result = PanelGenerator.Generate(new GenerationRequest(
            Circuits(), CatalogueFixture.LargeEnclosure(), rules, withoutIsolator,
            CatalogueFixture.AllEnclosures()));

        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Code == DiagnosticCodes.NoPreferredDevice);
    }

    [Fact]
    public void An_isolator_that_is_not_wanted_is_not_looked_for()
    {
        var withoutIsolator = new DeviceCatalogue(
            CatalogueFixture.Catalogue().All.Where(d => d.Id != CatalogueFixture.IsolatorId));

        var result = PanelGenerator.Generate(new GenerationRequest(
            Circuits(), CatalogueFixture.LargeEnclosure(), CatalogueFixture.Rules(), withoutIsolator,
            CatalogueFixture.AllEnclosures(), null, IncludeIsolator: false));

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void The_bill_of_materials_loses_the_isolator_along_with_the_device()
    {
        var without = PanelGenerator.Generate(Request(includeIsolator: false));
        var isolator = CatalogueFixture.Catalogue().FindActive(CatalogueFixture.IsolatorId)!;

        Assert.DoesNotContain(without.Bom.Lines, l => l.CatalogueId == isolator.Id);
    }
}
