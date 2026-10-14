using System.Globalization;
using System.Security;
using System.Text;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Rendering;

/// The rack drawn to scale for the PDF, in millimetres: a 19-inch panel is
/// 482.6 mm wide and a rack unit 44.45 mm tall, so the drawing has the
/// proportions of the real cabinet. Like PanelSvgRenderer, it draws what the
/// snapshot says and decides nothing.
public static class RackSvgRenderer
{
    private const double UnitMm = 44.45;
    private const double PanelMm = 482.6;
    private const double GutterMm = 32;
    private const double PortMm = 7;
    private const double PortGapMm = 1.6;

    /// The whole cabinet, U1 at the bottom.
    public static string Render(RackSnapshot snapshot) => Render(snapshot, snapshot.Cabinet.HeightU, 1);

    /// One band of the cabinet: only U bottomU up to topU, with the items whose
    /// top U is in it, drawn whole. RackBands picks the bands so that an item's
    /// bottom is never below the band's.
    public static string Render(RackSnapshot snapshot, int topU, int bottomU)
    {
        var types = snapshot.Catalogue.ToDictionary(t => t.Id);
        var svg = new StringBuilder();

        // InvariantCulture throughout: a comma decimal separator would make invalid SVG.
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(GutterMm + PanelMm)} {N((topU - bottomU + 1) * UnitMm)}\">");

        for (var u = topU; u >= bottomU; u--)
        {
            var y = (topU - u) * UnitMm;
            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(GutterMm)}\" y=\"{N(y)}\" width=\"{N(PanelMm)}\" height=\"{N(UnitMm)}\" fill=\"#f1f5f9\" stroke=\"#cbd5e1\" stroke-width=\"0.4\"/>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(GutterMm - 4)}\" y=\"{N(y + UnitMm / 2)}\" text-anchor=\"end\" dominant-baseline=\"middle\" font-size=\"9\" fill=\"#475569\">U{u}</text>");
        }

        foreach (var item in snapshot.Items.Where(i => i.TopU <= topU && i.TopU >= bottomU))
        {
            var type = types[item.RackDeviceTypeId];
            var x = GutterMm + 2;
            var y = (topU - item.TopU) * UnitMm + 2;
            var width = PanelMm - 4;
            var height = item.HeightU * UnitMm - 4;

            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\" rx=\"2\" fill=\"#ffffff\" stroke=\"#0f172a\" stroke-width=\"0.8\"/>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + 6)}\" y=\"{N(y + 12)}\" font-size=\"9\" font-weight=\"bold\" fill=\"#0f172a\">{SecurityElement.Escape(item.Label)}</text>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + 6)}\" y=\"{N(y + 24)}\" font-size=\"7\" fill=\"#475569\">{SecurityElement.Escape(type.Model)}</text>");

            Ports(svg, item, type, x + 120, y + 6);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// Laid out as on the front panel: a switch's copper ports odd on the top
    /// row and even below, its uplinks to the right; a patch panel in one row.
    /// A labelled port is filled; a PoE port carries an orange dot.
    private static void Ports(StringBuilder svg, RackItemSnapshot item, RackDeviceType type, double left, double top)
    {
        var stored = item.Ports.ToDictionary(p => p.Number);
        var copper = type.PortGroups.Where(g => g.PoeTick).SelectMany(Numbers).ToList();
        var uplinks = type.PortGroups.Where(g => !g.PoeTick).SelectMany(Numbers).ToList();

        var rows = type.Kind == RackDeviceKind.PatchPanel
            ? new[] { copper, new List<int>() }
            : new[] { copper.Where(n => n % 2 == 1).ToList(), copper.Where(n => n % 2 == 0).ToList() };

        for (var r = 0; r < rows.Length; r++)
        {
            for (var i = 0; i < rows[r].Count; i++)
            {
                Port(svg, stored, rows[r][i], left + i * (PortMm + PortGapMm), top + r * (PortMm + PortGapMm));
            }
        }

        var uplinkLeft = left + rows.Max(row => row.Count) * (PortMm + PortGapMm) + 8;
        for (var i = 0; i < uplinks.Count; i++)
        {
            Port(svg, stored, uplinks[i], uplinkLeft + i / 2 * (PortMm + PortGapMm), top + i % 2 * (PortMm + PortGapMm));
        }
    }

    private static void Port(StringBuilder svg, IReadOnlyDictionary<int, RackPortSnapshot> stored, int number, double x, double y)
    {
        stored.TryGetValue(number, out var port);
        var fill = port?.Label is null ? "#ffffff" : "#334155";
        svg.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(PortMm)}\" height=\"{N(PortMm)}\" fill=\"{fill}\" stroke=\"#334155\" stroke-width=\"0.4\"/>");
        if (port?.Poe == true)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<circle cx=\"{N(x + PortMm - 1.5)}\" cy=\"{N(y + 1.5)}\" r=\"1.3\" fill=\"#ea580c\"/>");
        }
    }

    private static IEnumerable<int> Numbers(PortGroup group) => Enumerable.Range(group.From, group.Count);

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
