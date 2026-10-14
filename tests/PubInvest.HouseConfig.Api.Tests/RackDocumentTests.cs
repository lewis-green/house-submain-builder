using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackDocumentTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record ItemDto(Guid Id, string Label);
    private record LayoutDto(ItemDto[] Items);
    private record RevisionDto(Guid Id, int Number);

    private static string PdfText(byte[] bytes)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private static int PageCount(byte[] bytes)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return document.NumberOfPages;
    }

    /// A rack fed from "Ground floor" holding a switch with two labelled ports.
    private async Task<(HttpClient Client, Guid RevisionId)> IssuedRack(string firstPortLabel, Guid? cabinetId = null)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Willow Lane" })).Content.ReadFromJsonAsync<IdDto>();
        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Ground floor", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project.Id}/racks",
            new { name = "Comms cupboard", rackTypeId = cabinetId ?? TestSeed.Cabinet4UId, fedFromSubmainId = submain!.Id }))
            .Content.ReadFromJsonAsync<IdDto>();
        var layout = await (await client.PostAsJsonAsync($"/racks/{rack!.Id}/items", new { rackDeviceTypeId = TestSeed.Switch48Id }))
            .Content.ReadFromJsonAsync<LayoutDto>();
        var item = layout!.Items[0];
        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = firstPortLabel, poe = true });
        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/49", new { label = "Uplink to router", poe = false });

        var revision = await (await client.PostAsJsonAsync($"/racks/{rack.Id}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>();
        return (client, revision!.Id);
    }

    [Fact]
    public async Task The_pdf_is_a_pdf_with_the_title_block_the_schedule_and_the_parts()
    {
        var (client, revisionId) = await IssuedRack("Kitchen AP");

        var response = await client.GetAsync($"/rack-revisions/{revisionId}/export/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        var text = PdfText(bytes);
        Assert.Contains("Comms cupboard", text);
        Assert.Contains("Fed from Ground floor", text);
        Assert.Contains("Revision 1", text);
        Assert.Contains("Switch 1", text);
        Assert.Contains("Kitchen AP", text);
        Assert.Contains("Uplink to router", text);
        Assert.Contains("SFP+", text);
        Assert.Contains("T-SW48", text);
        Assert.Contains("T-CAB4", text);
    }

    [Fact]
    public async Task Every_port_is_scheduled_even_the_unlabelled_ones()
    {
        var (client, revisionId) = await IssuedRack("Kitchen AP");

        var text = PdfText(await client.GetByteArrayAsync($"/rack-revisions/{revisionId}/export/pdf"));

        // Port 52 has no label, but a patching sheet needs its row.
        Assert.Contains("52", text);
        // Of the 52 ports, 2 are labelled; each of the other 50 prints an em
        // dash in its label cell. The headers carry a few more.
        var dashes = text.Count(c => c == '—');
        Assert.True(dashes >= 50, $"Expected an em dash for each of the 50 unlabelled ports, but the PDF text holds {dashes}.");
    }

    /// A 42U cabinet drawn whole on one page would be shrunk past reading.
    /// Printed in bands, it takes more pages than a wall cabinet and the switch
    /// is still named on the drawing.
    [Fact]
    public async Task A_42u_cabinet_prints_in_bands_over_more_pages_than_a_4u_one()
    {
        var (client, smallId) = await IssuedRack("Kitchen AP");
        var (_, tallId) = await IssuedRack("Kitchen AP", TestSeed.Cabinet42UId);

        var small = await client.GetByteArrayAsync($"/rack-revisions/{smallId}/export/pdf");
        var tall = await client.GetByteArrayAsync($"/rack-revisions/{tallId}/export/pdf");

        Assert.True(PageCount(tall) > PageCount(small), $"42U: {PageCount(tall)} pages, 4U: {PageCount(small)} pages.");
        var text = PdfText(tall);
        Assert.Contains("Switch 1", text);
        Assert.Contains("U42–U29", text);
        Assert.Contains("U14–U1", text);
        Assert.Contains("T-CAB42", text);
    }

    /// Review focus: what an electrician types is what prints.
    [Fact]
    public async Task A_label_with_accents_and_symbols_prints_as_typed()
    {
        var (client, revisionId) = await IssuedRack("Café – Bureau ½");

        var text = PdfText(await client.GetByteArrayAsync($"/rack-revisions/{revisionId}/export/pdf"));

        Assert.Contains("Café – Bureau ½", text);
    }

    [Fact]
    public async Task An_unknown_revision_is_not_found()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/rack-revisions/{Guid.NewGuid()}/export/pdf")).StatusCode);
    }
}
