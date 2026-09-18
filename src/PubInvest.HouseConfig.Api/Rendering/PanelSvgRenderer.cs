using System.Globalization;
using System.Security;
using System.Text;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Api.Rendering;

/// Draws a panel as a static SVG string, for embedding in a PDF.
///
/// This is the second renderer — ui/src/panel/PanelSvg.tsx is the interactive
/// one. They cannot share an implementation without running a browser on the
/// server or trusting the client to upload its own drawing, so instead both
/// derive positions from slot units and are pinned by tests.
///
/// No font-family is set anywhere, deliberately. QuestPDF renders this SVG
/// through Skia, which silently DROPS any text whose font it cannot resolve —
/// "sans-serif", "Arial" and "Helvetica" all produce a drawing with no labels
/// at all. Omitting the attribute uses the default font in both QuestPDF and a
/// browser. If you add a font-family here, look at a generated PDF before
/// believing it worked.
public static class PanelSvgRenderer
{
    /// Below this width no horizontal label fits, so it is drawn rotated.
    private const double NarrowMm = 24.0;

    private static readonly Dictionary<DeviceCategory, (string Fill, string Stroke, string Kind)> Styles = new()
    {
        [DeviceCategory.Dimmer240] = ("#bae6fd", "#0284c7", "Dimmer"),
        [DeviceCategory.Dimmer0_10V] = ("#c7d2fe", "#4f46e5", "Tape dimmer"),
        [DeviceCategory.Relay] = ("#99f6e4", "#0d9488", "Relay"),
        [DeviceCategory.Isolator]      = ("#fecaca", "#dc2626", "Isolator"),
        [DeviceCategory.Dc24VPositive] = ("#fde68a", "#d97706", "+24V"),
        [DeviceCategory.Dc24VNegative] = ("#e2e8f0", "#64748b", "-24V"),
        [DeviceCategory.ExternalDriver]= ("#f1f5f9", "#cbd5e1", "Driver (external)"),
        [DeviceCategory.Terminal240] = ("#e2e8f0", "#94a3b8", "Terminal"),
        [DeviceCategory.Accessory] = ("#f1f5f9", "#cbd5e1", "Accessory"),
    };

    public static string Render(PanelLayout layout, IReadOnlyDictionary<Guid, string> circuitNames)
    {
        var (width, height) = PanelGeometry.PanelSize(layout);
        var svg = new StringBuilder();

        // InvariantCulture throughout: a comma decimal separator would produce
        // invalid SVG on a machine with a European locale.
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(width)} {N(height)}\" ")
           .Append(CultureInfo.InvariantCulture, $"width=\"{N(width)}mm\" height=\"{N(height)}mm\">");

        RenderRails(svg, layout, width);
        RenderTerminalGroups(svg, layout);
        RenderDevices(svg, layout, circuitNames);

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void RenderRails(StringBuilder svg, PanelLayout layout, double width)
    {
        for (var row = 0; row < layout.Rows; row++)
        {
            var y = PanelGeometry.RowToY(row);
            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"0\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(PanelGeometry.RowMm)}\" ")
               .Append("rx=\"1\" fill=\"#f8fafc\" stroke=\"#e2e8f0\" stroke-width=\"0.3\"/>");

            for (var m = 0; m <= layout.SlotsPerRow / DinUnits.PerModule; m++)
            {
                var x = PanelGeometry.SlotToX(m * DinUnits.PerModule);
                svg.Append(CultureInfo.InvariantCulture,
                    $"<line x1=\"{N(x)}\" y1=\"{N(y)}\" x2=\"{N(x)}\" y2=\"{N(y + PanelGeometry.RowMm)}\" ")
                   .Append("stroke=\"#e2e8f0\" stroke-width=\"0.2\"/>");
            }
        }
    }

    private static void RenderTerminalGroups(StringBuilder svg, PanelLayout layout)
    {
        foreach (var group in GroupTerminals(layout))
        {
            var (x, y, w, h) = (
                PanelGeometry.SlotToX(group.StartSlot),
                PanelGeometry.RowToY(group.RowIndex),
                PanelGeometry.SlotToX(group.Slots),
                PanelGeometry.RowMm);

            var style = Styles[DeviceCategory.Terminal240];

            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"1\" ")
               .Append(CultureInfo.InvariantCulture,
                   $"fill=\"{style.Fill}\" stroke=\"{style.Stroke}\" stroke-width=\"0.3\"/>");

            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + w / 2)}\" y=\"{N(y + h / 2)}\" text-anchor=\"middle\" ")
               .Append("dominant-baseline=\"middle\" font-size=\"3\" fill=\"#1e293b\">")
               .Append(SecurityElement.Escape(group.Label))
               .Append("</text>");
        }
    }

    private static void RenderDevices(
        StringBuilder svg,
        PanelLayout layout,
        IReadOnlyDictionary<Guid, string> circuitNames)
    {
        foreach (var device in layout.Devices.Where(d => d.Category != DeviceCategory.Terminal240))
        {
            var (x, y, w, h) = PanelGeometry.DeviceBox(device);
            var style = Styles[device.Category];

            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"1\" ")
               .Append(CultureInfo.InvariantCulture,
                   $"fill=\"{style.Fill}\" stroke=\"{style.Stroke}\" stroke-width=\"0.4\"/>");

            if (w < NarrowMm)
            {
                var cx = x + w / 2;
                var cy = y + h - 2;
                svg.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{N(cx)}\" y=\"{N(cy)}\" transform=\"rotate(-90 {N(cx)} {N(cy)})\" ")
                   .Append("font-size=\"3\" fill=\"#1e293b\">")
                   .Append(SecurityElement.Escape(device.Label))
                   .Append("</text>");
                continue;
            }

            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + 2)}\" y=\"{N(y + 6)}\" font-size=\"3.5\" ")
               .Append("font-weight=\"bold\" fill=\"#1e293b\">")
               .Append(SecurityElement.Escape(device.Label))
               .Append("</text>");

            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + 2)}\" y=\"{N(y + 11)}\" font-size=\"2.6\" ")
               .Append("fill=\"#64748b\">")
               .Append(SecurityElement.Escape(style.Kind))
               .Append("</text>");

            var line = 0;
            foreach (var channel in device.Channels.Where(c => c.CircuitId is not null))
            {
                if (!circuitNames.TryGetValue(channel.CircuitId!.Value, out var name)) continue;

                svg.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{N(x + 2)}\" y=\"{N(y + 17 + line * 4.5)}\" ")
                   .Append("font-size=\"2.6\" fill=\"#475569\">")
                   .Append(SecurityElement.Escape(name))
                   .Append("</text>");
                line++;
            }
        }
    }

    private sealed record Group(int RowIndex, int StartSlot, int Slots, string Label, TerminalRole Role);

    private static List<Group> GroupTerminals(PanelLayout layout)
    {
        var groups = new List<Group>();

        foreach (var device in layout.Devices
                     .Where(d => d.Category == DeviceCategory.Terminal240)
                     .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot))
        {
            var last = groups.Count == 0 ? null : groups[^1];

            if (last is not null
                && last.RowIndex == device.RowIndex
                && last.StartSlot + last.Slots == device.StartSlot
                && last.Role == device.TerminalRole)
            {
                var first = last.Label.Split('–')[0];
                groups[^1] = last with
                {
                    Slots = last.Slots + device.ModuleWidth,
                    Label = $"{first}–{device.Label}",
                };
            }
            else
            {
                groups.Add(new Group(device.RowIndex, device.StartSlot, device.ModuleWidth, device.Label, device.TerminalRole));
            }
        }

        return groups;
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
