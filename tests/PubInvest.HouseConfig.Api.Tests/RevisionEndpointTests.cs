using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RevisionEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record DesignDto(int LayoutVersion);
    private record RevisionDto(Guid Id, Guid SubmainId, int Number, int LayoutVersion, DateTimeOffset IssuedAt, string IssuedBy, bool Current);
    private record RevisionDetailDto(Guid Id, int Number, int LayoutVersion, bool Current, string SnapshotJson);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName);
    private record DeviceDto(Guid? Id, string Category, string Label, int RowIndex, int StartSlot, ChannelDto[] Channels);
    private record LayoutDto(DeviceDto[] Devices);
    private record StoredDesignDto(int LayoutVersion, LayoutDto Layout);

    private static object[] ThreeDimmed =>
    [
        new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 },
        new { type = "DimmedLighting", name = "Kitchen island", sequence = 2 },
        new { type = "Switched", name = "Immersion", sequence = 3 }
    ];

    private async Task<Guid> NewSubmain(HttpClient client, object[] circuits, object[]? extraFixtures = null)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Rev {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Issued submain",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits,
            extraFixtures = extraFixtures ?? [],
        })).Content.ReadFromJsonAsync<SubmainDto>();

        return submain!.Id;
    }

    private static async Task<RevisionDto> Issue(HttpClient client, Guid submainId) =>
        (await (await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>())!;

    private static async Task<RevisionDto> Listed(HttpClient client, Guid submainId, Guid revisionId) =>
        (await client.GetFromJsonAsync<RevisionDto[]>($"/submains/{submainId}/revisions"))!
            .Single(r => r.Id == revisionId);

    private static string PdfText(byte[] bytes)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private async Task<(Guid SubmainId, DesignDto Design)> Generated(HttpClient client, object[] circuits)
    {
        var submainId = await NewSubmain(client, circuits);
        var design = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();
        return (submainId, design!);
    }

    [Fact]
    public async Task Issuing_a_revision_records_the_layout_version_and_who_issued_it()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, ThreeDimmed);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var revision = await response.Content.ReadFromJsonAsync<RevisionDto>();
        Assert.Equal(design.LayoutVersion, revision!.LayoutVersion);
        Assert.NotEqual(default, revision.IssuedAt);
        Assert.Equal(submainId, revision.SubmainId);
    }

    [Fact]
    public async Task A_revision_keeps_the_catalogue_it_was_issued_with()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await (await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>();

        // An admin renames the dimmer afterwards.
        await using (var db = factory.NewDbContext())
        {
            var dimmer = await db.DeviceTypes.SingleAsync(d => d.Id == TestSeed.DimmerId);
            dimmer.Model = "Renamed after issue";
            await db.SaveChangesAsync();
        }

        var stored = await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{revision!.Id}");

        // What was issued must keep meaning what it meant.
        Assert.DoesNotContain("Renamed after issue", stored!.SnapshotJson);
        Assert.Contains("Test Dimmer 2", stored.SnapshotJson);
    }

    [Fact]
    public async Task A_revision_carries_the_layout_circuits_and_enclosure()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);

        var revision = await (await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>();
        var stored = await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{revision!.Id}");

        Assert.Contains("Kitchen ceiling", stored!.SnapshotJson);
        Assert.Contains("Dimmer 1", stored.SnapshotJson);
        Assert.Contains("Box 6x24", stored.SnapshotJson);
    }

    [Fact]
    public async Task Issuing_a_submain_with_no_panel_is_a_validation_problem()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, ThreeDimmed);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revisions_are_listed_newest_first()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);

        await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });
        await Task.Delay(10);
        await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });

        var list = await client.GetFromJsonAsync<RevisionDto[]>($"/submains/{submainId}/revisions");

        Assert.Equal(2, list!.Length);
        Assert.True(list[0].IssuedAt >= list[1].IssuedAt);
    }

    /// The demo "Ground floor" has a blind; every panel with one printed a 500
    /// while the revision was created and the screen announced success.
    [Fact]
    public async Task A_panel_with_blinds_a_meter_and_network_gear_issues_a_real_pdf()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client,
            [
                new { type = "DimmedLighting", name = "Kitchen ceiling", room = "Kitchen", sequence = 1 },
                new { type = "Cover", name = "Snug blind", room = "Snug", sequence = 2 },
                new { type = "RgbwTape", name = "Cove", room = "Lounge", sequence = 3, wattsPerMetre = 14.4m, lengthMetres = 5m },
            ],
            [
                new { deviceTypeId = TestSeed.MeterId, quantity = 1 },
                new { deviceTypeId = TestSeed.LanId, quantity = 1 },
            ]);
        var generated = await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { });
        Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
        var stored = await client.GetFromJsonAsync<StoredDesignDto>($"/submains/{submainId}/layout");
        Assert.Contains(stored!.Layout.Devices, d => d.Category == "Cover");
        Assert.Contains(stored.Layout.Devices, d => d.Category == "LedController");
        Assert.Contains(stored.Layout.Devices, d => d.Category == "EnergyMeter");
        Assert.Contains(stored.Layout.Devices, d => d.Category == "Network");

        var revision = await Issue(client, submainId);
        var response = await client.GetAsync($"/revisions/{revision.Id}/export/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());

        // What the screen says, the paper says: labels as the server gives them,
        // the kind in the wizard's words, positions counted from one.
        var text = PdfText(bytes);
        Assert.Contains("Cover 1", text);
        Assert.Contains("Blinds", text);
        Assert.Contains("Meter 1", text);
        Assert.Contains("LAN 1", text);
        Assert.Contains("Snug blind", text);
        Assert.DoesNotContain("module 0", text);
        Assert.DoesNotContain("DimmedLighting", text);
        foreach (var device in stored.Layout.Devices.Where(d => d.Channels.Any(c => c.CircuitId is not null)))
        {
            var module = (device.StartSlot / 3m + 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains($"row {device.RowIndex + 1}, module {module}", text);
        }
    }

    /// Staleness is the server's call: the listing compares what each revision
    /// embeds with what issuing now would embed, so a renamed circuit — which
    /// changes no geometry and bumps no layout version — still marks the PDF
    /// out of date, and putting the name back makes it current again.
    [Fact]
    public async Task A_revision_is_current_until_anything_it_prints_changes()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await Issue(client, submainId);
        Assert.True(revision.Current);
        Assert.True((await Listed(client, submainId, revision.Id)).Current);

        var stored = await client.GetFromJsonAsync<StoredDesignDto>($"/submains/{submainId}/layout");
        var channel = stored!.Layout.Devices.SelectMany(d => d.Channels).Single(c => c.CircuitName == "Immersion");

        // A rename: the layout version does not move, the schedule does.
        await client.PatchAsJsonAsync($"/circuits/{channel.CircuitId}", new { name = "Immersion heater", room = "Airing cupboard" });
        var afterRename = await client.GetFromJsonAsync<StoredDesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(stored.LayoutVersion, afterRename!.LayoutVersion);
        Assert.False((await Listed(client, submainId, revision.Id)).Current);
        Assert.False((await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{revision.Id}"))!.Current);

        await client.PatchAsJsonAsync($"/circuits/{channel.CircuitId}", new { name = "Immersion", room = (string?)null });
        Assert.True((await Listed(client, submainId, revision.Id)).Current);

        // A move.
        var dimmer = stored.Layout.Devices.First(d => d.Label == "Dimmer 1");
        var moved = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position", new
        {
            rowIndex = dimmer.RowIndex, startSlot = 12, basedOnLayoutVersion = afterRename.LayoutVersion,
        });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.False((await Listed(client, submainId, revision.Id)).Current);

        // A new revision is current; the old one stays behind.
        var again = await Issue(client, submainId);
        Assert.True((await Listed(client, submainId, again.Id)).Current);
        Assert.False((await Listed(client, submainId, revision.Id)).Current);
    }

    [Fact]
    public async Task Renaming_the_submain_makes_its_revisions_stale()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await Issue(client, submainId);

        await client.PatchAsJsonAsync($"/submains/{submainId}", new
        {
            name = "Renamed submain", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
        });

        Assert.False((await Listed(client, submainId, revision.Id)).Current);
    }

    /// An install option saved without a rebuild changes no device, so the
    /// layout alone would still match the PDF; the snapshot records the options
    /// too, so the PDF reads as out of date until the panel is built for them.
    [Fact]
    public async Task An_install_option_saved_without_a_rebuild_makes_the_revision_stale()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await Issue(client, submainId);
        Assert.True((await Listed(client, submainId, revision.Id)).Current);

        await client.PatchAsJsonAsync($"/submains/{submainId}", new
        {
            name = "Issued submain", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            terminalsAtBottom = true,
        });
        Assert.False((await Listed(client, submainId, revision.Id)).Current);

        await client.PatchAsJsonAsync($"/submains/{submainId}", new
        {
            name = "Issued submain", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            terminalsAtBottom = false,
        });
        Assert.True((await Listed(client, submainId, revision.Id)).Current);
    }

    /// Revisions issued before the snapshot recorded the install options are
    /// judged on what they say, not on the shape of their JSON.
    [Fact]
    public async Task A_revision_issued_before_the_options_were_recorded_is_still_current()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await Issue(client, submainId);

        await using (var db = factory.NewDbContext())
        {
            var stored = await db.PanelRevisions.SingleAsync(r => r.Id == revision.Id);
            Assert.Contains("\"hasIsolator\"", stored.SnapshotJson);
            stored.SnapshotJson = stored.SnapshotJson
                .Replace(",\"hasIsolator\":true", "")
                .Replace(",\"terminalsAtBottom\":false", "");
            Assert.DoesNotContain("hasIsolator", stored.SnapshotJson);
            await db.SaveChangesAsync();
        }

        Assert.True((await Listed(client, submainId, revision.Id)).Current);
    }

    /// Likewise for revisions issued before circuits recorded a terminal place
    /// and count: they read as one terminal each, which is what they were.
    [Fact]
    public async Task A_revision_issued_before_terminal_places_were_recorded_is_still_current()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await Issue(client, submainId);

        await using (var db = factory.NewDbContext())
        {
            var stored = await db.PanelRevisions.SingleAsync(r => r.Id == revision.Id);
            Assert.Contains("\"terminalCount\":1", stored.SnapshotJson);
            stored.SnapshotJson = stored.SnapshotJson
                .Replace(",\"terminalPosition\":null", "")
                .Replace(",\"terminalCount\":1", "");
            Assert.DoesNotContain("terminalCount", stored.SnapshotJson);
            Assert.DoesNotContain("terminalPosition", stored.SnapshotJson);
            await db.SaveChangesAsync();
        }

        Assert.True((await Listed(client, submainId, revision.Id)).Current);
    }

    [Fact]
    public async Task The_server_numbers_revisions_in_the_order_they_were_issued()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);

        var first = await Issue(client, submainId);
        await Task.Delay(10);
        var second = await Issue(client, submainId);

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        var list = await client.GetFromJsonAsync<RevisionDto[]>($"/submains/{submainId}/revisions");
        Assert.Equal([2, 1], list!.Select(r => r.Number).ToArray());
        Assert.Equal(1, (await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{first.Id}"))!.Number);
    }

    /// Two revisions issued in the same tick share a timestamp; they must not
    /// share a number, and the number each answers to must be the one the list
    /// gives it.
    [Fact]
    public async Task Revisions_issued_in_the_same_tick_still_get_distinct_numbers_that_agree_with_the_list()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var first = await Issue(client, submainId);
        var second = await Issue(client, submainId);

        await using (var db = factory.NewDbContext())
        {
            var sameTick = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            await foreach (var r in db.PanelRevisions.Where(r => r.SubmainId == submainId).AsAsyncEnumerable())
            {
                r.IssuedAt = sameTick;
            }
            await db.SaveChangesAsync();
        }

        var list = (await client.GetFromJsonAsync<RevisionDto[]>($"/submains/{submainId}/revisions"))!;
        Assert.Equal([2, 1], list.Select(r => r.Number).ToArray());
        foreach (var id in new[] { first.Id, second.Id })
        {
            var detail = await client.GetFromJsonAsync<RevisionDetailDto>($"/revisions/{id}");
            Assert.Equal(list.Single(r => r.Id == id).Number, detail!.Number);
        }
    }

    [Fact]
    public async Task An_unknown_revision_is_a_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/revisions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
