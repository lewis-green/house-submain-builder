using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class DevicePositionTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record DeviceDto(Guid? Id, string Category, string Label, int RowIndex, int StartSlot, int ModuleWidth);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record DiagnosticDto(string Severity, string Code, string Message, string? Suggestion);
    private record DesignDto(int LayoutVersion, LayoutDto Layout, DiagnosticDto[] Diagnostics);

    private async Task<(Guid SubmainId, DesignDto Design)> Generated(HttpClient client, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Move {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Moves",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var design = await (await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

        return (submain.Id, design!);
    }

    /// TestSeed's enclosure is 6 rows of 24 slots and its dimmer is 2 slots wide.
    /// Slot 12 is clear of the dimmers packing from the left at 0-6 and of the
    /// 4-slot relay packing from the right at 20-23.
    private const int FreeSlot = 12;

    private static object[] ThreeDimmed =>
    [
        new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
        new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
        new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 }
    ];

    [Fact]
    public async Task Moving_a_device_persists_the_new_slot_and_bumps_the_version()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, ThreeDimmed);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = FreeSlot, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = factory.NewDbContext();
        Assert.Equal(FreeSlot, (await db.DeviceInstances.SingleAsync(d => d.Id == dimmer.Id)).StartSlot);
        Assert.Equal(design.LayoutVersion + 1,
            (await db.Submains.SingleAsync(s => s.Id == submainId)).LayoutVersion);
    }

    [Fact]
    public async Task A_move_onto_an_occupied_slot_is_rejected_and_changes_nothing()
    {
        var client = factory.CreateClient();
        var (_, design) = await Generated(client, ThreeDimmed);
        var first = design.Layout.Devices.Single(d => d.Label == "Dimmer 1");
        var second = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{second.Id}/position",
            new { rowIndex = first.RowIndex, startSlot = first.StartSlot, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var db = factory.NewDbContext();
        Assert.Equal(second.StartSlot, (await db.DeviceInstances.SingleAsync(d => d.Id == second.Id)).StartSlot);
    }

    [Fact]
    public async Task A_move_outside_the_enclosure_is_rejected()
    {
        var client = factory.CreateClient();
        var (_, design) = await Generated(client, ThreeDimmed);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = 99, startSlot = 0, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_stale_move_is_rejected_with_409()
    {
        var client = factory.CreateClient();
        var (_, design) = await Generated(client, ThreeDimmed);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = FreeSlot, basedOnLayoutVersion = design.LayoutVersion - 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_moved_device_stays_put_when_the_design_is_regenerated()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, ThreeDimmed);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = FreeSlot, basedOnLayoutVersion = design.LayoutVersion });

        var regenerated = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new
        {
            circuits = new object[]
            {
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
                new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 },
                new { type = "Switched", name = "Immersion", sequence = 4 }
            }
        })).Content.ReadFromJsonAsync<DesignDto>();

        Assert.Equal(FreeSlot, regenerated!.Layout.Devices.Single(d => d.Label == "Dimmer 2").StartSlot);
        Assert.DoesNotContain(regenerated.Diagnostics, d => d.Code == "POSITION_OVERRIDE_DROPPED");
    }

    [Fact]
    public async Task An_override_the_engineer_set_is_never_silently_abandoned()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, ThreeDimmed);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = FreeSlot, basedOnLayoutVersion = design.LayoutVersion });

        // Enough extra circuits that the terminal band grows across the row the
        // engineer moved the dimmer into.
        var many = Enumerable.Range(1, 20)
            .Select(n => (object)new { type = "DimmedLighting", name = $"Lighting {n}", sequence = n })
            .ToArray();

        var regenerated = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate",
            new { circuits = many })).Content.ReadFromJsonAsync<DesignDto>();

        // Either it was honoured, or the engineer was told why not. Never neither.
        var moved = regenerated!.Layout.Devices.Single(d => d.Label == "Dimmer 2");
        var honoured = moved.StartSlot == FreeSlot && moved.RowIndex == dimmer.RowIndex;
        var reported = regenerated.Diagnostics.Any(
            d => d.Code == "POSITION_OVERRIDE_DROPPED" && d.Message.Contains("Dimmer 2"));

        // As the layout stands today this scenario takes the dropped-and-reported
        // branch (verified by asserting !honoured once). The invariant is what is
        // asserted, so a future packing change cannot make this test lie.
        Assert.True(honoured || reported,
            $"Dimmer 2 ended at row {moved.RowIndex} slot {moved.StartSlot} with no diagnostic explaining why.");
    }
}
