# House Panel Designer — Designer UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the engineer a mobile-friendly app that turns circuit counts into a to-scale panel drawing they can rearrange by dragging, and name channels on.

**Architecture:** React 19 + Vite + TypeScript + Tailwind talking to the existing .NET API. The server stays authoritative: every layout comes from `PanelGenerator`, and every edit is validated server-side against the submain's `layoutVersion`. Drag feels instant because the client applies the move optimistically and reverts on a `409`.

**Tech Stack:** React 19, Vite, TypeScript, Tailwind 4, `react-oidc-context` for Keycloak, Vitest + Testing Library, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-17-house-panel-designer-design.md`
**Builds on:** `docs/superpowers/plans/2026-09-17-backend-core.md` (merged)

## Global Constraints

- Widths are in **slot units**, where 3 slot units = 1 DIN module (T). `DinUnits.PerModule` is the server-side constant; the UI mirrors it as `SLOT_UNITS_PER_MODULE = 3` and divides by it for anything a person reads.
- Mobile-first. Every screen must work at 390 px wide. Touch targets ≥ 44 px.
- The UI never computes a layout. It renders what `/design/preview` or `/design/generate` returned. Any layout arithmetic in the client is a bug.
- Every mutating call sends the `layoutVersion` it was based on. A `409` is a normal outcome, not an error page.
- API project stays on `net10.0`, central package management, plain xUnit `Assert` (no assertion library).
- UI runs at `http://localhost:5173`; the API's `HouseConfig:AllowedOrigins` must include it.
- No new npm dependency without a reason stated in the task that adds it.

## The one design decision in this plan

**How a dragged device survives re-generation.**

Backend core replaces device rows wholesale on re-generate, which is correct only while nothing on a device was put there by hand. Dragging changes that: a position the engineer chose is real work, and losing it when they add one circuit would be as bad as losing a captured MAC would have been.

The approach here: a dragged device is recorded as a **position override keyed by device label** (`Dimmer 2`, `L7` — labels are deterministic, see `DeviceDemandCalculator`). Re-generation packs normally, then re-applies each override where the target slots are free and in the right band. An override that no longer fits is dropped with a `POSITION_OVERRIDE_DROPPED` warning naming the device, so the engineer is told rather than quietly moved.

Rejected alternatives: keying overrides by database id (ids change on re-generate, so nothing would match), and freezing overridden devices before packing (one awkward override could push everything else into a worse layout with no way to see why).

## File Structure

```
house-config/
  src/PubInvest.HouseConfig.Domain/
    Generation/PositionOverride.cs      label -> (row, slot), and the re-apply pass
    Generation/OverrideApplier.cs       re-applies overrides after packing
  src/PubInvest.HouseConfig.Data/
    Entities/DeviceInstance.cs          + IsManuallyPlaced
    Entities/PositionOverrideRow.cs     survives device row replacement
  src/PubInvest.HouseConfig.Api/
    Endpoints/DeviceEndpoints.cs        position + channel edits
    Endpoints/CircuitEndpoints.cs       circuit rename
    Contracts/EditContracts.cs
  ui/
    index.html
    vite.config.ts                      dev proxy to the API
    src/main.tsx                        OIDC provider + router
    src/api/client.ts                   typed fetch wrapper, 409 handling
    src/api/types.ts                    mirrors the API contracts
    src/routes/ProjectsPage.tsx
    src/routes/ProjectPage.tsx          submain list
    src/routes/NewSubmainPage.tsx       the wizard
    src/routes/PanelPage.tsx            the drawing
    src/panel/PanelSvg.tsx              to-scale renderer, pure
    src/panel/useDeviceDrag.ts          pointer-events drag, slot snapping
    src/panel/DeviceSheet.tsx           bottom sheet: channels and names
    src/components/                     Button, Field, Sheet, Diagnostics
    src/units.ts                        SLOT_UNITS_PER_MODULE and formatters
  ui/tests/                             Vitest
  ui/e2e/                               Playwright
```

---

### Task 1: Stored layout, circuit rename and channel assignment endpoints

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Contracts/EditContracts.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/CircuitEndpoints.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/DeviceEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/DesignEndpoints.cs` (add `GET /submains/{id}/layout`)
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs` (map the new groups)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/EditEndpointTests.cs`

**Interfaces:**
- Consumes: `HouseConfigDbContext`, entities from backend core.
- Produces: `UpdateCircuitRequest(string Name, string? Room)`, `UpdateChannelRequest(Guid? CircuitId, bool IsSpare, int BasedOnLayoutVersion)`, `MapCircuitEndpoints()`, `MapDeviceEndpoints()`.

**Endpoints:** `GET /submains/{id}/layout`, `PATCH /circuits/{id}`, `PATCH /devices/{id}/channels/{index}`.

`GET /layout` is the one the panel screen loads. It reads the **stored** devices, so every device carries its database id and can be dragged. `/design/preview` cannot serve this purpose: it regenerates from scratch and returns `id: null` on every device, which is right for a wizard and useless for editing.

Renaming a circuit does **not** touch `layoutVersion` — it changes no geometry, so it needs no concurrency token and two people renaming different circuits never conflict. Re-assigning a channel does bump it.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/EditEndpointTests.cs`:

```csharp
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

    private async Task<(Guid SubmainId, DesignDto Design)> GeneratedSubmain(HttpClient client, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Edit {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
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

        var reloaded = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

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
```

Add to the same test class:

```csharp
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
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Empty {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();
        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Never generated", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submain!.Id}/layout");

        Assert.Equal(0, stored!.LayoutVersion);
        Assert.Empty(stored.Layout.Devices);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter EditEndpointTests`
Expected: FAIL — all three routes 404.

- [ ] **Step 3: Write the contracts**

`src/PubInvest.HouseConfig.Api/Contracts/EditContracts.cs`:

```csharp
namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record UpdateCircuitRequest(string Name, string? Room);

public sealed record UpdateChannelRequest(Guid? CircuitId, bool IsSpare, int BasedOnLayoutVersion);

public sealed record UpdatePositionRequest(int RowIndex, int StartSlot, int BasedOnLayoutVersion);

/// Returned with 409 so the client can re-render from current state
/// instead of guessing what changed.
public sealed record ConflictResponse(string Message, int CurrentLayoutVersion);
```

- [ ] **Step 4: Write the circuit endpoint**

`src/PubInvest.HouseConfig.Api/Endpoints/CircuitEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class CircuitEndpoints
{
    public static IEndpointRouteBuilder MapCircuitEndpoints(this IEndpointRouteBuilder app)
    {
        // Renaming changes no geometry, so it carries no layoutVersion and
        // never conflicts with someone renaming a different circuit.
        app.MapPatch("/circuits/{id:guid}", async (
                Guid id,
                UpdateCircuitRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["name"] = ["A circuit name is required."]
                    });
                }

                var circuit = await db.Circuits.SingleOrDefaultAsync(c => c.Id == id, ct);
                if (circuit is null) return Results.NotFound();

                circuit.Name = request.Name.Trim();
                circuit.Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim();
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { circuit.Id, circuit.Name, circuit.Room });
            })
            .WithTags("Circuits");

        return app;
    }
}
```

- [ ] **Step 5: Write the channel endpoint**

`src/PubInvest.HouseConfig.Api/Endpoints/DeviceEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/devices/{id:guid}/channels/{index:int}", async (
                Guid id,
                int index,
                UpdateChannelRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var device = await db.DeviceInstances.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null) return Results.NotFound();

                var submain = await db.Submains.SingleAsync(s => s.Id == device.SubmainId, ct);
                if (submain.LayoutVersion != request.BasedOnLayoutVersion)
                {
                    return Results.Json(
                        new ConflictResponse(
                            "This panel changed since you loaded it. Reload and try again.",
                            submain.LayoutVersion),
                        statusCode: StatusCodes.Status409Conflict);
                }

                var channel = await db.DeviceChannels
                    .SingleOrDefaultAsync(c => c.DeviceInstanceId == id && c.ChannelIndex == index, ct);
                if (channel is null) return Results.NotFound();

                if (request.CircuitId is { } circuitId)
                {
                    var circuit = await db.Circuits
                        .SingleOrDefaultAsync(c => c.Id == circuitId && c.SubmainId == device.SubmainId, ct);

                    if (circuit is null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["circuitId"] = ["That circuit is not on this submain."]
                        });
                    }

                    var alreadyElsewhere = await db.DeviceChannels
                        .Where(c => c.CircuitId == circuitId)
                        .AnyAsync(c => c.DeviceInstanceId != id || c.ChannelIndex != index, ct);

                    if (alreadyElsewhere)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["circuitId"] = ["That circuit is already wired to another channel. Free it first."]
                        });
                    }
                }

                channel.CircuitId = request.CircuitId;
                channel.IsSpare = request.CircuitId is null;

                submain.LayoutVersion++;
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { submain.LayoutVersion });
            })
            .WithTags("Devices");

        return app;
    }
}
```

`IsSpare` is derived from whether a circuit is present rather than trusted from the request: a channel with no circuit that is not spare would be a state with no meaning.

- [ ] **Step 6: Write the stored-layout endpoint**

Add to `DesignEndpoints`, reading rows rather than regenerating:

```csharp
app.MapGet("/submains/{id:guid}/layout", async (
        Guid id,
        HouseConfigDbContext db,
        CancellationToken ct) =>
    {
        var submain = await db.Submains
            .Include(s => s.Circuits)
            .SingleOrDefaultAsync(s => s.Id == id, ct);

        if (submain is null) return Results.NotFound();

        var devices = await db.DeviceInstances
            .Include(d => d.Channels)
            .Where(d => d.SubmainId == id)
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .ToListAsync(ct);

        var enclosure = submain.EnclosureTypeId is null
            ? null
            : await db.Enclosures.SingleOrDefaultAsync(e => e.Id == submain.EnclosureTypeId, ct);

        var names = submain.Circuits.ToDictionary(c => c.Id, c => c.Name);

        var placed = devices.Select(d => new PlacedDeviceResponse(
            d.Id,
            d.DeviceTypeId,
            d.Category,
            d.RowIndex,
            d.StartSlot,
            d.ModuleWidth,
            d.Label,
            d.TerminalRole,
            d.Channels.OrderBy(c => c.ChannelIndex).Select(c => new ChannelResponse(
                c.ChannelIndex,
                c.CircuitId,
                c.CircuitId is not null && names.TryGetValue(c.CircuitId.Value, out var n) ? n : null,
                c.IsSpare)).ToList())).ToList();

        var rowsUsed = placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;

        return Results.Ok(new DesignResponse(
            id,
            submain.LayoutVersion,
            new LayoutResponse(enclosure?.Rows ?? 0, enclosure?.SlotsPerRow ?? 0, placed),
            [],
            [],
            new DesignSummary(
                rowsUsed,
                placed.Sum(d => d.ModuleWidth),
                (enclosure?.Rows ?? 0) * (enclosure?.SlotsPerRow ?? 0),
                placed.Count,
                placed.SelectMany(d => d.Channels).Count(c => c.IsSpare),
                0m)));
    })
    .WithTags("Design");
```

Diagnostics and BOM come back empty here: both are products of *generating*, and a stored layout is a record of a generation that already happened. The panel screen shows the diagnostics returned by the generate call that produced the layout, not re-derived ones.

- [ ] **Step 7: Map all groups in Program.cs**

Next to the existing `app.MapDesignEndpoints();`:

```csharp
app.MapCircuitEndpoints();
app.MapDeviceEndpoints();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: PASS, 25 tests.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add stored layout, circuit rename and channel endpoints"
```

---

### Task 2: Position overrides in the Domain

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/PositionOverride.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Generation/OverrideApplier.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Diagnostics/Diagnostic.cs` (add the code)
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/PanelGenerator.cs` (apply overrides after packing)
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/OverrideApplierTests.cs`

**Interfaces:**
- Consumes: `PanelLayout`, `PlacedDevice`, `Diagnostic`, `DeviceCategory`.
- Produces: `PositionOverride(string Label, int RowIndex, int StartSlot)`; `OverrideApplier.Apply(PanelLayout, IReadOnlyList<PositionOverride>) -> (PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics)`; `DiagnosticCodes.PositionOverrideDropped = "POSITION_OVERRIDE_DROPPED"`; `GenerationRequest` gains `IReadOnlyList<PositionOverride> Overrides` (default empty).

**Rules:**
- An override moves the device with that label to `(RowIndex, StartSlot)`.
- It is applied only if the target range `[StartSlot, StartSlot + ModuleWidth)` is inside the row and free of every device that is not itself being moved.
- Overrides are applied in a stable order (row, then slot, then label) so two overrides that would collide resolve the same way every time.
- A rejected override is dropped and reported as a `Warning` naming the device and why.
- The device vacates its generated slot when moved, so the space it leaves is free for later overrides.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/OverrideApplierTests.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class OverrideApplierTests
{
    private static PlacedDevice Device(string label, int row, int slot, int width = 3) =>
        new(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, row, slot, width, label, [], TerminalRole.None);

    [Fact]
    public void An_override_moves_the_device_it_names()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0), Device("Dimmer 2", 1, 3)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 2", 1, 30)]);

        var dimmer2 = moved.Devices.Single(d => d.Label == "Dimmer 2");
        Assert.Equal(1, dimmer2.RowIndex);
        Assert.Equal(30, dimmer2.StartSlot);
        Assert.Equal(0, moved.Devices.Single(d => d.Label == "Dimmer 1").StartSlot);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_override_naming_a_device_that_no_longer_exists_is_reported()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (_, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 9", 1, 30)]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.PositionOverrideDropped, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Dimmer 9", diagnostic.Message);
    }

    [Fact]
    public void An_override_onto_an_occupied_slot_is_dropped_and_reported()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0), Device("Dimmer 2", 1, 3)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 2", 1, 0)]);

        Assert.Equal(3, moved.Devices.Single(d => d.Label == "Dimmer 2").StartSlot);
        Assert.Equal(DiagnosticCodes.PositionOverrideDropped, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void An_override_running_past_the_end_of_a_row_is_dropped()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 1", 1, 52)]);

        Assert.Equal(0, moved.Devices.Single(d => d.Label == "Dimmer 1").StartSlot);
        Assert.Contains("does not fit", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void An_override_onto_a_row_that_does_not_exist_is_dropped()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, [new PositionOverride("Dimmer 1", 9, 0)]);

        Assert.Equal(1, moved.Devices.Single(d => d.Label == "Dimmer 1").RowIndex);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void A_device_can_move_into_the_slot_another_override_vacates()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0), Device("Dimmer 2", 1, 3)]);

        // Dimmer 1 leaves slot 0, so Dimmer 2 may take it.
        var (moved, diagnostics) = OverrideApplier.Apply(layout,
        [
            new PositionOverride("Dimmer 1", 1, 30),
            new PositionOverride("Dimmer 2", 1, 0)
        ]);

        Assert.Equal(30, moved.Devices.Single(d => d.Label == "Dimmer 1").StartSlot);
        Assert.Equal(0, moved.Devices.Single(d => d.Label == "Dimmer 2").StartSlot);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void No_overrides_returns_the_layout_untouched()
    {
        var layout = new PanelLayout(4, 54, [Device("Dimmer 1", 1, 0)]);

        var (moved, diagnostics) = OverrideApplier.Apply(layout, []);

        Assert.Same(layout, moved);
        Assert.Empty(diagnostics);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter OverrideApplierTests`
Expected: FAIL — `OverrideApplier` does not exist.

- [ ] **Step 3: Add the diagnostic code**

In `DiagnosticCodes`:

```csharp
public const string PositionOverrideDropped = "POSITION_OVERRIDE_DROPPED";
```

- [ ] **Step 4: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/PositionOverride.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Generation;

/// A position the engineer chose by dragging, keyed by the device's generated
/// label rather than its database id: ids are re-issued on every re-generation,
/// labels are deterministic.
public sealed record PositionOverride(string Label, int RowIndex, int StartSlot);
```

`src/PubInvest.HouseConfig.Domain/Generation/OverrideApplier.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

public static class OverrideApplier
{
    public static (PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics) Apply(
        PanelLayout layout,
        IReadOnlyList<PositionOverride> overrides)
    {
        if (overrides.Count == 0) return (layout, []);

        var devices = layout.Devices.ToList();
        var diagnostics = new List<Diagnostic>();

        // Stable order so two overrides that would collide always resolve the same way.
        var ordered = overrides
            .OrderBy(o => o.RowIndex)
            .ThenBy(o => o.StartSlot)
            .ThenBy(o => o.Label, StringComparer.Ordinal);

        foreach (var request in ordered)
        {
            var index = devices.FindIndex(d => d.Label == request.Label);
            if (index < 0)
            {
                diagnostics.Add(Dropped(request, "it is not in this design any more"));
                continue;
            }

            var device = devices[index];
            var end = request.StartSlot + device.ModuleWidth;

            if (request.RowIndex < 0 || request.RowIndex >= layout.Rows)
            {
                diagnostics.Add(Dropped(request, $"row {request.RowIndex + 1} does not exist"));
                continue;
            }

            if (request.StartSlot < 0 || end > layout.SlotsPerRow)
            {
                diagnostics.Add(Dropped(request, "it does not fit in that row"));
                continue;
            }

            var blocked = devices.Any(other =>
                !ReferenceEquals(other, device)
                && other.RowIndex == request.RowIndex
                && other.StartSlot < end
                && request.StartSlot < other.EndSlotExclusive);

            if (blocked)
            {
                diagnostics.Add(Dropped(request, "another device is already there"));
                continue;
            }

            devices[index] = device with { RowIndex = request.RowIndex, StartSlot = request.StartSlot };
        }

        return (layout with { Devices = devices }, diagnostics);
    }

    private static Diagnostic Dropped(PositionOverride request, string why) => new(
        DiagnosticSeverity.Warning,
        DiagnosticCodes.PositionOverrideDropped,
        $"'{request.Label}' could not be kept where you put it because {why}; it has been placed automatically.",
        "Drag it again if the new position is not what you want.");
}
```

- [ ] **Step 5: Apply overrides in PanelGenerator**

Add `IReadOnlyList<PositionOverride> Overrides` to `GenerationRequest` with a default of `[]` so existing call sites keep compiling, then after `BandPacker.Pack`:

```csharp
var (layout, overrideDiagnostics) = OverrideApplier.Apply(packed.Layout, request.Overrides);
diagnostics.AddRange(overrideDiagnostics);

var bom = BomBuilder.Build(layout, terminals.Accessories, request.Enclosure, request.Catalogue);

return new GenerationResult(layout, diagnostics, bom);
```

Overrides run **after** packing and before the BOM, so the parts list counts the same devices the drawing shows.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 51 tests. The golden-file test must still pass unchanged — a design with no overrides is unaffected.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: keep dragged device positions across re-generation"
```

---

### Task 3: Persist position overrides and the move endpoint

**Files:**
- Create: `src/PubInvest.HouseConfig.Data/Entities/PositionOverrideRow.cs`
- Modify: `src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs` (DbSet + unique index)
- Create: `src/PubInvest.HouseConfig.Data/Migrations/` (new migration `PositionOverrides`)
- Modify: `src/PubInvest.HouseConfig.Api/Services/DesignService.cs` (load overrides, record moves)
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/DeviceEndpoints.cs` (add the position route)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/DevicePositionTests.cs`

**Interfaces:**
- Consumes: `PositionOverride`, `UpdatePositionRequest`, `ConflictResponse`.
- Produces: `PositionOverrideRow(Guid Id, Guid SubmainId, string Label, int RowIndex, int StartSlot)`; `PATCH /devices/{id}/position`.

**Behaviour:** moving a device writes (or replaces) the override for its label, moves the device row, and bumps `layoutVersion`. `LoadAsync` passes the submain's overrides into `GenerationRequest`, so the next re-generate honours them. A move is validated against the *current* layout — same fit rules as `OverrideApplier` — and rejected with `422` and a reason if it will not fit, rather than being stored and silently dropped later.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/DevicePositionTests.cs`:

```csharp
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

    private static object[] TwoDimmers =>
    [
        new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
        new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
        new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 }
    ];

    [Fact]
    public async Task Moving_a_device_persists_the_new_slot_and_bumps_the_version()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, TwoDimmers);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = 30, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = factory.NewDbContext();
        Assert.Equal(30, (await db.DeviceInstances.SingleAsync(d => d.Id == dimmer.Id)).StartSlot);
        Assert.Equal(design.LayoutVersion + 1,
            (await db.Submains.SingleAsync(s => s.Id == submainId)).LayoutVersion);
    }

    [Fact]
    public async Task A_move_onto_an_occupied_slot_is_rejected_and_changes_nothing()
    {
        var client = factory.CreateClient();
        var (_, design) = await Generated(client, TwoDimmers);
        var first = design.Layout.Devices.Single(d => d.Label == "Dimmer 1");
        var second = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{second.Id}/position",
            new { rowIndex = first.RowIndex, startSlot = first.StartSlot, basedOnLayoutVersion = design.LayoutVersion });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var db = factory.NewDbContext();
        Assert.Equal(second.StartSlot, (await db.DeviceInstances.SingleAsync(d => d.Id == second.Id)).StartSlot);
    }

    [Fact]
    public async Task A_stale_move_is_rejected_with_409()
    {
        var client = factory.CreateClient();
        var (_, design) = await Generated(client, TwoDimmers);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        var response = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = 30, basedOnLayoutVersion = design.LayoutVersion - 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_moved_device_stays_put_when_the_design_is_regenerated()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, TwoDimmers);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = 30, basedOnLayoutVersion = design.LayoutVersion });

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

        Assert.Equal(30, regenerated!.Layout.Devices.Single(d => d.Label == "Dimmer 2").StartSlot);
        Assert.DoesNotContain(regenerated.Diagnostics, d => d.Code == "POSITION_OVERRIDE_DROPPED");
    }

    [Fact]
    public async Task An_override_that_no_longer_fits_is_dropped_with_a_warning()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client, TwoDimmers);
        var dimmer = design.Layout.Devices.Single(d => d.Label == "Dimmer 2");

        await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/position",
            new { rowIndex = dimmer.RowIndex, startSlot = 30, basedOnLayoutVersion = design.LayoutVersion });

        // Enough terminals to push the dimmer band onto a later row, so row/slot 30 is taken.
        var many = Enumerable.Range(1, 40)
            .Select(n => (object)new { type = "DimmedLighting", name = $"Lighting {n}", sequence = n })
            .ToArray();

        var regenerated = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate",
            new { circuits = many, enclosureTypeId = TestSeed.EnclosureId })).Content.ReadFromJsonAsync<DesignDto>();

        // Either it fitted or it was reported — never silently moved with no word.
        var dimmer2 = regenerated!.Layout.Devices.Single(d => d.Label == "Dimmer 2");
        if (dimmer2.StartSlot != 30)
        {
            Assert.Contains(regenerated.Diagnostics,
                d => d.Code == "POSITION_OVERRIDE_DROPPED" && d.Message.Contains("Dimmer 2"));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter DevicePositionTests`
Expected: FAIL — the position route 404s.

- [ ] **Step 3: Add the entity and migration**

`src/PubInvest.HouseConfig.Data/Entities/PositionOverrideRow.cs`:

```csharp
namespace PubInvest.HouseConfig.Data.Entities;

/// Survives the device rows being replaced on re-generation, which is the whole
/// point: it is keyed by label, not by device id.
public class PositionOverrideRow
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public string Label { get; set; } = "";
    public int RowIndex { get; set; }
    public int StartSlot { get; set; }
}
```

In `HouseConfigDbContext`:

```csharp
public DbSet<PositionOverrideRow> PositionOverrides => Set<PositionOverrideRow>();
```

and in `OnModelCreating`:

```csharp
b.Entity<PositionOverrideRow>().HasIndex(o => new { o.SubmainId, o.Label }).IsUnique();
b.Entity<Submain>().HasMany<PositionOverrideRow>().WithOne()
    .HasForeignKey(o => o.SubmainId).OnDelete(DeleteBehavior.Cascade);
```

Then:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add PositionOverrides \
  --project src/PubInvest.HouseConfig.Data \
  --startup-project src/PubInvest.HouseConfig.Data \
  --output-dir Migrations
```

- [ ] **Step 4: Load overrides in DesignService**

In `LoadAsync`, before building the `GenerationRequest`:

```csharp
var overrides = (await db.PositionOverrides
        .Where(o => o.SubmainId == submainId)
        .OrderBy(o => o.Label)
        .ToListAsync(ct))
    .Select(o => new PositionOverride(o.Label, o.RowIndex, o.StartSlot))
    .ToList();
```

and pass `Overrides: overrides` into the `GenerationRequest`.

- [ ] **Step 5: Write the position endpoint**

Add to `DeviceEndpoints`:

```csharp
app.MapPatch("/devices/{id:guid}/position", async (
        Guid id,
        UpdatePositionRequest request,
        HouseConfigDbContext db,
        CancellationToken ct) =>
    {
        var device = await db.DeviceInstances.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return Results.NotFound();

        var submain = await db.Submains.SingleAsync(s => s.Id == device.SubmainId, ct);
        if (submain.LayoutVersion != request.BasedOnLayoutVersion)
        {
            return Results.Json(
                new ConflictResponse(
                    "This panel changed since you loaded it. Reload and try again.",
                    submain.LayoutVersion),
                statusCode: StatusCodes.Status409Conflict);
        }

        var enclosure = submain.EnclosureTypeId is null
            ? null
            : await db.Enclosures.SingleOrDefaultAsync(e => e.Id == submain.EnclosureTypeId, ct);

        if (enclosure is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["design"] = ["This submain has no enclosure."]
            });
        }

        var end = request.StartSlot + device.ModuleWidth;

        if (request.RowIndex < 0 || request.RowIndex >= enclosure.Rows
            || request.StartSlot < 0 || end > enclosure.SlotsPerRow)
        {
            return Results.Json(
                new { message = "That position is outside the enclosure." },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var blocked = await db.DeviceInstances.AnyAsync(other =>
            other.SubmainId == device.SubmainId
            && other.Id != device.Id
            && other.RowIndex == request.RowIndex
            && other.StartSlot < end
            && request.StartSlot < other.StartSlot + other.ModuleWidth, ct);

        if (blocked)
        {
            return Results.Json(
                new { message = "Another device is already in that space." },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        device.RowIndex = request.RowIndex;
        device.StartSlot = request.StartSlot;

        var existing = await db.PositionOverrides
            .SingleOrDefaultAsync(o => o.SubmainId == device.SubmainId && o.Label == device.Label, ct);

        if (existing is null)
        {
            db.PositionOverrides.Add(new Data.Entities.PositionOverrideRow
            {
                Id = Guid.NewGuid(),
                SubmainId = device.SubmainId,
                Label = device.Label,
                RowIndex = request.RowIndex,
                StartSlot = request.StartSlot
            });
        }
        else
        {
            existing.RowIndex = request.RowIndex;
            existing.StartSlot = request.StartSlot;
        }

        submain.LayoutVersion++;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { submain.LayoutVersion, device.RowIndex, device.StartSlot });
    })
    .WithTags("Devices");
```

The unique `(SubmainId, RowIndex, StartSlot)` index on `DeviceInstances` also guards this at the database level, so a race that slips past the check still cannot produce two devices in one slot.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS across all four projects.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: persist dragged positions and honour them on re-generation"
```

---

### Task 4: UI scaffold, units and the typed API client

**Files:**
- Create: `ui/` via Vite, `ui/vite.config.ts`, `ui/tailwind.config.js`, `ui/src/index.css`
- Create: `ui/src/units.ts`, `ui/src/api/types.ts`, `ui/src/api/client.ts`
- Create: `ui/tests/units.test.ts`, `ui/tests/client.test.ts`
- Modify: `src/PubInvest.HouseConfig.Api/appsettings.Development.json` (allow the dev origin)

**Interfaces:**
- Produces: `SLOT_UNITS_PER_MODULE`, `toModules(slotUnits)`, `formatModules(slotUnits)`; the TypeScript mirrors of `DesignResponse`, `LayoutResponse`, `PlacedDeviceResponse`, `ChannelResponse`, `DiagnosticResponse`, `BomLineResponse`, `DesignSummary`, `ProjectResponse`, `SubmainResponse`; and `api` with `get/post/patch`, plus `ApiError` carrying `status`, `problem` and `conflict`.

- [ ] **Step 1: Scaffold**

```bash
cd /Users/lewis/src/pubinvest/house-config
npm create vite@latest ui -- --template react-ts
cd ui
npm install
npm install -D tailwindcss @tailwindcss/vite vitest @testing-library/react @testing-library/user-event jsdom
npm install react-router
```

`react-router` is the only runtime dependency beyond React: the app has four screens and no shared server state worth a data library. Everything else is dev-only.

`ui/vite.config.ts`:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: { '/api': { target: 'http://localhost:5020', changeOrigin: true, rewrite: p => p.replace(/^\/api/, '') } },
  },
  test: { environment: 'jsdom', globals: true },
})
```

The dev proxy means the browser only ever talks to one origin, so CORS is a production concern rather than something to fight locally.

- [ ] **Step 2: Write the failing tests**

`ui/tests/units.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { SLOT_UNITS_PER_MODULE, formatModules, toModules } from '../src/units'

describe('slot units', () => {
  it('counts three slot units to a DIN module', () => {
    expect(SLOT_UNITS_PER_MODULE).toBe(3)
    expect(toModules(3)).toBe(1)
    expect(toModules(9)).toBe(3)
  })

  it('formats a whole module without a decimal point', () => {
    expect(formatModules(3)).toBe('1T')
    expect(formatModules(9)).toBe('3T')
  })

  it('formats a part module to one decimal place', () => {
    expect(formatModules(1)).toBe('0.3T')
    expect(formatModules(4)).toBe('1.3T')
  })
})
```

`ui/tests/client.test.ts`:

```ts
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../src/api/client'

describe('api client', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('returns parsed json on success', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 'abc' }), { status: 200, headers: { 'content-type': 'application/json' } })))

    await expect(api.get('/projects')).resolves.toEqual({ id: 'abc' })
  })

  it('marks a 409 as a conflict so callers can revert instead of erroring', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ message: 'changed', currentLayoutVersion: 7 }),
        { status: 409, headers: { 'content-type': 'application/json' } })))

    const error = await api.patch('/devices/1/position', {}).catch(e => e as ApiError)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.conflict).toBe(true)
    expect(error.currentLayoutVersion).toBe(7)
  })

  it('surfaces validation problems field by field', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ title: 'invalid', errors: { name: ['A circuit name is required.'] } }),
        { status: 400, headers: { 'content-type': 'application/problem+json' } })))

    const error = await api.post('/projects', {}).catch(e => e as ApiError)

    expect(error.fieldErrors.name).toEqual(['A circuit name is required.'])
  })

  it('does not treat a 422 as a conflict', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ message: 'no room' }), { status: 422, headers: { 'content-type': 'application/json' } })))

    const error = await api.patch('/devices/1/position', {}).catch(e => e as ApiError)

    expect(error.conflict).toBe(false)
    expect(error.status).toBe(422)
  })
})
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd ui && npx vitest run`
Expected: FAIL — neither module exists.

- [ ] **Step 4: Write units.ts**

```ts
/** Mirrors DinUnits.PerModule on the server. Three of these make one DIN module (T). */
export const SLOT_UNITS_PER_MODULE = 3

export const toModules = (slotUnits: number) => slotUnits / SLOT_UNITS_PER_MODULE

export function formatModules(slotUnits: number): string {
  const modules = toModules(slotUnits)
  return Number.isInteger(modules) ? `${modules}T` : `${modules.toFixed(1)}T`
}
```

- [ ] **Step 5: Write api/types.ts**

Mirror the API contracts exactly — field names are camelCase because the API serialises that way:

```ts
export interface ProjectResponse { id: string; name: string; address: string | null; notes: string | null; submainCount: number }

export interface SubmainResponse {
  id: string; projectId: string; name: string; reference: string | null
  feedCableSize: string | null; originBreakerAmps: number | null; phase: string | null
  enclosureTypeId: string | null; ruleSetId: string | null; notes: string | null
  layoutVersion: number; circuitCount: number; deviceCount: number
}

export interface ChannelResponse { channelIndex: number; circuitId: string | null; circuitName: string | null; isSpare: boolean }

export interface PlacedDeviceResponse {
  id: string | null; deviceTypeId: string; category: DeviceCategory
  rowIndex: number; startSlot: number; moduleWidth: number
  label: string; terminalRole: TerminalRole; channels: ChannelResponse[]
}

export type DeviceCategory = 'Terminal240' | 'Dimmer240' | 'Dimmer0_10V' | 'Relay' | 'Psu24V' | 'Accessory'
export type TerminalRole = 'None' | 'Line' | 'Neutral' | 'Earth'
export type Severity = 'Info' | 'Warning' | 'Error'

export interface LayoutResponse { rows: number; slotsPerRow: number; devices: PlacedDeviceResponse[] }
export interface DiagnosticResponse { severity: Severity; code: string; message: string; suggestion: string | null }
export interface BomLineResponse { catalogueId: string; partNumber: string; description: string; quantity: number; unitCost: number; lineTotal: number }
export interface DesignSummary { rowsUsed: number; slotsUsed: number; totalSlots: number; deviceCount: number; spareChannels: number; bomTotal: number }

export interface DesignResponse {
  submainId: string; layoutVersion: number; layout: LayoutResponse
  diagnostics: DiagnosticResponse[]; bom: BomLineResponse[]; summary: DesignSummary
}

export interface EnclosureType { id: string; manufacturer: string; model: string; rows: number; slotsPerRow: number; ipRating: string; cost: number; totalSlots: number; description: string }

export interface CircuitInput {
  id?: string; type: 'DimmedLighting' | 'Switched' | 'LedTape'
  name: string; room?: string | null; sequence: number
  wattsPerMetre?: number | null; lengthMetres?: number | null
}
```

- [ ] **Step 6: Write api/client.ts**

```ts
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly fieldErrors: Record<string, string[]> = {},
    readonly currentLayoutVersion?: number,
  ) { super(message) }

  /** A 409 means someone else changed the panel: reload, don't report a failure. */
  get conflict() { return this.status === 409 }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method,
    headers: body === undefined ? {} : { 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const text = await response.text()
  const payload = text ? JSON.parse(text) : null

  if (response.ok) return payload as T

  throw new ApiError(
    response.status,
    payload?.title ?? payload?.message ?? `Request failed (${response.status})`,
    payload?.errors ?? {},
    payload?.currentLayoutVersion,
  )
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd ui && npx vitest run`
Expected: PASS, 8 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: scaffold ui with typed api client and slot unit helpers"
```

---

### Task 5: Projects and submains screens

**Files:**
- Create: `ui/src/main.tsx` (router), `ui/src/App.tsx`
- Create: `ui/src/routes/ProjectsPage.tsx`, `ui/src/routes/ProjectPage.tsx`
- Create: `ui/src/components/Button.tsx`, `Field.tsx`, `Spinner.tsx`, `ErrorNote.tsx`
- Create: `ui/tests/ProjectsPage.test.tsx`
- Delete: the Vite template's `App.css`, `assets/`, boilerplate in `App.tsx`

**Interfaces:**
- Consumes: `api`, `ProjectResponse`, `SubmainResponse`.
- Produces: routes `/` (projects), `/projects/:projectId` (submains), and the shared form components every later screen uses.

**Screens:** the projects list shows name, address and submain count with an inline "new project" form. A project shows its submains with state at a glance — *not designed* when `deviceCount === 0`, otherwise `n devices · m circuits` — and links to the wizard and the panel.

- [ ] **Step 1: Write the failing test**

`ui/tests/ProjectsPage.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProjectsPage } from '../src/routes/ProjectsPage'

const projects = [{ id: 'p1', name: 'Willow House', address: '1 Lane', notes: null, submainCount: 2 }]

describe('ProjectsPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists the projects it loads', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify(projects), { status: 200, headers: { 'content-type': 'application/json' } })))

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)

    expect(await screen.findByText('Willow House')).toBeTruthy()
    expect(screen.getByText(/2 submains/)).toBeTruthy()
  })

  it('shows an empty state rather than a bare list', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })))

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)

    expect(await screen.findByText(/no houses yet/i)).toBeTruthy()
  })

  it('reports a field error from the API instead of failing silently', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(
        JSON.stringify({ title: 'invalid', errors: { name: ['A project name is required.'] } }),
        { status: 400, headers: { 'content-type': 'application/problem+json' } }))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)
    await screen.findByText(/no houses yet/i)

    await userEvent.click(screen.getByRole('button', { name: /add house/i }))

    await waitFor(() => expect(screen.getByText('A project name is required.')).toBeTruthy())
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ui && npx vitest run ProjectsPage`
Expected: FAIL — `ProjectsPage` does not exist.

- [ ] **Step 3: Write the shared components**

`ui/src/components/Button.tsx` — a button with `variant` of `primary | secondary | ghost`, `min-height: 44px`, disabled styling, and `type="button"` by default so a stray button never submits a form.

`ui/src/components/Field.tsx` — label, input, optional hint, and an `errors?: string[]` list rendered in red beneath, wired with `aria-describedby` and `aria-invalid`.

`ui/src/components/Spinner.tsx` — an inline loading indicator with `role="status"` and an accessible name.

`ui/src/components/ErrorNote.tsx` — a red panel taking a message and optional retry handler.

Write these as plain typed function components with Tailwind classes; no component library.

- [ ] **Step 4: Write ProjectsPage**

```tsx
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Spinner } from '../components/Spinner'

export function ProjectsPage() {
  const [projects, setProjects] = useState<ProjectResponse[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  const load = () =>
    api.get<ProjectResponse[]>('/projects').then(setProjects).catch((e: ApiError) => setLoadError(e.message))

  useEffect(() => { void load() }, [])

  async function add() {
    setSaving(true)
    setFieldErrors({})
    try {
      await api.post<ProjectResponse>('/projects', { name, address: address || null, notes: null })
      setName('')
      setAddress('')
      await load()
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) setLoadError(error.message)
    } finally {
      setSaving(false)
    }
  }

  if (loadError) return <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />
  if (!projects) return <Spinner label="Loading houses" />

  return (
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Houses</h1>

      {projects.length === 0 ? (
        <p className="text-slate-500">No houses yet. Add one below to get started.</p>
      ) : (
        <ul className="space-y-2">
          {projects.map(p => (
            <li key={p.id}>
              <Link to={`/projects/${p.id}`} className="block rounded-lg border p-4 active:bg-slate-50">
                <span className="font-medium">{p.name}</span>
                {p.address && <span className="block text-sm text-slate-500">{p.address}</span>}
                <span className="block text-sm text-slate-500">
                  {p.submainCount} {p.submainCount === 1 ? 'submain' : 'submains'}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      <div className="space-y-3 rounded-lg border p-4">
        <Field label="House name" value={name} onChange={setName} errors={fieldErrors.name} />
        <Field label="Address" value={address} onChange={setAddress} />
        <Button onClick={add} disabled={saving}>Add house</Button>
      </div>
    </div>
  )
}
```

- [ ] **Step 5: Write ProjectPage and the router**

`ProjectPage` loads `/projects/{id}` and `/projects/{id}/submains`, renders each submain as a card showing *not designed* when `deviceCount === 0` and `{deviceCount} devices · {circuitCount} circuits` otherwise, with a link to `/submains/:submainId/panel` and a "new submain" button routing to `/projects/:projectId/submains/new`.

`main.tsx` mounts `BrowserRouter` with routes `/`, `/projects/:projectId`, `/projects/:projectId/submains/new`, `/submains/:submainId/panel`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ui && npx vitest run`
Expected: PASS, 11 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add projects and submains screens"
```

---

### Task 6: The new-submain wizard with live preview

**Files:**
- Create: `ui/src/routes/NewSubmainPage.tsx`
- Create: `ui/src/wizard/CircuitCounts.tsx`, `ui/src/wizard/TapeCircuits.tsx`, `ui/src/wizard/PreviewSummary.tsx`
- Create: `ui/src/wizard/buildCircuits.ts`
- Create: `ui/tests/buildCircuits.test.ts`, `ui/tests/PreviewSummary.test.tsx`

**Interfaces:**
- Consumes: `api`, `CircuitInput`, `DesignResponse`, `EnclosureType`, `formatModules`.
- Produces: `buildCircuits({ dimmed, switched, tape }) -> CircuitInput[]`; `PreviewSummary` component; route `/projects/:projectId/submains/new`.

**Flow:** name and supply details → enclosure picker (from `/catalogue/enclosures`) → counts for dimmed and switched, plus one row per LED tape circuit with W/m and length → live preview → generate, which navigates to the panel.

The wizard creates the submain first (so it has an id), then calls `/design/preview` on each change, debounced by 400 ms. Preview persists nothing, so an abandoned wizard leaves an empty submain and no devices.

- [ ] **Step 1: Write the failing tests**

`ui/tests/buildCircuits.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { buildCircuits } from '../src/wizard/buildCircuits'

describe('buildCircuits', () => {
  it('numbers each type from one and sequences them across the whole set', () => {
    const circuits = buildCircuits({ dimmed: 2, switched: 1, tape: [{ wattsPerMetre: 14.4, lengthMetres: 5 }] })

    expect(circuits.map(c => [c.type, c.name, c.sequence])).toEqual([
      ['DimmedLighting', 'Lighting 1', 1],
      ['DimmedLighting', 'Lighting 2', 2],
      ['Switched', 'Switched 1', 3],
      ['LedTape', 'Tape 1', 4],
    ])
  })

  it('carries tape dimensions onto the tape circuits only', () => {
    const circuits = buildCircuits({ dimmed: 1, switched: 0, tape: [{ wattsPerMetre: 9.6, lengthMetres: 4 }] })

    expect(circuits[0].wattsPerMetre).toBeUndefined()
    expect(circuits[1].wattsPerMetre).toBe(9.6)
    expect(circuits[1].lengthMetres).toBe(4)
  })

  it('returns nothing for an empty submain', () => {
    expect(buildCircuits({ dimmed: 0, switched: 0, tape: [] })).toEqual([])
  })
})
```

`ui/tests/PreviewSummary.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { PreviewSummary } from '../src/wizard/PreviewSummary'
import type { DesignResponse } from '../src/api/types'

const design = (overrides: Partial<DesignResponse['summary']> = {}, diagnostics: DesignResponse['diagnostics'] = []) => ({
  submainId: 's1', layoutVersion: 0,
  layout: { rows: 4, slotsPerRow: 54, devices: [
    { id: null, deviceTypeId: 'd', category: 'Dimmer240', rowIndex: 1, startSlot: 0, moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None', channels: [] },
    { id: null, deviceTypeId: 'r', category: 'Relay', rowIndex: 2, startSlot: 0, moduleWidth: 9, label: 'Relay 1', terminalRole: 'None', channels: [] },
  ] },
  diagnostics, bom: [],
  summary: { rowsUsed: 3, slotsUsed: 36, totalSlots: 216, deviceCount: 2, spareChannels: 1, bomTotal: 0, ...overrides },
}) as DesignResponse

describe('PreviewSummary', () => {
  it('counts devices by kind in modules, not slot units', () => {
    render(<PreviewSummary design={design()} />)

    expect(screen.getByText(/1 dimmer/i)).toBeTruthy()
    expect(screen.getByText(/1 relay/i)).toBeTruthy()
    expect(screen.getByText(/12T of 72T/i)).toBeTruthy()
  })

  it('shows an error diagnostic with its suggestion', () => {
    render(<PreviewSummary design={design({}, [
      { severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'Needs 5 rows', suggestion: 'Use the 6x24' },
    ])} />)

    expect(screen.getByText(/needs 5 rows/i)).toBeTruthy()
    expect(screen.getByText(/use the 6x24/i)).toBeTruthy()
  })

  it('says a design fits when there is nothing to report', () => {
    render(<PreviewSummary design={design()} />)

    expect(screen.getByText(/fits/i)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ui && npx vitest run buildCircuits PreviewSummary`
Expected: FAIL — neither module exists.

- [ ] **Step 3: Write buildCircuits**

```ts
import type { CircuitInput } from '../api/types'

export interface TapeInput { wattsPerMetre: number; lengthMetres: number }
export interface CountsInput { dimmed: number; switched: number; tape: TapeInput[] }

export function buildCircuits({ dimmed, switched, tape }: CountsInput): CircuitInput[] {
  const circuits: CircuitInput[] = []
  let sequence = 1

  for (let n = 1; n <= dimmed; n++) {
    circuits.push({ type: 'DimmedLighting', name: `Lighting ${n}`, sequence: sequence++ })
  }
  for (let n = 1; n <= switched; n++) {
    circuits.push({ type: 'Switched', name: `Switched ${n}`, sequence: sequence++ })
  }
  tape.forEach((t, i) => {
    circuits.push({
      type: 'LedTape',
      name: `Tape ${i + 1}`,
      sequence: sequence++,
      wattsPerMetre: t.wattsPerMetre,
      lengthMetres: t.lengthMetres,
    })
  })

  return circuits
}
```

- [ ] **Step 4: Write PreviewSummary**

It counts `design.layout.devices` by category (`Dimmer240` and `Dimmer0_10V` reported separately as "dimmer" and "tape dimmer"), renders slot usage through `formatModules`, lists diagnostics with severity styling and their suggestions, and says "Fits in this enclosure" when there are no errors. It must never do layout arithmetic — only count and format what the server sent.

- [ ] **Step 5: Write NewSubmainPage**

Three sections on one scrolling page rather than paged steps — on a phone, back-and-forth between steps costs more than scrolling. It creates the submain on first save, then debounces `/design/preview` 400 ms after any change, showing the previous preview greyed while a new one is in flight. "Generate panel" posts to `/design/generate` with the built circuits and navigates to `/submains/:id/panel` on success; on `422` it stays put and shows the diagnostics.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ui && npx vitest run`
Expected: PASS, 17 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add new submain wizard with live preview"
```

---

### Task 7: The to-scale panel drawing

**Files:**
- Create: `ui/src/panel/PanelSvg.tsx`, `ui/src/panel/geometry.ts`, `ui/src/panel/deviceStyle.ts`
- Create: `ui/tests/geometry.test.ts`, `ui/tests/PanelSvg.test.tsx`

**Interfaces:**
- Consumes: `LayoutResponse`, `PlacedDeviceResponse`, `SLOT_UNITS_PER_MODULE`.
- Produces: `slotToX(slot)`, `rowToY(row)`, `deviceRect(device)`, `panelSize(layout)`; `deviceFill(category)`, `deviceLabel(device)`; `<PanelSvg layout selectedId onSelect />`.

**Geometry:** one slot unit is `SLOT_PX = 8` wide, so a DIN module is 24 px — a 18-module row is 432 px, which fits a 390 px phone at a slight zoom-out and is legible at 1:1 on a tablet. Row height is `ROW_PX = 72` with `ROW_GAP = 16` between rails. All of it lives in `geometry.ts` as pure functions so it can be tested without rendering.

**Drawing rules:**
- Each row is a rail: a horizontal grey band with slot ticks every module.
- A device is a rounded rect spanning its `moduleWidth`, filled by category.
- Terminal blocks are 1 slot unit — too narrow for text — so a run of adjacent terminals with the same `terminalRole` is drawn as one grouped block labelled `L1–L12`, with the individual ticks still visible. This is the only place the UI groups anything, and it groups for drawing only; the underlying devices are untouched.
- Every device gets an accessible name so the drawing is navigable and testable without pixel-hunting.

- [ ] **Step 1: Write the failing tests**

`ui/tests/geometry.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { ROW_GAP, ROW_PX, SLOT_PX, deviceRect, panelSize, rowToY, slotToX } from '../src/panel/geometry'

const device = (rowIndex: number, startSlot: number, moduleWidth: number) =>
  ({ rowIndex, startSlot, moduleWidth }) as never

describe('panel geometry', () => {
  it('places a slot at a multiple of the slot width', () => {
    expect(slotToX(0)).toBe(0)
    expect(slotToX(3)).toBe(3 * SLOT_PX)
  })

  it('stacks rows with a gap between rails', () => {
    expect(rowToY(0)).toBe(0)
    expect(rowToY(2)).toBe(2 * (ROW_PX + ROW_GAP))
  })

  it('sizes a one-module device to three slot units', () => {
    const rect = deviceRect(device(1, 3, 3))

    expect(rect.x).toBe(3 * SLOT_PX)
    expect(rect.width).toBe(3 * SLOT_PX)
    expect(rect.y).toBe(ROW_PX + ROW_GAP)
    expect(rect.height).toBe(ROW_PX)
  })

  it('sizes the panel from rows and slots per row', () => {
    const size = panelSize({ rows: 4, slotsPerRow: 54, devices: [] })

    expect(size.width).toBe(54 * SLOT_PX)
    expect(size.height).toBe(4 * ROW_PX + 3 * ROW_GAP)
  })
})
```

`ui/tests/PanelSvg.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { PanelSvg } from '../src/panel/PanelSvg'
import type { LayoutResponse, PlacedDeviceResponse } from '../src/api/types'

const terminal = (n: number, slot: number): PlacedDeviceResponse => ({
  id: `t${n}`, deviceTypeId: 'tb', category: 'Terminal240', rowIndex: 0, startSlot: slot,
  moduleWidth: 1, label: `L${n}`, terminalRole: 'Line', channels: [],
})

const dimmer: PlacedDeviceResponse = {
  id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
  moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
  channels: [{ channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen ceiling', isSpare: false }],
}

const layout: LayoutResponse = {
  rows: 4, slotsPerRow: 54,
  devices: [terminal(1, 0), terminal(2, 1), terminal(3, 2), dimmer],
}

describe('PanelSvg', () => {
  it('draws a device with an accessible name', () => {
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /dimmer 1/i })).toBeTruthy()
  })

  it('groups a run of terminals into one labelled block', () => {
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /L1.*L3/ })).toBeTruthy()
  })

  it('reports which device was tapped', async () => {
    const onSelect = vi.fn()
    render(<PanelSvg layout={layout} selectedId={null} onSelect={onSelect} />)

    await userEvent.click(screen.getByRole('button', { name: /dimmer 1/i }))

    expect(onSelect).toHaveBeenCalledWith('d1')
  })

  it('marks the selected device for assistive tech, not just visually', () => {
    render(<PanelSvg layout={layout} selectedId="d1" onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /dimmer 1/i }).getAttribute('aria-pressed')).toBe('true')
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ui && npx vitest run geometry PanelSvg`
Expected: FAIL — neither module exists.

- [ ] **Step 3: Write geometry.ts**

```ts
import type { LayoutResponse, PlacedDeviceResponse } from '../api/types'

/** Pixels per slot unit. A DIN module is three of these. */
export const SLOT_PX = 8
export const ROW_PX = 72
export const ROW_GAP = 16

export const slotToX = (slot: number) => slot * SLOT_PX
export const rowToY = (row: number) => row * (ROW_PX + ROW_GAP)

export function deviceRect(device: Pick<PlacedDeviceResponse, 'rowIndex' | 'startSlot' | 'moduleWidth'>) {
  return {
    x: slotToX(device.startSlot),
    y: rowToY(device.rowIndex),
    width: slotToX(device.moduleWidth),
    height: ROW_PX,
  }
}

export function panelSize(layout: Pick<LayoutResponse, 'rows' | 'slotsPerRow'>) {
  return {
    width: slotToX(layout.slotsPerRow),
    height: layout.rows * ROW_PX + Math.max(layout.rows - 1, 0) * ROW_GAP,
  }
}

/** Nearest slot for a pointer x, clamped so a drag can never leave the rail. */
export function xToSlot(x: number, moduleWidth: number, slotsPerRow: number) {
  const raw = Math.round(x / SLOT_PX)
  return Math.min(Math.max(raw, 0), slotsPerRow - moduleWidth)
}

/** Nearest row for a pointer y, clamped to the enclosure. */
export function yToRow(y: number, rows: number) {
  const raw = Math.round(y / (ROW_PX + ROW_GAP))
  return Math.min(Math.max(raw, 0), rows - 1)
}
```

- [ ] **Step 4: Write deviceStyle.ts**

Maps category to a fill and a short kind name: `Dimmer240` slate-blue "Dimmer", `Dimmer0_10V` indigo "Tape dimmer", `Relay` teal "Relay", `Psu24V` amber "PSU", `Terminal240` grey "Terminal". Colours must hold up against the panel background in both light and dark and never be the only thing distinguishing a device — every block carries its label too.

- [ ] **Step 5: Write PanelSvg.tsx**

A pure component: `{ layout, selectedId, onSelect, dragging? }`. It renders one `<g>` per row with the rail and module ticks, groups adjacent same-role terminals for drawing, and renders every other device as a `<g role="button" tabIndex={0} aria-pressed={...} aria-label={...}>` with a rect and text. The `aria-label` names the device and its circuits, e.g. `Dimmer 1, Kitchen ceiling, 1 spare channel`. It must not fetch, and must not mutate.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ui && npx vitest run`
Expected: PASS, 25 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: draw the panel to scale"
```

---

### Task 8: Panel screen, zoom and the device sheet

**Files:**
- Create: `ui/src/routes/PanelPage.tsx`, `ui/src/panel/DeviceSheet.tsx`, `ui/src/panel/usePanZoom.ts`
- Create: `ui/src/components/Sheet.tsx`, `ui/src/components/Diagnostics.tsx`
- Create: `ui/tests/DeviceSheet.test.tsx`, `ui/tests/PanelPage.test.tsx`

**Interfaces:**
- Consumes: `api`, `PanelSvg`, `DesignResponse`.
- Produces: route `/submains/:submainId/panel`; `<DeviceSheet device onRenameCircuit onFreeChannel onClose />`; `usePanZoom()` returning `{ scale, translate, bind, zoomIn, zoomOut, fit }`.

**Behaviour:** the page loads the stored design via `GET /submains/:id/layout` — **not** `/design/preview`, which returns `id: null` on every device and so cannot support dragging — renders `PanelSvg`, and opens a bottom sheet when a device is tapped. Pinch-zoom and one-finger pan via pointer events; a "fit" control resets. Diagnostics appear as a dismissible banner above the drawing, errors first.

- [ ] **Step 1: Write the failing tests**

`ui/tests/DeviceSheet.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DeviceSheet } from '../src/panel/DeviceSheet'
import type { PlacedDeviceResponse } from '../src/api/types'

const device: PlacedDeviceResponse = {
  id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
  moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
  channels: [
    { channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen ceiling', isSpare: false },
    { channelIndex: 1, circuitId: null, circuitName: null, isSpare: true },
  ],
}

describe('DeviceSheet', () => {
  it('shows the device, its width in modules and each channel', () => {
    render(<DeviceSheet device={device} onRenameCircuit={vi.fn()} onFreeChannel={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByText('Dimmer 1')).toBeTruthy()
    expect(screen.getByText(/1T/)).toBeTruthy()
    expect(screen.getByDisplayValue('Kitchen ceiling')).toBeTruthy()
    expect(screen.getByText(/spare/i)).toBeTruthy()
  })

  it('renames a circuit through its channel', async () => {
    const onRenameCircuit = vi.fn().mockResolvedValue(undefined)
    render(<DeviceSheet device={device} onRenameCircuit={onRenameCircuit} onFreeChannel={vi.fn()} onClose={vi.fn()} />)

    const input = screen.getByDisplayValue('Kitchen ceiling')
    await userEvent.clear(input)
    await userEvent.type(input, 'Kitchen island')
    await userEvent.tab()

    expect(onRenameCircuit).toHaveBeenCalledWith('c1', 'Kitchen island')
  })

  it('does not call the API when the name comes back unchanged', async () => {
    const onRenameCircuit = vi.fn()
    render(<DeviceSheet device={device} onRenameCircuit={onRenameCircuit} onFreeChannel={vi.fn()} onClose={vi.fn()} />)

    await userEvent.click(screen.getByDisplayValue('Kitchen ceiling'))
    await userEvent.tab()

    expect(onRenameCircuit).not.toHaveBeenCalled()
  })

  it('offers no rename box for a spare channel', () => {
    render(<DeviceSheet device={device} onRenameCircuit={vi.fn()} onFreeChannel={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getAllByRole('textbox')).toHaveLength(1)
  })
})
```

`ui/tests/PanelPage.test.tsx` asserts: the page renders the drawing from a stubbed `/submains/:id/layout`; an `Error` diagnostic appears with its suggestion; tapping a device opens the sheet; and a `409` on any edit shows a "panel changed, reloading" note and refetches rather than surfacing an error.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ui && npx vitest run DeviceSheet PanelPage`
Expected: FAIL — neither module exists.

- [ ] **Step 3: Write Sheet and Diagnostics**

`Sheet.tsx` — a bottom sheet fixed to the viewport bottom with `env(safe-area-inset-bottom)` padding, a drag handle, a backdrop that closes on tap, `role="dialog"` with `aria-modal`, focus moved in on open and restored on close, and Escape to close.

`Diagnostics.tsx` — takes `DiagnosticResponse[]`, sorts `Error` before `Warning` before `Info`, renders each with its message and suggestion, and renders nothing at all when the list is empty.

- [ ] **Step 4: Write usePanZoom**

Pointer-event based: one pointer pans, two pinch. It tracks `scale` (clamped 0.4–3) and `translate`, returns handlers to spread onto the SVG wrapper, and sets `touch-action: none` **only while a gesture is active** so ordinary page scrolling still works when the user is not manipulating the drawing.

- [ ] **Step 5: Write DeviceSheet**

Header with the device label, its kind, and width via `formatModules`. Then one row per channel: assigned channels get a text input defaulting to the circuit name that fires `onRenameCircuit(circuitId, name)` on blur **only when the value actually changed**, plus a "free this channel" button; spare channels show as "Spare". No layout arithmetic.

- [ ] **Step 6: Write PanelPage**

Loads the design, holds `layoutVersion` in state, renders `Diagnostics`, `PanelSvg` inside the pan-zoom wrapper, and `DeviceSheet` for the selected device. Edits call the API and refetch. An `ApiError` with `conflict` shows "This panel changed elsewhere — reloading" and refetches instead of showing a failure.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd ui && npx vitest run`
Expected: PASS, 33 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add panel screen with zoom and device sheet"
```

---

### Task 9: Drag to rearrange

**Files:**
- Create: `ui/src/panel/useDeviceDrag.ts`
- Modify: `ui/src/panel/PanelSvg.tsx` (render the drag ghost and invalid-target styling)
- Modify: `ui/src/routes/PanelPage.tsx` (commit the move, revert on rejection)
- Create: `ui/tests/useDeviceDrag.test.ts`, `ui/tests/dragTargets.test.ts`
- Create: `ui/src/panel/dragTargets.ts`

**Interfaces:**
- Consumes: `xToSlot`, `yToRow`, `LayoutResponse`.
- Produces: `isTargetFree(layout, deviceId, rowIndex, startSlot) -> boolean`; `useDeviceDrag({ layout, onCommit })` returning `{ dragging, bind }` where `dragging` is `{ deviceId, rowIndex, startSlot, valid }` or null.

**Behaviour:** long-press (350 ms) picks a device up — a plain drag would fight panning. While dragging, the device follows the pointer snapped to slot boundaries, and the ghost is drawn red when the target is occupied or off the rail. On release over an invalid target the drag is abandoned with no request. On a valid target the move is applied optimistically, then `PATCH /devices/{id}/position` is sent; a `422` or `409` reverts to the server's state.

Validity is decided by the same rule the server enforces — overlap against every other device in the row — so the UI and the API agree on what is legal. The client's check is for feedback only; the server's is the one that counts.

- [ ] **Step 1: Write the failing tests**

`ui/tests/dragTargets.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { isTargetFree } from '../src/panel/dragTargets'
import type { LayoutResponse } from '../src/api/types'

const device = (id: string, rowIndex: number, startSlot: number, moduleWidth: number) =>
  ({ id, deviceTypeId: 'x', category: 'Dimmer240', rowIndex, startSlot, moduleWidth, label: id, terminalRole: 'None', channels: [] }) as never

const layout = {
  rows: 4, slotsPerRow: 54,
  devices: [device('a', 1, 0, 3), device('b', 1, 3, 3), device('c', 2, 0, 9)],
} as LayoutResponse

describe('isTargetFree', () => {
  it('accepts empty space in the same row', () => {
    expect(isTargetFree(layout, 'b', 1, 30)).toBe(true)
  })

  it('rejects overlapping another device', () => {
    expect(isTargetFree(layout, 'b', 1, 1)).toBe(false)
  })

  it('accepts a device landing exactly where it already is', () => {
    expect(isTargetFree(layout, 'b', 1, 3)).toBe(true)
  })

  it('rejects running off the end of the row', () => {
    expect(isTargetFree(layout, 'b', 1, 52)).toBe(false)
  })

  it('rejects a row outside the enclosure', () => {
    expect(isTargetFree(layout, 'b', 9, 0)).toBe(false)
  })

  it('accepts an empty row', () => {
    expect(isTargetFree(layout, 'b', 3, 0)).toBe(true)
  })
})
```

`ui/tests/useDeviceDrag.test.ts` uses `renderHook` with fake timers to assert: a pointer-down alone does not start a drag; holding past 350 ms does; pointer-move snaps `startSlot` to slot boundaries; releasing over an invalid target calls neither `onCommit` nor leaves `dragging` set; releasing over a valid target calls `onCommit` once with the snapped row and slot.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ui && npx vitest run dragTargets useDeviceDrag`
Expected: FAIL — neither module exists.

- [ ] **Step 3: Write dragTargets.ts**

```ts
import type { LayoutResponse } from '../api/types'

/** The same overlap rule the server enforces, so the UI never promises a move the API will refuse. */
export function isTargetFree(
  layout: LayoutResponse,
  deviceId: string,
  rowIndex: number,
  startSlot: number,
): boolean {
  const device = layout.devices.find(d => d.id === deviceId)
  if (!device) return false

  const end = startSlot + device.moduleWidth
  if (rowIndex < 0 || rowIndex >= layout.rows) return false
  if (startSlot < 0 || end > layout.slotsPerRow) return false

  return !layout.devices.some(other =>
    other.id !== deviceId &&
    other.rowIndex === rowIndex &&
    other.startSlot < end &&
    startSlot < other.startSlot + other.moduleWidth)
}
```

- [ ] **Step 4: Write useDeviceDrag.ts**

Holds `{ deviceId, rowIndex, startSlot, valid } | null`. On `pointerdown` it starts a 350 ms timer and captures the pointer; a move beyond 8 px before the timer fires cancels it, so a pan never turns into a drag. Once dragging, `pointermove` converts client coordinates to panel coordinates (accounting for the current pan-zoom transform), snaps with `xToSlot`/`yToRow`, and sets `valid` from `isTargetFree`. `pointerup` calls `onCommit(deviceId, rowIndex, startSlot)` only when `valid`, then clears state. `pointercancel` clears without committing.

- [ ] **Step 5: Wire the commit in PanelPage**

```tsx
async function commitMove(deviceId: string, rowIndex: number, startSlot: number) {
  const previous = design
  setDesign(applyMoveLocally(design, deviceId, rowIndex, startSlot))  // optimistic

  try {
    const result = await api.patch<{ layoutVersion: number }>(`/devices/${deviceId}/position`, {
      rowIndex, startSlot, basedOnLayoutVersion: design.layoutVersion,
    })
    setDesign(d => ({ ...d, layoutVersion: result.layoutVersion }))
  } catch (e) {
    const error = e as ApiError
    setDesign(previous)                       // put it back where it was
    if (error.conflict) { setNotice('This panel changed elsewhere — reloading'); await reload() }
    else setNotice(error.message)             // 422: "Another device is already in that space."
  }
}
```

`applyMoveLocally` only rewrites `rowIndex`/`startSlot` on the one device — it is not a re-layout, and must not become one.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ui && npx vitest run`
Expected: PASS, 44 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: drag devices to rearrange the panel"
```

---

### Task 10: Keycloak sign-in

**Files:**
- Modify: `ui/src/main.tsx` (auth provider), `ui/src/api/client.ts` (bearer token)
- Create: `ui/src/auth/AuthGate.tsx`, `ui/.env.development`
- Modify: `src/PubInvest.HouseConfig.Api/appsettings.json` (`AllowedOrigins`)
- Create: `ui/tests/client.auth.test.ts`

**Interfaces:**
- Produces: `AuthGate` (renders children only when signed in), and a token getter the client calls per request.

`react-oidc-context` matches `crm-ui`, so the Keycloak wiring is one the team already maintains.

- [ ] **Step 1: Write the failing test**

`ui/tests/client.auth.test.ts` asserts the client sends `Authorization: Bearer <token>` when a token getter is set, sends no such header when it is not, and does not cache a stale token across calls.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd ui && npx vitest run client.auth`
Expected: FAIL — the client has no token support.

- [ ] **Step 3: Add token support to the client**

A module-level `setTokenGetter(fn: () => string | undefined)`, called once from the auth provider. `request` adds the header when the getter returns a token. Keeping it out of React means non-component code can call the API without prop-drilling.

- [ ] **Step 4: Wire the provider and gate**

`main.tsx` wraps the router in `AuthProvider` configured from `VITE_OIDC_AUTHORITY` and `VITE_OIDC_CLIENT_ID`, and `AuthGate` shows a sign-in button when signed out, a spinner while loading, and the app when signed in. `.env.development` points at the dev realm and leaves a comment that `HouseConfig__AuthEnabled=false` makes local work possible without Keycloak at all.

- [ ] **Step 5: Allow the dev origin on the API**

In `appsettings.Development.json`, set `HouseConfig:AllowedOrigins` to `["http://localhost:5173"]`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ui && npx vitest run` and `dotnet test`
Expected: PASS on both.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: sign in with keycloak"
```

---

### Task 11: End-to-end test of the drag interaction

**Files:**
- Create: `ui/playwright.config.ts`, `ui/e2e/panel.spec.ts`, `ui/e2e/fixtures.ts`
- Modify: `ui/package.json` (`test:e2e` script)

**Interfaces:**
- Consumes: the running API and UI.
- Produces: a Playwright suite covering the one interaction unit tests cannot really prove.

Drag on touch is the part most likely to break silently — it can pass every unit test and still be unusable on a phone. This suite runs against a real browser in a real mobile viewport.

- [ ] **Step 1: Install and configure**

```bash
cd ui
npm install -D @playwright/test
npx playwright install chromium
```

`playwright.config.ts` uses the `Pixel 7` device descriptor, `baseURL: 'http://localhost:5173'`, and a `webServer` entry that starts Vite. The API and Postgres must already be running (`docker-compose up -d postgres`, then the API) — state that in the test file's header comment.

- [ ] **Step 2: Write the failing spec**

`ui/e2e/panel.spec.ts` covers, each against a freshly seeded submain created through the API in `beforeEach`:

1. **A panel can be generated from the wizard** — fill counts, tap Generate, land on the panel screen, see a dimmer and a relay.
2. **A device can be dragged to a free slot** — long-press a dimmer, drag right, release; it lands on the new slot and survives a page reload.
3. **A drag onto an occupied slot is refused** — the ghost shows invalid, release leaves the device where it was, no error banner appears.
4. **A dragged position survives re-generation** — move a device, add a circuit through the API, reload; the device is still where it was put.
5. **The drawing is usable at 390 px** — the panel is visible and no horizontal page scroll is introduced (the drawing pans inside its own container).

- [ ] **Step 3: Run it to verify it fails**

Run: `cd ui && npx playwright test`
Expected: FAIL before Task 9's drag exists; after it, each assertion should be checked individually as it goes green.

- [ ] **Step 4: Fix what it finds**

Touch drag is where hand-written pointer handling usually falls down. Expect to fix at least one of: the long-press timer firing during a pan, pointer capture lost mid-drag, or coordinates not accounting for the pan-zoom transform. Fix in `useDeviceDrag.ts`, not in the test.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test: cover panel drag end to end on a mobile viewport"
```

---

## Done when

- `dotnet test` and `cd ui && npx vitest run` are both green, and `npx playwright test` passes against a running stack.
- An engineer can, on a phone: create a house, add a submain by entering counts, watch the preview update as they type, generate a panel, see it drawn to scale, tap a device to name its channels, and drag a device to a different slot.
- A dragged device is still where they put it after adding a circuit and re-generating — or, if it genuinely cannot stay, they are told why.
- Two tabs open on the same panel cannot silently overwrite each other: the second edit gets a `409` and reloads.

## Deferred to Plan 3

- PDF panel drawing and circuit schedule; BOM export; `POST /submains/{id}/revisions`.
- Catalogue and ruleset admin screens (reads already exist; writes do not).
- Anything to do with identifying physical devices — dropped from the design, see the spec.
