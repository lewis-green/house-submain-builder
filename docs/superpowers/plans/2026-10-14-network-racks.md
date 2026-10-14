# Network Racks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an electrician add network racks to a house beside its submains, fill them from a catalogue of UniFi switches and patch panels placed by rack unit, label every port with a PoE tick, link the rack to the submain that feeds it, and issue a rack PDF with a drawing, port schedule and parts list.

**Architecture:** Two new catalogue tables (rack cabinets, rack items with port groups) and three per-house tables (racks, rack items, sparse ports) beside the existing panel tables, untouched panel generator. Placement is a pure Domain function the API calls on every add, move and cabinet change; the UI only draws `/racks/{id}/layout`. Rack revisions embed a snapshot exactly as panel revisions do, and the PDF, rack BOM and house BOM are rendered from it.

**Tech Stack:** .NET 10 minimal API, EF Core 10.0.9 + Npgsql 10.0.2, QuestPDF 2026.9.0, xUnit 2.9.3 + Testcontainers + PdfPig 0.1.16; React 19, TypeScript ~6, Tailwind 4, Vite 8, Vitest 5, Playwright 1.63 (WebKit).

**Spec:** `docs/superpowers/specs/2026-10-14-network-racks-design.md` — read it before starting any task.

## Global Constraints

- The server decides every placement. The UI draws what `/racks/{id}/layout` returns; any client-side "is this U free" check is feedback only, exactly like `ui/src/panel/dragTargets.ts`.
- U1 is the bottom U. Items fill from the top down: the first item in a 12U cabinet goes in U12.
- Ports are numbered 1 to N across the whole item, as UniFi numbers them (a 48-port switch's SFP+ uplinks are 49–52).
- The panel generator, rulesets and the DIN catalogue (`DeviceTypeRow`, `DeviceCategory`) do not change.
- Bills of materials carry **no prices**.
- Every seeded rack cabinet, patch panel and accessory part number is ASSUMED; the switches' port splits and PoE budgets come from retailer listings and are ASSUMED too.
- A port is stored only once it has a label or a PoE tick. A port label is at most 60 characters after trimming; a label of only spaces clears it.
- Refusals the user can act on are returned as `422` with `{ "message": "<plain words>" }` (`RefusalResponse`); field errors as `ValidationProblem` keyed by the camelCase field; version conflicts as `409` `ConflictResponse(message, currentLayoutVersion)`.
- No new runtime dependencies (NuGet or npm).
- UI: reuse `AppShell`, `Sheet`, `Field`, `Button`/`ButtonLink`, `ConfirmButton`, `ErrorNote`, `EmptyState`, `Skeleton`, `announce`, `useSingleFlight`. Targets ≥44px, inputs ≥16px, selects use the existing `.select` class, checkboxes the `.checkbox` class. Copy is plain English.
- Comments explain *why*, in full sentences, at the density of the surrounding code.
- **Environment (this machine):**
  - Node: prefix every npm/npx command with `export PATH=$HOME/.nvm/versions/node/v22.23.0/bin:$PATH`.
  - NuGet: the user config has no sources; pass `--source https://api.nuget.org/v3/index.json` to `dotnet restore` and `--add-source https://api.nuget.org/v3/index.json` to `dotnet tool install`. Never edit `~/.nuget/NuGet/NuGet.Config`.
  - Data and Api tests (Testcontainers) need: `export PATH=/opt/homebrew/bin:$PATH DOCKER_HOST=unix:///Users/sinamoradian/.colima/default/docker.sock TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock TESTCONTAINERS_RYUK_DISABLED=true`.
  - Git has no identity configured: commit with `git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit ...` and end every message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Work on branch `network-racks`; never commit to `main`.
- After any task that changes the API, restart the dev API so the running app reflects it (see `README.md` → Running it for development).

## Review Focus

1. **Two adds racing for the same U** (two people, or a double tap past the guard): neither request may 500, and no two items may ever overlap. → Task 4, `Concurrent_adds_never_share_a_u`.
2. **A port label of only spaces, or longer than 60 characters**: spaces clear the port; an overlong label is refused with the limit and the typed text stays in the box. → Task 4, `A_label_of_only_spaces_clears_the_port`, `An_overlong_label_is_refused_with_the_limit`; Task 10, `keeps the typed label when the server refuses it`.
3. **Non-ASCII labels** ("Café – Bureau ½") must reach the PDF intact rather than crash QuestPDF or come out as boxes. → Task 6, `A_label_with_accents_and_symbols_prints_as_typed`.
4. **Issuing an empty rack** must be refused with a reason, not produce a blank drawing. → Task 5, `An_empty_rack_cannot_be_issued`.
5. **A catalogue item deactivated after it was placed** must still be drawn, scheduled and on the BOM, but no longer offered in "Add to rack". → Task 5, `A_deactivated_item_still_appears_in_a_new_revision`; Task 11, `offers only active switches and patch panels`.

---

## File map

**Domain** (`src/PubInvest.HouseConfig.Domain/Racks/`)
- `RackCatalogue.cs` — `RackDeviceKind`, `PortGroup`, `RackType`, `RackDeviceType`.
- `RackPlacement.cs` — `RackSpan`, `RackPlacement` (next free U, can place, free U, blocking range).
- `RackBom.cs` — parts list for a rack.

**Data** (`src/PubInvest.HouseConfig.Data/`)
- `Entities/RackCatalogueRows.cs` — `RackTypeRow`, `RackDeviceTypeRow`.
- `Entities/Rack.cs` — `Rack`, `RackItem`, `RackPort`.
- `Entities/RackRevision.cs`.
- `HouseConfigDbContext.cs`, `Mapping/DomainMapper.cs`, `Seeding/SeedDocument.cs`, `Seeding/CatalogueSeeder.cs` — modified.
- `Migrations/*_RackCatalogue.cs`, `*_Racks.cs`, `*_RackRevisions.cs` — generated.

**API** (`src/PubInvest.HouseConfig.Api/`)
- `Contracts/RackContracts.cs`.
- `Racks/RackViews.cs` — builds rack and layout responses, validates a rack request.
- `Racks/RackSnapshot.cs`, `Racks/RackRevisionService.cs`.
- `Endpoints/RackEndpoints.cs` (racks, layout, submain's racks), `Endpoints/RackItemEndpoints.cs` (items, ports), `Endpoints/RackRevisionEndpoints.cs` (revisions, PDF, BOM).
- `Rendering/RackSvgRenderer.cs`, `Documents/RackDocument.cs`.
- `Endpoints/CatalogueEndpoints.cs`, `Endpoints/CatalogueAdminEndpoints.cs`, `Endpoints/ExportEndpoints.cs`, `Program.cs` — modified.

**UI** (`ui/src/`)
- `api/client.ts`, `api/types.ts`, `App.tsx` — modified.
- `drag/usePressDrag.ts` — the drag gesture, shared by panels and racks; `panel/useDeviceDrag.ts` becomes its adapter.
- `racks/rackGeometry.ts`, `racks/rackTargets.ts`, `racks/ports.ts`, `racks/useRackDrag.ts`, `racks/RackDrawing.tsx`, `racks/ItemSheet.tsx`, `racks/AddItemSheet.tsx`, `racks/RackSettings.tsx`.
- `routes/NewRackPage.tsx`, `routes/RackPage.tsx`, `routes/RackCatalogueSections.tsx`.
- `routes/ProjectPage.tsx`, `routes/BomPage.tsx`, `routes/PanelPage.tsx`, `routes/CataloguePage.tsx`, `panel/IssueButton.tsx` — modified.

**Seed and docs:** `seed/catalogue.v1.json`, `README.md`.

---

### Task 1: Rack catalogue — tables, seed, seeder, read endpoints

**Files:**
- Create: `.config/dotnet-tools.json` (by command)
- Create: `src/PubInvest.HouseConfig.Domain/Racks/RackCatalogue.cs`
- Create: `src/PubInvest.HouseConfig.Data/Entities/RackCatalogueRows.cs`
- Modify: `src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs`
- Modify: `src/PubInvest.HouseConfig.Data/Mapping/DomainMapper.cs`
- Modify: `src/PubInvest.HouseConfig.Data/Seeding/SeedDocument.cs`
- Modify: `src/PubInvest.HouseConfig.Data/Seeding/CatalogueSeeder.cs`
- Create: `src/PubInvest.HouseConfig.Data/Migrations/<timestamp>_RackCatalogue.cs` (generated)
- Modify: `seed/catalogue.v1.json`
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueEndpoints.cs`
- Modify: `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/CatalogueSeederTests.cs`, `tests/PubInvest.HouseConfig.Data.Tests/ShippedSeedTests.cs`, `tests/PubInvest.HouseConfig.Api.Tests/RackCatalogueEndpointTests.cs`

**Interfaces:**
- Produces (Domain, namespace `PubInvest.HouseConfig.Domain.Racks`):
  - `enum RackDeviceKind { Switch, PatchPanel, Accessory }`
  - `record PortGroup(string Name, int From, int To, bool PoeTick)` with `int Count`, `bool Contains(int number)`
  - `record RackType(Guid Id, string Manufacturer, string Model, string PartNumber, int HeightU, string Mounting)` with `string Description`
  - `record RackDeviceType(Guid Id, string Manufacturer, string Model, string PartNumber, RackDeviceKind Kind, int HeightU, IReadOnlyList<PortGroup> PortGroups, int? PoeBudgetW, Guid? RequiredAccessoryId, bool Active)` with `string Description`, `int PortCount`, `PortGroup? GroupOf(int number)`
- Produces (Data): `RackTypeRow`, `RackDeviceTypeRow`; `DbSet<RackTypeRow> RackTypes`, `DbSet<RackDeviceTypeRow> RackDeviceTypes`; `DomainMapper.ToDomain(RackTypeRow)`, `DomainMapper.ToDomain(RackDeviceTypeRow)`; `SeedRackType`, `SeedRackDeviceType`; `SeedDocument.RackTypes`, `SeedDocument.RackDeviceTypes` (both nullable, default null).
- Produces (API): `GET /catalogue/rack-types` → `RackType[]`; `GET /catalogue/rack-device-types` → `RackDeviceType[]`.
- Produces (tests): `TestSeed.Cabinet4UId`, `Cabinet2UId`, `Switch48Id`, `Switch16Id`, `PatchPanelId`, `PatchPanel2UId`, `RackKitId`, `InactiveSwitchId`.

- [ ] **Step 1: Install the EF Core tool locally, so migrations are made the way the existing ones were**

```bash
cd /Users/sinamoradian/src/house-submain-builder
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.9 --add-source https://api.nuget.org/v3/index.json
dotnet ef --version
```

Expected: `.config/dotnet-tools.json` exists and the last command prints `10.0.9`.

- [ ] **Step 2: Write the Domain catalogue types**

Create `src/PubInvest.HouseConfig.Domain/Racks/RackCatalogue.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Racks;

public enum RackDeviceKind
{
    Switch,
    PatchPanel,

    /// Bought with an item but never placed in the rack, such as the rack-mount
    /// kit a half-width switch needs. It reaches the bill of materials only
    /// through the item that names it.
    Accessory,
}

/// A run of consecutively numbered ports that share a name ("1G", "SFP+") and
/// whether the PoE tick is offered on them.
public sealed record PortGroup(string Name, int From, int To, bool PoeTick)
{
    public int Count => To - From + 1;

    public bool Contains(int number) => number >= From && number <= To;
}

/// A rack cabinet. Heights are in rack units; U1 is the bottom of the cabinet.
public sealed record RackType(
    Guid Id,
    string Manufacturer,
    string Model,
    string PartNumber,
    int HeightU,
    string Mounting)
{
    public string Description => $"{Manufacturer} {Model}";
}

/// Something that can go in a rack, or be bought with something that does.
public sealed record RackDeviceType(
    Guid Id,
    string Manufacturer,
    string Model,
    string PartNumber,
    RackDeviceKind Kind,
    int HeightU,
    IReadOnlyList<PortGroup> PortGroups,
    int? PoeBudgetW,
    Guid? RequiredAccessoryId,
    bool Active)
{
    public string Description => $"{Manufacturer} {Model}";

    public int PortCount => PortGroups.Sum(g => g.Count);

    public PortGroup? GroupOf(int number) => PortGroups.FirstOrDefault(g => g.Contains(number));
}
```

- [ ] **Step 3: Write the catalogue rows and register them**

Create `src/PubInvest.HouseConfig.Data/Entities/RackCatalogueRows.cs`:

```csharp
namespace PubInvest.HouseConfig.Data.Entities;

public class RackTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public int HeightU { get; set; }

    /// "Wall" or "Floor", for display: it changes nothing about placement.
    public string Mounting { get; set; } = "Wall";
}

public class RackDeviceTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Kind { get; set; } = "";
    public int HeightU { get; set; }

    /// The item's port groups as JSON. They are only ever read and written
    /// whole, so a table of their own would buy nothing but joins.
    public string PortGroupsJson { get; set; } = "[]";

    public int? PoeBudgetW { get; set; }
    public Guid? RequiredAccessoryId { get; set; }
    public bool Active { get; set; }
}
```

In `src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs`, add after `public DbSet<RuleSetRow> RuleSets => Set<RuleSetRow>();`:

```csharp
    public DbSet<RackTypeRow> RackTypes => Set<RackTypeRow>();
    public DbSet<RackDeviceTypeRow> RackDeviceTypes => Set<RackDeviceTypeRow>();
```

and inside `OnModelCreating`, after `b.Entity<DeviceTypeRow>().HasIndex(d => d.PartNumber).IsUnique();`:

```csharp
        b.Entity<RackDeviceTypeRow>().HasIndex(d => d.PartNumber).IsUnique();
```

- [ ] **Step 4: Map rows to the Domain**

In `src/PubInvest.HouseConfig.Data/Mapping/DomainMapper.cs`, add `using PubInvest.HouseConfig.Domain.Racks;` and, after the `ToDomain(EnclosureTypeRow row)` method:

```csharp
    public static RackType ToDomain(RackTypeRow row) => new(
        row.Id, row.Manufacturer, row.Model, row.PartNumber, row.HeightU, row.Mounting);

    public static RackDeviceType ToDomain(RackDeviceTypeRow row) => new(
        row.Id, row.Manufacturer, row.Model, row.PartNumber,
        Enum.Parse<RackDeviceKind>(row.Kind),
        row.HeightU,
        JsonSerializer.Deserialize<List<PortGroup>>(row.PortGroupsJson, Json) ?? [],
        row.PoeBudgetW, row.RequiredAccessoryId, row.Active);
```

- [ ] **Step 5: Teach the seed document and seeder about racks**

Replace `src/PubInvest.HouseConfig.Data/Seeding/SeedDocument.cs` with:

```csharp
using PubInvest.HouseConfig.Domain.Racks;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Seeding;

/// Rack sections default to null so a seed written before racks existed, and
/// every test that builds one in code, still reads.
public sealed record SeedDocument(
    int Version,
    IReadOnlyList<SeedDeviceType> DeviceTypes,
    IReadOnlyList<SeedEnclosure> Enclosures,
    IReadOnlyList<SeedRuleSet> RuleSets,
    IReadOnlyList<SeedRackType>? RackTypes = null,
    IReadOnlyList<SeedRackDeviceType>? RackDeviceTypes = null);

public sealed record SeedDeviceType(
    Guid Id, string Manufacturer, string Model, string PartNumber, string Category,
    int ModuleWidth, int ChannelCount, int? MaxLoadPerChannelW, int? MaxTotalLoadW, bool Active);

public sealed record SeedEnclosure(
    Guid Id, string Manufacturer, string Model, int Rows, int SlotsPerRow, string IpRating);

public sealed record SeedRuleSet(
    Guid Id, string Name, int Version, bool IsDefault, RuleSetPayload Payload);

public sealed record SeedRackType(
    Guid Id, string Manufacturer, string Model, string PartNumber, int HeightU, string Mounting);

public sealed record SeedRackDeviceType(
    Guid Id, string Manufacturer, string Model, string PartNumber, string Kind, int HeightU,
    IReadOnlyList<PortGroup> PortGroups, int? PoeBudgetW, Guid? RequiredAccessoryId, bool Active);
```

In `src/PubInvest.HouseConfig.Data/Seeding/CatalogueSeeder.cs`, insert before `var existingRuleSets = ...`:

```csharp
        // Rack rows follow the same rule as device types: added by id once,
        // never overwritten, so a confirmed part number survives a redeploy.
        var existingRackTypeIds = await db.RackTypes.Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var r in (seed.RackTypes ?? []).Where(r => !existingRackTypeIds.Contains(r.Id)))
        {
            db.RackTypes.Add(new RackTypeRow
            {
                Id = r.Id, Manufacturer = r.Manufacturer, Model = r.Model, PartNumber = r.PartNumber,
                HeightU = r.HeightU, Mounting = r.Mounting
            });
            inserted++;
        }

        var existingRackDeviceIds = await db.RackDeviceTypes.Select(d => d.Id).ToListAsync(cancellationToken);
        foreach (var d in (seed.RackDeviceTypes ?? []).Where(d => !existingRackDeviceIds.Contains(d.Id)))
        {
            db.RackDeviceTypes.Add(new RackDeviceTypeRow
            {
                Id = d.Id, Manufacturer = d.Manufacturer, Model = d.Model, PartNumber = d.PartNumber,
                Kind = d.Kind, HeightU = d.HeightU,
                PortGroupsJson = JsonSerializer.Serialize(d.PortGroups, DomainMapper.Json),
                PoeBudgetW = d.PoeBudgetW, RequiredAccessoryId = d.RequiredAccessoryId, Active = d.Active
            });
            inserted++;
        }
```

- [ ] **Step 6: Write the failing seeder test**

In `tests/PubInvest.HouseConfig.Data.Tests/CatalogueSeederTests.cs`, add `using PubInvest.HouseConfig.Domain.Racks;` and this test inside the class:

```csharp
    [Fact]
    public async Task Rack_catalogue_rows_are_added_once_and_an_edit_survives_a_reseed()
    {
        const string tag = "c0c0c0c0";
        Guid Id(int n) => new($"{tag}-0000-4000-8000-{n:D12}");

        var seed = Document(tag) with
        {
            RackTypes = [new SeedRackType(Id(101), "Generic", "12U wall cabinet", $"{tag}-CAB12", 12, "Wall")],
            RackDeviceTypes =
            [
                new SeedRackDeviceType(Id(102), "Ubiquiti", "Test switch", $"{tag}-SW", "Switch", 1,
                    [new PortGroup("1G", 1, 8, true), new PortGroup("SFP+", 9, 10, false)], 60, null, true),
            ],
        };

        await using (var db = fixture.CreateContext())
        {
            await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var row = await db.RackDeviceTypes.SingleAsync(d => d.Id == Id(102));
            row.PartNumber = $"{tag}-SW-CONFIRMED";
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        Assert.Equal(12, (await verify.RackTypes.SingleAsync(r => r.Id == Id(101))).HeightU);

        var sw = DomainMapper.ToDomain(await verify.RackDeviceTypes.SingleAsync(d => d.Id == Id(102)));
        Assert.Equal($"{tag}-SW-CONFIRMED", sw.PartNumber);
        Assert.Equal(10, sw.PortCount);
        Assert.False(sw.GroupOf(9)!.PoeTick);
        Assert.Null(sw.GroupOf(11));
    }
```

- [ ] **Step 7: Generate the migration and run the seeder test**

```bash
cd /Users/sinamoradian/src/house-submain-builder
dotnet ef migrations add RackCatalogue --project src/PubInvest.HouseConfig.Data
export PATH=/opt/homebrew/bin:$PATH DOCKER_HOST=unix:///Users/sinamoradian/.colima/default/docker.sock TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock TESTCONTAINERS_RYUK_DISABLED=true
dotnet test tests/PubInvest.HouseConfig.Data.Tests --filter "FullyQualifiedName~Rack_catalogue_rows"
```

Expected: the migration creates `RackTypes` and `RackDeviceTypes` (open the generated file and check there are no other table changes), and the test PASSES. If it fails to compile, the error names the missing piece from Steps 2–5.

- [ ] **Step 8: Add the shipped rack catalogue**

In `seed/catalogue.v1.json`, add these two top-level arrays after `"enclosures": [...]` (keep `"version": 1`):

```json
  "rackTypes": [
    { "id": "c1000000-0000-4000-8000-000000000001", "manufacturer": "Generic", "model": "6U wall cabinet",   "partNumber": "ASSUMED-CAB-6U-WALL",   "heightU": 6,  "mounting": "Wall" },
    { "id": "c1000000-0000-4000-8000-000000000002", "manufacturer": "Generic", "model": "9U wall cabinet",   "partNumber": "ASSUMED-CAB-9U-WALL",   "heightU": 9,  "mounting": "Wall" },
    { "id": "c1000000-0000-4000-8000-000000000003", "manufacturer": "Generic", "model": "12U wall cabinet",  "partNumber": "ASSUMED-CAB-12U-WALL",  "heightU": 12, "mounting": "Wall" },
    { "id": "c1000000-0000-4000-8000-000000000004", "manufacturer": "Generic", "model": "22U floor cabinet", "partNumber": "ASSUMED-CAB-22U-FLOOR", "heightU": 22, "mounting": "Floor" },
    { "id": "c1000000-0000-4000-8000-000000000005", "manufacturer": "Generic", "model": "42U floor cabinet", "partNumber": "ASSUMED-CAB-42U-FLOOR", "heightU": 42, "mounting": "Floor" }
  ],
  "rackDeviceTypes": [
    {
      "id": "c2000000-0000-4000-8000-000000000001", "manufacturer": "Ubiquiti", "model": "USW-Pro-Max-48-PoE",
      "partNumber": "USW-Pro-Max-48-PoE", "kind": "Switch", "heightU": 1,
      "portGroups": [
        { "name": "1G",   "from": 1,  "to": 32, "poeTick": true },
        { "name": "2.5G", "from": 33, "to": 48, "poeTick": true },
        { "name": "SFP+", "from": 49, "to": 52, "poeTick": false }
      ],
      "poeBudgetW": 720, "requiredAccessoryId": null, "active": true
    },
    {
      "id": "c2000000-0000-4000-8000-000000000002", "manufacturer": "Ubiquiti", "model": "USW-Pro-Max-24-PoE",
      "partNumber": "USW-Pro-Max-24-PoE", "kind": "Switch", "heightU": 1,
      "portGroups": [
        { "name": "1G",   "from": 1,  "to": 16, "poeTick": true },
        { "name": "2.5G", "from": 17, "to": 24, "poeTick": true },
        { "name": "SFP+", "from": 25, "to": 26, "poeTick": false }
      ],
      "poeBudgetW": 400, "requiredAccessoryId": null, "active": true
    },
    {
      "id": "c2000000-0000-4000-8000-000000000003", "manufacturer": "Ubiquiti", "model": "USW-Pro-Max-16-PoE",
      "partNumber": "USW-Pro-Max-16-PoE", "kind": "Switch", "heightU": 1,
      "portGroups": [
        { "name": "1G",   "from": 1,  "to": 12, "poeTick": true },
        { "name": "2.5G", "from": 13, "to": 16, "poeTick": true },
        { "name": "SFP+", "from": 17, "to": 18, "poeTick": false }
      ],
      "poeBudgetW": 180, "requiredAccessoryId": "c2000000-0000-4000-8000-000000000005", "active": true
    },
    {
      "id": "c2000000-0000-4000-8000-000000000004", "manufacturer": "Generic", "model": "24-port patch panel",
      "partNumber": "ASSUMED-PP24-1U", "kind": "PatchPanel", "heightU": 1,
      "portGroups": [ { "name": "RJ45", "from": 1, "to": 24, "poeTick": true } ],
      "poeBudgetW": null, "requiredAccessoryId": null, "active": true
    },
    {
      "id": "c2000000-0000-4000-8000-000000000005", "manufacturer": "Ubiquiti", "model": "Rack-mount kit for USW-Pro-Max-16-PoE",
      "partNumber": "ASSUMED-USW-PM16-RACK-KIT", "kind": "Accessory", "heightU": 0,
      "portGroups": [], "poeBudgetW": null, "requiredAccessoryId": null, "active": true
    }
  ],
```

Append to the `"_assumed"` string: ` Rack cabinets, the 24-port patch panel and the USW-Pro-Max-16-PoE rack-mount kit carry ASSUMED part numbers, and the three switches' port splits and PoE budgets come from retailer listings rather than Ubiquiti's datasheets: confirm them before ordering.`

- [ ] **Step 9: Write the shipped-seed tests**

In `tests/PubInvest.HouseConfig.Data.Tests/ShippedSeedTests.cs`, add inside the class:

```csharp
    [Fact]
    public void The_shipped_rack_catalogue_has_cabinets_the_three_switches_a_patch_panel_and_the_rack_kit()
    {
        var seed = Load();

        Assert.Equal(new[] { 6, 9, 12, 22, 42 }, seed.RackTypes!.Select(r => r.HeightU).Order());
        var models = seed.RackDeviceTypes!.Select(d => d.Model).ToList();
        Assert.Contains("USW-Pro-Max-48-PoE", models);
        Assert.Contains("USW-Pro-Max-24-PoE", models);
        Assert.Contains("USW-Pro-Max-16-PoE", models);
        Assert.Contains(seed.RackDeviceTypes!, d => d.Kind == "PatchPanel");
        Assert.Contains(seed.RackDeviceTypes!, d => d.Kind == "Accessory");
    }

    /// Ports run 1..N across groups with no gap and no overlap, or "port 33" on
    /// a schedule would not be the 33rd socket on the front panel.
    [Fact]
    public void Every_shipped_rack_item_numbers_its_ports_from_one_without_gaps()
    {
        foreach (var d in Load().RackDeviceTypes!.Where(d => d.Kind != "Accessory"))
        {
            var next = 1;
            foreach (var g in d.PortGroups)
            {
                Assert.True(g.From == next, $"{d.Model}: group {g.Name} starts at {g.From}, expected {next}");
                Assert.True(g.To >= g.From, $"{d.Model}: group {g.Name} ends before it starts");
                next = g.To + 1;
            }
            Assert.True(next > 1, $"{d.Model} has no ports");
            Assert.True(d.HeightU >= 1, $"{d.Model} takes no rack space");
        }
    }

    [Fact]
    public void Every_required_accessory_is_a_shipped_accessory()
    {
        var seed = Load();
        var accessories = seed.RackDeviceTypes!.Where(d => d.Kind == "Accessory").Select(d => d.Id).ToHashSet();

        foreach (var d in seed.RackDeviceTypes!.Where(d => d.RequiredAccessoryId is not null))
        {
            Assert.Contains(d.RequiredAccessoryId!.Value, accessories);
        }

        // The half-width switch cannot go in a rack without its kit.
        Assert.Contains(seed.RackDeviceTypes!, d => d.Model == "USW-Pro-Max-16-PoE" && d.RequiredAccessoryId is not null);
    }
```

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests --filter "FullyQualifiedName~ShippedSeedTests"` (with the Testcontainers env vars). Expected: PASS.

- [ ] **Step 10: Add rack rows to the API test seed**

In `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs`, add `using PubInvest.HouseConfig.Domain.Racks;`, these ids after `RuleSetId`:

```csharp
    public static readonly Guid Cabinet4UId      = new("dddd0000-0000-4000-8000-000000000001");
    public static readonly Guid Cabinet2UId      = new("dddd0000-0000-4000-8000-000000000002");
    public static readonly Guid Switch48Id       = new("dddd0000-0000-4000-8000-000000000011");
    public static readonly Guid Switch16Id       = new("dddd0000-0000-4000-8000-000000000012");
    public static readonly Guid PatchPanelId     = new("dddd0000-0000-4000-8000-000000000013");
    public static readonly Guid PatchPanel2UId   = new("dddd0000-0000-4000-8000-000000000014");
    public static readonly Guid RackKitId        = new("dddd0000-0000-4000-8000-000000000015");
    public static readonly Guid InactiveSwitchId = new("dddd0000-0000-4000-8000-000000000016");
```

and, in `Document()`, after the `RuleSets: [...]` argument (add a comma after its closing `]`):

```csharp
        RackTypes:
        [
            new SeedRackType(Cabinet4UId, "Test", "4U cabinet", "T-CAB4", 4, "Wall"),
            new SeedRackType(Cabinet2UId, "Test", "2U cabinet", "T-CAB2", 2, "Wall"),
        ],
        RackDeviceTypes:
        [
            new SeedRackDeviceType(Switch48Id, "Ubiquiti", "Test 48 PoE", "T-SW48", "Switch", 1,
                [new PortGroup("1G", 1, 32, true), new PortGroup("2.5G", 33, 48, true), new PortGroup("SFP+", 49, 52, false)],
                720, null, true),
            new SeedRackDeviceType(Switch16Id, "Ubiquiti", "Test 16 PoE", "T-SW16", "Switch", 1,
                [new PortGroup("1G", 1, 12, true), new PortGroup("2.5G", 13, 16, true), new PortGroup("SFP+", 17, 18, false)],
                180, RackKitId, true),
            new SeedRackDeviceType(PatchPanelId, "Test", "24-port patch panel", "T-PP24", "PatchPanel", 1,
                [new PortGroup("RJ45", 1, 24, true)], null, null, true),
            new SeedRackDeviceType(PatchPanel2UId, "Test", "48-port patch panel 2U", "T-PP48", "PatchPanel", 2,
                [new PortGroup("RJ45", 1, 48, true)], null, null, true),
            new SeedRackDeviceType(RackKitId, "Ubiquiti", "Test rack kit", "T-KIT", "Accessory", 0, [], null, null, true),
            new SeedRackDeviceType(InactiveSwitchId, "Ubiquiti", "Test retired switch", "T-SWOLD", "Switch", 1,
                [new PortGroup("1G", 1, 8, true)], null, null, false),
        ]);
```

- [ ] **Step 11: Write the failing catalogue endpoint test**

Create `tests/PubInvest.HouseConfig.Api.Tests/RackCatalogueEndpointTests.cs`:

```csharp
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackCatalogueEndpointTests(HouseConfigApiFactory factory)
{
    private record PortGroupDto(string Name, int From, int To, bool PoeTick);
    private record RackTypeDto(Guid Id, string Model, int HeightU, string Description);
    private record RackDeviceTypeDto(
        Guid Id, string Model, string Kind, int HeightU, PortGroupDto[] PortGroups, int PortCount,
        int? PoeBudgetW, Guid? RequiredAccessoryId, bool Active);

    private async Task<HttpClient> Seeded()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }
        return factory.CreateClient();
    }

    [Fact]
    public async Task Lists_rack_cabinets_by_height()
    {
        var client = await Seeded();

        var cabinets = await client.GetFromJsonAsync<RackTypeDto[]>("/catalogue/rack-types");

        var four = cabinets!.Single(c => c.Id == TestSeed.Cabinet4UId);
        Assert.Equal(4, four.HeightU);
        Assert.Equal("Test 4U cabinet", four.Description);
        Assert.Equal(cabinets!.OrderBy(c => c.HeightU).Select(c => c.Id), cabinets!.Select(c => c.Id));
    }

    [Fact]
    public async Task Lists_rack_items_with_their_port_groups_and_required_accessory()
    {
        var client = await Seeded();

        var items = await client.GetFromJsonAsync<RackDeviceTypeDto[]>("/catalogue/rack-device-types");

        var sw = items!.Single(i => i.Id == TestSeed.Switch48Id);
        Assert.Equal("Switch", sw.Kind);
        Assert.Equal(52, sw.PortCount);
        Assert.Equal(new[] { "1G", "2.5G", "SFP+" }, sw.PortGroups.Select(g => g.Name));
        Assert.False(sw.PortGroups[2].PoeTick);
        Assert.Equal(TestSeed.RackKitId, items!.Single(i => i.Id == TestSeed.Switch16Id).RequiredAccessoryId);
        Assert.Equal("Accessory", items!.Single(i => i.Id == TestSeed.RackKitId).Kind);
    }
}
```

Run (with the Testcontainers env vars): `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackCatalogueEndpointTests"`
Expected: FAIL — both requests return 404.

- [ ] **Step 12: Add the catalogue read endpoints**

In `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueEndpoints.cs`, add before `return app;`:

```csharp
        group.MapGet("/rack-types", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.RackTypes.OrderBy(r => r.HeightU).ThenBy(r => r.Model).ToListAsync(ct))
                .Select(DomainMapper.ToDomain));

        group.MapGet("/rack-device-types", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.RackDeviceTypes.OrderBy(d => d.Kind).ThenBy(d => d.Model).ToListAsync(ct))
                .Select(DomainMapper.ToDomain));
```

Run the test from Step 11 again. Expected: PASS. Then run the whole backend: `dotnet test` (env vars set). Expected: all pass, exit 0.

- [ ] **Step 13: Commit**

```bash
git add .config/dotnet-tools.json src/PubInvest.HouseConfig.Domain/Racks src/PubInvest.HouseConfig.Data seed/catalogue.v1.json src/PubInvest.HouseConfig.Api/Endpoints/CatalogueEndpoints.cs tests
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Add rack cabinets and rack items to the catalogue

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rack placement — a pure function the server calls for every add, move and cabinet change

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Racks/RackPlacement.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/RackPlacementTests.cs`

**Interfaces:**
- Produces:
  - `record RackSpan(Guid ItemId, int TopU, int HeightU)` with `int BottomU`, `bool Overlaps(int topU, int heightU)`
  - `static int? RackPlacement.NextTopU(int rackHeightU, int heightU, IReadOnlyCollection<RackSpan> occupied)`
  - `static bool RackPlacement.CanPlace(int rackHeightU, int topU, int heightU, IReadOnlyCollection<RackSpan> occupied, Guid? movingItemId = null)`
  - `static int RackPlacement.FreeU(int rackHeightU, IReadOnlyCollection<RackSpan> occupied)`
  - `static (int From, int To)? RackPlacement.Blocking(int newHeightU, IReadOnlyCollection<RackSpan> occupied)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PubInvest.HouseConfig.Domain.Tests/RackPlacementTests.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Domain.Tests;

public class RackPlacementTests
{
    private static RackSpan At(int topU, int heightU = 1) => new(Guid.NewGuid(), topU, heightU);

    [Fact]
    public void The_first_item_goes_in_the_top_u() =>
        Assert.Equal(12, RackPlacement.NextTopU(12, 1, []));

    [Fact]
    public void The_next_item_goes_under_the_last() =>
        Assert.Equal(11, RackPlacement.NextTopU(12, 1, [At(12)]));

    [Fact]
    public void A_gap_left_by_a_removed_item_is_filled_first() =>
        Assert.Equal(12, RackPlacement.NextTopU(12, 1, [At(11), At(10)]));

    /// U12 is free but U11 is taken, so a 2U item cannot start at the top: it
    /// takes U10–U9, the highest run of two free U.
    [Fact]
    public void A_taller_item_needs_a_run_of_free_u_tall_enough() =>
        Assert.Equal(10, RackPlacement.NextTopU(12, 2, [At(11)]));

    [Fact]
    public void A_full_rack_has_no_room() =>
        Assert.Null(RackPlacement.NextTopU(2, 1, [At(2), At(1)]));

    [Fact]
    public void An_item_taller_than_any_free_run_has_no_room() =>
        Assert.Null(RackPlacement.NextTopU(3, 2, [At(2)]));

    [Theory]
    [InlineData(12, 1, true)]
    [InlineData(13, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    public void A_place_must_be_inside_the_cabinet(int topU, int heightU, bool allowed) =>
        Assert.Equal(allowed, RackPlacement.CanPlace(12, topU, heightU, []));

    [Fact]
    public void A_place_on_another_item_is_refused() =>
        Assert.False(RackPlacement.CanPlace(12, 5, 1, [At(5)]));

    [Fact]
    public void A_two_u_place_that_clips_another_item_is_refused() =>
        Assert.False(RackPlacement.CanPlace(12, 6, 2, [At(5)]));

    [Fact]
    public void A_move_that_overlaps_its_own_old_place_is_allowed()
    {
        var self = At(5, 2);
        Assert.True(RackPlacement.CanPlace(12, 6, 2, [self], self.ItemId));
    }

    [Fact]
    public void Free_u_counts_what_is_left() =>
        Assert.Equal(9, RackPlacement.FreeU(12, [At(12), At(11, 2)]));

    /// The whole of each item above the new top has to go, so the range starts
    /// at the lowest U of those items, not at the new top.
    [Fact]
    public void Shrinking_past_items_names_the_u_to_clear() =>
        Assert.Equal((10, 12), RackPlacement.Blocking(9, [At(12), At(11, 2), At(3)]));

    [Fact]
    public void Shrinking_with_everything_at_or_below_the_new_top_is_fine() =>
        Assert.Null(RackPlacement.Blocking(9, [At(9), At(3)]));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter "FullyQualifiedName~RackPlacementTests"`
Expected: build FAILS — `RackSpan` and `RackPlacement` do not exist.

- [ ] **Step 3: Write the placement**

Create `src/PubInvest.HouseConfig.Domain/Racks/RackPlacement.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Racks;

/// The U an item occupies, from BottomU up to TopU, both included. U1 is the
/// bottom of the cabinet.
public sealed record RackSpan(Guid ItemId, int TopU, int HeightU)
{
    public int BottomU => TopU - HeightU + 1;

    public bool Overlaps(int topU, int heightU) => topU - heightU + 1 <= TopU && BottomU <= topU;
}

/// Where items go in a rack. Pure, like the panel packer: the server calls it
/// for every add, move and cabinet change, and the UI only ever draws what the
/// server decided.
public static class RackPlacement
{
    /// The highest TopU where an item heightU tall fits on free U, or null when
    /// no run of free U is tall enough. Items fill from the top down, and a gap
    /// a removed item left near the top is filled before anything lower.
    public static int? NextTopU(int rackHeightU, int heightU, IReadOnlyCollection<RackSpan> occupied)
    {
        for (var top = rackHeightU; top - heightU + 1 >= 1; top--)
        {
            if (CanPlace(rackHeightU, top, heightU, occupied)) return top;
        }

        return null;
    }

    /// Inside the cabinet and on free U. An item being moved may overlap its
    /// own current place, so a one-U nudge of a 2U item is allowed.
    public static bool CanPlace(
        int rackHeightU,
        int topU,
        int heightU,
        IReadOnlyCollection<RackSpan> occupied,
        Guid? movingItemId = null)
        => heightU >= 1
           && topU <= rackHeightU
           && topU - heightU + 1 >= 1
           && occupied.All(s => s.ItemId == movingItemId || !s.Overlaps(topU, heightU));

    public static int FreeU(int rackHeightU, IReadOnlyCollection<RackSpan> occupied)
        => rackHeightU - occupied.Sum(s => s.HeightU);

    /// For a change to a shorter cabinet: the U holding items that would end up
    /// above the new top, from the lowest U of those items to the highest — or
    /// null when everything still fits. Items are never shuffled to make room;
    /// the person changing the cabinet is told what to move.
    public static (int From, int To)? Blocking(int newHeightU, IReadOnlyCollection<RackSpan> occupied)
    {
        var above = occupied.Where(s => s.TopU > newHeightU).ToList();
        return above.Count == 0 ? null : (above.Min(s => s.BottomU), above.Max(s => s.TopU));
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter "FullyQualifiedName~RackPlacementTests"`
Expected: PASS (17 cases).

- [ ] **Step 5: Commit**

```bash
git add src/PubInvest.HouseConfig.Domain/Racks/RackPlacement.cs tests/PubInvest.HouseConfig.Domain.Tests/RackPlacementTests.cs
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Place rack items top down, and refuse what does not fit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Racks on a house — tables, rack endpoints, layout, and the racks a submain feeds

**Files:**
- Create: `src/PubInvest.HouseConfig.Data/Entities/Rack.cs`
- Modify: `src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs`
- Create: `src/PubInvest.HouseConfig.Data/Migrations/<timestamp>_Racks.cs` (generated)
- Create: `src/PubInvest.HouseConfig.Api/Contracts/RackContracts.cs`
- Create: `src/PubInvest.HouseConfig.Api/Racks/RackViews.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/RackEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/RackEndpointTests.cs`

**Interfaces:**
- Consumes: Task 1 catalogue types and rows; Task 2 `RackSpan`, `RackPlacement.FreeU`, `RackPlacement.Blocking`.
- Produces (Data): `Rack { Id, ProjectId, Name, RackTypeId, FedFromSubmainId, Notes, LayoutVersion, SwitchesNamed, PatchPanelsNamed, CreatedAt, Items }`, `RackItem { Id, RackId, RackDeviceTypeId, TopU, Sequence, Label, Ports }`, `RackPort { RackItemId, Number, Label, Poe }`; DbSets `Racks`, `RackItems`, `RackPorts`.
- Produces (API contracts, namespace `PubInvest.HouseConfig.Api.Contracts`):
  - `SaveRackRequest(string Name, Guid RackTypeId, Guid? FedFromSubmainId, string? Notes = null)`
  - `RackResponse(Guid Id, Guid ProjectId, string Name, Guid RackTypeId, string Cabinet, int HeightU, int UsedU, Guid? FedFromSubmainId, string? FedFromSubmainName, string? Notes, int LayoutVersion)`
  - `RackPortResponse(int Number, string? Label, bool Poe)`
  - `RackItemResponse(Guid Id, Guid RackDeviceTypeId, RackDeviceKind Kind, string Model, string PartNumber, string Label, int TopU, int HeightU, IReadOnlyList<PortGroup> PortGroups, int? PoeBudgetW, IReadOnlyList<RackPortResponse> Ports)`
  - `RackLayoutResponse(Guid RackId, int HeightU, int FreeU, int LayoutVersion, IReadOnlyList<RackItemResponse> Items)` — items ordered top U first
  - `AddRackItemRequest(Guid RackDeviceTypeId)`, `MoveRackItemRequest(int TopU, int BasedOnLayoutVersion)`, `SaveRackPortRequest(string? Label, bool Poe)`, `FedRackResponse(Guid Id, string Name)`, `RefusalResponse(string Message)`
- Produces (API helpers, `PubInvest.HouseConfig.Api.Racks.RackViews`): `ResponseAsync(db, Rack, ct)`, `LayoutAsync(db, Guid rackId, ct)` → `RackLayoutResponse?`, `SpansAsync(db, Rack, ct)` → `List<RackSpan>`, `TypesAsync(db, IEnumerable<RackItem>, ct)` → `Dictionary<Guid, RackDeviceType>`, `ValidateAsync(db, Guid projectId, SaveRackRequest, ct)` → field problems.
- Produces (routes): `GET/POST /projects/{id}/racks`, `GET/PATCH /racks/{id}`, `GET /racks/{id}/layout`, `GET /submains/{id}/racks`.

- [ ] **Step 1: Write the rack entities**

Create `src/PubInvest.HouseConfig.Data/Entities/Rack.cs`:

```csharp
namespace PubInvest.HouseConfig.Data.Entities;

/// A network rack in a house. It sits beside the submains rather than inside
/// one: the submain that feeds it is a fact about the rack, recorded as a link
/// that can be blank or change without the rack moving.
public class Rack
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public Guid RackTypeId { get; set; }
    public Guid? FedFromSubmainId { get; set; }
    public string? Notes { get; set; }

    /// Bumped by every add, move and removal. A concurrency token, so two adds
    /// racing for the same U cannot both land.
    public int LayoutVersion { get; set; }

    /// How many switches and patch panels this rack has ever named. A removed
    /// item's name is never handed to a later one, so "Switch 2" on a printed
    /// drawing keeps meaning the same switch.
    public int SwitchesNamed { get; set; }
    public int PatchPanelsNamed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public List<RackItem> Items { get; set; } = [];
}

public class RackItem
{
    public Guid Id { get; set; }
    public Guid RackId { get; set; }
    public Guid RackDeviceTypeId { get; set; }

    /// The highest U the item occupies; U1 is the bottom of the cabinet.
    public int TopU { get; set; }

    public int Sequence { get; set; }
    public string Label { get; set; } = "";
    public List<RackPort> Ports { get; set; } = [];
}

/// Stored only once a port has a label or a PoE tick, so a new 48-port switch
/// writes nothing here.
public class RackPort
{
    public Guid RackItemId { get; set; }
    public int Number { get; set; }
    public string? Label { get; set; }
    public bool Poe { get; set; }
}
```

In `HouseConfigDbContext.cs`, add the DbSets after `RackDeviceTypes`:

```csharp
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<RackItem> RackItems => Set<RackItem>();
    public DbSet<RackPort> RackPorts => Set<RackPort>();
```

and in `OnModelCreating`, after the `RackDeviceTypeRow` index:

```csharp
        b.Entity<Project>().HasMany<Rack>().WithOne()
            .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // The feed is a link, not ownership: if the submain ever goes, the rack
        // stays and simply has no feed recorded.
        b.Entity<Rack>().HasOne<Submain>().WithMany()
            .HasForeignKey(r => r.FedFromSubmainId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Rack>().HasOne<RackTypeRow>().WithMany()
            .HasForeignKey(r => r.RackTypeId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Rack>().Property(r => r.LayoutVersion).IsConcurrencyToken();

        b.Entity<Rack>().HasMany(r => r.Items).WithOne()
            .HasForeignKey(i => i.RackId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RackItem>().HasIndex(i => new { i.RackId, i.TopU }).IsUnique();

        b.Entity<RackItem>().HasOne<RackDeviceTypeRow>().WithMany()
            .HasForeignKey(i => i.RackDeviceTypeId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<RackItem>().HasMany(i => i.Ports).WithOne()
            .HasForeignKey(p => p.RackItemId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RackPort>().HasKey(p => new { p.RackItemId, p.Number });
```

Run: `dotnet ef migrations add Racks --project src/PubInvest.HouseConfig.Data`
Expected: a migration creating `Racks`, `RackItems`, `RackPorts` with the foreign keys above and nothing else.

- [ ] **Step 2: Write the contracts**

Create `src/PubInvest.HouseConfig.Api/Contracts/RackContracts.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record SaveRackRequest(string Name, Guid RackTypeId, Guid? FedFromSubmainId, string? Notes = null);

public sealed record RackResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    Guid RackTypeId,
    string Cabinet,
    int HeightU,
    int UsedU,
    Guid? FedFromSubmainId,
    string? FedFromSubmainName,
    string? Notes,
    int LayoutVersion);

public sealed record RackPortResponse(int Number, string? Label, bool Poe);

public sealed record RackItemResponse(
    Guid Id,
    Guid RackDeviceTypeId,
    RackDeviceKind Kind,
    string Model,
    string PartNumber,
    string Label,
    int TopU,
    int HeightU,
    IReadOnlyList<PortGroup> PortGroups,
    int? PoeBudgetW,
    /// Only the ports that have a label or a tick; the rest are implied by the groups.
    IReadOnlyList<RackPortResponse> Ports);

/// Items top U first, the order a person reads a rack in.
public sealed record RackLayoutResponse(
    Guid RackId,
    int HeightU,
    int FreeU,
    int LayoutVersion,
    IReadOnlyList<RackItemResponse> Items);

public sealed record AddRackItemRequest(Guid RackDeviceTypeId);

public sealed record MoveRackItemRequest(int TopU, int BasedOnLayoutVersion);

public sealed record SaveRackPortRequest(string? Label, bool Poe);

public sealed record FedRackResponse(Guid Id, string Name);

/// A request the rack cannot take, said in words: "No room in this rack: 0U free".
public sealed record RefusalResponse(string Message);
```

- [ ] **Step 3: Write the failing endpoint tests**

Create `tests/PubInvest.HouseConfig.Api.Tests/RackEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record RackDto(
        Guid Id, string Name, Guid RackTypeId, string Cabinet, int HeightU, int UsedU,
        Guid? FedFromSubmainId, string? FedFromSubmainName, int LayoutVersion);
    private record LayoutDto(Guid RackId, int HeightU, int FreeU, int LayoutVersion, object[] Items);
    private record FedDto(Guid Id, string Name);
    private record ProblemDto(Dictionary<string, string[]> Errors);

    private async Task<(HttpClient Client, Guid ProjectId)> House()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Racks {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        return (client, project!.Id);
    }

    private static async Task<Guid> Submain(HttpClient client, Guid projectId, string name) =>
        (await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name, enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId, circuits = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<IdDto>())!.Id;

    private static Task<HttpResponseMessage> AddRack(HttpClient client, Guid projectId, object body) =>
        client.PostAsJsonAsync($"/projects/{projectId}/racks", body);

    [Fact]
    public async Task A_rack_is_added_to_a_house_and_listed_with_its_cabinet_and_feed()
    {
        var (client, projectId) = await House();
        var ground = await Submain(client, projectId, "Ground floor");

        var response = await AddRack(client, projectId,
            new { name = "  Comms cupboard ", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = ground });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rack = await response.Content.ReadFromJsonAsync<RackDto>();
        Assert.Equal("Comms cupboard", rack!.Name);
        Assert.Equal("Test 4U cabinet", rack.Cabinet);
        Assert.Equal(4, rack.HeightU);
        Assert.Equal(0, rack.UsedU);
        Assert.Equal("Ground floor", rack.FedFromSubmainName);

        var listed = await client.GetFromJsonAsync<RackDto[]>($"/projects/{projectId}/racks");
        Assert.Equal(rack.Id, Assert.Single(listed!).Id);
    }

    [Fact]
    public async Task A_rack_can_be_fed_from_no_submain()
    {
        var (client, projectId) = await House();

        var rack = await (await AddRack(client, projectId,
            new { name = "Loft", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = (Guid?)null }))
            .Content.ReadFromJsonAsync<RackDto>();

        Assert.Null(rack!.FedFromSubmainId);
        Assert.Null(rack.FedFromSubmainName);
    }

    [Fact]
    public async Task A_feed_from_another_house_is_refused()
    {
        var (client, projectId) = await House();
        var (_, otherProject) = await House();
        var elsewhere = await Submain(client, otherProject, "Next door");

        var response = await AddRack(client, projectId,
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = elsewhere });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Contains("fedFromSubmainId", problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_rack_needs_a_name_and_a_cabinet_from_the_catalogue()
    {
        var (client, projectId) = await House();

        var response = await AddRack(client, projectId, new { name = " ", rackTypeId = Guid.NewGuid() });

        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", problem!.Errors.Keys);
        Assert.Contains("rackTypeId", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_new_rack_has_an_empty_layout_with_every_u_free()
    {
        var (client, projectId) = await House();
        var rack = await (await AddRack(client, projectId, new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId }))
            .Content.ReadFromJsonAsync<RackDto>();

        var layout = await client.GetFromJsonAsync<LayoutDto>($"/racks/{rack!.Id}/layout");

        Assert.Equal(4, layout!.HeightU);
        Assert.Equal(4, layout.FreeU);
        Assert.Empty(layout.Items);
    }

    [Fact]
    public async Task The_feed_can_be_changed_and_cleared_and_the_submain_lists_what_it_feeds()
    {
        var (client, projectId) = await House();
        var ground = await Submain(client, projectId, "Ground floor");
        var first = await Submain(client, projectId, "First floor");
        var rack = await (await AddRack(client, projectId,
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = ground }))
            .Content.ReadFromJsonAsync<RackDto>();

        Assert.Equal("Comms", Assert.Single((await client.GetFromJsonAsync<FedDto[]>($"/submains/{ground}/racks"))!).Name);

        var moved = await (await client.PatchAsJsonAsync($"/racks/{rack!.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = first }))
            .Content.ReadFromJsonAsync<RackDto>();
        Assert.Equal("First floor", moved!.FedFromSubmainName);
        Assert.Empty((await client.GetFromJsonAsync<FedDto[]>($"/submains/{ground}/racks"))!);

        var cleared = await (await client.PatchAsJsonAsync($"/racks/{rack.Id}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = (Guid?)null }))
            .Content.ReadFromJsonAsync<RackDto>();
        Assert.Null(cleared!.FedFromSubmainId);
    }

    [Fact]
    public async Task An_unknown_rack_or_house_is_not_found()
    {
        var (client, _) = await House();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/racks/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/racks/{Guid.NewGuid()}/layout")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/projects/{Guid.NewGuid()}/racks")).StatusCode);
    }
}
```

Run (Testcontainers env vars set): `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackEndpointTests"`
Expected: FAIL — the routes return 404 or 405.

- [ ] **Step 4: Write the view builder**

Create `src/PubInvest.HouseConfig.Api/Racks/RackViews.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// Builds what the endpoints return, from the stored rack. The layout is the
/// one place the UI learns where anything is.
public static class RackViews
{
    public static async Task<Dictionary<Guid, RackDeviceType>> TypesAsync(
        HouseConfigDbContext db,
        IEnumerable<RackItem> items,
        CancellationToken ct)
    {
        var ids = items.Select(i => i.RackDeviceTypeId).Distinct().ToList();
        return (await db.RackDeviceTypes.Where(t => ids.Contains(t.Id)).ToListAsync(ct))
            .Select(DomainMapper.ToDomain)
            .ToDictionary(t => t.Id);
    }

    /// Needs rack.Items loaded.
    public static async Task<List<RackSpan>> SpansAsync(HouseConfigDbContext db, Rack rack, CancellationToken ct)
    {
        var types = await TypesAsync(db, rack.Items, ct);
        return rack.Items.Select(i => new RackSpan(i.Id, i.TopU, types[i.RackDeviceTypeId].HeightU)).ToList();
    }

    /// Needs rack.Items loaded.
    public static async Task<RackResponse> ResponseAsync(HouseConfigDbContext db, Rack rack, CancellationToken ct)
    {
        var cabinet = DomainMapper.ToDomain(await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct));
        var spans = await SpansAsync(db, rack, ct);
        var feed = rack.FedFromSubmainId is { } submainId
            ? await db.Submains.Where(s => s.Id == submainId).Select(s => s.Name).SingleOrDefaultAsync(ct)
            : null;

        return new RackResponse(
            rack.Id, rack.ProjectId, rack.Name, cabinet.Id, cabinet.Description, cabinet.HeightU,
            spans.Sum(s => s.HeightU), rack.FedFromSubmainId, feed, rack.Notes, rack.LayoutVersion);
    }

    public static async Task<RackLayoutResponse?> LayoutAsync(HouseConfigDbContext db, Guid rackId, CancellationToken ct)
    {
        var rack = await db.Racks
            .AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Ports)
            .SingleOrDefaultAsync(r => r.Id == rackId, ct);
        if (rack is null) return null;

        var cabinet = await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct);
        var types = await TypesAsync(db, rack.Items, ct);
        var spans = rack.Items.Select(i => new RackSpan(i.Id, i.TopU, types[i.RackDeviceTypeId].HeightU)).ToList();

        return new RackLayoutResponse(
            rack.Id,
            cabinet.HeightU,
            RackPlacement.FreeU(cabinet.HeightU, spans),
            rack.LayoutVersion,
            rack.Items
                .OrderByDescending(i => i.TopU)
                .Select(i => Item(i, types[i.RackDeviceTypeId]))
                .ToList());
    }

    public static RackItemResponse Item(RackItem item, RackDeviceType type) => new(
        item.Id, type.Id, type.Kind, type.Description, type.PartNumber, item.Label, item.TopU, type.HeightU,
        type.PortGroups, type.PoeBudgetW,
        item.Ports.OrderBy(p => p.Number).Select(p => new RackPortResponse(p.Number, p.Label, p.Poe)).ToList());

    public static async Task<Dictionary<string, string[]>> ValidateAsync(
        HouseConfigDbContext db,
        Guid projectId,
        SaveRackRequest request,
        CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problems["name"] = ["A rack name is required."];
        }

        if (!await db.RackTypes.AnyAsync(t => t.Id == request.RackTypeId, ct))
        {
            problems["rackTypeId"] = ["Choose a cabinet from the catalogue."];
        }

        if (request.FedFromSubmainId is { } feed
            && !await db.Submains.AnyAsync(s => s.Id == feed && s.ProjectId == projectId, ct))
        {
            problems["fedFromSubmainId"] = ["Choose a submain in this house, or none."];
        }

        return problems;
    }
}
```

- [ ] **Step 5: Write the rack endpoints and map them**

Create `src/PubInvest.HouseConfig.Api/Endpoints/RackEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackEndpoints
{
    public static IEndpointRouteBuilder MapRackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/{id:guid}/racks", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                var racks = await db.Racks.Include(r => r.Items)
                    .Where(r => r.ProjectId == id).OrderBy(r => r.Name).ToListAsync(ct);

                var responses = new List<RackResponse>();
                foreach (var rack in racks) responses.Add(await RackViews.ResponseAsync(db, rack, ct));
                return Results.Ok(responses);
            })
            .WithTags("Racks");

        app.MapPost("/projects/{id:guid}/racks", async (
                Guid id,
                SaveRackRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                var problems = await RackViews.ValidateAsync(db, id, request, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                var rack = new Rack
                {
                    Id = Guid.NewGuid(),
                    ProjectId = id,
                    Name = request.Name.Trim(),
                    RackTypeId = request.RackTypeId,
                    FedFromSubmainId = request.FedFromSubmainId,
                    Notes = request.Notes,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                db.Racks.Add(rack);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/racks/{rack.Id}", await RackViews.ResponseAsync(db, rack, ct));
            })
            .WithTags("Racks");

        app.MapGet("/racks/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                var rack = await db.Racks.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
                return rack is null ? Results.NotFound() : Results.Ok(await RackViews.ResponseAsync(db, rack, ct));
            })
            .WithTags("Racks");

        app.MapPatch("/racks/{id:guid}", async (
                Guid id,
                SaveRackRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var rack = await db.Racks.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
                if (rack is null) return Results.NotFound();

                var problems = await RackViews.ValidateAsync(db, rack.ProjectId, request, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                // A shorter cabinet is refused rather than items being shuffled:
                // the person changing it is told exactly what to move first.
                if (request.RackTypeId != rack.RackTypeId)
                {
                    var cabinet = await db.RackTypes.SingleAsync(t => t.Id == request.RackTypeId, ct);
                    var spans = await RackViews.SpansAsync(db, rack, ct);
                    if (RackPlacement.Blocking(cabinet.HeightU, spans) is { } blocked)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["rackTypeId"] =
                            [
                                $"A {cabinet.HeightU}U cabinet is too short for what is in this rack. " +
                                $"Move or remove what's in U{blocked.From}–U{blocked.To} first."
                            ]
                        });
                    }
                }

                rack.Name = request.Name.Trim();
                rack.RackTypeId = request.RackTypeId;
                rack.FedFromSubmainId = request.FedFromSubmainId;
                rack.Notes = request.Notes;

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // An item was added, moved or removed between our read and our
                    // save, so the cabinet check above may be stale.
                    db.ChangeTracker.Clear();
                    var current = await db.Racks.Where(r => r.Id == id).Select(r => r.LayoutVersion).SingleAsync(ct);
                    return Results.Json(
                        new ConflictResponse("This rack changed since you loaded it. Reload and try again.", current),
                        statusCode: StatusCodes.Status409Conflict);
                }

                return Results.Ok(await RackViews.ResponseAsync(db, rack, ct));
            })
            .WithTags("Racks");

        app.MapGet("/racks/{id:guid}/layout", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
                await RackViews.LayoutAsync(db, id, ct) is { } layout ? Results.Ok(layout) : Results.NotFound())
            .WithTags("Racks");

        // Its own endpoint rather than a field on the submain: the submain
        // response is built in many places, and only the panel screen asks.
        app.MapGet("/submains/{id:guid}/racks", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
                await db.Racks
                    .Where(r => r.FedFromSubmainId == id)
                    .OrderBy(r => r.Name)
                    .Select(r => new FedRackResponse(r.Id, r.Name))
                    .ToListAsync(ct))
            .WithTags("Racks");

        return app;
    }
}
```

In `src/PubInvest.HouseConfig.Api/Program.cs`, add after `app.MapExportEndpoints();`:

```csharp
app.MapRackEndpoints();
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackEndpointTests"`
Expected: PASS (7 tests). Then `dotnet test` for the whole backend: exit 0.

- [ ] **Step 7: Commit**

```bash
git add src/PubInvest.HouseConfig.Data src/PubInvest.HouseConfig.Api tests/PubInvest.HouseConfig.Api.Tests/RackEndpointTests.cs
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Let a house have network racks, each optionally fed from a submain

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Rack items and ports — add, move, remove, label, PoE

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/RackItemEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/RackItemEndpointTests.cs`

**Interfaces:**
- Consumes: Task 2 placement; Task 3 entities, contracts, `RackViews`.
- Produces:
  - `POST /racks/{id}/items` `{ rackDeviceTypeId }` → `201` `RackLayoutResponse`, or `422` `RefusalResponse`
  - `PATCH /rack-items/{id}/position` `{ topU, basedOnLayoutVersion }` → `200 { layoutVersion }`, `409` `ConflictResponse`, or `422` `RefusalResponse`
  - `DELETE /rack-items/{id}?basedOnLayoutVersion=n` → `200` `RackLayoutResponse` or `409`
  - `PUT /rack-items/{id}/ports/{number}` `{ label, poe }` → `200` `RackPortResponse`, `400` (fields `label`, `poe`), or `404`
  - `RackItemEndpoints.MaxPortLabel = 60`

- [ ] **Step 1: Write the failing tests**

Create `tests/PubInvest.HouseConfig.Api.Tests/RackItemEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackItemEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record PortDto(int Number, string? Label, bool Poe);
    private record ItemDto(Guid Id, string Kind, string Label, int TopU, int HeightU, PortDto[] Ports);
    private record LayoutDto(Guid RackId, int HeightU, int FreeU, int LayoutVersion, ItemDto[] Items);
    private record VersionDto(int LayoutVersion);
    private record RefusalDto(string Message);
    private record ConflictDto(string Message, int CurrentLayoutVersion);
    private record ProblemDto(Dictionary<string, string[]> Errors);

    private async Task<(HttpClient Client, Guid RackId)> Rack(Guid cabinetId)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Items {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = cabinetId })).Content.ReadFromJsonAsync<IdDto>();
        return (client, rack!.Id);
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, Guid rackId, Guid typeId) =>
        client.PostAsJsonAsync($"/racks/{rackId}/items", new { rackDeviceTypeId = typeId });

    private static async Task<LayoutDto> Added(HttpClient client, Guid rackId, Guid typeId)
    {
        var response = await Add(client, rackId, typeId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LayoutDto>())!;
    }

    private static async Task<LayoutDto> Layout(HttpClient client, Guid rackId) =>
        (await client.GetFromJsonAsync<LayoutDto>($"/racks/{rackId}/layout"))!;

    private static Task<HttpResponseMessage> Port(HttpClient client, Guid itemId, int number, string? label, bool poe) =>
        client.PutAsJsonAsync($"/rack-items/{itemId}/ports/{number}", new { label, poe });

    [Fact]
    public async Task Items_fill_the_rack_from_the_top_down_and_are_named_by_kind()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);

        await Added(client, rackId, TestSeed.Switch48Id);
        await Added(client, rackId, TestSeed.PatchPanelId);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);

        Assert.Equal(new[] { 4, 3, 2 }, layout.Items.Select(i => i.TopU));
        Assert.Equal(new[] { "Switch 1", "Patch panel 1", "Switch 2" }, layout.Items.Select(i => i.Label));
        Assert.Equal(1, layout.FreeU);
    }

    [Fact]
    public async Task A_removed_items_name_is_never_given_to_a_later_one()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);
        var second = layout.Items.Single(i => i.Label == "Switch 2");

        await client.DeleteAsync($"/rack-items/{second.Id}?basedOnLayoutVersion={layout.LayoutVersion}");
        var after = await Added(client, rackId, TestSeed.Switch48Id);

        Assert.Contains(after.Items, i => i.Label == "Switch 3");
        Assert.DoesNotContain(after.Items, i => i.Label == "Switch 2");
    }

    [Fact]
    public async Task A_full_rack_refuses_another_item_in_words_and_saves_nothing()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet2UId);
        await Added(client, rackId, TestSeed.PatchPanel2UId);

        var response = await Add(client, rackId, TestSeed.Switch48Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.StartsWith("No room in this rack: 0U free", (await response.Content.ReadFromJsonAsync<RefusalDto>())!.Message);
        Assert.Single((await Layout(client, rackId)).Items);
    }

    [Fact]
    public async Task An_accessory_or_a_retired_item_is_never_placed()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(client, rackId, TestSeed.RackKitId)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(client, rackId, TestSeed.InactiveSwitchId)).StatusCode);
        Assert.Empty((await Layout(client, rackId)).Items);
    }

    [Fact]
    public async Task A_move_to_a_free_u_is_saved_and_bumps_the_version()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);
        var item = Assert.Single(layout.Items);

        var response = await client.PatchAsJsonAsync($"/rack-items/{item.Id}/position",
            new { topU = 1, basedOnLayoutVersion = layout.LayoutVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(layout.LayoutVersion + 1, (await response.Content.ReadFromJsonAsync<VersionDto>())!.LayoutVersion);
        Assert.Equal(1, Assert.Single((await Layout(client, rackId)).Items).TopU);
    }

    [Fact]
    public async Task A_move_onto_another_item_or_outside_the_cabinet_is_refused()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        var layout = await Added(client, rackId, TestSeed.PatchPanelId);
        var panel = layout.Items.Single(i => i.Label == "Patch panel 1");

        var onto = await client.PatchAsJsonAsync($"/rack-items/{panel.Id}/position",
            new { topU = 4, basedOnLayoutVersion = layout.LayoutVersion });
        var outside = await client.PatchAsJsonAsync($"/rack-items/{panel.Id}/position",
            new { topU = 5, basedOnLayoutVersion = layout.LayoutVersion });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, onto.StatusCode);
        Assert.Contains("U4", (await onto.Content.ReadFromJsonAsync<RefusalDto>())!.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outside.StatusCode);
        Assert.Equal(3, (await Layout(client, rackId)).Items.Single(i => i.Id == panel.Id).TopU);
    }

    [Fact]
    public async Task A_move_based_on_an_old_layout_is_a_conflict()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var first = await Added(client, rackId, TestSeed.Switch48Id);
        await Added(client, rackId, TestSeed.PatchPanelId);

        var response = await client.PatchAsJsonAsync($"/rack-items/{first.Items[0].Id}/position",
            new { topU = 1, basedOnLayoutVersion = first.LayoutVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(first.LayoutVersion + 1, (await response.Content.ReadFromJsonAsync<ConflictDto>())!.CurrentLayoutVersion);
    }

    [Fact]
    public async Task Removing_an_item_frees_its_u_and_drops_its_port_labels()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var layout = await Added(client, rackId, TestSeed.Switch48Id);
        var item = Assert.Single(layout.Items);
        await Port(client, item.Id, 1, "Kitchen AP", true);

        var response = await client.DeleteAsync($"/rack-items/{item.Id}?basedOnLayoutVersion={layout.LayoutVersion}");

        var after = (await response.Content.ReadFromJsonAsync<LayoutDto>())!;
        Assert.Empty(after.Items);
        Assert.Equal(4, after.FreeU);
        await using var db = factory.NewDbContext();
        Assert.False(db.RackPorts.Any(p => p.RackItemId == item.Id));
    }

    [Fact]
    public async Task A_port_label_and_poe_tick_are_saved_and_shown_in_the_layout()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);

        var response = await Port(client, item.Id, 1, "  Kitchen AP  ", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var port = Assert.Single(Assert.Single((await Layout(client, rackId)).Items).Ports);
        Assert.Equal(new PortDto(1, "Kitchen AP", true), port);
    }

    [Fact]
    public async Task A_label_of_only_spaces_clears_the_port()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);
        await Port(client, item.Id, 2, "Lounge TV", false);

        await Port(client, item.Id, 2, "   ", false);

        Assert.Empty(Assert.Single((await Layout(client, rackId)).Items).Ports);
    }

    [Fact]
    public async Task An_overlong_label_is_refused_with_the_limit()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);

        var response = await Port(client, item.Id, 3, new string('x', 61), false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("60", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["label"][0]);
        Assert.Equal(HttpStatusCode.OK, (await Port(client, item.Id, 3, new string('x', 60), false)).StatusCode);
    }

    [Fact]
    public async Task Poe_on_an_sfp_port_is_refused_but_a_patch_panel_port_takes_it()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        var layout = await Added(client, rackId, TestSeed.PatchPanelId);
        var sw = layout.Items.Single(i => i.Kind == "Switch");
        var panel = layout.Items.Single(i => i.Kind == "PatchPanel");

        var sfp = await Port(client, sw.Id, 49, "Uplink", true);
        var outlet = await Port(client, panel.Id, 5, "Office AP", true);

        Assert.Equal(HttpStatusCode.BadRequest, sfp.StatusCode);
        Assert.Contains("poe", (await sfp.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
        Assert.Equal(HttpStatusCode.OK, (await Port(client, sw.Id, 49, "Uplink", false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, outlet.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public async Task A_port_outside_the_item_is_not_found(int number)
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        var item = Assert.Single((await Added(client, rackId, TestSeed.Switch48Id)).Items);

        Assert.Equal(HttpStatusCode.NotFound, (await Port(client, item.Id, number, "x", false)).StatusCode);
    }

    [Fact]
    public async Task Changing_to_a_cabinet_too_short_is_refused_with_the_u_to_clear()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);
        await Added(client, rackId, TestSeed.Switch48Id);
        await Added(client, rackId, TestSeed.PatchPanelId);

        var response = await client.PatchAsJsonAsync($"/racks/{rackId}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet2UId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("U3–U4", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["rackTypeId"][0]);
    }

    /// Review focus: two adds racing for the same U. However they interleave,
    /// no request may fail with a 500, and no two items may share a U.
    [Fact]
    public async Task Concurrent_adds_never_share_a_u()
    {
        var (client, rackId) = await Rack(TestSeed.Cabinet4UId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Add(client, rackId, TestSeed.Switch48Id)));

        Assert.All(responses, r => Assert.Contains(r.StatusCode,
            new[] { HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict }));
        var layout = await Layout(client, rackId);
        Assert.Equal(layout.Items.Length, layout.Items.Select(i => i.TopU).Distinct().Count());
        Assert.Equal(responses.Count(r => r.StatusCode == HttpStatusCode.Created), layout.Items.Length);
    }
}
```

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackItemEndpointTests"`
Expected: FAIL — the item and port routes do not exist yet (the cabinet-change test already passes from Task 3).

- [ ] **Step 2: Write the item and port endpoints**

Create `src/PubInvest.HouseConfig.Api/Endpoints/RackItemEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackItemEndpoints
{
    /// The longest port label the schedule's column holds without wrapping badly.
    public const int MaxPortLabel = 60;

    /// Adds racing for the same rack are placed again against what the winner
    /// left, up to this many times, before the loser is told to reload.
    private const int MaxAddAttempts = 3;

    public static IEndpointRouteBuilder MapRackItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/racks/{id:guid}/items", async (
                Guid id,
                AddRackItemRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var typeRow = await db.RackDeviceTypes.SingleOrDefaultAsync(t => t.Id == request.RackDeviceTypeId, ct);
                if (typeRow is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["rackDeviceTypeId"] = ["Choose an item from the catalogue."]
                    });
                }

                var type = DomainMapper.ToDomain(typeRow);
                if (type.Kind == RackDeviceKind.Accessory)
                {
                    return Refuse($"{type.Description} is an accessory. It goes on the bill of materials with the item that needs it, not in the rack.");
                }

                if (!type.Active) return Refuse($"{type.Description} is no longer offered in the catalogue.");

                for (var attempt = 1; ; attempt++)
                {
                    var rack = await db.Racks.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
                    if (rack is null) return Results.NotFound();

                    var cabinet = await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct);
                    var spans = await RackViews.SpansAsync(db, rack, ct);

                    if (RackPlacement.NextTopU(cabinet.HeightU, type.HeightU, spans) is not { } topU)
                    {
                        return Refuse(
                            $"No room in this rack: {RackPlacement.FreeU(cabinet.HeightU, spans)}U free, " +
                            $"and {type.Description} needs {type.HeightU}U.");
                    }

                    rack.Items.Add(new RackItem
                    {
                        Id = Guid.NewGuid(),
                        RackId = rack.Id,
                        RackDeviceTypeId = type.Id,
                        TopU = topU,
                        Sequence = rack.Items.Count == 0 ? 1 : rack.Items.Max(i => i.Sequence) + 1,
                        Label = NextLabel(rack, type.Kind),
                    });
                    rack.LayoutVersion++;

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        return Results.Created($"/racks/{rack.Id}/layout", await RackViews.LayoutAsync(db, rack.Id, ct));
                    }
                    // Another add saved first: the rack's version moved on (a
                    // concurrency failure) or it took the same U (the unique
                    // index). Either way, place again against what is there now.
                    catch (DbUpdateException) when (attempt < MaxAddAttempts)
                    {
                        db.ChangeTracker.Clear();
                    }
                    catch (DbUpdateException)
                    {
                        db.ChangeTracker.Clear();
                        return Conflict(await db.Racks.Where(r => r.Id == id).Select(r => r.LayoutVersion).SingleAsync(ct));
                    }
                }
            })
            .WithTags("Racks");

        app.MapPatch("/rack-items/{id:guid}/position", async (
                Guid id,
                MoveRackItemRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                var rack = await db.Racks.Include(r => r.Items).SingleAsync(r => r.Id == item.RackId, ct);
                if (rack.LayoutVersion != request.BasedOnLayoutVersion) return Conflict(rack.LayoutVersion);

                var cabinet = await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct);
                var spans = await RackViews.SpansAsync(db, rack, ct);
                var height = spans.Single(s => s.ItemId == id).HeightU;
                var bottom = request.TopU - height + 1;

                if (request.TopU > cabinet.HeightU || bottom < 1)
                {
                    return Refuse($"U{request.TopU} is outside this {cabinet.HeightU}U cabinet.");
                }

                if (!RackPlacement.CanPlace(cabinet.HeightU, request.TopU, height, spans, id))
                {
                    return Refuse(height == 1 ? $"U{request.TopU} is taken." : $"U{bottom}–U{request.TopU} is not free.");
                }

                item.TopU = request.TopU;
                rack.LayoutVersion++;

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    return Conflict(await db.Racks.Where(r => r.Id == rack.Id).Select(r => r.LayoutVersion).SingleAsync(ct));
                }

                return Results.Ok(new { rack.LayoutVersion });
            })
            .WithTags("Racks");

        app.MapDelete("/rack-items/{id:guid}", async (
                Guid id,
                int basedOnLayoutVersion,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                var rack = await db.Racks.SingleAsync(r => r.Id == item.RackId, ct);
                if (rack.LayoutVersion != basedOnLayoutVersion) return Conflict(rack.LayoutVersion);

                // Its ports go with it, by the cascade on RackPorts.
                db.RackItems.Remove(item);
                rack.LayoutVersion++;

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    return Conflict(await db.Racks.Where(r => r.Id == rack.Id).Select(r => r.LayoutVersion).SingleAsync(ct));
                }

                return Results.Ok(await RackViews.LayoutAsync(db, rack.Id, ct));
            })
            .WithTags("Racks");

        // Port edits leave the layout version alone, as circuit names do: they
        // move nothing. They still make an issued PDF out of date, which the
        // revision's snapshot comparison sees.
        app.MapPut("/rack-items/{id:guid}/ports/{number:int}", async (
                Guid id,
                int number,
                SaveRackPortRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                var type = DomainMapper.ToDomain(await db.RackDeviceTypes.SingleAsync(t => t.Id == item.RackDeviceTypeId, ct));
                var group = type.GroupOf(number);
                if (group is null) return Results.NotFound();

                var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
                if (label is { Length: > MaxPortLabel })
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["label"] = [$"Keep a port label to {MaxPortLabel} characters; this one is {label.Length}."]
                    });
                }

                if (request.Poe && !group.PoeTick)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["poe"] = [$"Port {number} is an {group.Name} port, which cannot supply PoE."]
                    });
                }

                var port = await db.RackPorts.SingleOrDefaultAsync(p => p.RackItemId == id && p.Number == number, ct);

                if (label is null && !request.Poe)
                {
                    // An empty port is not stored at all.
                    if (port is not null) db.RackPorts.Remove(port);
                }
                else if (port is null)
                {
                    db.RackPorts.Add(new RackPort { RackItemId = id, Number = number, Label = label, Poe = request.Poe });
                }
                else
                {
                    port.Label = label;
                    port.Poe = request.Poe;
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(new RackPortResponse(number, label, request.Poe));
            })
            .WithTags("Racks");

        return app;
    }

    private static string NextLabel(Rack rack, RackDeviceKind kind) => kind switch
    {
        RackDeviceKind.Switch => $"Switch {++rack.SwitchesNamed}",
        RackDeviceKind.PatchPanel => $"Patch panel {++rack.PatchPanelsNamed}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only switches and patch panels are placed."),
    };

    private static IResult Refuse(string message) =>
        Results.Json(new RefusalResponse(message), statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult Conflict(int currentLayoutVersion) =>
        Results.Json(
            new ConflictResponse("This rack changed since you loaded it. Reload and try again.", currentLayoutVersion),
            statusCode: StatusCodes.Status409Conflict);
}
```

In `Program.cs`, add after `app.MapRackEndpoints();`:

```csharp
app.MapRackItemEndpoints();
```

- [ ] **Step 3: Run the tests to see them pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackItemEndpointTests"`
Expected: PASS (15 cases). Run `Concurrent_adds_never_share_a_u` five times (`for i in 1 2 3 4 5; do dotnet test ... --filter "FullyQualifiedName~Concurrent_adds"; done`) — it must pass every time. Then `dotnet test`: exit 0.

- [ ] **Step 4: Commit**

```bash
git add src/PubInvest.HouseConfig.Api tests/PubInvest.HouseConfig.Api.Tests/RackItemEndpointTests.cs
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Add, move and remove rack items, and label every port

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Rack revisions and bills of materials

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Racks/RackBom.cs`
- Create: `src/PubInvest.HouseConfig.Data/Entities/RackRevision.cs`
- Modify: `src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs`
- Create: `src/PubInvest.HouseConfig.Data/Migrations/<timestamp>_RackRevisions.cs` (generated)
- Create: `src/PubInvest.HouseConfig.Api/Racks/RackSnapshot.cs`
- Create: `src/PubInvest.HouseConfig.Api/Racks/RackRevisionService.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/RackRevisionEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/ExportEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/RackBomTests.cs`, `tests/PubInvest.HouseConfig.Api.Tests/RackRevisionEndpointTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `static BillOfMaterials RackBom.Build(RackType cabinet, IEnumerable<RackDeviceType> placed, IReadOnlyDictionary<Guid, RackDeviceType> catalogue)` — one `placed` entry per item
  - `RackRevision { Id, RackId, LayoutVersion, SnapshotJson, IssuedAt, IssuedBy }`; DbSet `RackRevisions`
  - `RackSnapshot(string ProjectName, string RackName, RackType Cabinet, string? FedFromSubmainName, IReadOnlyList<RackItemSnapshot> Items, IReadOnlyList<RackDeviceType> Catalogue, BillOfMaterials Bom)`, `RackItemSnapshot(Guid RackDeviceTypeId, string Label, int TopU, int HeightU, IReadOnlyList<RackPortSnapshot> Ports)`, `RackPortSnapshot(int Number, string? Label, bool Poe)`
  - `RackRevisionService`: `IssueAsync(Guid rackId, string issuedBy, ct)` → `(RackRevision?, string? Error)`, `CurrentCheckAsync(Guid rackId, ct)` → `Func<RackRevision, bool>`, `NumberOfAsync(RackRevision, ct)`, `FindAsync`, `LatestAsync`, `ListAsync` (newest first), `static RackSnapshot Read(RackRevision)`
  - Routes: `POST /racks/{id}/revisions`, `GET /racks/{id}/revisions` (each `{ id, rackId, number, layoutVersion, issuedAt, issuedBy, current }`), `GET /racks/{id}/bom`, `GET /racks/{id}/bom.csv`; house BOM now includes each rack's latest revision.
  - `ExportEndpoints.Describe(BillOfMaterials)` and `ExportEndpoints.Slug(string)` become `internal`.

- [ ] **Step 1: Write the failing Domain BOM tests**

Create `tests/PubInvest.HouseConfig.Domain.Tests/RackBomTests.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Domain.Tests;

public class RackBomTests
{
    private static readonly RackType Cabinet = new(Guid.NewGuid(), "Generic", "12U wall cabinet", "CAB-12", 12, "Wall");
    private static readonly RackDeviceType Kit = new(Guid.NewGuid(), "Ubiquiti", "Rack kit", "KIT", RackDeviceKind.Accessory, 0, [], null, null, true);
    private static readonly RackDeviceType Small = new(Guid.NewGuid(), "Ubiquiti", "16 PoE", "SW16", RackDeviceKind.Switch, 1,
        [new PortGroup("1G", 1, 16, true)], 180, Kit.Id, true);
    private static readonly RackDeviceType Panel = new(Guid.NewGuid(), "Generic", "Patch panel", "PP24", RackDeviceKind.PatchPanel, 1,
        [new PortGroup("RJ45", 1, 24, true)], null, null, true);

    private static Dictionary<Guid, RackDeviceType> Catalogue => new[] { Kit, Small, Panel }.ToDictionary(t => t.Id);

    [Fact]
    public void Lists_the_cabinet_once_and_each_item_by_count()
    {
        var bom = RackBom.Build(Cabinet, [Panel, Panel, Small], Catalogue);

        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "CAB-12").Quantity);
        Assert.Equal(2, bom.Lines.Single(l => l.PartNumber == "PP24").Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "SW16").Quantity);
    }

    [Fact]
    public void Adds_one_required_accessory_per_item_that_needs_it()
    {
        var bom = RackBom.Build(Cabinet, [Small, Small, Panel], Catalogue);

        Assert.Equal(2, bom.Lines.Single(l => l.PartNumber == "KIT").Quantity);
    }

    [Fact]
    public void Orders_lines_by_part_number_and_carries_no_prices()
    {
        var bom = RackBom.Build(Cabinet, [Small, Panel], Catalogue);

        Assert.Equal(new[] { "CAB-12", "KIT", "PP24", "SW16" }, bom.Lines.Select(l => l.PartNumber));
        Assert.All(bom.Lines, l => Assert.True(l.PanelMounted));
    }
}
```

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter "FullyQualifiedName~RackBomTests"` — Expected: build FAILS, `RackBom` does not exist.

- [ ] **Step 2: Write the rack BOM**

Create `src/PubInvest.HouseConfig.Domain/Racks/RackBom.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Bom;

namespace PubInvest.HouseConfig.Domain.Racks;

/// What to order for a rack: the cabinet, each item, and whatever an item
/// cannot be mounted without. A parts list, so no prices — as the panel's.
public static class RackBom
{
    /// `placed` has one entry per item in the rack. The catalogue must hold every
    /// accessory those items require; the revision service checks that first.
    public static BillOfMaterials Build(
        RackType cabinet,
        IEnumerable<RackDeviceType> placed,
        IReadOnlyDictionary<Guid, RackDeviceType> catalogue)
    {
        var items = placed.ToList();
        var lines = new List<BomLine> { new(cabinet.Id, cabinet.PartNumber, cabinet.Description, 1) };

        lines.AddRange(items
            .GroupBy(t => t.Id)
            .Select(g => new BomLine(g.Key, g.First().PartNumber, g.First().Description, g.Count())));

        lines.AddRange(items
            .Where(t => t.RequiredAccessoryId is not null)
            .GroupBy(t => t.RequiredAccessoryId!.Value)
            .Select(g =>
            {
                var accessory = catalogue[g.Key];
                return new BomLine(accessory.Id, accessory.PartNumber, accessory.Description, g.Count());
            }));

        return new BillOfMaterials(lines.OrderBy(l => l.PartNumber, StringComparer.Ordinal).ToList());
    }
}
```

Run the Step 1 tests again. Expected: PASS.

- [ ] **Step 3: Add the revision table**

Create `src/PubInvest.HouseConfig.Data/Entities/RackRevision.cs`:

```csharp
namespace PubInvest.HouseConfig.Data.Entities;

/// An issued rack: everything needed to redraw it, schedule its ports and list
/// its parts, embedded rather than referenced, as panel revisions are.
public class RackRevision
{
    public Guid Id { get; set; }
    public Guid RackId { get; set; }
    public int LayoutVersion { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public string IssuedBy { get; set; } = "";
}
```

In `HouseConfigDbContext.cs` add `public DbSet<RackRevision> RackRevisions => Set<RackRevision>();` after `RackPorts`, and in `OnModelCreating`:

```csharp
        b.Entity<Rack>().HasMany<RackRevision>().WithOne()
            .HasForeignKey(r => r.RackId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RackRevision>().HasIndex(r => r.RackId);
```

Run: `dotnet ef migrations add RackRevisions --project src/PubInvest.HouseConfig.Data` — Expected: a migration creating only `RackRevisions`.

- [ ] **Step 4: Write the snapshot and the revision service**

Create `src/PubInvest.HouseConfig.Api/Racks/RackSnapshot.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// Everything an issued rack prints, embedded: an admin editing the catalogue
/// later must not be able to change what an issued drawing meant.
public sealed record RackSnapshot(
    string ProjectName,
    string RackName,
    RackType Cabinet,
    string? FedFromSubmainName,
    /// Top U first.
    IReadOnlyList<RackItemSnapshot> Items,
    /// The item types placed and the accessories they need, as they were.
    IReadOnlyList<RackDeviceType> Catalogue,
    BillOfMaterials Bom);

public sealed record RackItemSnapshot(
    Guid RackDeviceTypeId,
    string Label,
    int TopU,
    int HeightU,
    IReadOnlyList<RackPortSnapshot> Ports);

public sealed record RackPortSnapshot(int Number, string? Label, bool Poe);
```

Create `src/PubInvest.HouseConfig.Api/Racks/RackRevisionService.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// Issues rack revisions and judges whether one is still current, the same way
/// RevisionService does for panels.
public sealed class RackRevisionService(HouseConfigDbContext db)
{
    public async Task<(RackRevision? Revision, string? Error)> IssueAsync(
        Guid rackId,
        string issuedBy,
        CancellationToken ct)
    {
        var (snapshot, rack, error) = await BuildAsync(rackId, ct);
        if (snapshot is null || rack is null) return (null, error);

        var revision = new RackRevision
        {
            Id = Guid.NewGuid(),
            RackId = rackId,
            LayoutVersion = rack.LayoutVersion,
            SnapshotJson = JsonSerializer.Serialize(snapshot, DomainMapper.Json),
            IssuedAt = DateTimeOffset.UtcNow,
            IssuedBy = issuedBy,
        };

        db.RackRevisions.Add(revision);
        await db.SaveChangesAsync(ct);
        return (revision, null);
    }

    /// Current when the revision's snapshot says exactly what issuing now would:
    /// a label, a tick, a move, a cabinet or a feed change all show, without the
    /// client knowing which edits matter. A rack that can no longer be issued
    /// (emptied, say) is current to no revision.
    public async Task<Func<RackRevision, bool>> CurrentCheckAsync(Guid rackId, CancellationToken ct)
    {
        var (snapshot, _, _) = await BuildAsync(rackId, ct);
        var now = snapshot is null ? null : JsonSerializer.Serialize(snapshot, DomainMapper.Json);
        return revision => now is not null && JsonSerializer.Serialize(Read(revision), DomainMapper.Json) == now;
    }

    /// Counted from one in issue order — the "revision 3" a drawing is called by.
    public async Task<int> NumberOfAsync(RackRevision revision, CancellationToken ct)
        => (await Ordered(revision.RackId).Select(r => r.Id).ToListAsync(ct)).IndexOf(revision.Id) + 1;

    public async Task<RackRevision?> FindAsync(Guid revisionId, CancellationToken ct)
        => await db.RackRevisions.SingleOrDefaultAsync(r => r.Id == revisionId, ct);

    public async Task<RackRevision?> LatestAsync(Guid rackId, CancellationToken ct)
        => await Ordered(rackId).LastOrDefaultAsync(ct);

    /// Newest first.
    public async Task<List<RackRevision>> ListAsync(Guid rackId, CancellationToken ct)
        => await Ordered(rackId).Reverse().ToListAsync(ct);

    public static RackSnapshot Read(RackRevision revision)
        => JsonSerializer.Deserialize<RackSnapshot>(revision.SnapshotJson, DomainMapper.Json)
           ?? throw new InvalidOperationException($"Rack revision {revision.Id} has an unreadable snapshot.");

    /// Oldest first; the id breaks a tie between two issued in the same tick.
    private IOrderedQueryable<RackRevision> Ordered(Guid rackId)
        => db.RackRevisions.Where(r => r.RackId == rackId).OrderBy(r => r.IssuedAt).ThenBy(r => r.Id);

    private async Task<(RackSnapshot? Snapshot, Rack? Rack, string? Error)> BuildAsync(Guid rackId, CancellationToken ct)
    {
        var rack = await db.Racks
            .AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Ports)
            .SingleOrDefaultAsync(r => r.Id == rackId, ct);

        if (rack is null) return (null, null, null);
        if (rack.Items.Count == 0)
        {
            return (null, null, "This rack is empty. Add something to it before issuing a revision.");
        }

        var project = await db.Projects.SingleAsync(p => p.Id == rack.ProjectId, ct);
        var cabinet = DomainMapper.ToDomain(await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct));
        var feed = rack.FedFromSubmainId is { } submainId
            ? await db.Submains.Where(s => s.Id == submainId).Select(s => s.Name).SingleOrDefaultAsync(ct)
            : null;

        // The whole rack catalogue, inactive rows included: an item retired
        // after it was placed is still in the rack and still has to be drawn,
        // scheduled and ordered.
        var catalogue = (await db.RackDeviceTypes.ToListAsync(ct)).Select(DomainMapper.ToDomain).ToDictionary(t => t.Id);
        var placed = rack.Items.Select(i => catalogue[i.RackDeviceTypeId]).ToList();

        var orphan = placed.FirstOrDefault(t => t.RequiredAccessoryId is { } a && !catalogue.ContainsKey(a));
        if (orphan is not null)
        {
            return (null, null, $"{orphan.Description} needs an accessory that is no longer in the catalogue.");
        }

        var used = placed
            .Concat(placed.Where(t => t.RequiredAccessoryId is not null).Select(t => catalogue[t.RequiredAccessoryId!.Value]))
            .DistinctBy(t => t.Id)
            .OrderBy(t => t.PartNumber, StringComparer.Ordinal)
            .ToList();

        var snapshot = new RackSnapshot(
            project.Name,
            rack.Name,
            cabinet,
            feed,
            rack.Items
                .OrderByDescending(i => i.TopU)
                .Select(i => new RackItemSnapshot(
                    i.RackDeviceTypeId,
                    i.Label,
                    i.TopU,
                    catalogue[i.RackDeviceTypeId].HeightU,
                    i.Ports.OrderBy(p => p.Number).Select(p => new RackPortSnapshot(p.Number, p.Label, p.Poe)).ToList()))
                .ToList(),
            used,
            RackBom.Build(cabinet, placed, catalogue));

        return (snapshot, rack, null);
    }
}
```

- [ ] **Step 5: Write the failing API tests**

Create `tests/PubInvest.HouseConfig.Api.Tests/RackRevisionEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackRevisionEndpointTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record ItemDto(Guid Id, string Label, int TopU);
    private record LayoutDto(int LayoutVersion, ItemDto[] Items);
    private record RevisionDto(Guid Id, Guid RackId, int Number, bool Current);
    private record BomLineDto(string PartNumber, int Quantity);
    private record BomDto(BomLineDto[] Lines);
    private record ProblemDto(Dictionary<string, string[]> Errors);

    private async Task<(HttpClient Client, Guid ProjectId, Guid RackId)> Rack()
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Rack revs {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId })).Content.ReadFromJsonAsync<IdDto>();
        return (client, project.Id, rack!.Id);
    }

    private static async Task<LayoutDto> Add(HttpClient client, Guid rackId, Guid typeId) =>
        (await (await client.PostAsJsonAsync($"/racks/{rackId}/items", new { rackDeviceTypeId = typeId }))
            .Content.ReadFromJsonAsync<LayoutDto>())!;

    private static async Task<RevisionDto> Issue(HttpClient client, Guid rackId) =>
        (await (await client.PostAsJsonAsync($"/racks/{rackId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>())!;

    private static async Task<bool> LatestIsCurrent(HttpClient client, Guid rackId) =>
        (await client.GetFromJsonAsync<RevisionDto[]>($"/racks/{rackId}/revisions"))![0].Current;

    [Fact]
    public async Task Revisions_are_numbered_from_one_and_the_newest_is_current()
    {
        var (client, _, rackId) = await Rack();
        await Add(client, rackId, TestSeed.Switch48Id);

        var first = await Issue(client, rackId);
        var second = await Issue(client, rackId);

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        var listed = await client.GetFromJsonAsync<RevisionDto[]>($"/racks/{rackId}/revisions");
        Assert.Equal(new[] { 2, 1 }, listed!.Select(r => r.Number));
        Assert.True(listed[0].Current);
    }

    [Fact]
    public async Task An_empty_rack_cannot_be_issued()
    {
        var (client, _, rackId) = await Rack();

        var response = await client.PostAsJsonAsync($"/racks/{rackId}/revisions", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("empty", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["revision"][0]);
    }

    [Fact]
    public async Task A_label_edit_makes_the_pdf_out_of_date_and_putting_it_back_makes_it_current()
    {
        var (client, _, rackId) = await Rack();
        var item = (await Add(client, rackId, TestSeed.Switch48Id)).Items[0];
        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = "Kitchen AP", poe = true });
        await Issue(client, rackId);

        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = "Kitchen AP 2", poe = true });
        Assert.False(await LatestIsCurrent(client, rackId));

        await client.PutAsJsonAsync($"/rack-items/{item.Id}/ports/1", new { label = "Kitchen AP", poe = true });
        Assert.True(await LatestIsCurrent(client, rackId));
    }

    [Fact]
    public async Task A_move_or_a_feed_change_makes_the_pdf_out_of_date()
    {
        var (client, projectId, rackId) = await Rack();
        var layout = await Add(client, rackId, TestSeed.Switch48Id);
        await Issue(client, rackId);

        await client.PatchAsJsonAsync($"/rack-items/{layout.Items[0].Id}/position",
            new { topU = 1, basedOnLayoutVersion = layout.LayoutVersion });
        Assert.False(await LatestIsCurrent(client, rackId));

        await Issue(client, rackId);
        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Ground floor", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<IdDto>();
        await client.PatchAsJsonAsync($"/racks/{rackId}",
            new { name = "Comms", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = submain!.Id });
        Assert.False(await LatestIsCurrent(client, rackId));
    }

    [Fact]
    public async Task The_rack_bom_lists_the_cabinet_the_items_and_the_kit_a_half_width_switch_needs()
    {
        var (client, _, rackId) = await Rack();
        await Add(client, rackId, TestSeed.Switch16Id);
        await Add(client, rackId, TestSeed.PatchPanelId);
        await Add(client, rackId, TestSeed.PatchPanelId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/racks/{rackId}/bom")).StatusCode);
        await Issue(client, rackId);
        var bom = await client.GetFromJsonAsync<BomDto>($"/racks/{rackId}/bom");

        Assert.Equal(1, bom!.Lines.Single(l => l.PartNumber == "T-CAB4").Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "T-SW16").Quantity);
        Assert.Equal(1, bom.Lines.Single(l => l.PartNumber == "T-KIT").Quantity);
        Assert.Equal(2, bom.Lines.Single(l => l.PartNumber == "T-PP24").Quantity);

        var csv = await client.GetStringAsync($"/racks/{rackId}/bom.csv");
        Assert.Contains("T-KIT", csv);
    }

    [Fact]
    public async Task The_house_bom_adds_rack_lines_to_panel_lines()
    {
        var (client, projectId, rackId) = await Rack();
        await Add(client, rackId, TestSeed.Switch48Id);
        await Issue(client, rackId);
        var submain = await (await client.PostAsJsonAsync($"/projects/{projectId}/submains", new
        {
            name = "Ground floor", enclosureTypeId = TestSeed.EnclosureId, ruleSetId = TestSeed.RuleSetId,
            circuits = new object[] { new { type = "DimmedLighting", name = "Kitchen", sequence = 1 } },
            extraFixtures = Array.Empty<object>(), generate = true,
        })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/submains/{submain!.Id}/revisions", new { });

        var bom = await client.GetFromJsonAsync<BomDto>($"/projects/{projectId}/bom");

        Assert.Contains(bom!.Lines, l => l.PartNumber == "T-SW48");
        Assert.Contains(bom.Lines, l => l.PartNumber == "T-CAB4");
        Assert.Contains(bom.Lines, l => l.PartNumber == "T-DIM2");
    }

    /// Review focus: an item retired after it was placed is still in the rack.
    [Fact]
    public async Task A_deactivated_item_still_appears_in_a_new_revision()
    {
        var (client, _, rackId) = await Rack();
        var item = (await Add(client, rackId, TestSeed.PatchPanelId)).Items[0];
        await using (var db = factory.NewDbContext())
        {
            var row = db.RackDeviceTypes.Single(t => t.Id == TestSeed.PatchPanelId);
            row.Active = false;
            await db.SaveChangesAsync();
        }

        try
        {
            await Issue(client, rackId);
            var bom = await client.GetFromJsonAsync<BomDto>($"/racks/{rackId}/bom");
            Assert.Contains(bom!.Lines, l => l.PartNumber == "T-PP24");
            Assert.Contains((await client.GetFromJsonAsync<LayoutDto>($"/racks/{rackId}/layout"))!.Items, i => i.Id == item.Id);
        }
        finally
        {
            await using var db = factory.NewDbContext();
            db.RackDeviceTypes.Single(t => t.Id == TestSeed.PatchPanelId).Active = true;
            await db.SaveChangesAsync();
        }
    }
}
```

Run (env vars set): `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackRevisionEndpointTests"` — Expected: FAIL, routes missing.

- [ ] **Step 6: Write the revision and BOM endpoints, and add racks to the house BOM**

In `src/PubInvest.HouseConfig.Api/Endpoints/ExportEndpoints.cs`:
- change `private static object Describe(` to `internal static object Describe(` and `private static string Slug(` to `internal static string Slug(`;
- add `using PubInvest.HouseConfig.Api.Racks;`;
- add a `RackRevisionService racks,` parameter after `RevisionService revisions,` in both `/projects/{id:guid}/bom` and `/projects/{id:guid}/bom.csv` lambdas, and pass it: `await HouseBom(id, db, revisions, racks, ct)`;
- replace `HouseBom` with:

```csharp
    /// A house BOM sums each submain's and each rack's latest *issued*
    /// revision, so it lists what was issued rather than whatever is being
    /// edited right now.
    private static async Task<(BillOfMaterials? Bom, string? Error)> HouseBom(
        Guid projectId,
        HouseConfigDbContext db,
        RevisionService revisions,
        RackRevisionService racks,
        CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return (null, "No such house.");

        var lines = new List<BomLine>();

        foreach (var submainId in await db.Submains.Where(s => s.ProjectId == projectId).Select(s => s.Id).ToListAsync(ct))
        {
            var revision = await revisions.LatestAsync(submainId, ct);
            if (revision is not null) lines.AddRange(RevisionService.Read(revision).Bom.Lines);
        }

        foreach (var rackId in await db.Racks.Where(r => r.ProjectId == projectId).Select(r => r.Id).ToListAsync(ct))
        {
            var revision = await racks.LatestAsync(rackId, ct);
            if (revision is not null) lines.AddRange(RackRevisionService.Read(revision).Bom.Lines);
        }

        var merged = lines
            .GroupBy(l => l.CatalogueId)
            .Select(g => new BomLine(
                g.Key,
                g.First().PartNumber,
                g.First().Description,
                g.Sum(l => l.Quantity),
                g.First().PanelMounted))
            .OrderBy(l => l.PartNumber, StringComparer.Ordinal)
            .ToList();

        return (new BillOfMaterials(merged), null);
    }
```

Create `src/PubInvest.HouseConfig.Api/Endpoints/RackRevisionEndpoints.cs`:

```csharp
using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Api.Racks;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackRevisionEndpoints
{
    public static IEndpointRouteBuilder MapRackRevisionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/racks/{id:guid}/revisions", async (
                Guid id,
                RackRevisionService revisions,
                HttpContext http,
                CancellationToken ct) =>
            {
                var (revision, error) = await revisions.IssueAsync(id, http.User.Identity?.Name ?? "anonymous", ct);

                if (error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["revision"] = [error] });
                }

                if (revision is null) return Results.NotFound();

                // Just issued from the rack as it is, so current by definition.
                return Results.Created($"/rack-revisions/{revision.Id}", new
                {
                    revision.Id,
                    revision.RackId,
                    Number = await revisions.NumberOfAsync(revision, ct),
                    revision.LayoutVersion,
                    revision.IssuedAt,
                    revision.IssuedBy,
                    Current = true,
                });
            })
            .WithTags("Rack revisions");

        app.MapGet("/racks/{id:guid}/revisions", async (
                Guid id,
                RackRevisionService revisions,
                CancellationToken ct) =>
            {
                var isCurrent = await revisions.CurrentCheckAsync(id, ct);
                var list = await revisions.ListAsync(id, ct);
                return list.Select((r, i) => new
                {
                    r.Id, r.RackId, Number = list.Count - i, r.LayoutVersion, r.IssuedAt, r.IssuedBy, Current = isCurrent(r),
                });
            })
            .WithTags("Rack revisions");

        app.MapGet("/racks/{id:guid}/bom", async (Guid id, RackRevisionService revisions, CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);
                return revision is null
                    ? Results.NotFound()
                    : Results.Ok(ExportEndpoints.Describe(RackRevisionService.Read(revision).Bom));
            })
            .WithTags("Rack revisions");

        app.MapGet("/racks/{id:guid}/bom.csv", async (Guid id, RackRevisionService revisions, CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var snapshot = RackRevisionService.Read(revision);
                var csv = BomCsv.Write(snapshot.Bom, $"{snapshot.ProjectName} — {snapshot.RackName}");
                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                    $"{ExportEndpoints.Slug(snapshot.RackName)}-bom.csv");
            })
            .WithTags("Rack revisions");

        return app;
    }
}
```

In `Program.cs`, register the service after the `RevisionService` line:

```csharp
builder.Services.AddScoped<PubInvest.HouseConfig.Api.Racks.RackRevisionService>();
```

and map after `app.MapRackItemEndpoints();`:

```csharp
app.MapRackRevisionEndpoints();
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackRevisionEndpointTests|FullyQualifiedName~BomExportTests"` then `dotnet test`.
Expected: all PASS, exit 0 — the existing house BOM tests still pass with no racks in their houses.

- [ ] **Step 8: Commit**

```bash
git add src tests
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Issue rack revisions, and put racks on the bill of materials

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The rack PDF — drawing, port schedule and parts list

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Rendering/RackSvgRenderer.cs`
- Create: `src/PubInvest.HouseConfig.Api/Documents/RackDocument.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/RackRevisionEndpoints.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/RackDocumentTests.cs`

**Interfaces:**
- Consumes: Task 5 `RackSnapshot`, `RackRevisionService`.
- Produces: `static string RackSvgRenderer.Render(RackSnapshot)`; `RackDocument(RackSnapshot snapshot, int number, DateTimeOffset issuedAt, string issuedBy) : IDocument`; route `GET /rack-revisions/{id}/export/pdf` → `application/pdf`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PubInvest.HouseConfig.Api.Tests/RackDocumentTests.cs`:

```csharp
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

    /// A rack fed from "Ground floor" holding a switch with two labelled ports.
    private async Task<(HttpClient Client, Guid RevisionId)> IssuedRack(string firstPortLabel)
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
            new { name = "Comms cupboard", rackTypeId = TestSeed.Cabinet4UId, fedFromSubmainId = submain!.Id }))
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
        Assert.Contains("—", text);
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
```

Run (env vars set): `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackDocumentTests"` — Expected: FAIL, the PDF route is 404.

- [ ] **Step 2: Write the SVG renderer**

Create `src/PubInvest.HouseConfig.Api/Rendering/RackSvgRenderer.cs`:

```csharp
using System.Globalization;
using System.Security;
using System.Text;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Rendering;

/// The rack drawn to scale for the PDF, in millimetres: a 19-inch panel is
/// 482.6 mm wide and a rack unit 44.45 mm tall, so the drawing has the
/// proportions of the real cabinet. Like PanelSvgRenderer, it draws what the
/// snapshot says and decides nothing.
public static class RackSvgRenderer
{
    private const double UnitMm = 44.45;
    private const double PanelMm = 482.6;
    private const double GutterMm = 32;
    private const double PortMm = 7;
    private const double PortGapMm = 1.6;

    public static string Render(RackSnapshot snapshot)
    {
        var heightU = snapshot.Cabinet.HeightU;
        var types = snapshot.Catalogue.ToDictionary(t => t.Id);
        var svg = new StringBuilder();

        // InvariantCulture throughout: a comma decimal separator would make invalid SVG.
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(GutterMm + PanelMm)} {N(heightU * UnitMm)}\">");

        for (var u = heightU; u >= 1; u--)
        {
            var y = (heightU - u) * UnitMm;
            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(GutterMm)}\" y=\"{N(y)}\" width=\"{N(PanelMm)}\" height=\"{N(UnitMm)}\" fill=\"#f1f5f9\" stroke=\"#cbd5e1\" stroke-width=\"0.4\"/>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(GutterMm - 4)}\" y=\"{N(y + UnitMm / 2)}\" text-anchor=\"end\" dominant-baseline=\"middle\" font-size=\"9\" fill=\"#475569\">U{u}</text>");
        }

        foreach (var item in snapshot.Items)
        {
            var type = types[item.RackDeviceTypeId];
            var x = GutterMm + 2;
            var y = (heightU - item.TopU) * UnitMm + 2;
            var width = PanelMm - 4;
            var height = item.HeightU * UnitMm - 4;

            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\" rx=\"2\" fill=\"#ffffff\" stroke=\"#0f172a\" stroke-width=\"0.8\"/>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + 6)}\" y=\"{N(y + 12)}\" font-size=\"9\" font-weight=\"bold\" fill=\"#0f172a\">{SecurityElement.Escape(item.Label)}</text>");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{N(x + 6)}\" y=\"{N(y + 24)}\" font-size=\"7\" fill=\"#475569\">{SecurityElement.Escape(type.Model)}</text>");

            Ports(svg, item, type, x + 120, y + 6);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// Laid out as on the front panel: a switch's copper ports odd on the top
    /// row and even below, its uplinks to the right; a patch panel in one row.
    /// A labelled port is filled; a PoE port carries an orange dot.
    private static void Ports(StringBuilder svg, RackItemSnapshot item, RackDeviceType type, double left, double top)
    {
        var stored = item.Ports.ToDictionary(p => p.Number);
        var copper = type.PortGroups.Where(g => g.PoeTick).SelectMany(Numbers).ToList();
        var uplinks = type.PortGroups.Where(g => !g.PoeTick).SelectMany(Numbers).ToList();

        var rows = type.Kind == RackDeviceKind.PatchPanel
            ? new[] { copper, new List<int>() }
            : new[] { copper.Where(n => n % 2 == 1).ToList(), copper.Where(n => n % 2 == 0).ToList() };

        for (var r = 0; r < rows.Length; r++)
        {
            for (var i = 0; i < rows[r].Count; i++)
            {
                Port(svg, stored, rows[r][i], left + i * (PortMm + PortGapMm), top + r * (PortMm + PortGapMm));
            }
        }

        var uplinkLeft = left + rows.Max(row => row.Count) * (PortMm + PortGapMm) + 8;
        for (var i = 0; i < uplinks.Count; i++)
        {
            Port(svg, stored, uplinks[i], uplinkLeft + i / 2 * (PortMm + PortGapMm), top + i % 2 * (PortMm + PortGapMm));
        }
    }

    private static void Port(StringBuilder svg, IReadOnlyDictionary<int, RackPortSnapshot> stored, int number, double x, double y)
    {
        stored.TryGetValue(number, out var port);
        var fill = port?.Label is null ? "#ffffff" : "#334155";
        svg.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(PortMm)}\" height=\"{N(PortMm)}\" fill=\"{fill}\" stroke=\"#334155\" stroke-width=\"0.4\"/>");
        if (port?.Poe == true)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<circle cx=\"{N(x + PortMm - 1.5)}\" cy=\"{N(y + 1.5)}\" r=\"1.3\" fill=\"#ea580c\"/>");
        }
    }

    private static IEnumerable<int> Numbers(PortGroup group) => Enumerable.Range(group.From, group.Count);

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 3: Write the document and the export route**

Create `src/PubInvest.HouseConfig.Api/Documents/RackDocument.cs`:

```csharp
using System.Globalization;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Api.Rendering;
using PubInvest.HouseConfig.Domain.Racks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PubInvest.HouseConfig.Api.Documents;

/// Page 1: the rack drawing. Then the port schedule, one table per item with
/// every port in it, and last the parts list. Everything comes from an issued
/// revision, never from live tables.
public sealed class RackDocument(
    RackSnapshot snapshot,
    int number,
    DateTimeOffset issuedAt,
    string issuedBy) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        // Portrait: a rack is tall, not wide.
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(Header);
            page.Content().PaddingTop(6, Unit.Millimetre).Element(Drawing);
            page.Footer().Element(Footer);
        });

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(Header);
            page.Content().PaddingTop(6, Unit.Millimetre).Column(column =>
            {
                column.Item().Element(Schedule);
                column.Item().PaddingTop(8, Unit.Millimetre).Element(Parts);
            });
            page.Footer().Element(Footer);
        });
    }

    private void Header(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.Span($"{snapshot.ProjectName} — ").SemiBold().FontSize(13);
                text.Span(snapshot.RackName).FontSize(13);
            });

            column.Item().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontColor(Colors.Grey.Darken1));
                text.Span($"{snapshot.Cabinet.Description}  ·  {snapshot.Cabinet.HeightU}U");
                text.Span(snapshot.FedFromSubmainName is null
                    ? "  ·  Not fed from a submain"
                    : $"  ·  Fed from {snapshot.FedFromSubmainName}");
                text.Span($"  ·  Revision {number}");
            });
        });

    private void Drawing(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Svg(RackSvgRenderer.Render(snapshot)).FitArea();
            column.Item().PaddingTop(4, Unit.Millimetre)
                .Text("Filled port: labelled.  Orange dot: PoE.  U1 is the bottom of the cabinet.")
                .FontColor(Colors.Grey.Darken1);
        });

    private void Schedule(IContainer container)
    {
        var types = snapshot.Catalogue.ToDictionary(t => t.Id);

        container.Column(column =>
        {
            column.Item().PaddingBottom(3, Unit.Millimetre).Text("Port schedule").SemiBold().FontSize(12);

            foreach (var item in snapshot.Items)
            {
                var type = types[item.RackDeviceTypeId];
                var stored = item.Ports.ToDictionary(p => p.Number);

                column.Item().PaddingTop(4, Unit.Millimetre)
                    .Text($"{item.Label} — {type.Description} — U{item.TopU}").SemiBold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(14, Unit.Millimetre);
                        c.ConstantColumn(18, Unit.Millimetre);
                        c.RelativeColumn();
                        c.ConstantColumn(14, Unit.Millimetre);
                    });

                    table.Header(h =>
                    {
                        foreach (var heading in new[] { "Port", "Type", "Label", "PoE" })
                        {
                            h.Cell().BorderBottom(0.5f).PaddingVertical(2).Text(heading).SemiBold();
                        }
                    });

                    foreach (var group in type.PortGroups)
                    {
                        for (var n = group.From; n <= group.To; n++)
                        {
                            stored.TryGetValue(n, out var port);
                            table.Cell().PaddingVertical(1).Text(n.ToString(CultureInfo.InvariantCulture));
                            table.Cell().PaddingVertical(1).Text(group.Name);
                            table.Cell().PaddingVertical(1).Text(port?.Label ?? "—");
                            table.Cell().PaddingVertical(1).Text(port?.Poe == true ? "PoE" : "");
                        }
                    }
                });
            }
        });
    }

    private void Parts(IContainer container) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(3, Unit.Millimetre).Text("Parts list").SemiBold().FontSize(12);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(55, Unit.Millimetre);
                    c.RelativeColumn();
                    c.ConstantColumn(14, Unit.Millimetre);
                });

                table.Header(h =>
                {
                    h.Cell().BorderBottom(0.5f).PaddingVertical(2).Text("Part").SemiBold();
                    h.Cell().BorderBottom(0.5f).PaddingVertical(2).Text("Description").SemiBold();
                    h.Cell().BorderBottom(0.5f).PaddingVertical(2).AlignRight().Text("Qty").SemiBold();
                });

                foreach (var line in snapshot.Bom.Lines)
                {
                    table.Cell().PaddingVertical(1).Text(line.PartNumber);
                    table.Cell().PaddingVertical(1).Text(line.Description);
                    table.Cell().PaddingVertical(1).AlignRight().Text(line.Quantity.ToString(CultureInfo.InvariantCulture));
                }
            });
        });

    private void Footer(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem().Text(
                $"Issued {issuedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)} by {issuedBy}")
                .FontSize(7).FontColor(Colors.Grey.Darken1);

            row.AutoItem().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Darken1));
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
}
```

In `RackRevisionEndpoints.cs`, add `using QuestPDF.Fluent;` and, before `return app;`:

```csharp
        app.MapGet("/rack-revisions/{id:guid}/export/pdf", async (
                Guid id,
                RackRevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.FindAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var snapshot = RackRevisionService.Read(revision);
                var bytes = new RackDocument(
                        snapshot, await revisions.NumberOfAsync(revision, ct), revision.IssuedAt, revision.IssuedBy)
                    .GeneratePdf();

                return Results.File(bytes, "application/pdf", $"{ExportEndpoints.Slug(snapshot.RackName)}-rack.pdf");
            })
            .WithTags("Rack revisions");
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackDocumentTests"` — Expected: PASS. If `A_label_with_accents_and_symbols_prints_as_typed` fails because a glyph is missing from QuestPDF's default font, set a font with full Latin coverage on both pages (`page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Lato))`, the family QuestPDF bundles) rather than weakening the test. Then `dotnet test`: exit 0.

- [ ] **Step 5: Restart the dev API and commit**

Restart the API on :5020 (README → Running it for development) so the running app has the rack endpoints, then:

```bash
git add src tests
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Print a rack PDF: the rack to scale, every port, and the parts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 7: Rack catalogue in the Catalogue screen

Part numbers seeded as ASSUMED must be correctable without a redeploy, as DIN parts are. The Catalogue screen edits names, part numbers, cabinet heights and whether an item is offered; port groups are corrected through the same API by whoever maintains the seed, because a typo in a group would renumber every port on a schedule.

**Files:**
- Modify: `src/PubInvest.HouseConfig.Api/Contracts/CatalogueContracts.cs`
- Create: `src/PubInvest.HouseConfig.Api/Racks/RackCatalogueRules.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueAdminEndpoints.cs`
- Modify: `ui/src/api/types.ts` (rack catalogue types only; the rest come in Task 8)
- Create: `ui/src/routes/RackCatalogueSections.tsx`
- Modify: `ui/src/routes/CataloguePage.tsx`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/RackCatalogueAdminTests.cs`, `ui/tests/RackCatalogueSections.test.tsx`

**Interfaces:**
- Consumes: Task 1 rows and Domain types; Task 3 `Rack`, `RackItem`.
- Produces: `SaveRackTypeRequest(string Manufacturer, string Model, string PartNumber, int HeightU, string Mounting)`, `SaveRackDeviceTypeRequest(string Manufacturer, string Model, string PartNumber, string Kind, int HeightU, IReadOnlyList<PortGroup> PortGroups, int? PoeBudgetW, Guid? RequiredAccessoryId, bool Active)`; `PATCH /catalogue/rack-types/{id}`, `PATCH /catalogue/rack-device-types/{id}` (admin policy when auth is on); TS `RackDeviceKind`, `PortGroup`, `RackTypeResponse`, `RackDeviceTypeResponse`.

- [ ] **Step 1: Write the failing API tests**

Create `tests/PubInvest.HouseConfig.Api.Tests/RackCatalogueAdminTests.cs` (each test makes its own catalogue rows, so the shared test seed is never changed under another test):

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class RackCatalogueAdminTests(HouseConfigApiFactory factory)
{
    private record IdDto(Guid Id);
    private record ProblemDto(Dictionary<string, string[]> Errors);
    private record RackDeviceTypeDto(Guid Id, string PartNumber, bool Active);

    private static readonly PortGroup[] Ports = [new("1G", 1, 8, true), new("SFP+", 9, 10, false)];

    private async Task<(HttpClient Client, Guid SwitchId, Guid CabinetId)> OwnRows()
    {
        await using var db = factory.NewDbContext();
        await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);

        var sw = new RackDeviceTypeRow
        {
            Id = Guid.NewGuid(), Manufacturer = "Ubiquiti", Model = "Admin switch", PartNumber = $"ADM-{Guid.NewGuid():N}",
            Kind = "Switch", HeightU = 1, PortGroupsJson = JsonSerializer.Serialize(Ports, DomainMapper.Json), Active = true,
        };
        var cabinet = new RackTypeRow
        {
            Id = Guid.NewGuid(), Manufacturer = "Generic", Model = "Admin cabinet", PartNumber = $"CAB-{Guid.NewGuid():N}",
            HeightU = 6, Mounting = "Wall",
        };
        db.RackDeviceTypes.Add(sw);
        db.RackTypes.Add(cabinet);
        await db.SaveChangesAsync();
        return (factory.CreateClient(), sw.Id, cabinet.Id);
    }

    private static object SwitchBody(string partNumber, PortGroup[]? ports = null, int heightU = 1, bool active = true) => new
    {
        manufacturer = "Ubiquiti", model = "Admin switch", partNumber, kind = "Switch", heightU,
        portGroups = ports ?? Ports, poeBudgetW = 60, requiredAccessoryId = (Guid?)null, active,
    };

    [Fact]
    public async Task A_confirmed_part_number_is_saved()
    {
        var (client, switchId, _) = await OwnRows();
        var confirmed = $"USW-CONFIRMED-{Guid.NewGuid():N}";

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", SwitchBody(confirmed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(confirmed, (await response.Content.ReadFromJsonAsync<RackDeviceTypeDto>())!.PartNumber);
    }

    [Fact]
    public async Task Port_groups_with_a_gap_are_refused()
    {
        var (client, switchId, _) = await OwnRows();

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}",
            SwitchBody("ADM-GAP", [new PortGroup("1G", 1, 8, true), new PortGroup("SFP+", 10, 11, false)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("portGroups", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
    }

    [Fact]
    public async Task A_placed_items_height_and_ports_cannot_change_but_its_part_number_can()
    {
        var (client, switchId, cabinetId) = await OwnRows();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Admin house" })).Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = cabinetId })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/racks/{rack!.Id}/items", new { rackDeviceTypeId = switchId });

        var taller = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", SwitchBody("ADM-TALL", heightU: 2));
        var renamed = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", SwitchBody($"ADM-OK-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.BadRequest, taller.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    [Fact]
    public async Task A_cabinet_cannot_be_made_shorter_than_an_item_placed_in_it()
    {
        var (client, switchId, cabinetId) = await OwnRows();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Admin house 2" })).Content.ReadFromJsonAsync<IdDto>();
        var rack = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/racks",
            new { name = "Comms", rackTypeId = cabinetId })).Content.ReadFromJsonAsync<IdDto>();
        await client.PostAsJsonAsync($"/racks/{rack!.Id}/items", new { rackDeviceTypeId = switchId });

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-types/{cabinetId}", new
        {
            manufacturer = "Generic", model = "Admin cabinet", partNumber = "CAB-SHORT", heightU = 4, mounting = "Wall",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("U6", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors["heightU"][0]);
    }

    [Fact]
    public async Task A_required_accessory_must_be_an_accessory()
    {
        var (client, switchId, _) = await OwnRows();

        var response = await client.PatchAsJsonAsync($"/catalogue/rack-device-types/{switchId}", new
        {
            manufacturer = "Ubiquiti", model = "Admin switch", partNumber = "ADM-ACC", kind = "Switch", heightU = 1,
            portGroups = Ports, poeBudgetW = 60, requiredAccessoryId = TestSeed.PatchPanelId, active = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("requiredAccessoryId", (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Errors.Keys);
    }
}
```

Run (env vars set): `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~RackCatalogueAdminTests"` — Expected: FAIL, 405 on the PATCH routes.

- [ ] **Step 2: Write the contracts, rules and endpoints**

In `src/PubInvest.HouseConfig.Api/Contracts/CatalogueContracts.cs`, add `using PubInvest.HouseConfig.Domain.Racks;` and:

```csharp
public sealed record SaveRackTypeRequest(
    string Manufacturer,
    string Model,
    string PartNumber,
    int HeightU,
    string Mounting);

public sealed record SaveRackDeviceTypeRequest(
    string Manufacturer,
    string Model,
    string PartNumber,
    string Kind,
    int HeightU,
    IReadOnlyList<PortGroup> PortGroups,
    int? PoeBudgetW,
    Guid? RequiredAccessoryId,
    bool Active);
```

Create `src/PubInvest.HouseConfig.Api/Racks/RackCatalogueRules.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// What a rack catalogue row must be before it is saved. A row that breaks
/// these would only fail later, on site: a port group with a gap renumbers a
/// schedule, and an accessory that is not one never reaches the parts list.
public static class RackCatalogueRules
{
    public static Dictionary<string, string[]> Validate(SaveRackTypeRequest request)
    {
        var problems = new Dictionary<string, string[]>();
        Required(problems, "manufacturer", request.Manufacturer);
        Required(problems, "model", request.Model);
        Required(problems, "partNumber", request.PartNumber);

        if (request.HeightU is < 1 or > 60) problems["heightU"] = ["A cabinet is between 1U and 60U tall."];
        if (request.Mounting is not ("Wall" or "Floor")) problems["mounting"] = ["Mounting is Wall or Floor."];
        return problems;
    }

    public static async Task<Dictionary<string, string[]>> ValidateAsync(
        HouseConfigDbContext db,
        Guid id,
        SaveRackDeviceTypeRequest request,
        CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();
        Required(problems, "manufacturer", request.Manufacturer);
        Required(problems, "model", request.Model);
        Required(problems, "partNumber", request.PartNumber);

        if (!Enum.TryParse<RackDeviceKind>(request.Kind, out var kind))
        {
            problems["kind"] = ["Kind is Switch, PatchPanel or Accessory."];
            return problems;
        }

        if (kind == RackDeviceKind.Accessory)
        {
            if (request.HeightU != 0 || request.PortGroups.Count > 0 || request.RequiredAccessoryId is not null)
            {
                problems["kind"] = ["An accessory takes no rack space, has no ports and needs nothing else."];
            }
        }
        else
        {
            if (request.HeightU is < 1 or > 10) problems["heightU"] = ["A rack item is between 1U and 10U tall."];

            var next = 1;
            foreach (var group in request.PortGroups)
            {
                if (string.IsNullOrWhiteSpace(group.Name) || group.From != next || group.To < group.From)
                {
                    problems["portGroups"] = [$"Ports run from 1 with no gaps or overlaps: the next group should start at port {next}."];
                    break;
                }
                next = group.To + 1;
            }

            if (request.PortGroups.Count == 0) problems["portGroups"] = ["A switch or patch panel needs at least one port group."];
        }

        if (request.RequiredAccessoryId is { } accessoryId
            && !await db.RackDeviceTypes.AnyAsync(t => t.Id == accessoryId && t.Kind == nameof(RackDeviceKind.Accessory), ct))
        {
            problems["requiredAccessoryId"] = ["The required accessory must be an accessory in the catalogue."];
        }

        var partNumber = request.PartNumber.Trim();
        if (await db.RackDeviceTypes.AnyAsync(t => t.Id != id && t.PartNumber == partNumber, ct))
        {
            problems["partNumber"] = [$"'{partNumber}' is already in the catalogue."];
        }

        return problems;
    }

    private static void Required(Dictionary<string, string[]> problems, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) problems[key] = ["This is required."];
    }
}
```

In `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueAdminEndpoints.cs`, add `using PubInvest.HouseConfig.Api.Racks;` and, before `return app;` in `MapCatalogueAdminEndpoints`:

```csharp
        group.MapPatch("/rack-types/{id:guid}", async (
                Guid id,
                SaveRackTypeRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var row = await db.RackTypes.SingleOrDefaultAsync(r => r.Id == id, ct);
                if (row is null) return Results.NotFound();

                var problems = RackCatalogueRules.Validate(request);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                // Making a cabinet shorter than an item already placed in one
                // would leave that item outside its own cabinet.
                var tallest = await (from rack in db.Racks
                                     where rack.RackTypeId == id
                                     from item in rack.Items
                                     orderby item.TopU descending
                                     select new { rack.Name, item.TopU }).FirstOrDefaultAsync(ct);
                if (tallest is not null && tallest.TopU > request.HeightU)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["heightU"] = [$"Rack '{tallest.Name}' has an item at U{tallest.TopU}. Move it before making this cabinet shorter."]
                    });
                }

                row.Manufacturer = request.Manufacturer.Trim();
                row.Model = request.Model.Trim();
                row.PartNumber = request.PartNumber.Trim();
                row.HeightU = request.HeightU;
                row.Mounting = request.Mounting;
                await db.SaveChangesAsync(ct);

                return Results.Ok(DomainMapper.ToDomain(row));
            });

        group.MapPatch("/rack-device-types/{id:guid}", async (
                Guid id,
                SaveRackDeviceTypeRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var row = await db.RackDeviceTypes.SingleOrDefaultAsync(r => r.Id == id, ct);
                if (row is null) return Results.NotFound();

                var problems = await RackCatalogueRules.ValidateAsync(db, id, request, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                // An item already in a rack keeps its shape: a new height could
                // overlap its neighbours, and new port groups would renumber its
                // labelled ports. Its name, part number and whether it is still
                // offered can change freely.
                var current = DomainMapper.ToDomain(row);
                var placed = await db.RackItems.CountAsync(i => i.RackDeviceTypeId == id, ct);
                var reshaped = request.HeightU != current.HeightU
                               || request.Kind != current.Kind.ToString()
                               || !request.PortGroups.SequenceEqual(current.PortGroups);
                if (placed > 0 && reshaped)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["portGroups"] = [$"Placed in {placed} {(placed == 1 ? "rack" : "racks")}: its kind, height and ports cannot change. Add a new catalogue item instead."]
                    });
                }

                row.Manufacturer = request.Manufacturer.Trim();
                row.Model = request.Model.Trim();
                row.PartNumber = request.PartNumber.Trim();
                row.Kind = request.Kind;
                row.HeightU = request.HeightU;
                row.PortGroupsJson = JsonSerializer.Serialize(request.PortGroups, DomainMapper.Json);
                row.PoeBudgetW = request.PoeBudgetW;
                row.RequiredAccessoryId = request.RequiredAccessoryId;
                row.Active = request.Active;
                await db.SaveChangesAsync(ct);

                return Results.Ok(DomainMapper.ToDomain(row));
            });
```

Run the Step 1 tests again — Expected: PASS. Then `dotnet test`: exit 0.

- [ ] **Step 3: Add the rack catalogue types to the UI**

In `ui/src/api/types.ts`, append:

```ts
export type RackDeviceKind = 'Switch' | 'PatchPanel' | 'Accessory'

/** A run of consecutively numbered ports that share a name, and whether PoE can be ticked on them. */
export interface PortGroup {
  name: string
  from: number
  to: number
  poeTick: boolean
}

/** A rack cabinet. Heights are in rack units; U1 is the bottom. */
export interface RackTypeResponse {
  id: string
  manufacturer: string
  model: string
  partNumber: string
  heightU: number
  mounting: 'Wall' | 'Floor'
  description: string
}

export interface RackDeviceTypeResponse {
  id: string
  manufacturer: string
  model: string
  partNumber: string
  kind: RackDeviceKind
  heightU: number
  portGroups: PortGroup[]
  poeBudgetW: number | null
  requiredAccessoryId: string | null
  active: boolean
  description: string
  portCount: number
}
```

- [ ] **Step 4: Write the failing UI test**

Create `ui/tests/RackCatalogueSections.test.tsx`:

```tsx
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RackCatalogueSections } from '../src/routes/RackCatalogueSections'
import { StatusRegion } from '../src/components/StatusRegion'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const cabinet = { id: 'c12', manufacturer: 'Generic', model: '12U wall cabinet', partNumber: 'ASSUMED-CAB-12U-WALL', heightU: 12, mounting: 'Wall', description: 'Generic 12U wall cabinet' }
const groups = [{ name: '1G', from: 1, to: 32, poeTick: true }, { name: '2.5G', from: 33, to: 48, poeTick: true }, { name: 'SFP+', from: 49, to: 52, poeTick: false }]
const sw = {
  id: 'sw48', manufacturer: 'Ubiquiti', model: 'USW-Pro-Max-48-PoE', partNumber: 'USW-Pro-Max-48-PoE', kind: 'Switch',
  heightU: 1, portGroups: groups, poeBudgetW: 720, requiredAccessoryId: null, active: true,
  description: 'Ubiquiti USW-Pro-Max-48-PoE', portCount: 52,
}

function stub() {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PATCH') return json({ ...sw, ...JSON.parse(String(init.body)) })
    if (url === '/api/catalogue/rack-types') return json([cabinet])
    if (url === '/api/catalogue/rack-device-types') return json([sw])
    return json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('RackCatalogueSections', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists rack cabinets and rack items with their part numbers', async () => {
    stub()
    render(<RackCatalogueSections />)

    const cabinets = await screen.findByRole('region', { name: 'Rack cabinets' })
    expect(within(cabinets).getByText('Generic 12U wall cabinet')).toBeInTheDocument()
    expect(within(cabinets).getByText(/12U · ASSUMED-CAB-12U-WALL/)).toBeInTheDocument()
    const items = screen.getByRole('region', { name: 'Rack items' })
    expect(within(items).getByText(/52 ports · USW-Pro-Max-48-PoE/)).toBeInTheDocument()
  })

  it('saves a corrected part number and sends the ports back unchanged', async () => {
    const fetchMock = stub()
    render(<><RackCatalogueSections /><StatusRegion /></>)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit Ubiquiti USW-Pro-Max-48-PoE' }))
    const part = screen.getByLabelText('Part number')
    await userEvent.clear(part)
    await userEvent.type(part, 'USW-PRO-MAX-48-POE-UK')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const patch = fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')!
    expect(patch[0]).toBe('/api/catalogue/rack-device-types/sw48')
    const body = JSON.parse(String(patch[1]!.body))
    expect(body.partNumber).toBe('USW-PRO-MAX-48-POE-UK')
    expect(body.portGroups).toEqual(groups)
    expect(body.kind).toBe('Switch')
    expect(await screen.findByRole('status')).toHaveTextContent(/saved/i)
  })
})
```

Run: `cd ui && export PATH=$HOME/.nvm/versions/node/v22.23.0/bin:$PATH && npx vitest run tests/RackCatalogueSections.test.tsx` — Expected: FAIL, module not found.

- [ ] **Step 5: Write the sections and add them to the Catalogue screen**

Create `ui/src/routes/RackCatalogueSections.tsx`:

```tsx
import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { RackDeviceTypeResponse, RackTypeResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Toggle } from '../components/Toggle'
import { announce } from '../components/status'

/**
 * Rack cabinets and rack items, so an ASSUMED part number can be confirmed
 * without a redeploy. Ports are not edited here: a slip in a port group would
 * renumber every port on a printed schedule, so that stays with whoever
 * maintains the seed.
 */
export function RackCatalogueSections() {
  const [cabinets, setCabinets] = useState<RackTypeResponse[] | null>(null)
  const [items, setItems] = useState<RackDeviceTypeResponse[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<string | null>(null)

  const load = useCallback(() =>
    Promise.all([
      api.get<RackTypeResponse[]>('/catalogue/rack-types'),
      api.get<RackDeviceTypeResponse[]>('/catalogue/rack-device-types'),
    ])
      .then(([c, i]) => { setCabinets(c); setItems(i); setError(null) })
      .catch((e: ApiError) => setError(e.message)), [])

  useEffect(() => { void load() }, [load])

  const done = () => { setEditing(null); void load() }

  if (error) {
    return <div className="mt-8"><ErrorNote message={`Could not load the rack catalogue: ${error}`} onRetry={() => void load()} /></div>
  }
  if (!cabinets || !items) return null

  return (
    <>
      <section aria-labelledby="rack-cabinets-heading" className="mt-8">
        <h2 id="rack-cabinets-heading" className="mb-2 font-semibold">Rack cabinets</h2>
        <ul className="divide-y divide-rule border-y border-rule">
          {cabinets.map(c => (
            <li key={c.id} className="py-3">
              {editing === c.id
                ? <CabinetForm cabinet={c} onDone={done} onCancel={() => setEditing(null)} />
                : <Row title={c.description} detail={`${c.heightU}U · ${c.partNumber}`} onEdit={() => setEditing(c.id)} />}
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="rack-items-heading" className="mt-8">
        <h2 id="rack-items-heading" className="mb-2 font-semibold">Rack items</h2>
        <ul className="divide-y divide-rule border-y border-rule">
          {items.map(item => (
            <li key={item.id} className="py-3">
              {editing === item.id
                ? <ItemForm item={item} onDone={done} onCancel={() => setEditing(null)} />
                : (
                  <Row
                    title={item.description}
                    detail={`${item.kind === 'Accessory' ? 'Accessory' : `${item.heightU}U · ${item.portCount} ports`} · ${item.partNumber}${item.active ? '' : ' · not offered'}`}
                    onEdit={() => setEditing(item.id)}
                  />
                )}
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}

function Row({ title, detail, onEdit }: { title: string; detail: string; onEdit: () => void }) {
  return (
    <div className="flex items-center gap-3">
      <div className="min-w-0 flex-1 wrap-anywhere">
        <p className="font-semibold">{title}</p>
        <p className="text-ink-2">{detail}</p>
      </div>
      <Button variant="ghost" aria-label={`Edit ${title}`} onClick={onEdit}>Edit</Button>
    </div>
  )
}

function CabinetForm({ cabinet, onDone, onCancel }: { cabinet: RackTypeResponse; onDone: () => void; onCancel: () => void }) {
  const [manufacturer, setManufacturer] = useState(cabinet.manufacturer)
  const [model, setModel] = useState(cabinet.model)
  const [partNumber, setPartNumber] = useState(cabinet.partNumber)
  const [height, setHeight] = useState(String(cabinet.heightU))
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  async function save() {
    setErrors({})
    try {
      await api.patch(`/catalogue/rack-types/${cabinet.id}`, {
        manufacturer, model, partNumber, heightU: Number(height) || 0, mounting: cabinet.mounting,
      })
      announce(`${manufacturer} ${model} saved`, 'ok')
      onDone()
    } catch (e) {
      const error = e as ApiError
      setErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) announce(error.message, 'error')
    }
  }

  return (
    <form className="space-y-3" onSubmit={e => { e.preventDefault(); void save() }} noValidate>
      <Field label="Manufacturer" value={manufacturer} onChange={setManufacturer} errors={errors.manufacturer} />
      <Field label="Model" value={model} onChange={setModel} errors={errors.model} />
      <Field label="Part number" value={partNumber} onChange={setPartNumber} errors={errors.partNumber} autoCapitalize="characters" />
      <Field label="Height (U)" value={height} onChange={setHeight} inputMode="numeric" errors={errors.heightU} />
      <div className="flex gap-2">
        <Button type="submit">Save</Button>
        <Button variant="secondary" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}

function ItemForm({ item, onDone, onCancel }: { item: RackDeviceTypeResponse; onDone: () => void; onCancel: () => void }) {
  const [manufacturer, setManufacturer] = useState(item.manufacturer)
  const [model, setModel] = useState(item.model)
  const [partNumber, setPartNumber] = useState(item.partNumber)
  const [active, setActive] = useState(item.active)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  async function save() {
    setErrors({})
    try {
      // Everything this form does not edit goes back as it came, so a part
      // number fix can never disturb the ports.
      await api.patch(`/catalogue/rack-device-types/${item.id}`, {
        manufacturer, model, partNumber, kind: item.kind, heightU: item.heightU, portGroups: item.portGroups,
        poeBudgetW: item.poeBudgetW, requiredAccessoryId: item.requiredAccessoryId, active,
      })
      announce(`${manufacturer} ${model} saved`, 'ok')
      onDone()
    } catch (e) {
      const error = e as ApiError
      setErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) announce(error.message, 'error')
    }
  }

  return (
    <form className="space-y-3" onSubmit={e => { e.preventDefault(); void save() }} noValidate>
      <Field label="Manufacturer" value={manufacturer} onChange={setManufacturer} errors={errors.manufacturer} />
      <Field label="Model" value={model} onChange={setModel} errors={errors.model} />
      <Field label="Part number" value={partNumber} onChange={setPartNumber} errors={errors.partNumber} autoCapitalize="characters" />
      {item.kind !== 'Accessory' && (
        <Toggle
          label="Offered in Add to rack"
          hint="Turning this off keeps it in racks that already have it."
          checked={active}
          onChange={setActive}
        />
      )}
      <div className="flex gap-2">
        <Button type="submit">Save</Button>
        <Button variant="secondary" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}
```

In `ui/src/routes/CataloguePage.tsx`, add `import { RackCatalogueSections } from './RackCatalogueSections'` and render `<RackCatalogueSections />` immediately after the closing `</section>` of the Enclosures section (inside the same fragment).

- [ ] **Step 6: Run the UI checks**

Run: `cd ui && npx vitest run tests/RackCatalogueSections.test.tsx` — Expected: PASS. Then `npm test; echo $?` → `0` with no `Errors` line, `npm run build; echo $?` → `0`, `npm run lint` → 0 warnings. If an existing Catalogue test stubs fetch so the new requests fail, it shows the compact error under the existing sections; that is the intended behaviour — leave those tests' assertions alone unless they fail.

- [ ] **Step 7: Commit**

```bash
git add src tests ui/src ui/tests
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Correct rack part numbers in the Catalogue screen

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Racks on the house page, and adding one

**Files:**
- Modify: `ui/src/api/client.ts`
- Modify: `ui/src/api/types.ts`
- Create: `ui/src/components/SelectField.tsx`
- Modify: `ui/src/routes/ProjectPage.tsx`
- Create: `ui/src/routes/NewRackPage.tsx`
- Modify: `ui/src/App.tsx`
- Test: `ui/tests/ProjectPage.test.tsx`, `ui/tests/NewRackPage.test.tsx`

**Interfaces:**
- Consumes: Task 3 routes; Task 7 TS catalogue types.
- Produces: `api.put<T>(path, body)`, `api.del<T>(path)`; TS `RackResponse`, `RackPortResponse`, `RackItemResponse`, `RackLayoutResponse`, `FedRack`; `RevisionView.submainId` becomes optional and `RevisionView.rackId?: string` is added; `SelectField({ label, value, options: {value,label}[], onChange, hint?, errors?, disabled? })`; route `/projects/:projectId/racks/new`.

- [ ] **Step 1: Add the client methods and types**

In `ui/src/api/client.ts`, extend `api`:

```ts
export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  del: <T>(path: string) => request<T>('DELETE', path),
}
```

In `ui/src/api/types.ts`, change `RevisionView` so it serves both panels and racks:

```ts
export interface RevisionView {
  id: string
  /** Set on a panel revision. */
  submainId?: string
  /** Set on a rack revision. */
  rackId?: string
  /** Where it stands in its history, counted from one by the server. */
  number: number
  layoutVersion: number
  issuedAt: string
  issuedBy: string
  /** Server-decided: does this revision still match the panel or rack as it is now? */
  current: boolean
}
```

and append:

```ts
export interface RackResponse {
  id: string
  projectId: string
  name: string
  rackTypeId: string
  cabinet: string
  heightU: number
  usedU: number
  fedFromSubmainId: string | null
  fedFromSubmainName: string | null
  notes: string | null
  layoutVersion: number
}

export interface RackPortResponse {
  number: number
  label: string | null
  poe: boolean
}

export interface RackItemResponse {
  id: string
  rackDeviceTypeId: string
  kind: RackDeviceKind
  model: string
  partNumber: string
  label: string
  /** The highest U the item occupies; U1 is the bottom of the cabinet. */
  topU: number
  heightU: number
  portGroups: PortGroup[]
  poeBudgetW: number | null
  /** Only ports with a label or a PoE tick; the rest are implied by the groups. */
  ports: RackPortResponse[]
}

/** What the server decided. The UI draws it and never places anything itself. */
export interface RackLayoutResponse {
  rackId: string
  heightU: number
  freeU: number
  layoutVersion: number
  /** Top U first. */
  items: RackItemResponse[]
}

export interface FedRack {
  id: string
  name: string
}
```

- [ ] **Step 2: Write SelectField**

Create `ui/src/components/SelectField.tsx`:

```tsx
import { useId } from 'react'

interface Option {
  value: string
  label: string
}

interface Props {
  label: string
  value: string
  options: Option[]
  onChange: (value: string) => void
  hint?: string
  errors?: string[]
  disabled?: boolean
}

/** A labelled select with the app's own chevron, and room for a hint and errors under it, as Field has. */
export function SelectField({ label, value, options, onChange, hint, errors, disabled }: Props) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const invalid = (errors?.length ?? 0) > 0

  return (
    <div>
      <label htmlFor={id} className="block font-medium">{label}</label>
      <select
        id={id}
        value={value}
        disabled={disabled}
        onChange={e => onChange(e.target.value)}
        aria-invalid={invalid}
        aria-describedby={[hint ? hintId : null, invalid ? errorId : null].filter(Boolean).join(' ') || undefined}
        className={`select mt-1 w-full min-w-0 rounded-lg border bg-sheet pl-3 ${invalid ? 'border-danger' : 'border-rule'}`}
      >
        {options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
      {hint && <p id={hintId} className="mt-1 text-sm text-ink-2">{hint}</p>}
      {invalid && (
        <ul id={errorId} className="mt-1 space-y-0.5">
          {errors!.map(e => <li key={e} className="text-sm font-medium text-danger">{e}</li>)}
        </ul>
      )}
    </div>
  )
}
```

- [ ] **Step 3: Write the failing page tests**

Create `ui/tests/ProjectPage.test.tsx`:

```tsx
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProjectPage } from '../src/routes/ProjectPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const rack = {
  id: 'r1', projectId: 'p1', name: 'Comms cupboard', rackTypeId: 'c12', cabinet: 'Generic 12U wall cabinet',
  heightU: 12, usedU: 4, fedFromSubmainId: 's1', fedFromSubmainName: 'Ground floor', notes: null, layoutVersion: 3,
}

function stub(racks: unknown[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/projects/p1') return json({ id: 'p1', name: 'Willow House', address: null, notes: null, submainCount: 1 })
    if (url === '/api/projects/p1/submains') return json([{ id: 's1', name: 'Ground floor', deviceCount: 3, circuitCount: 8 }])
    if (url === '/api/projects/p1/racks') return json(racks)
    return json([])
  }))
}

const show = () => render(
  <MemoryRouter initialEntries={['/projects/p1']}>
    <Routes><Route path="/projects/:projectId" element={<ProjectPage />} /></Routes>
  </MemoryRouter>,
)

describe('ProjectPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists submains and network racks side by side, each with its own Add button', async () => {
    stub([rack])
    show()

    const submains = await screen.findByRole('region', { name: 'Submains' })
    expect(within(submains).getByRole('link', { name: 'Add submain' })).toHaveAttribute('href', '/projects/p1/submains/new')

    const racks = screen.getByRole('region', { name: 'Network racks' })
    expect(within(racks).getByRole('link', { name: 'Add rack' })).toHaveAttribute('href', '/projects/p1/racks/new')
    const row = within(racks).getByRole('link', { name: /Comms cupboard/ })
    expect(row).toHaveAttribute('href', '/racks/r1')
    expect(row).toHaveTextContent('Generic 12U wall cabinet · 4 of 12U used')
    expect(row).toHaveTextContent('Fed from Ground floor')
  })

  it('invites a first rack when the house has none', async () => {
    stub([])
    show()

    const racks = await screen.findByRole('region', { name: 'Network racks' })
    expect(within(racks).getByText('No network racks yet')).toBeInTheDocument()
  })
})
```

Create `ui/tests/NewRackPage.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NewRackPage } from '../src/routes/NewRackPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const cabinets = [
  { id: 'c6', manufacturer: 'Generic', model: '6U wall cabinet', partNumber: 'A6', heightU: 6, mounting: 'Wall', description: 'Generic 6U wall cabinet' },
  { id: 'c12', manufacturer: 'Generic', model: '12U wall cabinet', partNumber: 'A12', heightU: 12, mounting: 'Wall', description: 'Generic 12U wall cabinet' },
]

function stub(post: () => Response) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') return post()
    if (url === '/api/catalogue/rack-types') return json(cabinets)
    if (url === '/api/projects/p1/submains') return json([{ id: 's1', name: 'Ground floor' }])
    return json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const show = () => render(
  <MemoryRouter initialEntries={['/projects/p1/racks/new']}>
    <Routes>
      <Route path="/projects/:projectId/racks/new" element={<NewRackPage />} />
      <Route path="/racks/:rackId" element={<p>Rack page</p>} />
    </Routes>
  </MemoryRouter>,
)

describe('NewRackPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('adds the rack with its cabinet and feed, then opens it', async () => {
    const fetchMock = stub(() => json({ id: 'r1', name: 'Comms cupboard' }, 201))
    show()

    await userEvent.type(await screen.findByLabelText('Rack name'), 'Comms cupboard')
    expect(screen.getByLabelText('Cabinet')).toHaveValue('c12')
    await userEvent.selectOptions(screen.getByLabelText('Fed from'), 's1')
    await userEvent.click(screen.getByRole('button', { name: 'Add rack' }))

    expect(await screen.findByText('Rack page')).toBeInTheDocument()
    const [url, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(url).toBe('/api/projects/p1/racks')
    expect(JSON.parse(String(init!.body))).toEqual({ name: 'Comms cupboard', rackTypeId: 'c12', fedFromSubmainId: 's1' })
  })

  it('can be fed from no submain', async () => {
    const fetchMock = stub(() => json({ id: 'r1', name: 'Loft' }, 201))
    show()

    await userEvent.type(await screen.findByLabelText('Rack name'), 'Loft')
    await userEvent.click(screen.getByRole('button', { name: 'Add rack' }))

    const [, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body)).fedFromSubmainId).toBeNull()
  })

  it('shows the server\'s field errors where they belong and keeps what was typed', async () => {
    stub(() => json({ title: 'One or more validation errors occurred.', errors: { name: ['A rack name is required.'] } }, 400))
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add rack' }))

    expect(await screen.findByText('A rack name is required.')).toBeInTheDocument()
    expect(screen.getByLabelText('Cabinet')).toHaveValue('c12')
  })
})
```

Run: `npx vitest run tests/ProjectPage.test.tsx tests/NewRackPage.test.tsx` — Expected: FAIL (no racks region; NewRackPage missing).

- [ ] **Step 4: Rewrite the house page**

Replace `ui/src/routes/ProjectPage.tsx` with:

```tsx
import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse, RackResponse, SubmainResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { ButtonLink } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'

/** A house is its submains and its network racks, listed side by side, each added the same way. */
export function ProjectPage() {
  const { projectId } = useParams()
  const [project, setProject] = useState<ProjectResponse | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[] | null>(null)
  const [racks, setRacks] = useState<RackResponse[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() =>
    Promise.all([
      api.get<ProjectResponse>(`/projects/${projectId}`),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
      api.get<RackResponse[]>(`/projects/${projectId}/racks`),
    ])
      .then(([p, s, r]) => { setProject(p); setSubmains(s); setRacks(r) })
      .catch((e: ApiError) => setError(e.message)), [projectId])

  useEffect(() => { void load() }, [load])

  const loaded = project !== null && submains !== null && racks !== null

  return (
    <AppShell
      title={project?.name ?? 'House'}
      subtitle={project?.address ?? undefined}
      back={{ to: '/', label: 'Houses' }}
      actions={loaded ? <ButtonLink to={`/projects/${projectId}/bom`} variant="ghost">Bill of materials</ButtonLink> : undefined}
    >
      {error && <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />}
      {!loaded && !error && <Skeleton label="Loading house" rows={3} rowClass="h-20" />}

      {loaded && (
        <>
          <section aria-labelledby="submains-heading">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 id="submains-heading" className="text-lg font-bold">Submains</h2>
              <ButtonLink to={`/projects/${projectId}/submains/new`} variant="primary">Add submain</ButtonLink>
            </div>
            {submains.length === 0 ? (
              <EmptyState
                title="No submains yet"
                hint="A submain is one panel. Add the first one and its panel is designed from the circuits it feeds."
              />
            ) : (
              <ul className="divide-y divide-rule border-y border-rule">
                {/* py-1 on each row keeps 8px between one tappable row and the next. */}
                {submains.map(s => (
                  <li key={s.id} className="py-1">
                    <Link to={`/submains/${s.id}/panel`} className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press">
                      {/* wrap-anywhere: a submain named by a long reference must break rather than spill off the page. */}
                      <span className="min-w-0 flex-1 wrap-anywhere">
                        <span className="block text-lg font-semibold">{s.name}</span>
                        <span className="block text-ink-2">
                          {s.deviceCount === 0 ? 'Not designed yet' : `${s.deviceCount} devices · ${s.circuitCount} circuits`}
                        </span>
                      </span>
                      <span aria-hidden="true" className="text-xl text-ink-2">›</span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section aria-labelledby="racks-heading" className="mt-8">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 id="racks-heading" className="text-lg font-bold">Network racks</h2>
              <ButtonLink to={`/projects/${projectId}/racks/new`} variant="secondary">Add rack</ButtonLink>
            </div>
            {racks.length === 0 ? (
              <EmptyState
                title="No network racks yet"
                hint="A rack holds the switches and patch panels. Add one, fill it from the catalogue and label its ports."
              />
            ) : (
              <ul className="divide-y divide-rule border-y border-rule">
                {racks.map(r => (
                  <li key={r.id} className="py-1">
                    <Link to={`/racks/${r.id}`} className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press">
                      <span className="min-w-0 flex-1 wrap-anywhere">
                        <span className="block text-lg font-semibold">{r.name}</span>
                        <span className="block text-ink-2">{r.cabinet} · {r.usedU} of {r.heightU}U used</span>
                        {r.fedFromSubmainName && <span className="block text-ink-2">Fed from {r.fedFromSubmainName}</span>}
                      </span>
                      <span aria-hidden="true" className="text-xl text-ink-2">›</span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </AppShell>
  )
}
```

- [ ] **Step 5: Write the Add rack page and its route**

Create `ui/src/routes/NewRackPage.tsx`:

```tsx
import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { RackResponse, RackTypeResponse, SubmainResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { SelectField } from '../components/SelectField'
import { Skeleton } from '../components/Skeleton'
import { announce, dismissStatus } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'

/**
 * Three answers and the rack exists: what it is called, which cabinet, and
 * which submain feeds it. What goes in it is added on the rack's own screen,
 * where it can be seen going in.
 */
export function NewRackPage() {
  const { projectId } = useParams()
  const navigate = useNavigate()
  const once = useSingleFlight()

  const [cabinets, setCabinets] = useState<RackTypeResponse[] | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [rackTypeId, setRackTypeId] = useState('')
  const [fedFrom, setFedFrom] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  const load = useCallback(() =>
    Promise.all([
      api.get<RackTypeResponse[]>('/catalogue/rack-types'),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
    ])
      .then(([c, s]) => {
        setCabinets(c)
        setSubmains(s)
        setLoadError(null)
        // A 12U wall cabinet is the common case in a house; anything else is one tap away.
        setRackTypeId(id => id || (c.find(x => x.heightU === 12) ?? c[0])?.id || '')
      })
      .catch((e: ApiError) => setLoadError(e.message)), [projectId])

  useEffect(() => { void load() }, [load])

  // A rack is something that then exists in the house: two taps in one tick must make one.
  const save = () => once(async () => {
    setSaving(true)
    setFieldErrors({})
    dismissStatus()
    try {
      const rack = await api.post<RackResponse>(`/projects/${projectId}/racks`, {
        name: name.trim(), rackTypeId, fedFromSubmainId: fedFrom || null,
      })
      announce(`${rack.name} added. Add what goes in it.`, 'ok')
      navigate(`/racks/${rack.id}`)
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) announce(error.message, 'error')
    } finally {
      setSaving(false)
    }
  })

  return (
    <AppShell
      title="New rack"
      back={{ to: `/projects/${projectId}`, label: 'House' }}
      actions={cabinets ? (
        <Button type="submit" form="new-rack" disabled={saving}>{saving ? 'Adding…' : 'Add rack'}</Button>
      ) : undefined}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}
      {!cabinets && !loadError && <Skeleton label="Loading cabinets" rows={3} rowClass="h-14" />}

      {cabinets && (
        <form id="new-rack" className="space-y-5" noValidate onSubmit={e => { e.preventDefault(); void save() }}>
          <p className="text-ink-2">A rack holds the switches and patch panels for part of the house. You'll add what goes in it next.</p>
          <Field
            label="Rack name"
            value={name}
            onChange={setName}
            errors={fieldErrors.name}
            placeholder="Comms cupboard"
            autoCapitalize="words"
            autoComplete="off"
            enterKeyHint="done"
          />
          <SelectField
            label="Cabinet"
            value={rackTypeId}
            onChange={setRackTypeId}
            errors={fieldErrors.rackTypeId}
            options={cabinets.map(c => ({ value: c.id, label: `${c.heightU}U · ${c.description}` }))}
          />
          <SelectField
            label="Fed from"
            hint="The submain this rack's supply comes from. It changes nothing on that panel."
            value={fedFrom}
            onChange={setFedFrom}
            errors={fieldErrors.fedFromSubmainId}
            options={[{ value: '', label: 'Not fed from a submain' }, ...submains.map(s => ({ value: s.id, label: s.name }))]}
          />
        </form>
      )}
    </AppShell>
  )
}
```

In `ui/src/App.tsx`, add `import { NewRackPage } from './routes/NewRackPage'` and the route after the new-submain route:

```tsx
      <Route path="/projects/:projectId/racks/new" element={<NewRackPage />} />
```

- [ ] **Step 6: Run the UI checks**

Run: `npx vitest run tests/ProjectPage.test.tsx tests/NewRackPage.test.tsx` — Expected: PASS. Then `npm test; echo $?` → `0` and no `Errors` line. If `navigation.test.tsx` or another test renders the house page, its stub already answers unknown URLs with `[]`, which is a valid empty rack list; fix any test that instead returns `{}` for `/projects/p1/racks` by returning `[]`. Then `npm run build` → 0 and `npm run lint` → 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add ui/src ui/tests
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "List racks on the house page and add one like a submain

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: One drag gesture for panels and racks

The long-press-for-touch, drag-straight-away-with-a-mouse gesture in `useDeviceDrag` took several rounds to get right in Safari. Racks need the same gesture over a different coordinate system, so it moves into a generic hook and the panel keeps its public API as an adapter. The panel's existing 13 tests and the e2e drag tests are the guard for this task: none of them change.

**Files:**
- Create: `ui/src/drag/usePressDrag.ts`
- Modify: `ui/src/panel/useDeviceDrag.ts` (becomes an adapter, same exports and signature)
- Test: `ui/tests/usePressDrag.test.tsx` (new); `ui/tests/useDeviceDrag.test.tsx` (unchanged, must pass)

**Interfaces:**
- Produces: `LONG_PRESS_MS` (re-exported from `useDeviceDrag` too); `interface PressDrag<T> { id: string; target: T; valid: boolean }`; `usePressDrag<T>(options: { idAttribute: string; start(id): T | null; locate(id, point): { target: T; valid: boolean } | null; exists(id): boolean; onCommit(id, target: T): void; toPoint(clientX, clientY): { x: number; y: number } })` → `{ dragging: PressDrag<T> | null, bind: { ref, onPointerDown, onPointerMove, onPointerUp, onPointerCancel } }`.
- `useDeviceDrag({ layout, onCommit, toPanelPoint })` keeps returning `{ dragging: DragState | null, bind }` exactly as today.

- [ ] **Step 1: Write the failing generic tests**

Create `ui/tests/usePressDrag.test.tsx`:

```tsx
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LONG_PRESS_MS, usePressDrag } from '../src/drag/usePressDrag'

/** A pointer event whose target sits inside an element carrying data-item-id. */
function pointerOn(id: string, clientY: number, pointerType = 'touch') {
  const holder = document.createElement('div')
  holder.setAttribute('data-item-id', id)
  const child = document.createElement('span')
  holder.appendChild(child)
  document.body.appendChild(holder)
  return {
    target: child, clientX: 0, clientY, pointerId: 1, pointerType, button: 0,
    currentTarget: { setPointerCapture: vi.fn() },
  } as unknown as React.PointerEvent
}

/** A vertical list of slots 10px tall; slot 3 is taken by someone else. */
const options = (onCommit = vi.fn()) => ({
  idAttribute: 'data-item-id',
  start: (id: string) => (id === 'a' ? 0 : null),
  locate: (_id: string, point: { x: number; y: number }) => {
    const slot = Math.round(point.y / 10)
    return { target: slot, valid: slot !== 3 }
  },
  exists: (id: string) => id === 'a',
  onCommit,
  toPoint: (x: number, y: number) => ({ x, y }),
})

describe('usePressDrag', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => { vi.useRealTimers(); document.body.innerHTML = '' })

  it('lifts an item with a finger only after the long press, then follows it', () => {
    const { result } = renderHook(() => usePressDrag(options()))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0)))
    expect(result.current.dragging).toBeNull()
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    act(() => result.current.bind.onPointerMove(pointerOn('a', 52)))

    expect(result.current.dragging).toEqual({ id: 'a', target: 5, valid: true })
  })

  it('lifts an item with a mouse as soon as it moves, and commits on release', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => usePressDrag(options(onCommit)))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 'mouse')))
    act(() => result.current.bind.onPointerMove(pointerOn('a', 70, 'mouse')))
    act(() => result.current.bind.onPointerUp())

    expect(onCommit).toHaveBeenCalledWith('a', 7)
  })

  it('never commits a drop the caller says is not allowed', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => usePressDrag(options(onCommit)))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 'mouse')))
    act(() => result.current.bind.onPointerMove(pointerOn('a', 30, 'mouse')))
    expect(result.current.dragging?.valid).toBe(false)
    act(() => result.current.bind.onPointerUp())

    expect(onCommit).not.toHaveBeenCalled()
  })

  it('does not lift an item that has gone by the time the long press fires', () => {
    const { result } = renderHook(() => usePressDrag(options()))

    act(() => result.current.bind.onPointerDown(pointerOn('gone', 0)))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })

    expect(result.current.dragging).toBeNull()
  })
})
```

Run: `npx vitest run tests/usePressDrag.test.tsx` — Expected: FAIL, module not found.

- [ ] **Step 2: Move the gesture into usePressDrag**

Create `ui/src/drag/usePressDrag.ts`:

```ts
import { useCallback, useEffect, useRef, useState } from 'react'

/** Long-press before a drag begins, so an ordinary pan never picks an item up. */
export const LONG_PRESS_MS = 350

/**
 * Movement past this before the timer fires means a finger is panning, not
 * dragging. A mouse cannot pan by dragging, so for a mouse the same distance
 * means the opposite: the item has been picked up.
 */
const PAN_TOLERANCE_PX = 8

export interface PressDrag<T> {
  id: string
  target: T
  valid: boolean
}

export interface PressDragOptions<T> {
  /** The attribute on an item's element that carries its id, e.g. "data-device-id". */
  idAttribute: string
  /** Where the item is now, asked when the drag arms; null when it has gone. */
  start: (id: string) => T | null
  /** Where a pointer at this point would put the item and whether it may go there; null when it has gone. */
  locate: (id: string, point: { x: number; y: number }) => { target: T; valid: boolean } | null
  /** Whether the item is still there to be dropped. */
  exists: (id: string) => boolean
  onCommit: (id: string, target: T) => void
  /** Converts client coordinates into drawing coordinates, undoing pan and zoom. */
  toPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

/**
 * The press-and-drag gesture the panel and the rack share. On a touchscreen a
 * long press picks an item up, so an ordinary pan never does; with a mouse a
 * press that moves picks it up straight away. What a position means — a row
 * and a slot, or a rack unit — belongs to the caller, through start and locate.
 */
export function usePressDrag<T>(options: PressDragOptions<T>) {
  const [dragging, setDragging] = useState<PressDrag<T> | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const origin = useRef<{
    x: number
    y: number
    id: string
    mouse: boolean
    element: Element
    pointerId: number
  } | null>(null)
  const armed = useRef(false)
  const captured = useRef<{ element: Element; pointerId: number } | null>(null)
  const wrapper = useRef<HTMLElement | null>(null)

  // The caller's callbacks as they are now, not as they were at the press: a
  // reload can land during the hold — a save from the sheet, or a rebuild that
  // replaces every item — and the timer must not lift an item from a drawing
  // that is already gone.
  const latest = useRef(options)
  useEffect(() => { latest.current = options })

  const cancel = useCallback(() => {
    clearTimeout(timer.current)
    if (captured.current) {
      const { element, pointerId } = captured.current
      ;(element as Element & { releasePointerCapture?: (id: number) => void })
        .releasePointerCapture?.(pointerId)
      captured.current = null
    }
    origin.current = null
    armed.current = false
    setDragging(null)
  }, [])

  /**
   * Lifts the pressed item: from here on, moves place it and release drops it.
   * Capture starts only now — capturing on every pointerdown would retarget the
   * click that follows a plain tap, so the item's sheet would never open.
   */
  const arm = useCallback(() => {
    const start = origin.current
    if (!start) return false

    const target = latest.current.start(start.id)
    // Nothing to lift: the press ends as a press, and the page keeps scrolling as normal.
    if (target === null) {
      origin.current = null
      return false
    }

    armed.current = true
    ;(start.element as Element & { setPointerCapture?: (id: number) => void }).setPointerCapture?.(start.pointerId)
    captured.current = { element: start.element, pointerId: start.pointerId }
    setDragging({ id: start.id, target, valid: true })
    return true
  }, [])

  const onPointerDown = useCallback((e: React.PointerEvent) => {
    const attribute = latest.current.idAttribute
    const id = (e.target as Element).closest(`[${attribute}]`)?.getAttribute(attribute)
    if (!id) return

    const mouse = e.pointerType === 'mouse'
    // A right-click opens a menu; it never picks an item up.
    if (mouse && e.button !== 0) return

    origin.current = { x: e.clientX, y: e.clientY, id, mouse, element: e.currentTarget as Element, pointerId: e.pointerId }
    armed.current = false

    // A mouse is armed by moving instead (see onPointerMove): it has no scroll
    // to tell apart from a drag, so making it hold still first only reads as
    // a drawing that won't let go of its items.
    if (!mouse) timer.current = setTimeout(arm, LONG_PRESS_MS)
  }, [arm])

  const onPointerMove = useCallback((e: React.PointerEvent) => {
    const start = origin.current
    if (!start) return

    if (!armed.current) {
      const moved = Math.hypot(e.clientX - start.x, e.clientY - start.y)
      if (moved <= PAN_TOLERANCE_PX) return
      if (!start.mouse) {
        cancel()
        return
      }
      if (!arm()) return
    }

    const located = latest.current.locate(start.id, latest.current.toPoint(e.clientX, e.clientY))
    if (!located) return
    setDragging({ id: start.id, target: located.target, valid: located.valid })
  }, [cancel, arm])

  const onPointerUp = useCallback(() => {
    // Only an item still on the drawing can be moved: one replaced by a
    // reload mid-drag would be a request the server can only refuse.
    if (dragging?.valid && latest.current.exists(dragging.id)) {
      latest.current.onCommit(dragging.id, dragging.target)
    }
    cancel()
  }, [dragging, cancel])

  /**
   * WebKit decides at touchstart whether a touch may scroll, and a passive
   * listener cannot change its mind later. So this is registered non-passive
   * on the real element for both touchmove and touchend: once the long press
   * has armed, every touchmove is cancelled so neither the drawing nor the
   * page scrolls out from under the drag, and the touchend is cancelled so
   * Safari does not synthesise a click that would open the sheet on drop. It
   * only reads a ref, so one function serves the life of the hook and the ref
   * callback below can remove exactly what it added.
   */
  const cancelWhenArmed = useCallback((e: TouchEvent) => {
    if (armed.current) e.preventDefault()
  }, [])

  const ref = useCallback((element: HTMLElement | null) => {
    const previous = wrapper.current
    if (previous) {
      previous.removeEventListener('touchmove', cancelWhenArmed)
      previous.removeEventListener('touchend', cancelWhenArmed)
    }
    wrapper.current = element
    if (element) {
      element.addEventListener('touchmove', cancelWhenArmed, { passive: false })
      element.addEventListener('touchend', cancelWhenArmed, { passive: false })
    }
  }, [cancelWhenArmed])

  return {
    dragging,
    /** Spread onto the drawing's wrapper; its ref must reach the DOM element. */
    bind: {
      ref,
      onPointerDown,
      onPointerMove,
      onPointerUp,
      onPointerCancel: cancel,
    },
  }
}
```

Replace `ui/src/panel/useDeviceDrag.ts` with the adapter:

```ts
import type { LayoutResponse } from '../api/types'
import { usePressDrag } from '../drag/usePressDrag'
import { isTargetFree } from './dragTargets'
import { SLOT_PX, xToSlot, yToRow } from './geometry'
import type { DragState } from './PanelSvg'

export { LONG_PRESS_MS } from '../drag/usePressDrag'

interface Options {
  layout: LayoutResponse
  onCommit: (deviceId: string, rowIndex: number, startSlot: number) => void
  /** Converts client coordinates into panel coordinates, undoing pan and zoom. */
  toPanelPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

/** The shared press-and-drag gesture, in the panel's terms: a row and a slot. */
export function useDeviceDrag({ layout, onCommit, toPanelPoint }: Options) {
  const find = (id: string) => layout.devices.find(d => d.id === id)

  const { dragging, bind } = usePressDrag<{ rowIndex: number; startSlot: number }>({
    idAttribute: 'data-device-id',
    start: id => {
      const device = find(id)
      return device ? { rowIndex: device.rowIndex, startSlot: device.startSlot } : null
    },
    locate: (id, point) => {
      const device = find(id)
      if (!device) return null
      const startSlot = xToSlot(point.x - (device.moduleWidth * SLOT_PX) / 2, device.moduleWidth, layout.slotsPerRow)
      const rowIndex = yToRow(point.y, layout.rows)
      return { target: { rowIndex, startSlot }, valid: isTargetFree(layout, id, rowIndex, startSlot) }
    },
    exists: id => find(id) !== undefined,
    onCommit: (id, target) => onCommit(id, target.rowIndex, target.startSlot),
    toPoint: toPanelPoint,
  })

  const state: DragState | null = dragging
    ? { deviceId: dragging.id, rowIndex: dragging.target.rowIndex, startSlot: dragging.target.startSlot, valid: dragging.valid }
    : null

  return { dragging: state, bind }
}
```

- [ ] **Step 3: Run the old and new tests**

Run: `npx vitest run tests/usePressDrag.test.tsx tests/useDeviceDrag.test.tsx tests/PanelPage.test.tsx` — Expected: PASS, with `useDeviceDrag.test.tsx` unchanged. Then run the panel drag e2e tests (they exercise real WebKit touch and mouse): `npx playwright test e2e/panel.spec.ts -g "drag"` — Expected: all pass. Then `npm test; echo $?` → 0, `npm run build` → 0, `npm run lint` → 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add ui/src/drag ui/src/panel/useDeviceDrag.ts ui/tests/usePressDrag.test.tsx
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Share the panel's press-and-drag gesture so racks can use it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: The item sheet — label every port, tick PoE, move, remove

**Files:**
- Create: `ui/src/racks/ports.ts`
- Create: `ui/src/racks/rackTargets.ts`
- Create: `ui/src/racks/ItemSheet.tsx`
- Test: `ui/tests/rackTargets.test.ts`, `ui/tests/ItemSheet.test.tsx`

**Interfaces:**
- Consumes: Task 8 TS types, `SelectField`; existing `Sheet`, `ConfirmButton`.
- Produces:
  - `ports.ts`: `interface PortRowData { number: number; group: PortGroup; label: string; poe: boolean }`, `portRows(item): PortRowData[]`, `poeCapable(item): number`, `poeTicked(item): number`, `frontPanel(item): { top: number[]; bottom: number[]; uplinks: number[] }`
  - `rackTargets.ts`: `isFree(layout, itemId, topU, heightU): boolean`, `freeTopUs(layout, item): number[]` (highest first, excluding its current top)
  - `ItemSheet({ item, layout, onSavePort(number, label | null, poe): Promise<void> /* rejects when refused */, onMove(topU), onRemove(), onClose })`

- [ ] **Step 1: Write the failing helper tests**

Create `ui/tests/rackTargets.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import type { RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { freeTopUs, isFree } from '../src/racks/rackTargets'
import { frontPanel, portRows, poeCapable, poeTicked } from '../src/racks/ports'

const item = (id: string, topU: number, heightU = 1, extra: Partial<RackItemResponse> = {}): RackItemResponse => ({
  id, rackDeviceTypeId: 't', kind: 'Switch', model: 'Test', partNumber: 'T', label: id, topU, heightU,
  portGroups: [], poeBudgetW: null, ports: [], ...extra,
})

const layout = (items: RackItemResponse[], heightU = 6): RackLayoutResponse =>
  ({ rackId: 'r', heightU, freeU: 0, layoutVersion: 1, items })

describe('rack targets', () => {
  it('a U is free inside the cabinet and off every other item', () => {
    const l = layout([item('a', 6), item('b', 4, 2)])
    expect(isFree(l, 'a', 5, 1)).toBe(true)
    expect(isFree(l, 'a', 4, 1)).toBe(false)
    expect(isFree(l, 'a', 3, 1)).toBe(false)
    expect(isFree(l, 'a', 7, 1)).toBe(false)
    expect(isFree(l, 'b', 5, 2)).toBe(true)
  })

  it('lists every other free top U, highest first', () => {
    const l = layout([item('a', 6), item('b', 4, 2)])
    expect(freeTopUs(l, l.items[0])).toEqual([5, 2, 1])
    expect(freeTopUs(l, l.items[1])).toEqual([2])
  })
})

describe('ports', () => {
  const sw = item('sw', 6, 1, {
    portGroups: [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }],
    ports: [{ number: 2, label: 'Lounge TV', poe: true }],
  })

  it('makes a row for every port, filled from what is stored', () => {
    const rows = portRows(sw)
    expect(rows.map(r => r.number)).toEqual([1, 2, 3, 4, 5, 6])
    expect(rows[1]).toMatchObject({ label: 'Lounge TV', poe: true, group: { name: '1G' } })
    expect(rows[0]).toMatchObject({ label: '', poe: false })
  })

  it('counts PoE-capable and ticked ports', () => {
    expect(poeCapable(sw)).toBe(4)
    expect(poeTicked(sw)).toBe(1)
  })

  it('lays a switch out odd above even with uplinks apart, and a patch panel in one row', () => {
    expect(frontPanel(sw)).toEqual({ top: [1, 3], bottom: [2, 4], uplinks: [5, 6] })
    const panel = { ...sw, kind: 'PatchPanel' as const, portGroups: [{ name: 'RJ45', from: 1, to: 4, poeTick: true }] }
    expect(frontPanel(panel)).toEqual({ top: [1, 2, 3, 4], bottom: [], uplinks: [] })
  })
})
```

Run: `npx vitest run tests/rackTargets.test.ts` — Expected: FAIL, modules missing.

- [ ] **Step 2: Write the helpers**

Create `ui/src/racks/rackTargets.ts`:

```ts
import type { RackItemResponse, RackLayoutResponse } from '../api/types'

/**
 * Whether an item could sit with its top at `topU`: inside the cabinet and off
 * every other item. Feedback only — the drag's red ghost, the "Move to" list.
 * The server checks again and has the last word, as it does for panels.
 */
export function isFree(layout: RackLayoutResponse, itemId: string, topU: number, heightU: number): boolean {
  const bottomU = topU - heightU + 1
  if (topU > layout.heightU || bottomU < 1) return false
  return layout.items.every(other =>
    other.id === itemId || other.topU < bottomU || other.topU - other.heightU + 1 > topU)
}

/** Every other top U the item could move to, highest first. */
export function freeTopUs(layout: RackLayoutResponse, item: RackItemResponse): number[] {
  const tops: number[] = []
  for (let u = layout.heightU; u >= item.heightU; u--) {
    if (u !== item.topU && isFree(layout, item.id, u, item.heightU)) tops.push(u)
  }
  return tops
}
```

Create `ui/src/racks/ports.ts`:

```ts
import type { PortGroup, RackItemResponse } from '../api/types'

export interface PortRowData {
  number: number
  group: PortGroup
  label: string
  poe: boolean
}

const numbers = (group: PortGroup) => Array.from({ length: group.to - group.from + 1 }, (_, i) => group.from + i)

/** A row for every port the item has, filled in from the few the server stores. */
export function portRows(item: RackItemResponse): PortRowData[] {
  const stored = new Map(item.ports.map(p => [p.number, p]))
  return item.portGroups.flatMap(group => numbers(group).map(number => {
    const port = stored.get(number)
    return { number, group, label: port?.label ?? '', poe: port?.poe ?? false }
  }))
}

export const poeCapable = (item: RackItemResponse) =>
  item.portGroups.filter(g => g.poeTick).reduce((n, g) => n + g.to - g.from + 1, 0)

export const poeTicked = (item: RackItemResponse) => item.ports.filter(p => p.poe).length

/**
 * The ports as they sit on the real front panel: a switch's copper ports odd
 * on the top row and even below, its uplinks apart on the right; a patch
 * panel's in one row. Groups without the PoE tick are the uplinks.
 */
export function frontPanel(item: RackItemResponse): { top: number[]; bottom: number[]; uplinks: number[] } {
  const copper = item.portGroups.filter(g => g.poeTick).flatMap(numbers)
  const uplinks = item.portGroups.filter(g => !g.poeTick).flatMap(numbers)
  if (item.kind === 'PatchPanel') return { top: copper, bottom: [], uplinks }
  return { top: copper.filter(n => n % 2 === 1), bottom: copper.filter(n => n % 2 === 0), uplinks }
}
```

Run: `npx vitest run tests/rackTargets.test.ts` — Expected: PASS.

- [ ] **Step 3: Write the failing item sheet tests**

Create `ui/tests/ItemSheet.test.tsx`:

```tsx
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { ItemSheet } from '../src/racks/ItemSheet'

const sw: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 't48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, poeBudgetW: 720,
  portGroups: [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }],
  ports: [{ number: 3, label: 'Office', poe: false }],
}
const layout: RackLayoutResponse = {
  rackId: 'r1', heightU: 12, freeU: 10, layoutVersion: 3,
  items: [sw, { ...sw, id: 'i2', label: 'Patch panel 1', topU: 11, ports: [] }],
}

function show(onSavePort = vi.fn(async () => {}), extra: Partial<Parameters<typeof ItemSheet>[0]> = {}) {
  const onMove = vi.fn()
  const onRemove = vi.fn()
  render(<ItemSheet item={sw} layout={layout} onSavePort={onSavePort} onMove={onMove} onRemove={onRemove} onClose={() => {}} {...extra} />)
  return { onSavePort, onMove, onRemove }
}

describe('ItemSheet', () => {
  it('groups the rows by port group, and an SFP+ row offers no PoE tick and says why', () => {
    show()

    expect(screen.getByRole('heading', { name: /1G · ports 1–4/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /SFP\+ · ports 5–6 \(no PoE\)/ })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Port 1 PoE' })).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Port 5 PoE' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Port 3 label')).toHaveValue('Office')
  })

  it('Return saves the label and moves straight on to the next port', async () => {
    const { onSavePort } = show()

    await userEvent.type(screen.getByLabelText('Port 1 label'), 'Kitchen AP{Enter}')

    expect(onSavePort).toHaveBeenCalledWith(1, 'Kitchen AP', false)
    expect(screen.getByLabelText('Port 2 label')).toHaveFocus()
    expect(onSavePort).toHaveBeenCalledTimes(1)
  })

  it('saves on leaving a field, once, and not again for the same text', async () => {
    const { onSavePort } = show()

    await userEvent.type(screen.getByLabelText('Port 2 label'), 'Lounge TV')
    await userEvent.tab()
    await userEvent.click(screen.getByLabelText('Port 2 label'))
    await userEvent.tab()

    expect(onSavePort).toHaveBeenCalledTimes(1)
    expect(onSavePort).toHaveBeenCalledWith(2, 'Lounge TV', false)
  })

  it('a PoE tick saves straight away, with whatever label has been typed', async () => {
    const { onSavePort } = show()

    await userEvent.type(screen.getByLabelText('Port 4 label'), 'Garden cam')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Port 4 PoE' }))

    expect(onSavePort).toHaveBeenLastCalledWith(4, 'Garden cam', true)
    expect(screen.getByRole('checkbox', { name: 'Port 4 PoE' })).toBeChecked()
  })

  it('keeps the typed label when the server refuses it, and Return tries the same text again', async () => {
    const onSavePort = vi.fn()
      .mockRejectedValueOnce(new Error('refused'))
      .mockResolvedValueOnce(undefined)
    show(onSavePort)

    const box = screen.getByLabelText('Port 1 label')
    await userEvent.type(box, 'Kitchen AP')
    await userEvent.tab()
    expect(box).toHaveValue('Kitchen AP')

    await userEvent.click(box)
    await userEvent.keyboard('{Enter}')

    expect(onSavePort).toHaveBeenCalledTimes(2)
    expect(onSavePort).toHaveBeenLastCalledWith(1, 'Kitchen AP', false)
  })

  it('unticks the box again when a PoE save is refused', async () => {
    show(vi.fn().mockRejectedValue(new Error('refused')))

    await userEvent.click(screen.getByRole('checkbox', { name: 'Port 2 PoE' }))

    expect(screen.getByRole('checkbox', { name: 'Port 2 PoE' })).not.toBeChecked()
  })

  it('tapping a port on the front panel jumps to its row', async () => {
    show()

    await userEvent.click(within(screen.getByTestId('front-panel')).getByText('4', { selector: '[data-port]' }))

    expect(screen.getByLabelText('Port 4 label')).toHaveFocus()
  })

  it('offers only free U to move to, and moves on a pick', async () => {
    const { onMove } = show()

    const select = screen.getByLabelText('Move to')
    const options = within(select).getAllByRole('option').map(o => o.textContent)
    expect(options[0]).toBe('Stays in U12')
    expect(options).not.toContain('U11')
    expect(options).toContain('U10')

    await userEvent.selectOptions(select, '10')
    expect(onMove).toHaveBeenCalledWith(10)
  })

  it('asks before removing, naming the labels that go with it', async () => {
    const { onRemove } = show()

    await userEvent.click(screen.getByRole('button', { name: 'Remove from rack' }))
    expect(screen.getByText('Remove Switch 1 and its 1 port label?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Yes, remove it' }))

    expect(onRemove).toHaveBeenCalled()
  })
})
```

Run: `npx vitest run tests/ItemSheet.test.tsx` — Expected: FAIL, module missing.

- [ ] **Step 4: Write the item sheet**

Create `ui/src/racks/ItemSheet.tsx`:

```tsx
import { useCallback, useRef, useState } from 'react'
import type { RackItemResponse, RackLayoutResponse } from '../api/types'
import { ConfirmButton } from '../components/ConfirmButton'
import { SelectField } from '../components/SelectField'
import { Sheet } from '../components/Sheet'
import { frontPanel, poeCapable, poeTicked, portRows } from './ports'
import { freeTopUs } from './rackTargets'

interface Props {
  item: RackItemResponse
  layout: RackLayoutResponse
  /** Saves one port. Rejects when the server refuses it, having said why. */
  onSavePort: (number: number, label: string | null, poe: boolean) => Promise<void>
  onMove: (topU: number) => void
  onRemove: () => void
  onClose: () => void
}

/**
 * Where a rack item's ports are labelled. The rows do the work: one per port,
 * and Return saves and moves on to the next, so a 48-port switch is labelled
 * in one run with the keyboard up. The front panel above is the picture of
 * where each port is, and a way to jump to one.
 */
export function ItemSheet({ item, layout, onSavePort, onMove, onRemove, onClose }: Props) {
  const inputs = useRef(new Map<number, HTMLInputElement>())
  const rows = portRows(item)
  const capable = poeCapable(item)
  const labelled = item.ports.filter(p => p.label).length
  const positions = freeTopUs(layout, item)

  const register = useCallback((number: number, element: HTMLInputElement | null) => {
    if (element) inputs.current.set(number, element)
    else inputs.current.delete(number)
  }, [])

  const focusPort = (number: number) => inputs.current.get(number)?.focus()

  const focusAfter = (number: number) => {
    const next = rows.find(r => r.number > number)
    if (next) focusPort(next.number)
    else (document.activeElement as HTMLElement | null)?.blur()
  }

  const subtitle = [
    `U${item.topU}`,
    `${item.heightU}U`,
    capable > 0 ? `PoE ticked on ${poeTicked(item)} of ${capable} ports` : null,
    item.poeBudgetW ? `${item.poeBudgetW} W PoE budget` : null,
  ].filter(Boolean).join(' · ')

  const span = (top: number) => item.heightU === 1 ? `U${top}` : `U${top - item.heightU + 1}–U${top}`

  return (
    <Sheet title={`${item.label} · ${item.model}`} subtitle={subtitle} onClose={onClose}>
      {rows.length > 0 && <FrontPanel item={item} onPick={focusPort} />}

      {item.portGroups.map(group => (
        <section key={group.name} className="mt-4">
          <h3 className="font-semibold">
            {group.name} · ports {group.from}–{group.to}{group.poeTick ? '' : ' (no PoE)'}
          </h3>
          {!group.poeTick && <p className="text-sm text-ink-2">{group.name} ports carry data only, so there is no PoE tick.</p>}
          <ul>
            {rows.filter(r => r.group === group).map(row => (
              <PortRow
                key={row.number}
                number={row.number}
                poeOffered={group.poeTick}
                initialLabel={row.label}
                initialPoe={row.poe}
                register={register}
                onSave={(label, poe) => onSavePort(row.number, label, poe)}
                onNext={() => focusAfter(row.number)}
              />
            ))}
          </ul>
        </section>
      ))}

      <div className="mt-6 space-y-4 border-t border-rule pt-4">
        <SelectField
          label="Move to"
          value=""
          onChange={value => { if (value) onMove(Number(value)) }}
          options={[
            { value: '', label: `Stays in ${span(item.topU)}` },
            ...positions.map(u => ({ value: String(u), label: span(u) })),
          ]}
          hint={positions.length === 0 ? 'There is no free space to move it to.' : undefined}
        />
        <ConfirmButton
          label="Remove from rack"
          question={`Remove ${item.label}${labelled > 0 ? ` and its ${labelled} port ${labelled === 1 ? 'label' : 'labels'}` : ''}?`}
          confirmLabel="Yes, remove it"
          onConfirm={onRemove}
        />
      </div>
    </Sheet>
  )
}

/**
 * A picture, not a set of controls: the cells are smaller than a thumb, so
 * they are hidden from assistive tech and a tap only jumps to the port's row,
 * which is where the real controls are.
 */
function FrontPanel({ item, onPick }: { item: RackItemResponse; onPick: (number: number) => void }) {
  const { top, bottom, uplinks } = frontPanel(item)
  const stored = new Map(item.ports.map(p => [p.number, p]))

  const cell = (number: number) => {
    const port = stored.get(number)
    return (
      <span
        key={number}
        data-port={number}
        className={`relative flex h-6 min-w-6 items-center justify-center rounded-sm border text-[10px] font-semibold ${
          port?.label ? 'border-ink bg-ink text-paper' : 'border-ink-2 bg-sheet text-ink-2'}`}
      >
        {number}
        {port?.poe && <span className="absolute -top-1 -right-1 size-2 rounded-full bg-accent" />}
      </span>
    )
  }

  return (
    <div
      aria-hidden="true"
      data-testid="front-panel"
      className="flex gap-3 overflow-x-auto rounded-lg border border-rule bg-rail p-2"
      onClick={e => {
        const port = (e.target as HTMLElement).closest('[data-port]')?.getAttribute('data-port')
        if (port) onPick(Number(port))
      }}
    >
      <div className="space-y-1">
        <div className="flex gap-1">{top.map(cell)}</div>
        {bottom.length > 0 && <div className="flex gap-1">{bottom.map(cell)}</div>}
      </div>
      {uplinks.length > 0 && <div className="grid grid-flow-col grid-rows-2 gap-1">{uplinks.map(cell)}</div>}
    </div>
  )
}

function PortRow({ number, poeOffered, initialLabel, initialPoe, register, onSave, onNext }: {
  number: number
  poeOffered: boolean
  initialLabel: string
  initialPoe: boolean
  register: (number: number, element: HTMLInputElement | null) => void
  onSave: (label: string | null, poe: boolean) => Promise<void>
  onNext: () => void
}) {
  const [label, setLabel] = useState(initialLabel)
  const [poe, setPoe] = useState(initialPoe)

  // What the server has, recorded only once a save succeeds: a refused save
  // must leave the same text sendable again.
  const saved = useRef({ label: initialLabel.trim(), poe: initialPoe })
  // The save on its way, if any. The next one waits for it and compares
  // against what it stored, so a PoE tick straight after typing — which blurs
  // the box and saves the label first — sends the pair once more, with both.
  const inFlight = useRef<Promise<boolean> | null>(null)
  // Return saves and moves on; the blur that the move causes must not send
  // the pair a second time.
  const skipBlur = useRef(false)

  async function save(nextLabel: string, nextPoe: boolean): Promise<boolean> {
    if (inFlight.current) await inFlight.current

    const trimmed = nextLabel.trim()
    if (trimmed === saved.current.label && nextPoe === saved.current.poe) return true

    const attempt = onSave(trimmed || null, nextPoe).then(
      () => { saved.current = { label: trimmed, poe: nextPoe }; return true },
      () => false,
    )
    inFlight.current = attempt
    try {
      return await attempt
    } finally {
      if (inFlight.current === attempt) inFlight.current = null
    }
  }

  return (
    <li className="flex items-center gap-3 py-1">
      <label className="flex min-w-0 flex-1 items-center gap-3">
        <span className="w-8 shrink-0 text-right font-semibold tabular-nums">{number}</span>
        <input
          ref={element => register(number, element)}
          aria-label={`Port ${number} label`}
          value={label}
          maxLength={60}
          enterKeyHint="next"
          autoCapitalize="words"
          autoComplete="off"
          onChange={e => setLabel(e.target.value)}
          onBlur={() => {
            if (skipBlur.current) {
              skipBlur.current = false
              return
            }
            void save(label, poe)
          }}
          onKeyDown={e => {
            if (e.key !== 'Enter') return
            e.preventDefault()
            skipBlur.current = true
            void save(label, poe)
            onNext()
          }}
          className="min-h-11 w-full min-w-0 rounded-lg border border-rule bg-sheet px-3"
        />
      </label>
      {poeOffered && (
        <label className="flex min-h-11 shrink-0 items-center gap-2">
          <input
            type="checkbox"
            className="checkbox"
            aria-label={`Port ${number} PoE`}
            checked={poe}
            onChange={async e => {
              const next = e.target.checked
              setPoe(next)
              if (!(await save(label, next))) setPoe(!next)
            }}
          />
          <span aria-hidden="true">PoE</span>
        </label>
      )}
    </li>
  )
}
```

- [ ] **Step 5: Run the tests**

Run: `npx vitest run tests/ItemSheet.test.tsx tests/rackTargets.test.ts` — Expected: PASS. Then `npm test; echo $?` → 0, `npm run build` → 0, `npm run lint` → 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add ui/src/racks ui/tests/ItemSheet.test.tsx ui/tests/rackTargets.test.ts
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Label a rack item's ports in one run, with Return moving on

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: The rack screen

**Files:**
- Create: `ui/src/racks/rackGeometry.ts`
- Create: `ui/src/racks/useRackDrag.ts`
- Create: `ui/src/racks/RackDrawing.tsx`
- Create: `ui/src/racks/AddItemSheet.tsx`
- Create: `ui/src/racks/RackSettings.tsx`
- Create: `ui/src/routes/RackPage.tsx`
- Modify: `ui/src/panel/IssueButton.tsx`, `ui/tests/IssueButton.test.tsx`
- Modify: `ui/src/routes/PanelPage.tsx`
- Modify: `ui/src/routes/BomPage.tsx`
- Modify: `ui/src/App.tsx`
- Test: `ui/tests/rackGeometry.test.ts`, `ui/tests/RackPage.test.tsx`, `ui/tests/IssueButton.test.tsx`, `ui/tests/BomPage.test.tsx`

**Interfaces:**
- Consumes: Tasks 8–10.
- Produces:
  - `rackGeometry.ts`: `U_PX = 56`, `GUTTER_PX = 44`, `RACK_PX = 296`, `ITEM_INSET = 4`, `uTopY(u, heightU)`, `itemRect(item, rackHeightU)`, `drawingSize(heightU)`, `yToTopU(y, heightU, rackHeightU)`
  - `useRackDrag({ layout, onCommit(itemId, topU), toPoint })` → `{ dragging: RackDrag | null, bind }`
  - `RackDrawing({ layout, selectedId, onSelect, dragging })`, `interface RackDrag { itemId: string; topU: number; valid: boolean }`
  - `IssueButton({ kind: 'submain' | 'rack', ownerId, layoutVersion, syncKey? })` (replaces the `submainId` prop)
  - Routes `/racks/:rackId` and `/racks/:rackId/bom`

- [ ] **Step 1: Write the failing geometry test**

Create `ui/tests/rackGeometry.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { GUTTER_PX, ITEM_INSET, U_PX, drawingSize, itemRect, uTopY, yToTopU } from '../src/racks/rackGeometry'

describe('rack geometry', () => {
  it('puts U1 at the bottom of the drawing', () => {
    expect(uTopY(12, 12)).toBe(0)
    expect(uTopY(1, 12)).toBe(11 * U_PX)
    expect(drawingSize(12).height).toBe(12 * U_PX)
  })

  it('draws an item from its top U down, inset so neighbours read as two', () => {
    expect(itemRect({ topU: 11, heightU: 2 }, 12)).toEqual({
      x: GUTTER_PX + ITEM_INSET, y: U_PX + ITEM_INSET, width: expect.any(Number), height: 2 * U_PX - 2 * ITEM_INSET,
    })
  })

  it('turns a pointer y into the top U it would drop on, clamped to the cabinet', () => {
    expect(yToTopU(U_PX / 2, 1, 12)).toBe(12)
    expect(yToTopU(U_PX * 1.5, 1, 12)).toBe(11)
    expect(yToTopU(-500, 1, 12)).toBe(12)
    expect(yToTopU(99999, 2, 12)).toBe(2)
  })
})
```

Run: `npx vitest run tests/rackGeometry.test.ts` — Expected: FAIL.

- [ ] **Step 2: Write geometry, drag adapter and drawing**

Create `ui/src/racks/rackGeometry.ts`:

```ts
/**
 * One rack unit on screen at the drawing's own size. A 1U item, once its
 * inset is taken off, is a 48px target under a thumb.
 */
export const U_PX = 56

/** Room on the left for the U numbers. */
export const GUTTER_PX = 44

/** The rack's own width: beside the gutter it fits a 375px phone without zooming. */
export const RACK_PX = 296

/** Space between an item and the U lines, so two neighbours read as two. */
export const ITEM_INSET = 4

/** The y of the top edge of U `u` in a rack `heightU` tall. U1 is at the bottom. */
export const uTopY = (u: number, heightU: number) => (heightU - u) * U_PX

export function itemRect(item: { topU: number; heightU: number }, rackHeightU: number) {
  return {
    x: GUTTER_PX + ITEM_INSET,
    y: uTopY(item.topU, rackHeightU) + ITEM_INSET,
    width: RACK_PX - ITEM_INSET * 2,
    height: item.heightU * U_PX - ITEM_INSET * 2,
  }
}

export const drawingSize = (heightU: number) => ({ width: GUTTER_PX + RACK_PX, height: heightU * U_PX })

/** The top U an item `heightU` tall would take with its middle at y, clamped inside the cabinet. */
export function yToTopU(y: number, heightU: number, rackHeightU: number) {
  const topEdge = y - (heightU * U_PX) / 2
  const topU = rackHeightU - Math.round(topEdge / U_PX)
  return Math.min(Math.max(topU, heightU), rackHeightU)
}
```

Create `ui/src/racks/useRackDrag.ts`:

```ts
import type { RackLayoutResponse } from '../api/types'
import { usePressDrag } from '../drag/usePressDrag'
import type { RackDrag } from './RackDrawing'
import { yToTopU } from './rackGeometry'
import { isFree } from './rackTargets'

interface Options {
  layout: RackLayoutResponse
  onCommit: (itemId: string, topU: number) => void
  /** Converts client coordinates into drawing coordinates, undoing the drawing's scale. */
  toPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

/** The panel's press-and-drag gesture, in the rack's terms: a top U. */
export function useRackDrag({ layout, onCommit, toPoint }: Options) {
  const find = (id: string) => layout.items.find(i => i.id === id)

  const { dragging, bind } = usePressDrag<number>({
    idAttribute: 'data-item-id',
    start: id => find(id)?.topU ?? null,
    locate: (id, point) => {
      const item = find(id)
      if (!item) return null
      const topU = yToTopU(point.y, item.heightU, layout.heightU)
      return { target: topU, valid: isFree(layout, id, topU, item.heightU) }
    },
    exists: id => find(id) !== undefined,
    // A drop back where it started is not a move, and is not sent.
    onCommit: (id, topU) => { if (find(id)?.topU !== topU) onCommit(id, topU) },
    toPoint,
  })

  const state: RackDrag | null = dragging ? { itemId: dragging.id, topU: dragging.target, valid: dragging.valid } : null
  return { dragging: state, bind }
}
```

Create `ui/src/racks/RackDrawing.tsx`:

```tsx
import type { RackItemResponse, RackLayoutResponse } from '../api/types'
import { frontPanel } from './ports'
import { GUTTER_PX, RACK_PX, U_PX, drawingSize, itemRect, uTopY } from './rackGeometry'

export interface RackDrag {
  itemId: string
  topU: number
  valid: boolean
}

interface Props {
  layout: RackLayoutResponse
  selectedId: string | null
  onSelect: (itemId: string) => void
  dragging?: RackDrag | null
}

function describe(item: RackItemResponse) {
  const total = item.portGroups.reduce((n, g) => n + g.to - g.from + 1, 0)
  const labelled = item.ports.filter(p => p.label).length
  return `${item.label}, ${item.model}, U${item.topU}${total ? `, ${labelled} of ${total} ports labelled` : ''}`
}

/**
 * The rack top to bottom, every U drawn and numbered on the left. Drawn
 * exactly where the server put each item; a dragged item is drawn where the
 * pointer would drop it, dashed and marked when it would not fit.
 */
export function RackDrawing({ layout, selectedId, onSelect, dragging }: Props) {
  const size = drawingSize(layout.heightU)
  // The dragged item is drawn last, so its ghost sits on top of anything it is
  // over: an invalid drop has to be seen, not hidden under the item it hits.
  const items = [...layout.items].sort((a, b) =>
    Number(a.id === dragging?.itemId) - Number(b.id === dragging?.itemId))

  return (
    <svg
      viewBox={`0 0 ${size.width} ${size.height}`}
      width="100%"
      // Not role="img": that would hide every item button inside it from VoiceOver.
      role="group"
      aria-label={`Rack, ${layout.heightU}U, ${layout.freeU}U free`}
      className="block select-none"
    >
      {Array.from({ length: layout.heightU }, (_, i) => layout.heightU - i).map(u => (
        <g key={u}>
          <rect x={GUTTER_PX} y={uTopY(u, layout.heightU)} width={RACK_PX} height={U_PX} className="fill-rail stroke-rule" strokeWidth={1} />
          <text
            x={GUTTER_PX - 8}
            y={uTopY(u, layout.heightU) + U_PX / 2}
            textAnchor="end"
            dominantBaseline="middle"
            className="fill-ink-2 text-[12px] font-medium"
          >
            U{u}
          </text>
        </g>
      ))}

      {items.map(item => {
        const drag = dragging?.itemId === item.id ? dragging : null
        const rect = itemRect(drag ? { ...item, topU: drag.topU } : item, layout.heightU)
        const invalid = drag !== null && !drag.valid
        const selected = item.id === selectedId

        return (
          <g
            key={item.id}
            role="button"
            tabIndex={0}
            data-item-id={item.id}
            aria-label={describe(item)}
            aria-pressed={selected}
            onClick={() => onSelect(item.id)}
            onKeyDown={e => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                onSelect(item.id)
              }
            }}
            className="cursor-pointer"
            opacity={drag ? 0.9 : 1}
          >
            <rect
              {...rect}
              rx={4}
              className={invalid ? 'fill-danger-soft stroke-danger' : 'fill-sheet stroke-ink'}
              strokeWidth={selected ? 3 : 1.5}
              strokeDasharray={invalid ? '6 4' : undefined}
            />
            <text x={rect.x + 8} y={rect.y + 19} className="fill-ink text-[13px] font-bold">{item.label}</text>
            {/* Colour is never the only sign: an invalid drop says so in words. */}
            <text x={rect.x + 8} y={rect.y + 35} className={invalid ? 'fill-danger text-[11px] font-semibold' : 'fill-ink-2 text-[11px]'}>
              {invalid ? 'No room here' : item.model.replace(/^Ubiquiti /, '')}
            </text>
            <PortStrip item={item} x={rect.x + 150} y={rect.y + 10} width={rect.width - 158} />
          </g>
        )
      })}
    </svg>
  )
}

/** The front panel at a glance: filled once labelled, an accent dot for PoE. The detail is in the item's sheet. */
function PortStrip({ item, x, y, width }: { item: RackItemResponse; x: number; y: number; width: number }) {
  const { top, bottom, uplinks } = frontPanel(item)
  const copperColumns = Math.max(top.length, bottom.length)
  const columns = copperColumns + (uplinks.length ? Math.ceil(uplinks.length / 2) + 1 : 0)
  if (columns === 0) return null

  const cell = Math.min(10, width / columns)
  const stored = new Map(item.ports.map(p => [p.number, p]))

  const square = (number: number, cx: number, cy: number) => {
    const port = stored.get(number)
    return (
      <g key={number}>
        <rect x={cx} y={cy} width={cell - 1.5} height={cell - 1.5} className={port?.label ? 'fill-ink' : 'fill-sheet stroke-ink-2'} strokeWidth={0.75} />
        {port?.poe && <circle cx={cx + cell - 3} cy={cy + 1.5} r={1.6} className="fill-accent" />}
      </g>
    )
  }

  return (
    <g aria-hidden="true">
      {top.map((n, i) => square(n, x + i * cell, y))}
      {bottom.map((n, i) => square(n, x + i * cell, y + cell))}
      {uplinks.map((n, i) => square(n, x + (copperColumns + 1 + Math.floor(i / 2)) * cell, y + (i % 2) * cell))}
    </g>
  )
}
```

Run: `npx vitest run tests/rackGeometry.test.ts` — Expected: PASS.

- [ ] **Step 3: Generalise IssueButton for racks**

In `ui/src/panel/IssueButton.tsx`, replace the `Props` interface and the first lines of the component so the paths come from the kind:

```tsx
interface Props {
  kind: 'submain' | 'rack'
  /** The submain's or the rack's id. */
  ownerId: string
  /** The layout as drawn right now; a change here means the list is asked again. */
  layoutVersion: number
  /**
   * Bumped after every save that could change what the PDF prints but does
   * not move anything — a circuit name, a room, a port label. Positions bump
   * layoutVersion; these do not, so without it an edit would leave the old
   * PDF offered as current.
   */
  syncKey?: number
}

/** Panels and racks issue the same way, from different addresses. */
const routes = {
  submain: {
    revisions: (id: string) => `/submains/${id}/revisions`,
    pdf: (revisionId: string) => `/api/revisions/${revisionId}/export/pdf`,
    changed: 'The panel has changed since that PDF was issued.',
  },
  rack: {
    revisions: (id: string) => `/racks/${id}/revisions`,
    pdf: (revisionId: string) => `/api/rack-revisions/${revisionId}/export/pdf`,
    changed: 'The rack has changed since that PDF was issued.',
  },
} as const
```

Then, in the component: change the signature to `export function IssueButton({ kind, ownerId, layoutVersion, syncKey = 0 }: Props)`, add `const route = routes[kind]` as its first line, and replace:
- `` `/submains/${submainId}/revisions` `` (both the GET and the POST) with `route.revisions(ownerId)`;
- the effect's dependency `submainId` with `kind, ownerId`;
- `` href={`/api/revisions/${latest.id}/export/pdf`} `` with `href={route.pdf(latest.id)}`;
- `'The panel has changed since that PDF was issued.'` with `{route.changed}`.

Update the doc comment's "Issues a revision" paragraph to say "a panel or rack revision". In `ui/src/routes/PanelPage.tsx`, change the use to `<IssueButton kind="submain" ownerId={submainId} layoutVersion={design.layoutVersion} syncKey={loads} />`.

In `ui/tests/IssueButton.test.tsx`, change `show` to `render(<><IssueButton kind="submain" ownerId="s1" layoutVersion={4} syncKey={syncKey} /><StatusRegion /></>)` and add:

```tsx
  it('issues a rack revision from the rack\'s address and links its pdf', async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => init?.method === 'POST'
      ? json({ id: 'rr1', rackId: 'r1', number: 1, layoutVersion: 2, issuedAt: '2026-01-01', issuedBy: 'a', current: true })
      : json([]))
    vi.stubGlobal('fetch', fetchMock)

    render(<><IssueButton kind="rack" ownerId="r1" layoutVersion={2} /><StatusRegion /></>)
    await userEvent.click(await screen.findByRole('button', { name: /issue pdf/i }))

    expect(await screen.findByRole('link', { name: /open pdf/i })).toHaveAttribute('href', '/api/rack-revisions/rr1/export/pdf')
    expect(fetchMock.mock.calls.map(c => c[0])).toContain('/api/racks/r1/revisions')
  })
```

Run: `npx vitest run tests/IssueButton.test.tsx tests/PanelPage.test.tsx` — Expected: PASS.

- [ ] **Step 4: Say on the panel which racks a submain feeds, and give racks a BOM page**

In `ui/src/routes/PanelPage.tsx`, add `FedRack` to the type import, `Fragment` to the React import, and:

```tsx
  // The racks this submain feeds, a link each. A failure costs the line, not the panel.
  const [feeds, setFeeds] = useState<FedRack[]>([])
  useEffect(() => {
    api.get<FedRack[]>(`/submains/${submainId}/racks`)
      .then(list => setFeeds(Array.isArray(list) ? list : []))
      .catch(() => setFeeds([]))
  }, [submainId])
```

and render, as the first child inside the main content once the design has loaded (before the drawing's controls):

```tsx
        {feeds.length > 0 && (
          <p className="mb-3 text-ink-2">
            Feeds:{' '}
            {feeds.map((rack, i) => (
              <Fragment key={rack.id}>
                {i > 0 && ', '}
                <Link to={`/racks/${rack.id}`} className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
                  {rack.name}
                </Link>
              </Fragment>
            ))}
          </p>
        )}
```

In `ui/src/routes/BomPage.tsx`, take the rack id too:

```tsx
  const { projectId, submainId, rackId } = useParams()
  const scope = rackId ? `/racks/${rackId}` : submainId ? `/submains/${submainId}` : `/projects/${projectId}`
  const backTo = rackId ? `/racks/${rackId}` : submainId ? `/submains/${submainId}/panel` : `/projects/${projectId}`
  const owner = rackId ? 'rack' : submainId ? 'panel' : null
```

and use `owner` for the copy: subtitle `owner === 'rack' ? 'For this rack' : owner === 'panel' ? 'For this submain' : 'For the whole house, from each issued PDF'`; back label `owner === 'rack' ? 'Rack' : owner === 'panel' ? 'Panel' : 'House'`; empty-state action `owner ? \`Go to the ${owner}\` : 'Back to the house'`; and "The issued panel has no parts in it." becomes `` `The issued ${owner ?? 'drawings have'} no parts in ${owner ? 'it' : 'them'}.` `` — keep the existing wording for the panel case exactly as it is.

In `ui/tests/BomPage.test.tsx`, add a test that renders `/racks/r1/bom` with a stub returning `{ lines: [{ catalogueId: 'k', partNumber: 'T-KIT', description: 'Rack kit', quantity: 1, panelMounted: true }] }` for `/api/racks/r1/bom`, and asserts the line is listed, the CSV link is `/api/racks/r1/bom.csv`, and the back link says "Rack". Use the file's existing render helper style (`MemoryRouter` + `Routes` with a `/racks/:rackId/bom` route).

In `ui/src/App.tsx`, add `import { RackPage } from './routes/RackPage'` and:

```tsx
      <Route path="/racks/:rackId" element={<RackPage />} />
      <Route path="/racks/:rackId/bom" element={<BomPage />} />
```

- [ ] **Step 5: Write the failing rack screen tests**

Create `ui/tests/RackPage.test.tsx`:

```tsx
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { RackPage } from '../src/routes/RackPage'
import { StatusRegion } from '../src/components/StatusRegion'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const rack = {
  id: 'r1', projectId: 'p1', name: 'Comms cupboard', rackTypeId: 'c12', cabinet: 'Generic 12U wall cabinet', heightU: 12,
  usedU: 1, fedFromSubmainId: 's1', fedFromSubmainName: 'Ground floor', notes: null, layoutVersion: 3,
}
const groups = [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }]
const switch1: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 'sw48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, portGroups: groups, poeBudgetW: 720, ports: [],
}
const layout = (items: RackItemResponse[] = [switch1], layoutVersion = 3): RackLayoutResponse =>
  ({ rackId: 'r1', heightU: 12, freeU: 12 - items.length, layoutVersion, items })
const catalogue = [
  { id: 'sw48', manufacturer: 'Ubiquiti', model: 'USW-Pro-Max-48-PoE', partNumber: 'USW', kind: 'Switch', heightU: 1, portGroups: groups, poeBudgetW: 720, requiredAccessoryId: null, active: true, description: 'Ubiquiti USW-Pro-Max-48-PoE', portCount: 6 },
  { id: 'pp24', manufacturer: 'Generic', model: '24-port patch panel', partNumber: 'PP', kind: 'PatchPanel', heightU: 1, portGroups: [{ name: 'RJ45', from: 1, to: 24, poeTick: true }], poeBudgetW: null, requiredAccessoryId: null, active: true, description: 'Generic 24-port patch panel', portCount: 24 },
  { id: 'kit', manufacturer: 'Ubiquiti', model: 'Rack kit', partNumber: 'KIT', kind: 'Accessory', heightU: 0, portGroups: [], poeBudgetW: null, requiredAccessoryId: null, active: true, description: 'Ubiquiti Rack kit', portCount: 0 },
  { id: 'old', manufacturer: 'Ubiquiti', model: 'Retired switch', partNumber: 'OLD', kind: 'Switch', heightU: 1, portGroups: groups, poeBudgetW: null, requiredAccessoryId: null, active: false, description: 'Ubiquiti Retired switch', portCount: 6 },
]

function stub(handlers: Partial<Record<string, (init?: RequestInit) => Response>> = {}, current: () => RackLayoutResponse = () => layout()) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${url.split('?')[0]}`
    if (handlers[key]) return handlers[key]!(init)
    if (url === '/api/racks/r1') return json(rack)
    if (url === '/api/racks/r1/layout') return json(current())
    if (url === '/api/catalogue/rack-device-types') return json(catalogue)
    if (url === '/api/catalogue/rack-types') return json([{ id: 'c12', heightU: 12, description: 'Generic 12U wall cabinet' }])
    return json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const show = () => render(
  <MemoryRouter initialEntries={['/racks/r1']}>
    <Routes><Route path="/racks/:rackId" element={<><RackPage /><StatusRegion /></>} /></Routes>
  </MemoryRouter>,
)

describe('RackPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('draws every U and each item where the server put it, with the cabinet and feed in the title block', async () => {
    stub()
    show()

    expect(await screen.findByRole('heading', { name: 'Comms cupboard' })).toBeInTheDocument()
    expect(screen.getByText('Generic 12U wall cabinet · fed from Ground floor')).toBeInTheDocument()
    const drawing = screen.getByRole('group', { name: 'Rack, 12U, 11U free' })
    expect(within(drawing).getByText('U12')).toBeInTheDocument()
    expect(within(drawing).getByText('U1')).toBeInTheDocument()
    expect(within(drawing).getByRole('button', { name: /^Switch 1, Ubiquiti USW-Pro-Max-48-PoE, U12/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Bill of materials' })).toHaveAttribute('href', '/racks/r1/bom')
  })

  it('opens an item\'s sheet on a tap', async () => {
    stub()
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))

    expect(screen.getByRole('dialog', { name: /Switch 1/ })).toBeInTheDocument()
    expect(screen.getByLabelText('Port 1 label')).toBeInTheDocument()
  })

  it('offers only active switches and patch panels, adds the one picked and redraws', async () => {
    const fetchMock = stub({
      'POST /api/racks/r1/items': () => json(layout([switch1, { ...switch1, id: 'i2', rackDeviceTypeId: 'pp24', kind: 'PatchPanel', label: 'Patch panel 1', topU: 11, model: 'Generic 24-port patch panel' }], 4), 201),
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add to rack' }))
    const sheet = screen.getByRole('dialog', { name: 'Add to rack' })
    expect(within(sheet).queryByText('Rack kit')).not.toBeInTheDocument()
    expect(within(sheet).queryByText('Retired switch')).not.toBeInTheDocument()
    await userEvent.click(within(sheet).getByRole('button', { name: /24-port patch panel/ }))

    expect(await screen.findByRole('button', { name: /^Patch panel 1/ })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Add to rack' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Patch panel 1 added in U11')
    const [, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body))).toEqual({ rackDeviceTypeId: 'pp24' })
  })

  it('shows a refusal in the sheet, in the server\'s words', async () => {
    stub({ 'POST /api/racks/r1/items': () => json({ message: 'No room in this rack: 0U free, and the switch needs 1U.' }, 422) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add to rack' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Add to rack' })).getByRole('button', { name: /USW-Pro-Max-48-PoE/ }))

    expect(await screen.findByText(/No room in this rack: 0U free/)).toBeInTheDocument()
  })

  it('invites a first item when the rack is empty and offers no PDF yet', async () => {
    stub({}, () => layout([]))
    show()

    expect(await screen.findByText('Nothing in this rack yet')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /issue pdf/i })).not.toBeInTheDocument()
  })

  it('moves an item from its sheet against the layout it was drawn from', async () => {
    const fetchMock = stub({ 'PATCH /api/rack-items/i1/position': () => json({ layoutVersion: 4 }) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Move to'), '10')

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/rack-items/i1/position')!
    expect(JSON.parse(String(init!.body))).toEqual({ topU: 10, basedOnLayoutVersion: 3 })
    expect(await screen.findByRole('status')).toHaveTextContent('Moved to U10')
  })

  it('reloads instead of failing when the rack changed elsewhere', async () => {
    let version = 3
    const fetchMock = stub(
      { 'PATCH /api/rack-items/i1/position': () => { version = 5; return json({ message: 'This rack changed since you loaded it.', currentLayoutVersion: 5 }, 409) } },
      () => layout([switch1], version),
    )
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Move to'), '10')

    expect(await screen.findByRole('status')).toHaveTextContent(/changed elsewhere/)
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/layout').length).toBeGreaterThan(1)
  })

  it('removes an item with the layout version it was drawn from', async () => {
    const fetchMock = stub({ 'DELETE /api/rack-items/i1': () => json(layout([], 4)) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Remove from rack' }))
    await userEvent.click(screen.getByRole('button', { name: 'Yes, remove it' }))

    expect(await screen.findByText('Nothing in this rack yet')).toBeInTheDocument()
    expect(fetchMock.mock.calls.map(c => c[0])).toContain('/api/rack-items/i1?basedOnLayoutVersion=3')
  })

  it('saves a port label and makes the PDF ask again whether it is current', async () => {
    const fetchMock = stub({ 'PUT /api/rack-items/i1/ports/1': () => json({ number: 1, label: 'Kitchen AP', poe: false }) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    const revisionsBefore = fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/revisions').length
    await userEvent.type(screen.getByLabelText('Port 1 label'), 'Kitchen AP{Enter}')

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/rack-items/i1/ports/1')!
    expect(JSON.parse(String(init!.body))).toEqual({ label: 'Kitchen AP', poe: false })
    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/revisions').length).toBeGreaterThan(revisionsBefore))
  })
})
```

Run: `npx vitest run tests/RackPage.test.tsx` — Expected: FAIL, `RackPage` missing.

- [ ] **Step 6: Write the add sheet, the settings and the page**

Create `ui/src/racks/AddItemSheet.tsx`:

```tsx
import { useState } from 'react'
import type { RackDeviceTypeResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Sheet } from '../components/Sheet'
import { Skeleton } from '../components/Skeleton'
import { useSingleFlight } from '../components/useSingleFlight'

interface Props {
  catalogue: RackDeviceTypeResponse[] | null
  catalogueError: string | null
  onRetryCatalogue: () => void
  freeU: number
  /** Adds the item: resolves to null when it went in, or to the reason it did not. */
  onAdd: (rackDeviceTypeId: string) => Promise<string | null>
  onClose: () => void
}

function summary(type: RackDeviceTypeResponse) {
  const poe = type.portGroups.filter(g => g.poeTick).reduce((n, g) => n + g.to - g.from + 1, 0)
  return `${type.heightU}U · ${type.portCount} ports${type.kind === 'Switch' && poe ? ` (${poe} PoE)` : ''}`
}

/** What can go in a rack, one tap each. The server decides where it goes: the top free U. */
export function AddItemSheet({ catalogue, catalogueError, onRetryCatalogue, freeU, onAdd, onClose }: Props) {
  const once = useSingleFlight()
  const [busy, setBusy] = useState<string | null>(null)
  const [refusal, setRefusal] = useState<string | null>(null)

  // Accessories are bought with the item that needs them, never placed; an
  // item retired from the catalogue stays where it is but is not offered.
  const placeable = (catalogue ?? []).filter(t => t.active && t.kind !== 'Accessory')

  // Two taps in one tick must add one item, not two.
  const add = (id: string) => once(async () => {
    setBusy(id)
    setRefusal(null)
    try {
      setRefusal(await onAdd(id))
    } finally {
      setBusy(null)
    }
  })

  return (
    <Sheet title="Add to rack" subtitle={`${freeU}U free · it goes in the top free U`} onClose={onClose}>
      {catalogueError && <ErrorNote compact message={`Could not load the rack catalogue: ${catalogueError}`} onRetry={onRetryCatalogue} />}
      {!catalogue && !catalogueError && <Skeleton label="Loading the rack catalogue" rows={3} rowClass="h-14" />}
      {refusal && <div className="mb-3"><ErrorNote compact message={refusal} /></div>}

      <ul className="divide-y divide-rule">
        {placeable.map(type => (
          <li key={type.id} className="py-1">
            <Button
              variant="ghost"
              className="w-full justify-between text-left"
              disabled={busy !== null}
              onClick={() => void add(type.id)}
            >
              <span className="min-w-0 wrap-anywhere">
                <span className="block font-semibold">{type.model}</span>
                <span className="block text-sm font-normal text-ink-2">{summary(type)}</span>
              </span>
              <span aria-hidden="true" className="text-xl">{busy === type.id ? '…' : '+'}</span>
            </Button>
          </li>
        ))}
      </ul>
    </Sheet>
  )
}
```

Create `ui/src/racks/RackSettings.tsx`:

```tsx
import { useState } from 'react'
import { ApiError, api } from '../api/client'
import type { RackResponse, RackTypeResponse, SubmainResponse } from '../api/types'
import { Button } from '../components/Button'
import { Field } from '../components/Field'
import { SelectField } from '../components/SelectField'
import { announce } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'

interface Props {
  rack: RackResponse
  cabinets: RackTypeResponse[]
  submains: SubmainResponse[]
  onSaved: (rack: RackResponse) => void
}

/**
 * Name, cabinet and feed, folded away below the drawing as the panel's
 * install options are: set once, and rarely changed after. A shorter cabinet
 * is refused by the server with the U to clear, shown under the cabinet.
 */
export function RackSettings({ rack, cabinets, submains, onSaved }: Props) {
  const once = useSingleFlight()
  const [name, setName] = useState(rack.name)
  const [rackTypeId, setRackTypeId] = useState(rack.rackTypeId)
  const [fedFrom, setFedFrom] = useState(rack.fedFromSubmainId ?? '')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  const changed = name.trim() !== rack.name || rackTypeId !== rack.rackTypeId || (fedFrom || null) !== rack.fedFromSubmainId

  const save = () => once(async () => {
    setSaving(true)
    setFieldErrors({})
    try {
      const saved = await api.patch<RackResponse>(`/racks/${rack.id}`, {
        name: name.trim(), rackTypeId, fedFromSubmainId: fedFrom || null, notes: rack.notes,
      })
      onSaved(saved)
      announce('Rack settings saved', 'ok')
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      announce(Object.keys(error.fieldErrors).length > 0 ? 'Rack settings not saved — see the form' : error.message, 'error')
    } finally {
      setSaving(false)
    }
  })

  return (
    <details className="group rounded-lg border border-rule bg-sheet">
      <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between rounded-lg px-4 font-semibold active:bg-press">
        Rack settings
        <span aria-hidden="true" className="text-ink-2 transition-transform group-open:rotate-180 motion-reduce:transition-none">▾</span>
      </summary>
      <form className="space-y-4 px-4 pb-4" noValidate onSubmit={e => { e.preventDefault(); void save() }}>
        <Field label="Rack name" value={name} onChange={setName} errors={fieldErrors.name} autoCapitalize="words" autoComplete="off" />
        <SelectField
          label="Cabinet"
          value={rackTypeId}
          onChange={setRackTypeId}
          errors={fieldErrors.rackTypeId}
          options={cabinets.length > 0
            ? cabinets.map(c => ({ value: c.id, label: `${c.heightU}U · ${c.description}` }))
            : [{ value: rack.rackTypeId, label: rack.cabinet }]}
        />
        <SelectField
          label="Fed from"
          hint="The submain this rack's supply comes from. It changes nothing on that panel."
          value={fedFrom}
          onChange={setFedFrom}
          errors={fieldErrors.fedFromSubmainId}
          options={[{ value: '', label: 'Not fed from a submain' }, ...submains.map(s => ({ value: s.id, label: s.name }))]}
        />
        <Button type="submit" disabled={saving || !changed}>{saving ? 'Saving…' : 'Save settings'}</Button>
      </form>
    </details>
  )
}
```

Create `ui/src/routes/RackPage.tsx`:

```tsx
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type {
  RackDeviceTypeResponse, RackLayoutResponse, RackPortResponse, RackResponse, RackTypeResponse, SubmainResponse,
} from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'
import { announce } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'
import { IssueButton } from '../panel/IssueButton'
import { AddItemSheet } from '../racks/AddItemSheet'
import { ItemSheet } from '../racks/ItemSheet'
import { RackDrawing } from '../racks/RackDrawing'
import { RackSettings } from '../racks/RackSettings'
import { drawingSize } from '../racks/rackGeometry'
import { useRackDrag } from '../racks/useRackDrag'

const EMPTY_LAYOUT: RackLayoutResponse = { rackId: '', heightU: 0, freeU: 0, layoutVersion: 0, items: [] }

/**
 * A rack: the drawing first, the actions within thumb reach, the settings
 * folded away below. Like the panel screen, it draws what the server decided
 * and sends every change back for the server to decide again.
 */
export function RackPage() {
  const { rackId } = useParams()
  const [rack, setRack] = useState<RackResponse | null>(null)
  const [layout, setLayout] = useState<RackLayoutResponse | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [cabinets, setCabinets] = useState<RackTypeResponse[]>([])
  const [submains, setSubmains] = useState<SubmainResponse[]>([])
  const [catalogue, setCatalogue] = useState<RackDeviceTypeResponse[] | null>(null)
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  // Bumped by every port save, so the PDF's currency is asked again: a label
  // moves nothing, so the layout version alone would never notice it.
  const [syncKey, setSyncKey] = useState(0)
  const drawing = useRef<HTMLDivElement | null>(null)
  const once = useSingleFlight()

  const load = useCallback(() =>
    Promise.all([
      api.get<RackResponse>(`/racks/${rackId}`),
      api.get<RackLayoutResponse>(`/racks/${rackId}/layout`),
    ])
      .then(([r, l]) => {
        setRack(r)
        setLayout(l)
        setLoadError(null)
        // The settings need the cabinets and the house's submains. A failure
        // costs the settings form its choices, not the rack.
        api.get<RackTypeResponse[]>('/catalogue/rack-types').then(setCabinets).catch(() => setCabinets([]))
        api.get<SubmainResponse[]>(`/projects/${r.projectId}/submains`).then(setSubmains).catch(() => setSubmains([]))
      })
      .catch((e: ApiError) => setLoadError(e.message)), [rackId])

  // On its own, with its own retry: an empty "Add to rack" with no word of why reads as an empty catalogue.
  const loadCatalogue = useCallback(() =>
    api.get<RackDeviceTypeResponse[]>('/catalogue/rack-device-types')
      .then(list => { setCatalogue(list); setCatalogueError(null) })
      .catch((e: ApiError) => setCatalogueError(e.message)), [])

  useEffect(() => { void load() }, [load])
  useEffect(() => { void loadCatalogue() }, [loadCatalogue])

  /** The drawing is scaled to the screen's width; this undoes that scale. */
  const toPoint = useCallback((clientX: number, clientY: number) => {
    const box = drawing.current?.querySelector('svg')?.getBoundingClientRect()
    if (!box || !layout || box.width === 0) return { x: clientX, y: clientY }
    const scale = drawingSize(layout.heightU).width / box.width
    return { x: (clientX - box.left) * scale, y: (clientY - box.top) * scale }
  }, [layout])

  const commitMove = useCallback(async (itemId: string, topU: number) => {
    if (!layout) return
    const previous = layout

    // Optimistic: the one item moves; nothing is re-laid out here.
    setLayout({
      ...layout,
      items: layout.items.map(i => (i.id === itemId ? { ...i, topU } : i)).sort((a, b) => b.topU - a.topU),
    })

    try {
      const result = await api.patch<{ layoutVersion: number }>(`/rack-items/${itemId}/position`, {
        topU, basedOnLayoutVersion: previous.layoutVersion,
      })
      setLayout(l => (l ? { ...l, layoutVersion: result.layoutVersion } : l))
      announce(`Moved to U${topU}`, 'ok')
    } catch (e) {
      const error = e as ApiError
      setLayout(previous)
      if (error.conflict) {
        announce('This rack changed elsewhere — reloading')
        await load()
      } else {
        announce(`Not moved: ${error.message}`, 'error')
      }
    }
  }, [layout, load])

  const { dragging, bind } = useRackDrag({
    layout: layout ?? EMPTY_LAYOUT,
    onCommit: (id, topU) => { void commitMove(id, topU) },
    toPoint,
  })

  // Stable, so neither sheet is handed a new function on every re-render.
  const closeItem = useCallback(() => setSelectedId(null), [])
  const closeAdd = useCallback(() => setAdding(false), [])

  async function addItem(rackDeviceTypeId: string): Promise<string | null> {
    try {
      const next = await api.post<RackLayoutResponse>(`/racks/${rackId}/items`, { rackDeviceTypeId })
      const added = next.items.find(i => !layout?.items.some(old => old.id === i.id))
      setLayout(next)
      setAdding(false)
      announce(added ? `${added.label} added in U${added.topU}` : 'Added to the rack', 'ok')
      return null
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        await load()
        return 'This rack changed elsewhere. It has been reloaded — try again.'
      }
      return error.message
    }
  }

  async function savePort(itemId: string, number: number, label: string | null, poe: boolean) {
    try {
      const saved = await api.put<RackPortResponse>(`/rack-items/${itemId}/ports/${number}`, { label, poe })
      setLayout(l => l && {
        ...l,
        items: l.items.map(i => i.id !== itemId ? i : {
          ...i,
          ports: [...i.ports.filter(p => p.number !== number), ...(saved.label || saved.poe ? [saved] : [])]
            .sort((a, b) => a.number - b.number),
        }),
      })
      setSyncKey(n => n + 1)
    } catch (e) {
      const error = e as ApiError
      announce(`Port ${number} not saved: ${error.fieldErrors?.label?.[0] ?? error.fieldErrors?.poe?.[0] ?? error.message}`, 'error')
      throw e
    }
  }

  // A second "Yes, remove it" in the same tick would carry a stale version and
  // come back as a conflict; `once` drops it before it is sent.
  const removeItem = (itemId: string) => once(async () => {
    if (!layout) return
    try {
      const next = await api.del<RackLayoutResponse>(`/rack-items/${itemId}?basedOnLayoutVersion=${layout.layoutVersion}`)
      setSelectedId(null)
      setLayout(next)
      announce('Removed from the rack', 'ok')
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        announce('This rack changed elsewhere — reloading')
        await load()
      } else {
        announce(`Not removed: ${error.message}`, 'error')
      }
    }
  })

  const selected = layout?.items.find(i => i.id === selectedId) ?? null
  const subtitle = rack
    ? `${rack.cabinet} · ${rack.fedFromSubmainName ? `fed from ${rack.fedFromSubmainName}` : 'not fed from a submain'}`
    : undefined

  return (
    <AppShell
      title={rack?.name ?? 'Rack'}
      subtitle={subtitle}
      back={{ to: rack ? `/projects/${rack.projectId}` : '/', label: 'House' }}
      nav={rack ? (
        <Link to={`/racks/${rack.id}/bom`} className="inline-flex min-h-11 items-center rounded-lg px-2 font-medium text-ink-2 active:bg-press">
          Bill of materials
        </Link>
      ) : undefined}
      actions={layout && rack ? (
        <>
          <Button variant="secondary" onClick={() => setAdding(true)}>Add to rack</Button>
          {/* An empty rack is refused by the server; offering the button would only invite the refusal. */}
          {layout.items.length > 0 && <IssueButton kind="rack" ownerId={rack.id} layoutVersion={layout.layoutVersion} syncKey={syncKey} />}
        </>
      ) : undefined}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}
      {!layout && !loadError && <Skeleton label="Loading rack" rows={1} rowClass="h-96" />}

      {layout && rack && (
        <>
          {layout.items.length === 0 ? (
            <div className="mb-3">
              <EmptyState
                title="Nothing in this rack yet"
                hint="Add a switch or a patch panel and it goes in the top free U."
                action={<Button onClick={() => setAdding(true)}>Add to rack</Button>}
              />
            </div>
          ) : (
            <p className="mb-2 text-sm text-ink-2">Tap an item to label its ports. Drag it to move it — on a touchscreen, hold it first.</p>
          )}

          <div
            ref={el => { drawing.current = el; bind.ref(el) }}
            onPointerDown={bind.onPointerDown}
            onPointerMove={bind.onPointerMove}
            onPointerUp={bind.onPointerUp}
            onPointerCancel={bind.onPointerCancel}
            // No callout or selection on a long press: the press is how an item
            // is picked up, and the magnifier would steal it.
            style={{ WebkitTouchCallout: 'none', WebkitUserSelect: 'none', userSelect: 'none' } as React.CSSProperties}
            className="rounded-lg border border-rule bg-sheet p-2"
          >
            <RackDrawing layout={layout} selectedId={selectedId} onSelect={setSelectedId} dragging={dragging} />
          </div>
          <p className="mt-2 text-sm text-ink-2">{layout.freeU}U free of {layout.heightU}U. A filled port is labelled; a dot means PoE.</p>
          <div role="status" className="sr-only">
            {dragging && `U${dragging.topU}: ${dragging.valid ? 'free' : 'no room'}`}
          </div>

          <div className="mt-6">
            <RackSettings
              key={`${rack.name}|${rack.rackTypeId}|${rack.fedFromSubmainId}`}
              rack={rack}
              cabinets={cabinets}
              submains={submains}
              onSaved={saved => { setRack(saved); void load() }}
            />
          </div>
        </>
      )}

      {adding && layout && (
        <AddItemSheet
          catalogue={catalogue}
          catalogueError={catalogueError}
          onRetryCatalogue={() => void loadCatalogue()}
          freeU={layout.freeU}
          onAdd={addItem}
          onClose={closeAdd}
        />
      )}

      {selected && layout && (
        <ItemSheet
          key={selected.id}
          item={selected}
          layout={layout}
          onSavePort={(number, label, poe) => savePort(selected.id, number, label, poe)}
          onMove={topU => { void commitMove(selected.id, topU) }}
          onRemove={() => void removeItem(selected.id)}
          onClose={closeItem}
        />
      )}
    </AppShell>
  )
}
```

- [ ] **Step 7: Run every UI check**

Run: `npx vitest run tests/RackPage.test.tsx tests/BomPage.test.tsx tests/IssueButton.test.tsx tests/PanelPage.test.tsx` — Expected: PASS. Then `npm test; echo $?` → `0` with no `Errors` line, `npm run build; echo $?` → `0`, `npm run lint` → 0 warnings.

Then look at it for real (dev API restarted after Task 6): in WebKit with iPhone 15 emulation, open a rack, add a switch and a patch panel, label ports with Return, tick PoE, long-press-drag an item down two U, reload and confirm it stayed; check there is no horizontal overflow at 320/375/393/430 (`document.documentElement.scrollWidth === clientWidth`) on the rack screen with the item sheet open; and check dark mode. Fix what you find before committing.

- [ ] **Step 8: Commit**

```bash
git add ui/src ui/tests
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Draw a rack, add to it, move items by U and issue its PDF

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: End to end in WebKit, and the README

**Files:**
- Create: `ui/e2e/racks.spec.ts`
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above; `ui/e2e/fixtures.ts` (`API`), `ui/e2e/touch.ts` (`fingerDrag`).

- [ ] **Step 1: Write the e2e tests**

Create `ui/e2e/racks.spec.ts`:

```ts
import { expect, test, type APIRequestContext } from '@playwright/test'
import { API } from './fixtures'
import { fingerDrag } from './touch'

interface Seeded { projectId: string; rackId: string; items: { id: string; label: string; topU: number }[] }

/** A house with a 12U rack holding a 48-port switch and a patch panel, built straight through the API. */
async function seedRack(request: APIRequestContext): Promise<Seeded> {
  const cabinets = await (await request.get(`${API}/catalogue/rack-types`)).json()
  const types = await (await request.get(`${API}/catalogue/rack-device-types`)).json()
  const twelve = cabinets.find((c: { heightU: number }) => c.heightU === 12)
  const sw = types.find((t: { model: string }) => t.model === 'USW-Pro-Max-48-PoE')
  const panel = types.find((t: { kind: string }) => t.kind === 'PatchPanel')

  const project = await (await request.post(`${API}/projects`, { data: { name: `E2E rack ${Date.now()}-${Math.random()}` } })).json()
  const rack = await (await request.post(`${API}/projects/${project.id}/racks`, { data: { name: 'Comms cupboard', rackTypeId: twelve.id } })).json()
  await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: sw.id } })
  const layout = await (await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: panel.id } })).json()
  return { projectId: project.id, rackId: rack.id, items: layout.items }
}

const layoutOf = async (request: APIRequestContext, rackId: string) =>
  (await (await request.get(`${API}/racks/${rackId}/layout`)).json()) as {
    items: { id: string; label: string; topU: number; ports: { number: number; label: string | null; poe: boolean }[] }[]
  }

test('a rack is added from the house page, like a submain', async ({ page, request }) => {
  const project = await (await request.post(`${API}/projects`, { data: { name: `E2E house ${Date.now()}` } })).json()

  await page.goto(`/projects/${project.id}`)
  await page.getByRole('link', { name: 'Add rack' }).tap()
  await page.getByLabel('Rack name').fill('Comms cupboard')
  await page.getByRole('button', { name: 'Add rack' }).tap()

  await expect(page.getByRole('heading', { name: 'Comms cupboard' })).toBeVisible()
  await expect(page.getByText('Nothing in this rack yet')).toBeVisible()
})

test('ports are labelled in one run: Return saves and moves to the next port', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /^Switch 1/ }).tap()
  const saved = page.waitForResponse(r => r.request().method() === 'PUT' && r.url().endsWith('/ports/2'))
  await page.getByLabel('Port 1 label').fill('Kitchen AP')
  await page.keyboard.press('Enter')
  await expect(page.getByLabel('Port 2 label')).toBeFocused()
  await page.keyboard.type('Lounge TV')
  await page.keyboard.press('Enter')
  await saved
  const poeSaved = page.waitForResponse(r => r.request().method() === 'PUT' && r.url().endsWith('/ports/1'))
  await page.getByRole('checkbox', { name: 'Port 1 PoE' }).tap()
  await poeSaved

  const sw = (await layoutOf(request, rackId)).items.find(i => i.label === 'Switch 1')!
  expect(sw.ports).toEqual([
    { number: 1, label: 'Kitchen AP', poe: true },
    { number: 2, label: 'Lounge TV', poe: false },
  ])
})

test('an SFP+ port offers no PoE tick', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /^Switch 1/ }).tap()

  await expect(page.getByLabel('Port 49 label')).toBeVisible()
  await expect(page.getByRole('checkbox', { name: 'Port 49 PoE' })).toHaveCount(0)
})

test('an item can be dragged with a finger to another U and stays there after a reload', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  const panel = page.getByRole('button', { name: /^Patch panel 1/ })
  const box = (await panel.boundingBox())!
  // One U on screen is the item's drawn height plus its two insets; drop it three U lower.
  const unit = box.height * 56 / 48
  const saved = page.waitForResponse(r => r.request().method() === 'PATCH' && r.url().includes('/position'))

  const gesture = await fingerDrag(panel, { dx: 0, dy: unit * 3 })
  expect(gesture.movePrevented, 'the drag must stop Safari scrolling the page').toBe(true)
  expect((await saved).ok()).toBe(true)

  await page.reload()
  expect((await layoutOf(request, rackId)).items.find(i => i.label === 'Patch panel 1')!.topU).toBe(8)
})

test('issuing a rack PDF offers a plain link to a real PDF', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /issue pdf/i }).tap()
  const link = page.getByRole('link', { name: /open pdf \(revision 1\)/i })
  await expect(link).toBeVisible()

  const href = await link.getAttribute('href')
  const pdf = await request.get(`${API}${href!.replace(/^\/api/, '')}`)
  expect(pdf.headers()['content-type']).toContain('application/pdf')
  expect((await pdf.body()).subarray(0, 4).toString()).toBe('%PDF')
})

test.describe('with a mouse or trackpad', () => {
  test.use({ viewport: { width: 1280, height: 900 }, hasTouch: false, isMobile: false, deviceScaleFactor: 1 })

  test('a rack item can be dragged straight away, without holding it first', async ({ page, request }) => {
    const { rackId } = await seedRack(request)

    await page.goto(`/racks/${rackId}`)
    const panel = page.getByRole('button', { name: /^Patch panel 1/ })
    const box = (await panel.boundingBox())!
    const unit = box.height * 56 / 48
    const saved = page.waitForResponse(r => r.request().method() === 'PATCH' && r.url().includes('/position'))

    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.down()
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2 + unit * 2, { steps: 10 })
    await page.mouse.up()

    expect((await saved).ok()).toBe(true)
    await expect(page.getByRole('dialog'), 'a drop is not a click').toBeHidden()
    expect((await layoutOf(request, rackId)).items.find(i => i.label === 'Patch panel 1')!.topU).toBe(9)
  })
})
```

- [ ] **Step 2: Run the whole e2e suite**

Run: `cd ui && npx playwright test; echo $?` — Expected: `0`, every test passing (the existing 30 plus 6 new), on the suite's own stack. List `http://localhost:5020/projects` before and after: identical. If the drag test lands one U off, check the drawing's scale in `toPoint` against the item's measured height before changing the test's arithmetic.

- [ ] **Step 3: Update the README**

In `README.md`:
- In **Seeing it work**, add a step after the bill of materials:

  ```markdown
  8. **Add rack** on the house page. Pick a cabinet and, if it has one, the
     submain that feeds it. On the rack screen, **Add to rack** puts a switch or
     a patch panel in the top free U; drag an item to move it (on a touchscreen,
     hold it first). Tap an item to label its ports — Return moves to the next
     port — and tick PoE where the cable needs it. **Issue PDF revision** gives
     the rack drawing, a schedule of every port and the parts to order.
  ```

- In **The catalogue is not confirmed yet**, add: "The rack cabinets, the 24-port patch panel and the USW-Pro-Max-16-PoE's rack-mount kit carry ASSUMED part numbers, and the three switches' port splits and PoE budgets come from retailer listings rather than Ubiquiti's datasheets."
- In **Tests**, update the three counts to what `dotnet test`, `npm test` and `npx playwright test` now report.
- In **How it fits together**, add a paragraph:

  ```markdown
  **A rack sits beside the submains, not inside one.** The submain that feeds
  it is a link on the rack — blank, or changed, without the rack moving — and
  feeding a rack changes nothing on that panel. Racks are measured in rack
  units with U1 at the bottom; the server places each item in the top free U
  and checks every move, exactly as it decides every panel layout. Ports are
  numbered as UniFi numbers them, so a 48-port switch's uplinks are ports 49–52.
  ```

- [ ] **Step 4: Final verification**

From the repo root with the Testcontainers env vars: `dotnet test; echo $?` → `0`. From `ui/`: `npm test; echo $?` → `0` with no `Errors` line; `npm run build; echo $?` → `0`; `npm run lint` → 0 warnings; `npx playwright test; echo $?` → `0`. `git status` shows only intended changes; no temp files in `ui/` or the repo.

- [ ] **Step 5: Commit**

```bash
git add ui/e2e/racks.spec.ts README.md
git -c user.name="Sina Moradian" -c user.email="sina@jsmbars.co.uk" commit -m "Test racks end to end in WebKit, and document them

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
