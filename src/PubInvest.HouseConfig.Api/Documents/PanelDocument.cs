using System.Globalization;
using PubInvest.HouseConfig.Api.Rendering;
using PubInvest.HouseConfig.Api.Revisions;
using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PubInvest.HouseConfig.Api.Documents;

/// Page 1: the panel drawing. Pages 2+: the circuit schedule.
/// Everything comes from an issued revision, never from live tables.
public sealed class PanelDocument(
    RevisionSnapshot snapshot,
    DateTimeOffset issuedAt,
    string issuedBy,
    BillOfMaterials? bom = null) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
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
            page.Content().PaddingTop(6, Unit.Millimetre).Element(Schedule);
            page.Footer().Element(Footer);
        });
    }

    private void Header(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.Span($"{snapshot.ProjectName} — ").SemiBold().FontSize(13);
                text.Span(snapshot.SubmainName).FontSize(13);

                if (snapshot.LocationName is not null)
                {
                    text.Span($"  ·  {snapshot.LocationName}").FontSize(13);
                }
            });

            column.Item().Text(text =>
            {
                text.Span(snapshot.Enclosure.Description).FontColor(Colors.Grey.Darken1);
                text.Span($"  ·  {snapshot.Enclosure.Rows} rows of {Modules(snapshot.Enclosure.SlotsPerRow)} modules")
                    .FontColor(Colors.Grey.Darken1);

                if (!string.IsNullOrWhiteSpace(snapshot.Reference))
                {
                    text.Span($"  ·  ref {snapshot.Reference}").FontColor(Colors.Grey.Darken1);
                }
            });
        });

    private void Drawing(IContainer container) =>
        container.Column(column =>
        {
            column.Item()
                .Svg(PanelSvgRenderer.Render(snapshot.Layout, CircuitNames()))
                .FitArea();

            column.Item().PaddingTop(5, Unit.Millimetre).Row(row =>
            {
                foreach (var kind in Kinds())
                {
                    row.AutoItem().PaddingRight(6, Unit.Millimetre).Text(kind).FontColor(Colors.Grey.Darken1);
                }
            });
        });

    private void Schedule(IContainer container) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(3, Unit.Millimetre).Text("Circuit schedule").SemiBold().FontSize(12);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);   // terminal
                    columns.RelativeColumn(3);   // circuit
                    columns.RelativeColumn(2);   // room
                    columns.RelativeColumn(2);   // type
                    columns.RelativeColumn(2);   // device
                    columns.RelativeColumn(1);   // channel
                    columns.RelativeColumn(2);   // position
                });

                table.Header(header =>
                {
                    foreach (var heading in new[] { "Terminal", "Circuit", "Room", "Type", "Device", "Ch", "Position" })
                    {
                        header.Cell().Element(HeaderCell).Text(heading).SemiBold();
                    }
                });

                foreach (var row in Rows())
                {
                    table.Cell().Element(BodyCell).Text(row.Terminal);
                    table.Cell().Element(BodyCell).Text(row.Circuit);
                    table.Cell().Element(BodyCell).Text(row.Room);
                    table.Cell().Element(BodyCell).Text(row.Type);
                    table.Cell().Element(BodyCell).Text(row.Device);
                    table.Cell().Element(BodyCell).Text(row.Channel);
                    table.Cell().Element(BodyCell).Text(row.Position);
                }
            });

            if (bom is not null) column.Item().PaddingTop(8, Unit.Millimetre).Element(BomTable);
        });

    private void BomTable(IContainer container) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(3, Unit.Millimetre).Text("Parts list").SemiBold().FontSize(12);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(5);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    foreach (var heading in new[] { "Part", "Description", "Qty" })
                    {
                        header.Cell().Element(HeaderCell).Text(heading).SemiBold();
                    }
                });

                foreach (var line in bom!.Lines)
                {
                    table.Cell().Element(BodyCell).Text(line.PartNumber);
                    table.Cell().Element(BodyCell).Text(
                        line.PanelMounted ? line.Description : $"{line.Description} (external)");
                    table.Cell().Element(BodyCell).Text(line.Quantity.ToString(CultureInfo.InvariantCulture));
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

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(2);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2);

    private static string Modules(int slotUnits) =>
        DinUnits.ToModules(slotUnits).ToString("0.##", CultureInfo.InvariantCulture);

    /// A position as the screen says it: the first module is module 1, and a
    /// device starting a third of the way in is at "module 1.3" — the same one
    /// decimal the device sheet shows (ui/src/units.ts formatModuleCount).
    private static string ModulePosition(int startSlot) =>
        (DinUnits.ToModules(startSlot) + 1).ToString("0.#", CultureInfo.InvariantCulture);

    private Dictionary<Guid, string> CircuitNames() =>
        snapshot.Circuits.ToDictionary(c => c.Id, c => c.Name);

    private IEnumerable<string> Kinds()
    {
        var present = snapshot.Layout.Devices.Select(d => d.Category).Distinct();
        foreach (var category in present)
        {
            var count = snapshot.Layout.Devices.Count(d => d.Category == category);
            yield return $"{count} × {Wording.Kind(category)}";
        }
    }

    private sealed record ScheduleRow(string Terminal, string Circuit, string Room, string Type, string Device, string Channel, string Position);

    private IEnumerable<ScheduleRow> Rows()
    {
        var byId = snapshot.Circuits.ToDictionary(c => c.Id);

        // A circuit's terminal labels as one cell: "C3", or "C3.1–C3.6" when bridged.
        var terminals = snapshot.Layout.Devices
            .Where(d => d.Category == DeviceCategory.Terminal240 && d.CircuitId is not null)
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .GroupBy(d => d.CircuitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Count() == 1 ? g.First().Label : $"{g.First().Label}–{g.Last().Label}");

        foreach (var device in snapshot.Layout.Devices
                     .Where(d => d.Category != DeviceCategory.Terminal240)
                     .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot))
        {
            foreach (var channel in device.Channels.OrderBy(c => c.ChannelIndex))
            {
                var circuit = channel.CircuitId is { } id && byId.TryGetValue(id, out var c) ? c : null;

                yield return new ScheduleRow(
                    circuit is not null && terminals.TryGetValue(circuit.Id, out var terminal) ? terminal : "",
                    circuit?.Name ?? "— spare —",
                    circuit?.Room ?? "",
                    circuit is null ? "" : Wording.CircuitType(circuit.Type),
                    device.Label,
                    (channel.ChannelIndex + 1).ToString(CultureInfo.InvariantCulture),
                    $"row {device.RowIndex + 1}, module {ModulePosition(device.StartSlot)}");
            }
        }
    }
}
