using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Bom;

namespace PubInvest.HouseConfig.Api.Tests;

public class BomCsvTests
{
    [Fact]
    public void A_description_with_a_comma_is_quoted()
    {
        var csv = BomCsv.Write(new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "P-1", "Terminal, grey", 3)]), "Test");

        Assert.Contains("\"Terminal, grey\"", csv);
    }

    [Fact]
    public void A_description_with_a_quote_has_it_doubled()
    {
        var csv = BomCsv.Write(new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "P-2", "6\" rail", 1)]), "Test");

        Assert.Contains("\"6\"\" rail\"", csv);
    }

    [Fact]
    public void The_csv_is_a_parts_list_with_no_money_in_it()
    {
        var csv = BomCsv.Write(new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "P-1", "Dimmer", 2),
            new BomLine(Guid.NewGuid(), "P-2", "Driver", 1, PanelMounted: false)]), "Test");

        Assert.Contains("Part,Description,Quantity,Panel mounted", csv);
        Assert.Contains("P-1,Dimmer,2,yes", csv);
        Assert.Contains("P-2,Driver,1,no", csv);
        Assert.DoesNotContain("Total", csv);
        Assert.DoesNotContain("cost", csv, StringComparison.OrdinalIgnoreCase);
    }
}

[Collection("api")]
public class BomExportTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record BomLineDto(string PartNumber, string Description, int Quantity, bool PanelMounted);
    private record BomDto(BomLineDto[] Lines);

    private static object[] Circuits =>
    [
        new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 },
        new { type = "DimmedLighting", name = "Hall", sequence = 2 },
        new { type = "Switched", name = "Immersion", sequence = 3 }
    ];

    private async Task Seed()
    {
        await using var db = factory.NewDbContext();
        await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
    }

    private async Task<(Guid ProjectId, Guid SubmainId)> Issued(HttpClient client)
    {
        await Seed();

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Bom {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Bom submain",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits = Circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { });
        await client.PostAsJsonAsync($"/submains/{submain.Id}/revisions", new { });

        return (project.Id, submain.Id);
    }

    [Fact]
    public async Task A_submain_bom_aggregates_identical_parts()
    {
        var client = factory.CreateClient();
        var (_, submainId) = await Issued(client);

        var bom = await client.GetFromJsonAsync<BomDto>($"/submains/{submainId}/bom");

        Assert.NotEmpty(bom!.Lines);
        Assert.Equal(bom.Lines.Select(l => l.PartNumber).Distinct().Count(), bom.Lines.Length);

        // Four circuits' worth of terminals across three conductors, in one line.
        var terminals = bom.Lines.Single(l => l.PartNumber == "T-TB");
        Assert.True(terminals.Quantity > 1);
    }

    [Fact]
    public async Task A_bom_includes_accessories_that_occupy_no_slots()
    {
        var client = factory.CreateClient();
        var (_, submainId) = await Issued(client);

        var bom = await client.GetFromJsonAsync<BomDto>($"/submains/{submainId}/bom");

        Assert.Contains(bom!.Lines, l => l.PartNumber == "T-BAR");
        Assert.Contains(bom.Lines, l => l.PartNumber == "T-STOP");
    }

    [Fact]
    public async Task A_house_bom_sums_its_submains()
    {
        var client = factory.CreateClient();
        var (projectId, submainId) = await Issued(client);

        var single = await client.GetFromJsonAsync<BomDto>($"/submains/{submainId}/bom");
        var house = await client.GetFromJsonAsync<BomDto>($"/projects/{projectId}/bom");

        var singleDimmers = single!.Lines.Single(l => l.PartNumber == "T-DIM2").Quantity;
        var houseDimmers = house!.Lines.Single(l => l.PartNumber == "T-DIM2").Quantity;

        Assert.Equal(singleDimmers, houseDimmers);
    }

    [Fact]
    public async Task The_external_driver_is_marked_as_not_panel_mounted()
    {
        var client = factory.CreateClient();
        var (_, submainId) = await Issued(client);

        var bom = await client.GetFromJsonAsync<BomDto>($"/submains/{submainId}/bom");

        Assert.All(bom!.Lines.Where(l => l.PartNumber.StartsWith("T-PSU")), l => Assert.False(l.PanelMounted));
        Assert.All(bom.Lines.Where(l => l.PartNumber == "T-TB"), l => Assert.True(l.PanelMounted));
    }

    [Fact]
    public async Task A_house_with_nothing_issued_has_an_empty_parts_list()
    {
        var client = factory.CreateClient();
        await Seed();

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Empty {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var bom = await client.GetFromJsonAsync<BomDto>($"/projects/{project!.Id}/bom");

        Assert.Empty(bom!.Lines);
    }

    [Fact]
    public async Task The_csv_download_is_served_as_a_file()
    {
        var client = factory.CreateClient();
        var (_, submainId) = await Issued(client);

        var response = await client.GetAsync($"/submains/{submainId}/bom.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Part,Description,Quantity", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_submain_with_no_issued_revision_has_no_bom()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/submains/{Guid.NewGuid()}/bom");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_pdf_can_be_downloaded_for_a_submain()
    {
        var client = factory.CreateClient();
        var (_, submainId) = await Issued(client);

        var response = await client.GetAsync($"/submains/{submainId}/export/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }
}
