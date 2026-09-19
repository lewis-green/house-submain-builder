using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// Which end the cables enter, and whether this submain is isolated upstream.
[Collection("api")]
public class PanelOptionTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id, bool HasIsolator, bool TerminalsAtBottom, int LayoutVersion);
    private record DeviceDto(string Category, int RowIndex, int StartSlot, string Label);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record DesignDto(int LayoutVersion, LayoutDto Layout);

    private static readonly object[] Circuits =
    [
        new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
        new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
        new { type = "Switched", name = "Switched 1", sequence = 3 },
    ];

    private async Task<Guid> SeededProject(HttpClient client)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Options {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        return project!.Id;
    }

    private async Task<SubmainDto> Submain(HttpClient client, object body)
    {
        var projectId = await SeededProject(client);
        var response = await client.PostAsJsonAsync($"/projects/{projectId}/submains", body);

        return (await response.Content.ReadFromJsonAsync<SubmainDto>())!;
    }

    private static async Task<DesignDto> Generate(HttpClient client, Guid submainId) =>
        (await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>())!;

    private static int RowOf(DesignDto design, string category) =>
        design.Layout.Devices.Where(d => d.Category == category).Select(d => d.RowIndex).Distinct().Single();

    [Fact]
    public async Task A_submain_has_an_isolator_and_top_terminations_unless_it_says_otherwise()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Defaults", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Circuits,
        });

        Assert.True(submain.HasIsolator);
        Assert.False(submain.TerminalsAtBottom);
    }

    [Fact]
    public async Task Terminations_move_to_the_bottom_rail_when_the_submain_is_glanded_from_below()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Bottom fed", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            terminalsAtBottom = true, circuits = Circuits,
        });

        var design = await Generate(client, submain.Id);

        Assert.Equal(design.Layout.Rows - 1, RowOf(design, "Terminal240"));
        Assert.True(RowOf(design, "Terminal240") > RowOf(design, "Dimmer240"));
    }

    [Fact]
    public async Task A_submain_isolated_upstream_generates_without_an_isolator_and_without_complaint()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Upstream isolation", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            hasIsolator = false, circuits = Circuits,
        });

        var response = await client.PostAsJsonAsync($"/submains/{submain.Id}/design/generate", new { });
        var design = (await response.Content.ReadFromJsonAsync<DesignDto>())!;

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain(design.Layout.Devices, d => d.Category == "Isolator");
    }

    [Fact]
    public async Task Updating_a_submain_without_mentioning_the_options_leaves_them_alone()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Keep", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            hasIsolator = false, terminalsAtBottom = true, circuits = Circuits,
        });

        // The wizard patches a subset of the fields; a missing bool must not read
        // as false and quietly refit the isolator.
        var patched = await (await client.PatchAsJsonAsync($"/submains/{submain.Id}", new
        {
            name = "Renamed", enclosureTypeId = TestSeed.EnclosureId,
        })).Content.ReadFromJsonAsync<SubmainDto>();

        Assert.False(patched!.HasIsolator);
        Assert.True(patched.TerminalsAtBottom);
    }

    [Fact]
    public async Task Turning_the_panel_over_on_an_existing_submain_regenerates_it_the_other_way_up()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Flip", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Circuits,
        });

        var before = await Generate(client, submain.Id);
        Assert.Equal(0, RowOf(before, "Terminal240"));

        await client.PatchAsJsonAsync($"/submains/{submain.Id}", new
        {
            name = "Flip", enclosureTypeId = TestSeed.EnclosureId, terminalsAtBottom = true,
        });

        var after = await Generate(client, submain.Id);

        Assert.Equal(after.Layout.Rows - 1, RowOf(after, "Terminal240"));
        Assert.True(after.LayoutVersion > before.LayoutVersion);
    }

    [Fact]
    public async Task The_wizard_can_preview_both_options_before_the_submain_is_saved_either_way()
    {
        var client = factory.CreateClient();
        var submain = await Submain(client, new
        {
            name = "Preview", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Circuits,
        });

        var preview = await (await client.PostAsJsonAsync($"/submains/{submain.Id}/design/preview", new
        {
            enclosureTypeId = TestSeed.EnclosureId,
            terminalsAtBottom = true,
            hasIsolator = false,
            circuits = Circuits,
        })).Content.ReadFromJsonAsync<DesignDto>();

        Assert.Equal(preview!.Layout.Rows - 1, RowOf(preview, "Terminal240"));
        Assert.DoesNotContain(preview.Layout.Devices, d => d.Category == "Isolator");

        // Preview persists nothing: the submain is still as it was created.
        var stored = await client.GetFromJsonAsync<SubmainDto>($"/submains/{submain.Id}");
        Assert.True(stored!.HasIsolator);
        Assert.False(stored.TerminalsAtBottom);
    }
}
