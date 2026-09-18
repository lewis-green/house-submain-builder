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
            new BomLine(Guid.NewGuid(), "P-1", "Terminal, grey", 3, 1.50m)]), "Test");

        Assert.Contains("\"Terminal, grey\"", csv);
    }

    [Fact]
    public void A_description_with_a_quote_has_it_doubled()
    {
        var csv = BomCsv.Write(new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "P-2", "6\" rail", 1, 2m)]), "Test");

        Assert.Contains("\"6\"\" rail\"", csv);
    }

    [Fact]
    public void A_priced_bom_ends_with_its_total()
    {
        var csv = BomCsv.Write(new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "P-1", "Dimmer", 2, 60m),
            new BomLine(Guid.NewGuid(), "P-2", "Relay", 1, 95m)]), "Test");

        Assert.Contains("Total,215.00", csv);
        Assert.DoesNotContain("Not priced", csv);
    }

    [Fact]
    public void An_unpriced_line_leaves_its_cost_cells_empty_rather_than_zero()
    {
        var csv = BomCsv.Write(new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "P-1", "Dimmer", 2, 0m)]), "Test");

        Assert.Contains("P-1,Dimmer,2,,", csv);
        Assert.Contains("Not priced (1 of 1 parts have no cost)", csv);
        Assert.DoesNotContain("0.00", csv);
    }
}

[Collection("api")]
public class BomExportTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record BomLineDto(string PartNumber, string Description, int Quantity, decimal UnitCost);
    private record BomDto(BomLineDto[] Lines, decimal? Total, int UnpricedLines, bool Priced);

    private static object[] Circuits =>
    [
        new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 },
        new { type = "DimmedLighting", name = "Hall", sequence = 2 },
        new { type = "Switched", name = "Immersion", sequence = 3 }
    ];

    private async Task<(Guid ProjectId, Guid SubmainId)> Issued(HttpClient client)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

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
    public async Task A_bom_reports_a_total_when_everything_is_priced()
    {
        var client = factory.CreateClient();
        var (_, submainId) = await Issued(client);

        var bom = await client.GetFromJsonAsync<BomDto>($"/submains/{submainId}/bom");

        Assert.True(bom!.Priced);
        Assert.NotNull(bom.Total);
        Assert.Equal(0, bom.UnpricedLines);
    }

    [Fact]
    public async Task A_bom_with_an_unpriced_part_reports_no_total_at_all()
    {
        var client = factory.CreateClient();

        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
            var relay = await db.DeviceTypes.SingleAsync(d => d.Id == TestSeed.RelayId);
            relay.Cost = 0m;
            await db.SaveChangesAsync();
        }

        var (_, submainId) = await Issued(client);
        var bom = await client.GetFromJsonAsync<BomDto>($"/submains/{submainId}/bom");

        Assert.False(bom!.Priced);
        Assert.Null(bom.Total);
        Assert.True(bom.UnpricedLines > 0);
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
