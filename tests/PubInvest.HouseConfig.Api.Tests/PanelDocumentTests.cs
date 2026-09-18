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

    private static RevisionSnapshot Snapshot(decimal dimmerCost = 60m)
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
                [DeviceCategory.Isolator, DeviceCategory.Terminal240, DeviceCategory.Dimmer240, DeviceCategory.Relay],
                0.8m,
                new PreferredDevices(DimmerType, DimmerType, DimmerType, RelayType, DimmerType, DimmerType, [DimmerType]),
                new TerminalRules(DimmerType, 1, DimmerType, 10, DimmerType, 2),
                "bandPerRow"),
            [
                new DeviceType(DimmerType, "Shelly", "Pro Dimmer 2PM", "PH-DIM", DeviceCategory.Dimmer240, 3, 2, null, null, dimmerCost, true),
                new DeviceType(RelayType, "Shelly", "Pro relay", "PH-REL", DeviceCategory.Relay, 9, 4, null, null, 95m, true),
            ],
            new EnclosureType(Guid.NewGuid(), "NETWORK-CABS", "3 row x 24 way", 3, 72, "IP30", 260m),
            Bom(dimmerCost));
    }

    private static BillOfMaterials Bom(decimal dimmerCost = 60m) => new(
    [
        new BomLine(DimmerType, "PH-DIM", "Shelly Pro Dimmer 2PM", 1, dimmerCost),
        new BomLine(RelayType, "PH-REL", "Shelly Pro relay", 1, 95m),
    ]);

    private static PanelDocument Document(decimal dimmerCost = 60m, bool withBom = true) =>
        new(Snapshot(dimmerCost), DateTimeOffset.UnixEpoch, "tester", withBom ? Bom(dimmerCost) : null);

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
    public void A_priced_bom_shows_its_total()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("Total", text);
        Assert.DoesNotContain("Not priced", text);
    }

    [Fact]
    public void An_unpriced_catalogue_is_not_reported_as_a_zero_total()
    {
        var text = PdfText(Document(dimmerCost: 0m).GeneratePdf());

        Assert.Contains("Not priced", text);
        Assert.DoesNotContain("Total ", text);
    }

    [Fact]
    public void The_footer_records_who_issued_it_and_when()
    {
        var text = PdfText(Document().GeneratePdf());

        Assert.Contains("tester", text);
        Assert.Contains("1970", text);
    }
}
