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
            });

            column.Item().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontColor(Colors.Grey.Darken1));
                text.Span($"{snapshot.Cabinet.Description}  ·  {snapshot.Cabinet.HeightU}U");
                text.Span(snapshot.FedFromSubmainName is null
                    ? "  ·  Not fed from a submain"
                    : $"  ·  Fed from {snapshot.FedFromSubmainName}");
                text.Span($"  ·  Revision {number}");
            });
        });

    private void Drawing(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Svg(RackSvgRenderer.Render(snapshot)).FitArea();
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
                            table.Cell().PaddingVertical(1).Text(port?.Label ?? "—");
                            table.Cell().PaddingVertical(1).Text(port?.Poe == true ? "PoE" : "");
                        }
                    }
                });
            }
        });
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
