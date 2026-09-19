using System.Text;
using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Api.Revisions;
using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;
using QuestPDF.Fluent;
using UglyToad.PdfPig;

namespace PubInvest.HouseConfig.Api.Tests;

public class PanelDocumentTests
{
    static PanelDocumentTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private static readonly Guid CircuitA = new("77777777-0000-4000-8000-000000000001");
    private static readonly Guid CircuitB = new("77777777-0000-4000-8000-000000000002");
    private static readonly Guid DimmerType = new("77777777-0000-4000-8000-000000000010");
    private static readonly Guid RelayType = new("77777777-0000-4000-8000-000000000011");

    private static RevisionSnapshot Snapshot()
    {
        var devices = new List<PlacedDevice>
        {
            new(DimmerType, DeviceCategory.Dimmer240, 1, 0, 3, "Dimmer 1",
                [new ChannelAssignment(0, CircuitA, false), new ChannelAssignment(1, null, true)], TerminalRole.None),
            new(RelayType, DeviceCategory.Relay, 2, 0, 9, "Relay 1",
                [new ChannelAssignment(0, CircuitB, false)], TerminalRole.None),
        };

        return new RevisionSnapshot(
            "Willow House",
            "Ground Floor West",
            "GF-W",
            new PanelLayout(3, 54, devices),
            [
                new CircuitSnapshot(CircuitA, "DimmedLighting", "Kitchen ceiling", "Kitchen", 1),
                new CircuitSnapshot(CircuitB, "Switched", "Immersion", "Airing cupboard", 2),
            ],
            new RuleSetPayload(
                [
                    new PanelLayoutOption(
                    [
                        new PackingZone([DeviceCategory.Terminal240], [DeviceCategory.Isolator]),
                        new PackingZone([DeviceCategory.Dimmer240], [DeviceCategory.Relay]),
                    ]),
                ],
                0.8m,
                new PreferredDevices(DimmerType, DimmerType, DimmerType, RelayType, DimmerType, DimmerType, [DimmerType]),
                new TerminalRules(DimmerType, 1, DimmerType, 10, DimmerType, 2)),
            [
                new DeviceType(DimmerType, "Shelly", "Pro Dimmer 2PM", "PH-DIM", DeviceCategory.Dimmer240, 3, 2, null, null, true),
                new DeviceType(RelayType, "Shelly", "Pro relay", "PH-REL", DeviceCategory.Relay, 9, 4, null, null, true),
            ],
            new EnclosureType(Guid.NewGuid(), "NETWORK-CABS", "3 row x 24 way", 3, 72, "IP30"),
            Bom());
    }

    private static BillOfMaterials Bom() => new(
    [
        new BomLine(DimmerType, "PH-DIM", "Shelly Pro Dimmer 2PM", 1),
        new BomLine(RelayType, "PH-REL", "Shelly Pro relay", 1, PanelMounted: false),
    ]);

    private static PanelDocument Document(bool withBom = true) =>
        new(Snapshot(), DateTimeOffset.UnixEpoch, "tester", withBom ? Bom() : null);

    private static string PdfText(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        var text = new StringBuilder();
        foreach (var page in document.GetPages()) text.AppendLine(page.Text);
        return text.ToString();
    }

    [Fact]
    public void A_pdf_is_produced_and_is_a_pdf()
    {
        var bytes = Document().GeneratePdf();

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }

    [Fact]
    public void The_header_names_the_house_and_the_submain()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("Willow House", text);
        Assert.Contains("Ground Floor West", text);
    }

    [Fact]
    public void The_schedule_names_every_assigned_circuit()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("Kitchen ceiling", text);
        Assert.Contains("Immersion", text);
    }

    [Fact]
    public void The_schedule_names_the_room_each_circuit_feeds()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("Kitchen ceiling", text);
        Assert.Contains("Airing cupboard", text);
    }

    [Fact]
    public void The_schedule_shows_a_spare_channel_rather_than_omitting_it()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("spare", text);
    }

    [Fact]
    public void Positions_are_reported_in_modules_not_slot_units()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("module", text);
        Assert.DoesNotContain("slot unit", text);
    }

    [Fact]
    public void The_bill_of_materials_lists_parts_with_no_money_in_it()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("Bill of materials", text);
        Assert.Contains("PH-DIM", text);
        Assert.DoesNotContain("Total", text);
        Assert.DoesNotContain("£", text);
    }

    [Fact]
    public void A_part_that_is_not_panel_mounted_says_so()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("external", text);
    }

    [Fact]
    public void The_footer_records_who_issued_it_and_when()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("tester", text);
        Assert.Contains("1970", text);
    }
}
