using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class EditEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, bool IsSpare);
    private record DeviceDto(Guid? Id, string Category, string Label, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record DesignDto(int LayoutVersion, LayoutDto Layout);

    private async Task<Guid> SeededProject(HttpClient client)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Edit {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        return project!.Id;
    }

    private async Task<(Guid SubmainId, DesignDto Design)> GeneratedSubmain(HttpClient client, object[] circuits)
    {
        var projectId = await SeededProject(client);

        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Edits",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var design = await (await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

        return (submain.Id, design!);
    }

    [Fact]
    public async Task The_stored_layout_comes_back_with_database_ids_on_every_device()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await GeneratedSubmain(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");

        Assert.Equal(design.LayoutVersion, stored!.LayoutVersion);
        Assert.NotEmpty(stored.Layout.Devices);
        Assert.All(stored.Layout.Devices, d => Assert.NotNull(d.Id));
        Assert.Contains(stored.Layout.Devices.SelectMany(d => d.Channels), c => c.CircuitName == "Lighting 1");
    }

    [Fact]
    public async Task The_layout_of_a_submain_that_was_never_generated_is_empty_not_a_404()
    {
        var client = factory.CreateClient();
        var projectId = await SeededProject(client);

        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Never generated",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submain!.Id}/layout");

        Assert.Equal(0, stored!.LayoutVersion);
        Assert.Empty(stored.Layout.Devices);
    }

    [Fact]
    public async Task Renaming_a_circuit_shows_up_on_its_channel()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await GeneratedSubmain(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var circuitId = design.Layout.Devices
            .SelectMany(d => d.Channels)
            .First(c => c.CircuitId is not null).CircuitId!.Value;

        var response = await client.PatchAsJsonAsync($"/circuits/{circuitId}",
            new { name = "Kitchen ceiling", room = "Kitchen" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");

        Assert.Contains(
            reloaded!.Layout.Devices.SelectMany(d => d.Channels),
            c => c.CircuitName == "Kitchen ceiling");
    }

    [Fact]
    public async Task Renaming_a_circuit_does_not_bump_the_layout_version()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await GeneratedSubmain(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var circuitId = design.Layout.Devices
            .SelectMany(d => d.Channels)
            .First(c => c.CircuitId is not null).CircuitId!.Value;

        await client.PatchAsJsonAsync($"/circuits/{circuitId}", new { name = "Renamed", room = (string?)null });

        await using var db = factory.NewDbContext();
        Assert.Equal(
            design.LayoutVersion,
            (await db.Submains.SingleAsync(s => s.Id == submainId)).LayoutVersion);
    }

    [Fact]
    public async Task A_blank_circuit_name_is_rejected()
    {
        var client = factory.CreateClient();
        var (_, design) = await GeneratedSubmain(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var circuitId = design.Layout.Devices
            .SelectMany(d => d.Channels)
            .First(c => c.CircuitId is not null).CircuitId!.Value;

        var response = await client.PatchAsJsonAsync($"/circuits/{circuitId}",
            new { name = "   ", room = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_channel_can_be_freed_and_bumps_the_layout_version()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await GeneratedSubmain(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var device = design.Layout.Devices.Single(d => d.Label == "Dimmer 1");

        var response = await client.PatchAsJsonAsync(
            $"/devices/{device.Id}/channels/0",
            new { circuitId = (Guid?)null, isSpare = true, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = factory.NewDbContext();
        var channel = await db.DeviceChannels
            .SingleAsync(c => c.DeviceInstanceId == device.Id && c.ChannelIndex == 0);
        Assert.True(channel.IsSpare);
        Assert.Null(channel.CircuitId);
        Assert.Equal(design.LayoutVersion + 1,
            (await db.Submains.SingleAsync(s => s.Id == submainId)).LayoutVersion);
    }

    [Fact]
    public async Task A_stale_channel_edit_is_rejected_with_409_and_changes_nothing()
    {
        var client = factory.CreateClient();
        var (_, design) = await GeneratedSubmain(client,
            [new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 }]);

        var device = design.Layout.Devices.Single(d => d.Label == "Dimmer 1");

        var response = await client.PatchAsJsonAsync(
            $"/devices/{device.Id}/channels/0",
            new { circuitId = (Guid?)null, isSpare = true, basedOnLayoutVersion = design.LayoutVersion - 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = factory.NewDbContext();
        Assert.False((await db.DeviceChannels
            .SingleAsync(c => c.DeviceInstanceId == device.Id && c.ChannelIndex == 0)).IsSpare);
    }

    [Fact]
    public async Task A_circuit_cannot_be_assigned_to_two_channels_at_once()
    {
        var client = factory.CreateClient();
        var (_, design) = await GeneratedSubmain(client,
        [
            new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
            new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
            new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 }
        ]);

        var alreadyUsed = design.Layout.Devices
            .Single(d => d.Label == "Dimmer 1").Channels[0].CircuitId!.Value;
        var secondDevice = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");
        var spareIndex = secondDevice.Channels.First(c => c.IsSpare).ChannelIndex;

        var response = await client.PatchAsJsonAsync(
            $"/devices/{secondDevice.Id}/channels/{spareIndex}",
            new { circuitId = alreadyUsed, isSpare = false, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
