using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class DesignGenerateTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id, int LayoutVersion);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, bool IsSpare);
    private record DeviceDto(Guid? Id, string Category, string Label, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record DiagnosticDto(string Severity, string Code, string Message, string? Suggestion);
    private record DesignDto(int LayoutVersion, LayoutDto Layout, DiagnosticDto[] Diagnostics);

    private async Task<Guid> NewSubmain(HttpClient client, Guid enclosureId, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Gen {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Generated submain",
            enclosureTypeId = enclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        return submain!.Id;
    }

    [Fact]
    public async Task Generating_persists_devices_and_bumps_the_layout_version()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, TestSeed.EnclosureId,
        [
            new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
            new { type = "Switched", name = "Switched 1", sequence = 2 }
        ]);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.Equal(1, design!.LayoutVersion);
        Assert.All(design.Layout.Devices, d => Assert.NotNull(d.Id));

        await using var db = factory.NewDbContext();
        Assert.Equal(
            design.Layout.Devices.Length,
            await db.DeviceInstances.CountAsync(d => d.SubmainId == submainId));
    }

    [Fact]
    public async Task Regenerating_keeps_circuit_names_and_re_assigns_them()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, TestSeed.EnclosureId,
            [new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 }]);

        await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { });

        Guid keptCircuitId;
        await using (var db = factory.NewDbContext())
        {
            keptCircuitId = (await db.Circuits.SingleAsync(c => c.SubmainId == submainId)).Id;
        }

        // Add circuits so the layout genuinely changes.
        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new
        {
            circuits = new object[]
            {
                new { id = keptCircuitId, type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 },
                new { type = "DimmedLighting", name = "Kitchen island", sequence = 2 },
                new { type = "DimmedLighting", name = "Hall", sequence = 3 },
                new { type = "Switched", name = "Immersion", sequence = 4 }
            }
        });

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.Equal(2, design!.LayoutVersion);
        Assert.DoesNotContain(design.Diagnostics, d => d.Code == "ORPHANED_ASSIGNMENT");
        Assert.Contains(
            design.Layout.Devices.SelectMany(d => d.Channels),
            c => c.CircuitId == keptCircuitId && c.CircuitName == "Kitchen ceiling");
    }

    [Fact]
    public async Task Regenerating_without_a_circuit_reports_it_as_orphaned()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, TestSeed.EnclosureId,
        [
            new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
            new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 }
        ]);

        await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { });

        Guid droppedCircuitId;
        await using (var db = factory.NewDbContext())
        {
            droppedCircuitId = (await db.Circuits
                .SingleAsync(c => c.SubmainId == submainId && c.Name == "Lighting 2")).Id;
        }

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new
        {
            circuits = new object[] { new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 } }
        });

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.Contains(design!.Diagnostics, d =>
            d.Code == "ORPHANED_ASSIGNMENT"
            && d.Severity == "Warning"
            && d.Message.Contains(droppedCircuitId.ToString()));
    }

    /// The wizard's Generate is one call: the submain and its panel are saved
    /// together, so a design the generator refuses creates nothing and a retry
    /// cannot leave a second, empty submain in the house.
    [Fact]
    public async Task Creating_with_generate_saves_the_submain_and_its_panel_in_one_call()
    {
        var client = factory.CreateClient();
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Atomic {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var response = await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Generated at once", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new object[] { new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 } },
            generate = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submain = await response.Content.ReadFromJsonAsync<SubmainDto>();
        Assert.Equal(1, submain!.LayoutVersion);
        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submain.Id}/layout");
        Assert.Contains(stored!.Layout.Devices, d => d.Label == "Dimmer 1");
    }

    [Fact]
    public async Task A_refused_design_creates_no_submain_at_all()
    {
        var client = factory.CreateClient();
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Refused {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var response = await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Too big", enclosureTypeId = TestSeed.TinyEnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Enumerable.Range(1, 30)
                .Select(i => new { type = "DimmedLighting", name = $"Lighting {i}", sequence = i })
                .ToArray(),
            generate = true,
        });

        // The refused design comes back with its diagnostics, as from /design/generate ...
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.Contains(design!.Diagnostics, d => d.Severity == "Error");

        // ... and the house is exactly as it was.
        var submains = await client.GetFromJsonAsync<SubmainDto[]>($"/projects/{project.Id}/submains");
        Assert.Empty(submains!);
    }

    [Fact]
    public async Task A_design_that_does_not_fit_is_rejected_and_nothing_is_persisted()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, TestSeed.TinyEnclosureId,
            Enumerable.Range(1, 30)
                .Select(n => (object)new { type = "Switched", name = $"Switched {n}", sequence = n })
                .ToArray());

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        Assert.Contains(design!.Diagnostics, d => d.Code == "ENCLOSURE_TOO_SMALL" && d.Severity == "Error");
        Assert.Contains(design.Diagnostics, d => d.Suggestion != null);

        await using var db = factory.NewDbContext();
        Assert.Equal(0, await db.DeviceInstances.CountAsync(d => d.SubmainId == submainId));
    }
}
