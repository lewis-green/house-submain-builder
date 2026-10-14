using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security;
using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Api.Rendering;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Api.Tests;

public class PanelSvgRendererTests
{
    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static readonly Guid CircuitA = new("55555555-0000-4000-8000-000000000001");
    private static readonly Guid CircuitB = new("55555555-0000-4000-8000-000000000002");

    /// One 3-tier block per circuit, carrying L, N and E on a single slice.
    private static PlacedDevice Terminal(int n, int slot) =>
        new(new Guid("66666666-0000-4000-8000-000000000001"), DeviceCategory.Terminal240,
            1, slot, 1, $"C{n}", [], TerminalRole.All);

    private static PanelLayout Layout()
    {
        var devices = new List<PlacedDevice>
        {
            // The isolator leads: the incoming feed lands here, not on a terminal.
            new(new Guid("66666666-0000-4000-8000-000000000004"), DeviceCategory.Isolator,
                0, 0, 6, "Isolator", [], TerminalRole.None),
        };

        for (var n = 1; n <= 4; n++) devices.Add(Terminal(n, n - 1));

        devices.Add(new PlacedDevice(
            new Guid("66666666-0000-4000-8000-000000000002"), DeviceCategory.Dimmer240, 2, 0, 3, "Dimmer 1",
            [new ChannelAssignment(0, CircuitA, false), new ChannelAssignment(1, null, true)], TerminalRole.None));

        devices.Add(new PlacedDevice(
            new Guid("66666666-0000-4000-8000-000000000003"), DeviceCategory.Relay, 3, 0, 9, "Relay 1",
            [new ChannelAssignment(0, CircuitB, false)], TerminalRole.None));

        devices.Add(new PlacedDevice(
            new Guid("66666666-0000-4000-8000-000000000005"), DeviceCategory.Dc24VPositive, 4, 0, 4, "+24V",
            [], TerminalRole.None));

        return new PanelLayout(5, 54, devices);
    }

    private static Dictionary<Guid, string> Names() => new()
    {
        [CircuitA] = "Kitchen ceiling",
        [CircuitB] = "Immersion",
    };

    [Fact]
    public void The_svg_is_sized_from_the_panel_geometry()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());
        var (width, height) = PanelGeometry.PanelSize(Layout());

        Assert.Contains($"width=\"{width.ToString("0.##", CultureInfo.InvariantCulture)}mm\"", svg);
        Assert.Contains($"height=\"{height.ToString("0.##", CultureInfo.InvariantCulture)}mm\"", svg);
    }

    [Fact]
    public void A_device_is_placed_at_its_slot_position()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        // Relay 1 sits at row 3, slot 0, nine slot units wide.
        var x = PanelGeometry.SlotToX(0).ToString("0.##", CultureInfo.InvariantCulture);
        var y = PanelGeometry.RowToY(3).ToString("0.##", CultureInfo.InvariantCulture);
        var w = PanelGeometry.SlotToX(9).ToString("0.##", CultureInfo.InvariantCulture);

        Assert.Contains($"x=\"{x}\" y=\"{y}\" width=\"{w}\"", svg);
    }

    [Fact]
    public void A_run_of_terminals_is_one_block_with_a_label_per_terminal()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        Assert.Contains(">C1<", svg);
        Assert.Contains(">C2<", svg);
        Assert.DoesNotContain(">C1–C4<", svg);
    }

    [Fact]
    public void Each_terminal_in_a_run_is_labelled_and_a_bridged_circuit_gets_a_bar()
    {
        var circuit = Guid.NewGuid();
        var type = Guid.NewGuid();
        var layout = new PanelLayout(1, 12,
        [
            new(type, DeviceCategory.Terminal240, 0, 0, 1, "C1", [], TerminalRole.All, Guid.NewGuid()),
            new(type, DeviceCategory.Terminal240, 0, 1, 1, "C2.1", [], TerminalRole.All, circuit),
            new(type, DeviceCategory.Terminal240, 0, 2, 1, "C2.2", [], TerminalRole.All, circuit),
        ]);

        var svg = PanelSvgRenderer.Render(layout, new Dictionary<Guid, string>());

        Assert.Contains(">C1<", svg);
        Assert.Contains(">C2.1<", svg);
        Assert.Contains(">C2.2<", svg);
        Assert.Contains("data-bridge=\"C2.1–C2.2\"", svg);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "data-bridge="));
    }

    [Fact]
    public void A_narrow_device_gets_a_rotated_label()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        Assert.Contains("rotate(-90", svg);
    }

    [Fact]
    public void A_wide_device_names_its_circuits()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        Assert.Contains(">Immersion<", svg);
    }

    [Fact]
    public void A_kind_that_is_already_the_label_is_not_printed_twice()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        // "Isolator" is both the label and the kind; "Relay 1" still gets "Relay" under it.
        Assert.Equal(1, svg.Split(">Isolator<").Length - 1);
        Assert.Contains(">Relay 1<", svg);
        Assert.Contains(">Relay<", svg);
    }

    [Fact]
    public void Numbers_use_a_dot_even_under_a_comma_decimal_locale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var svg = PanelSvgRenderer.Render(Layout(), Names());

            Assert.DoesNotContain(",", svg);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_circuit_name_containing_markup_is_escaped()
    {
        var names = new Dictionary<Guid, string> { [CircuitB] = "Hall <b>& landing</b>" };

        var svg = PanelSvgRenderer.Render(Layout(), names);

        Assert.DoesNotContain("<b>", svg);
        Assert.Contains("&lt;b&gt;", svg);
    }

    [Fact]
    public void No_font_family_is_set_anywhere()
    {
        // QuestPDF renders this through Skia, which silently drops text whose
        // font it cannot resolve. A stray font-family produces a drawing with
        // no labels at all, and nothing fails until someone opens the PDF.
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        Assert.DoesNotContain("font-family", svg);
    }

    [Fact]
    public void Every_device_label_reaches_the_svg()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        Assert.Contains(">Dimmer 1<", svg);
        Assert.Contains(">Relay 1<", svg);
        Assert.Contains(">C4<", svg);
    }

    [Fact]
    public void Rendering_is_deterministic()
    {
        Assert.Equal(
            PanelSvgRenderer.Render(Layout(), Names()),
            PanelSvgRenderer.Render(Layout(), Names()));
    }

    [Fact]
    public void The_rendered_panel_matches_the_golden_svg()
    {
        var actual = PanelSvgRenderer.Render(Layout(), Names());
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "panel.svg");

        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            var source = Path.Combine(SourceDirectory(), "Golden", "panel.svg");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(source, actual);
            File.WriteAllText(goldenPath, actual);
        }

        if (!File.Exists(goldenPath))
        {
            throw new InvalidOperationException($"Golden file missing. Review it, then re-run with GOLDEN_UPDATE=1:\n{actual}");
        }

        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings(), actual.ReplaceLineEndings());
    }

    /// Four categories were once added to the generator and never to the
    /// renderer; every panel with a blinds controller in it printed a 500. One
    /// wide device of each category, rendered: it must draw, in its own colours,
    /// with its kind in words.
    [Theory]
    [MemberData(nameof(EveryCategory))]
    public void Every_device_category_renders_with_its_own_style(DeviceCategory category)
    {
        var device = new PlacedDevice(
            Guid.NewGuid(), category, 1, 0, 9, $"{category} 1", [], TerminalRole.None);
        var layout = new PanelLayout(2, 54, [device]);

        var svg = PanelSvgRenderer.Render(layout, new Dictionary<Guid, string>());

        Assert.True(PanelSvgRenderer.HasStyle(category), $"{category} would fall back to grey");
        Assert.Contains($">{SecurityElement.Escape($"{category} 1")}<", svg);
        if (category != DeviceCategory.Terminal240)
        {
            Assert.Contains($">{SecurityElement.Escape(Wording.Kind(category))}<", svg);
        }
    }

    [Fact]
    public void A_blinds_controller_keeps_its_label_and_is_described_as_blinds()
    {
        var cover = new PlacedDevice(
            Guid.NewGuid(), DeviceCategory.Cover, 1, 0, 9, "Cover 1", [], TerminalRole.None);

        var svg = PanelSvgRenderer.Render(new PanelLayout(2, 54, [cover]), new Dictionary<Guid, string>());

        Assert.Contains(">Cover 1<", svg);
        Assert.Contains(">Blinds<", svg);
        Assert.DoesNotContain(">Cover<", svg);
    }

    public static TheoryData<DeviceCategory> EveryCategory()
    {
        var data = new TheoryData<DeviceCategory>();
        foreach (var category in Enum.GetValues<DeviceCategory>()) data.Add(category);
        return data;
    }

    [Fact]
    public void The_server_renderer_scales_the_same_way_the_screen_does()
    {
        // ui/src/panel/geometry.ts uses SLOT_PX = 8; here SlotMm = 5. Both are
        // slot * constant, so the ratio between any two slots must match. If this
        // fails, one renderer has changed its scale and the PDF no longer agrees
        // with the screen.
        Assert.Equal(3.0, PanelGeometry.SlotToX(3) / PanelGeometry.SlotToX(1));
        Assert.Equal(3.0, PanelGeometry.SlotToX(9) / PanelGeometry.SlotToX(3));
        Assert.Equal(PanelGeometry.ModuleMm, PanelGeometry.SlotToX(DinUnits.PerModule));
    }
}
