using System.Globalization;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Api.Rendering;
using PubInvest.HouseConfig.Domain.Racks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PubInvest.HouseConfig.Api.Documents;

/// Page 1: the rack drawing. Then the port schedule, one table per item with
/// every port in it, and last the parts list. Everything comes from an issued
/// revision, never from live tables.
public sealed class RackDocument(
    RackSnapshot snapshot,
    int number,
    DateTimeOffset issuedAt,
    string issuedBy) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        // Portrait: a rack is tall, not wide.
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(Header);
            page.Content().PaddingTop(6, Unit.Millimetre).Element(Drawing);
            page.Footer().Element(Footer);
        });

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(Header);
            page.Content().PaddingTop(6, Unit.Millimetre).Column(column =>
            {
                column.Item().Element(Schedule);
                column.Item().PaddingTop(8, Unit.Millimetre).Element(Parts);
            });
            page.Footer().Element(Footer);
        });
    }

    private void Header(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.Span($"{snapshot.ProjectName} — ").SemiBold().FontSize(13);
                text.Span(snapshot.RackName).FontSize(13);

                if (snapshot.LocationName is not null)
                {
                    text.Span($"  ·  {snapshot.LocationName}").FontSize(13);
                }
            });

            column.Item().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontColor(Colors.Grey.Darken1));
                text.Span($"{snapshot.Cabinet.Description}  ·  {snapshot.Cabinet.HeightU}U");
                text.Span(snapshot.FedFromSubmainName is null
                    ? "  ·  Not fed from an automation panel"
                    : $"  ·  Fed from {snapshot.FedFromSubmainName}");
                text.Span($"  ·  Revision {number}");
            });
        });

    /// The cabinet in bands of at most 14U, the top band first, so a 42U
    /// cabinet prints at the same scale as a 12U one instead of being shrunk to
    /// fit one page. A band is drawn at the page's width and kept whole: one
    /// that does not fit under the band before it starts the next page.
    private void Drawing(IContainer container) =>
        container.Column(column =>
        {
            var spans = snapshot.Items.Select(i => new RackSpan(Guid.Empty, i.TopU, i.HeightU)).ToList();
            var bands = RackBands.Split(snapshot.Cabinet.HeightU, spans);

            foreach (var band in bands)
            {
                column.Item().ShowEntire().Column(drawing =>
                {
                    // A wall cabinet prints as one band and needs no caption.
                    if (bands.Count > 1)
                    {
                        drawing.Item().PaddingBottom(2, Unit.Millimetre)
                            .Text($"U{band.TopU}–U{band.BottomU}").SemiBold().FontColor(Colors.Grey.Darken1);
                    }

                    drawing.Item().Svg(RackSvgRenderer.Render(snapshot, band.TopU, band.BottomU));
                });
            }

            column.Item().PaddingTop(4, Unit.Millimetre)
                .Text("Filled port: labelled.  Orange dot: PoE.  U1 is the bottom of the cabinet.")
                .FontColor(Colors.Grey.Darken1);
        });

    private void Schedule(IContainer container)
    {
        var types = snapshot.Catalogue.ToDictionary(t => t.Id);

        container.Column(column =>
        {
            column.Item().PaddingBottom(3, Unit.Millimetre).Text("Port schedule").SemiBold().FontSize(12);

            foreach (var item in snapshot.Items)
            {
                var type = types[item.RackDeviceTypeId];
                var stored = item.Ports.ToDictionary(p => p.Number);
                var links = Links(item);

                column.Item().PaddingTop(4, Unit.Millimetre)
                    .Text($"{item.Label} — {type.Description} — U{item.TopU}").SemiBold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(14, Unit.Millimetre);
                        c.ConstantColumn(18, Unit.Millimetre);
                        c.RelativeColumn();
                        c.ConstantColumn(14, Unit.Millimetre);
                    });

                    table.Header(h =>
                    {
                        foreach (var heading in new[] { "Port", "Type", "Label", "PoE" })
                        {
                            h.Cell().BorderBottom(0.5f).PaddingVertical(2).Text(heading).SemiBold();
                        }
                    });

                    foreach (var group in type.PortGroups)
                    {
                        for (var n = group.From; n <= group.To; n++)
                        {
                            stored.TryGetValue(n, out var port);
                            table.Cell().PaddingVertical(1).Text(n.ToString(CultureInfo.InvariantCulture));
                            table.Cell().PaddingVertical(1).Text(group.Name);
                            links.TryGetValue(n, out var link);
                            table.Cell().PaddingVertical(1).Column(cell =>
                            {
                                if (port?.Label is not null || link is null) cell.Item().Text(port?.Label ?? "—");
                                if (link is not null) cell.Item().Text(link).FontColor(Colors.Grey.Darken2);
                            });
                            table.Cell().PaddingVertical(1).Text(port?.Poe == true ? "PoE" : "");
                        }
                    }
                });
            }
        });
    }

    /// What each linked port of the item says under its label: where its
    /// uplink goes, or whose uplink arrives there. The other switch's rack is
    /// named only when it is not this one.
    private static Dictionary<int, string> Links(RackItemSnapshot item)
    {
        var links = (item.Downlinks ?? [])
            .ToDictionary(d => d.Port, d => d.FromRackName is null
                ? $"Downlink from {d.FromLabel}"
                : $"Downlink from {d.FromLabel}  ·  {d.FromRackName}");

        if (item.Uplink is { } up)
        {
            links[up.Port] = up.ToText is not null
                ? $"Uplink to {up.ToText}"
                : string.Join("  ·  ", new[] { $"Uplink to {up.ToLabel}", up.ToRackName, up.ToLocationName, $"port {up.ToPort}" }
                    .Where(part => part is not null));
        }

        return links;
    }

    private void Parts(IContainer container) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(3, Unit.Millimetre).Text("Parts list").SemiBold().FontSize(12);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(55, Unit.Millimetre);
                    c.RelativeColumn();
                    c.ConstantColumn(14, Unit.Millimetre);
                });

                table.Header(h =>
                {
                    h.Cell().BorderBottom(0.5f).PaddingVertical(2).Text("Part").SemiBold();
                    h.Cell().BorderBottom(0.5f).PaddingVertical(2).Text("Description").SemiBold();
                    h.Cell().BorderBottom(0.5f).PaddingVertical(2).AlignRight().Text("Qty").SemiBold();
                });

                foreach (var line in snapshot.Bom.Lines)
                {
                    table.Cell().PaddingVertical(1).Text(line.PartNumber);
                    table.Cell().PaddingVertical(1).Text(line.Description);
                    table.Cell().PaddingVertical(1).AlignRight().Text(line.Quantity.ToString(CultureInfo.InvariantCulture));
                }
            });
        });

    private void Footer(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem().Text(
                $"Issued {issuedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)} by {issuedBy}")
                .FontSize(7).FontColor(Colors.Grey.Darken1);

            row.AutoItem().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Darken1));
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
}
