using System.Globalization;
using System.Runtime.CompilerServices;
using PubInvest.HouseConfig.Api.Rendering;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Api.Tests;

public class PanelSvgRendererTests
{
    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static readonly Guid CircuitA = new("55555555-0000-4000-8000-000000000001");
    private static readonly Guid CircuitB = new("55555555-0000-4000-8000-000000000002");

    private static PlacedDevice Terminal(int n, int slot, TerminalRole role, string prefix) =>
        new(new Guid("66666666-0000-4000-8000-000000000001"), DeviceCategory.Terminal240,
            0, slot, 1, $"{prefix}{n}", [], role);

    private static PanelLayout Layout()
    {
        var devices = new List<PlacedDevice>();
        for (var n = 1; n <= 4; n++) devices.Add(Terminal(n, n - 1, TerminalRole.Line, "L"));
        for (var n = 1; n <= 4; n++) devices.Add(Terminal(n, 3 + n, TerminalRole.Neutral, "N"));

        devices.Add(new PlacedDevice(
            new Guid("66666666-0000-4000-8000-000000000002"), DeviceCategory.Dimmer240, 1, 0, 3, "Dimmer 1",
            [new ChannelAssignment(0, CircuitA, false), new ChannelAssignment(1, null, true)], TerminalRole.None));

        devices.Add(new PlacedDevice(
            new Guid("66666666-0000-4000-8000-000000000003"), DeviceCategory.Relay, 2, 0, 9, "Relay 1",
            [new ChannelAssignment(0, CircuitB, false)], TerminalRole.None));

        return new PanelLayout(3, 54, devices);
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

        // Relay 1 sits at row 2, slot 0, nine slot units wide.
        var x = PanelGeometry.SlotToX(0).ToString("0.##", CultureInfo.InvariantCulture);
        var y = PanelGeometry.RowToY(2).ToString("0.##", CultureInfo.InvariantCulture);
        var w = PanelGeometry.SlotToX(9).ToString("0.##", CultureInfo.InvariantCulture);

        Assert.Contains($"x=\"{x}\" y=\"{y}\" width=\"{w}\"", svg);
    }

    [Fact]
    public void A_run_of_terminals_becomes_one_labelled_block_per_conductor()
    {
        var svg = PanelSvgRenderer.Render(Layout(), Names());

        Assert.Contains(">L1–L4<", svg);
        Assert.Contains(">N1–N4<", svg);
        Assert.DoesNotContain(">L2<", svg);
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
