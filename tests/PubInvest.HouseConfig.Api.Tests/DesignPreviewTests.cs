using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class DesignPreviewTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record SummaryDto(int RowsUsed, int SlotsUsed, int TotalSlots, int DeviceCount, int SpareChannels, decimal BomTotal);
    private record DiagnosticDto(string Severity, string Code, string Message, string? Suggestion);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, bool IsSpare);
    private record DeviceDto(Guid? Id, Guid DeviceTypeId, string Category, int RowIndex, int StartSlot, int ModuleWidth, string Label, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record BomLineDto(string PartNumber, int Quantity, decimal LineTotal);
    private record DesignDto(int LayoutVersion, LayoutDto Layout, DiagnosticDto[] Diagnostics, BomLineDto[] Bom, SummaryDto Summary);

    private async Task<Guid> SeededSubmain(HttpClient client, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Preview {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Preview submain",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        return submain!.Id;
    }

    [Fact]
    public async Task Preview_returns_a_banded_layout_and_a_summary()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client,
        [
            new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
            new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
            new { type = "Switched", name = "Switched 1", sequence = 3 }
        ]);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.NotEmpty(design!.Layout.Devices);
        Assert.All(
            design.Layout.Devices.Where(d => d.Category == "Terminal240"),
            d => Assert.Equal(0, d.RowIndex));
        Assert.Equal(design.Layout.Devices.Length, design.Summary.DeviceCount);
        Assert.NotEmpty(design.Bom);
    }

    [Fact]
    public async Task Preview_names_the_circuit_on_each_assigned_channel()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client,
            [new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 }]);

        var design = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

        Assert.Contains(
            design!.Layout.Devices.SelectMany(d => d.Channels),
            c => c.CircuitName == "Kitchen ceiling");
    }

    [Fact]
    public async Task Preview_persists_nothing()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client, [new { type = "Switched", name = "Switched 1", sequence = 1 }]);

        await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new { });

        await using var db = factory.NewDbContext();
        Assert.DoesNotContain(db.DeviceInstances, d => d.SubmainId == submainId);
    }

    [Fact]
    public async Task Preview_honours_an_overridden_circuit_list()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client, [new { type = "Switched", name = "Switched 1", sequence = 1 }]);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new
        {
            circuits = new object[]
            {
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
                new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 }
            }
        });

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.Contains(design!.Layout.Devices, d => d.Category == "Dimmer240");
        Assert.DoesNotContain(design.Layout.Devices, d => d.Category == "Relay");
    }

    [Fact]
    public async Task A_submain_with_no_enclosure_is_a_validation_problem_not_a_500()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "No enclosure" }))
            .Content.ReadFromJsonAsync<ProjectDto>();
        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Bare",
            circuits = new object[] { new { type = "Switched", name = "Switched 1", sequence = 1 } }
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var response = await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/preview", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Catalogue_endpoints_list_seeded_data()
    {
        var client = factory.CreateClient();
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/catalogue/device-types")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/catalogue/enclosures")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/catalogue/rulesets")).StatusCode);
    }
}
