# House Panel Designer — Outputs and Admin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn a finished design into the documents a job actually needs — a printable panel drawing with its circuit schedule, and a bill of materials — and let an admin maintain the catalogue and rules without a deploy.

**Architecture:** A design is *issued* as an immutable `PanelRevision` that embeds the ruleset and catalogue entries it used. PDFs and BOMs render from a revision, never from live state, so a drawing in someone's folder always means what it meant the day it was printed. Catalogue writes are role-gated and reuse the seeding entities.

**Tech Stack:** .NET 10, QuestPDF, EF Core 10, React 19 + Vite + TypeScript + Tailwind.

**Spec:** `docs/superpowers/specs/2026-09-17-house-panel-designer-design.md`
**Builds on:** `2026-09-17-backend-core.md` (merged), `2026-09-18-designer-ui.md` (branch `feat/designer-ui`)

## Global Constraints

- Widths stay in slot units (3 = 1 DIN module, `DinUnits.PerModule`). **Anything a person reads — PDF, BOM, screen — is in modules.** The backend-core diagnostics were already corrected for this; don't reintroduce slot units in user-facing text.
- A PDF or BOM is rendered from a `PanelRevision`, never from live tables. An endpoint that renders from live state is wrong.
- Catalogue and ruleset writes require the `catalogue-admin` role; reads stay open to any signed-in user.
- QuestPDF runs under its Community licence; set `QuestPDF.Settings.License = LicenseType.Community` once at startup.
- Plain xUnit `Assert`, central package management, `net10.0`.
- No new npm runtime dependency in `ui/` without a reason stated in the task that adds it.

## Three design decisions

**1. Two panel renderers, kept honest by a shared contract.**

The screen needs an interactive React SVG; the PDF needs a static one the server can produce without a browser. There is no honest way to share one implementation without either running a headless browser server-side (heavy, fragile) or trusting the client to upload its own drawing (a client that can draw anything can draw a lie into your records).

So there are two: `ui/src/panel/PanelSvg.tsx` and a new `PanelSvgRenderer` in C#. They are kept from drifting by:
- the geometry constants living in one documented place per side, mirrored the way `SLOT_UNITS_PER_MODULE` already mirrors `DinUnits.PerModule`;
- a golden-file test on the C# renderer, so a change to it has to be looked at;
- a test asserting both place a known device at the same x/y for the same layout.

**2. A revision embeds, rather than references, what it used.**

`PanelRevision.SnapshotJson` carries the layout, the circuits, the ruleset payload *as it was*, and the catalogue entries for every placed device. An admin re-pricing a part or editing a rule afterwards cannot retroactively change what an issued drawing said. This is the whole reason the table exists.

**3. An unpriced BOM says so.**

Every cost in the catalogue is currently `0.00`. A BOM that prints "Total: £0.00" looks like a priced document that came to nothing. Where any line has a zero unit cost, the BOM shows "—" for that line and the total reads "Not priced (n of m parts have no cost)". Printing a confident zero would be worse than printing nothing.

## File Structure

```
house-config/
  src/PubInvest.HouseConfig.Domain/
    Layout/PanelGeometry.cs             slot/row -> mm, shared by both renderers
  src/PubInvest.HouseConfig.Api/
    Revisions/RevisionSnapshot.cs       what an issued revision stores
    Revisions/RevisionService.cs        issue, load, list
    Rendering/PanelSvgRenderer.cs       layout -> static SVG string
    Documents/PanelDocument.cs          QuestPDF: drawing page + schedule pages
    Documents/BomCsv.cs                 BOM -> CSV
    Endpoints/RevisionEndpoints.cs
    Endpoints/ExportEndpoints.cs        pdf, bom json, bom csv
    Endpoints/CatalogueAdminEndpoints.cs
  ui/src/routes/BomPage.tsx
  ui/src/routes/CataloguePage.tsx
  ui/src/panel/IssueButton.tsx
  tests/...
```

---

### Task 1: Panel geometry shared by both renderers

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Layout/PanelGeometry.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/PanelGeometryTests.cs`

**Interfaces:**
- Consumes: `DinUnits`, `PlacedDevice`, `PanelLayout`.
- Produces: `PanelGeometry` with `SlotMm = 5.0`, `RowMm = 45.0`, `RowGapMm = 10.0`, `SlotToX(int)`, `RowToY(int)`, `DeviceBox(PlacedDevice)`, `PanelSize(PanelLayout)` — all in millimetres, which is what a printed drawing wants.

A DIN module is 17.5 mm in the real world; at `SlotMm = 5.0` a module draws 15 mm, close enough to read as true-to-scale on A4 while leaving margin. Document that it is a drawing scale, not life size.

- [ ] **Step 1: Write the failing test**

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PanelGeometryTests
{
    private static PlacedDevice Device(int row, int slot, int width) =>
        new(Guid.NewGuid(), DeviceCategory.Relay, row, slot, width, "Relay 1", [], TerminalRole.None);

    [Fact]
    public void One_din_module_is_three_slot_units_wide()
    {
        Assert.Equal(PanelGeometry.SlotMm * DinUnits.PerModule, PanelGeometry.SlotToX(DinUnits.PerModule));
    }

    [Fact]
    public void A_device_box_matches_its_slot_span()
    {
        var box = PanelGeometry.DeviceBox(Device(1, 3, 9));

        Assert.Equal(PanelGeometry.SlotToX(3), box.X);
        Assert.Equal(PanelGeometry.RowToY(1), box.Y);
        Assert.Equal(PanelGeometry.SlotToX(9), box.Width);
        Assert.Equal(PanelGeometry.RowMm, box.Height);
    }

    [Fact]
    public void A_four_row_panel_is_four_rails_and_three_gaps_tall()
    {
        var size = PanelGeometry.PanelSize(new PanelLayout(4, 54, []));

        Assert.Equal(4 * PanelGeometry.RowMm + 3 * PanelGeometry.RowGapMm, size.Height);
        Assert.Equal(PanelGeometry.SlotToX(54), size.Width);
    }

    [Fact]
    public void A_panel_with_no_rows_has_no_height()
    {
        Assert.Equal(0, PanelGeometry.PanelSize(new PanelLayout(0, 0, [])).Height);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter PanelGeometryTests`
Expected: FAIL — `PanelGeometry` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace PubInvest.HouseConfig.Domain.Layout;

/// Drawing geometry in millimetres. This is a drawing scale, not life size:
/// a real DIN module is 17.5mm, here it draws at 15mm so a 24-module row fits
/// an A4 landscape page with margin.
public static class PanelGeometry
{
    public const double SlotMm = 5.0;
    public const double RowMm = 45.0;
    public const double RowGapMm = 10.0;

    public static double SlotToX(int slot) => slot * SlotMm;
    public static double RowToY(int row) => row * (RowMm + RowGapMm);

    public static (double X, double Y, double Width, double Height) DeviceBox(PlacedDevice device) =>
        (SlotToX(device.StartSlot), RowToY(device.RowIndex), SlotToX(device.ModuleWidth), RowMm);

    public static (double Width, double Height) PanelSize(PanelLayout layout) =>
        (SlotToX(layout.SlotsPerRow),
         layout.Rows == 0 ? 0 : layout.Rows * RowMm + (layout.Rows - 1) * RowGapMm);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 55 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add shared panel drawing geometry in millimetres"
```

---

### Task 2: Issue a panel revision

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Revisions/RevisionSnapshot.cs`
- Create: `src/PubInvest.HouseConfig.Api/Revisions/RevisionService.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/RevisionEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/RevisionEndpointTests.cs`

**Interfaces:**
- Consumes: `HouseConfigDbContext`, `DomainMapper`, `PanelRevision`.
- Produces: `RevisionSnapshot(PanelLayoutSnapshot Layout, IReadOnlyList<CircuitSnapshot> Circuits, RuleSetPayload RuleSet, IReadOnlyList<DeviceType> Catalogue, EnclosureType Enclosure, string SubmainName, string? ProjectName)`; `RevisionService.IssueAsync(Guid submainId, string issuedBy, CancellationToken) -> Task<(PanelRevision? Revision, string? Error)>`, `LoadAsync(Guid revisionId, ...)`, `LatestAsync(Guid submainId, ...)`; `POST /submains/{id}/revisions`, `GET /submains/{id}/revisions`.

**Behaviour:** issuing snapshots the current stored layout with everything needed to render it later, and records `LayoutVersion`, `IssuedAt`, `IssuedBy`. Issuing a submain with no devices is a validation problem, not an empty revision.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/RevisionEndpointTests.cs` — reuse the `Generated(...)` helper shape from `DevicePositionTests`:

```csharp
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
    }

    [Fact]
    public async Task A_revision_keeps_the_prices_and_rules_it_was_issued_with()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);
        var revision = await (await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { }))
            .Content.ReadFromJsonAsync<RevisionDto>();

        // An admin re-prices the dimmer afterwards.
        await using (var db = factory.NewDbContext())
        {
            var dimmer = await db.DeviceTypes.SingleAsync(d => d.Id == TestSeed.DimmerId);
            dimmer.Cost = 999.99m;
            await db.SaveChangesAsync();
        }

        var stored = await client.GetFromJsonAsync<RevisionDto>($"/revisions/{revision!.Id}");

        Assert.DoesNotContain("999.99", stored!.SnapshotJson);
    }

    [Fact]
    public async Task Issuing_a_submain_with_no_panel_is_a_validation_problem()
    {
        var client = factory.CreateClient();
        var submainId = await UngeneratedSubmain(client);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revisions_are_listed_newest_first()
    {
        var client = factory.CreateClient();
        var (submainId, _) = await Generated(client, ThreeDimmed);

        await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });
        await client.PostAsJsonAsync($"/submains/{submainId}/revisions", new { });

        var list = await client.GetFromJsonAsync<RevisionDto[]>($"/submains/{submainId}/revisions");

        Assert.Equal(2, list!.Length);
        Assert.True(list[0].IssuedAt >= list[1].IssuedAt);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter RevisionEndpointTests`
Expected: FAIL — the routes 404.

- [ ] **Step 3: Write the snapshot record**

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Api.Revisions;

/// Everything needed to redraw and re-cost this design later, embedded rather
/// than referenced: an admin editing a rule or a price afterwards must not be
/// able to change what an issued drawing meant.
public sealed record RevisionSnapshot(
    string ProjectName,
    string SubmainName,
    PanelLayout Layout,
    IReadOnlyList<CircuitSnapshot> Circuits,
    RuleSetPayload RuleSet,
    IReadOnlyList<DeviceType> Catalogue,
    EnclosureType Enclosure);

public sealed record CircuitSnapshot(Guid Id, string Type, string Name, string? Room, int Sequence);
```

- [ ] **Step 4: Write the service and endpoints**

`RevisionService.IssueAsync` loads the submain with circuits, its devices and channels, the enclosure, the ruleset and every referenced device type; refuses with an error string when there are no devices; builds a `RevisionSnapshot`; serialises it with `DomainMapper.Json`; and saves a `PanelRevision`.

Endpoints: `POST /submains/{id}/revisions` returning `201` with `{ id, layoutVersion, issuedAt, issuedBy }`, `GET /submains/{id}/revisions` newest first, `GET /revisions/{id}` returning the row including `snapshotJson`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: PASS, 35 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: issue immutable panel revisions"
```

---

### Task 3: Server-side SVG renderer

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Rendering/PanelSvgRenderer.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/PanelSvgRendererTests.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/Golden/panel.svg`

**Interfaces:**
- Consumes: `PanelLayout`, `PanelGeometry`, `DinUnits`.
- Produces: `PanelSvgRenderer.Render(PanelLayout layout, IReadOnlyDictionary<Guid, string> deviceNames) -> string`.

Same drawing rules as the screen: rails with per-module ticks, adjacent same-role terminals drawn as one labelled block, narrow devices labelled rotated. Output is deterministic — no ids, no timestamps — so the golden file is stable.

- [ ] **Step 1: Write the failing test**

Assertions: the SVG's `width`/`height` match `PanelGeometry.PanelSize`; a 3-slot device at slot 3 emits a rect at `x="15"` (matching `PanelGeometry.SlotToX(3)`); a run of terminals emits one rect and one `L1–L9`-style label; the same layout renders byte-identical twice; and it matches `Golden/panel.svg`, refreshed only under `GOLDEN_UPDATE=1` exactly as the domain golden test does.

Add one test pinning the two renderers together:

```csharp
    [Fact]
    public void The_server_renderer_places_a_device_where_the_screen_does()
    {
        // ui/src/panel/geometry.ts: SLOT_PX = 8, so slot 3 is 24px.
        // Here slot 3 is 15mm. Both are slot * constant, so the ratio must hold
        // for every slot: if this ever fails, one renderer has changed its scale
        // and the PDF no longer matches the screen.
        Assert.Equal(PanelGeometry.SlotToX(3) / PanelGeometry.SlotToX(1), 3.0);
        Assert.Equal(PanelGeometry.SlotToX(9) / PanelGeometry.SlotToX(3), 3.0);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter PanelSvgRendererTests`
Expected: FAIL — `PanelSvgRenderer` does not exist.

- [ ] **Step 3: Write the renderer**

A `StringBuilder` emitting `<svg viewBox …>` with `CultureInfo.InvariantCulture` on every number (a `,` decimal separator would produce invalid SVG on a machine with a European locale — this is the single most likely way this breaks in production). Escape every label with `System.Security.SecurityElement.Escape`.

- [ ] **Step 4: Create the golden file**

Run with `GOLDEN_UPDATE=1`, **open the SVG and look at it**, then commit it. A golden file accepted without being looked at is worthless.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: PASS, 41 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: render a panel to static svg server-side"
```

---

### Task 4: PDF drawing and circuit schedule

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Documents/PanelDocument.cs`
- Modify: `Directory.Packages.props`, the Api `.csproj`, `Program.cs` (QuestPDF licence)
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/ExportEndpoints.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/PanelDocumentTests.cs`

**Interfaces:**
- Consumes: `RevisionSnapshot`, `PanelSvgRenderer`.
- Produces: `PanelDocument(RevisionSnapshot snapshot) : IDocument`; `GET /submains/{id}/export/pdf` (issues a revision if none exists, then renders the latest) and `GET /revisions/{id}/export/pdf`.

**Layout:** page 1 landscape A4 — title block (project, submain, revision, issued date), the panel drawing via the SVG from Task 3, and a legend of device kinds. Pages 2+ portrait — the circuit schedule as a table: circuit name, room, type, device, channel, and the device's row/slot in modules.

- [ ] **Step 1: Write the failing test**

PDF assertions are about substance, not pixels:

```csharp
    [Fact]
    public void A_pdf_is_produced_and_is_a_pdf()
    {
        var bytes = new PanelDocument(Snapshot()).GeneratePdf();

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }

    [Fact]
    public void The_schedule_names_every_assigned_circuit()
    {
        var text = PdfText(new PanelDocument(Snapshot()).GeneratePdf());

        foreach (var circuit in Snapshot().Circuits)
        {
            Assert.Contains(circuit.Name, text);
        }
    }

    [Fact]
    public void The_schedule_reports_positions_in_modules_not_slot_units()
    {
        var text = PdfText(new PanelDocument(Snapshot()).GeneratePdf());

        Assert.Contains("module", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("slot unit", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unpriced_catalogue_is_not_reported_as_a_zero_total()
    {
        var text = PdfText(new PanelDocument(SnapshotWithNoPrices()).GeneratePdf());

        Assert.DoesNotContain("£0.00", text);
        Assert.Contains("Not priced", text);
    }
```

`PdfText` extracts text with a small helper; add `PdfPig` as a **test-only** package for it rather than shipping a PDF parser in the API.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter PanelDocumentTests`
Expected: FAIL — `PanelDocument` does not exist.

- [ ] **Step 3: Add QuestPDF and write the document**

`Directory.Packages.props`: `QuestPDF` `2025.7.0`; test-only `PdfPig` `0.1.10`. In `Program.cs`, once: `QuestPDF.Settings.License = LicenseType.Community;`

Compose the document as described above, embedding the SVG with QuestPDF's `.Svg(...)`.

- [ ] **Step 4: Run the tests to verify they pass, then look at a real PDF**

```bash
dotnet test tests/PubInvest.HouseConfig.Api.Tests
curl -s http://localhost:5020/submains/<id>/export/pdf -o /tmp/panel.pdf && open /tmp/panel.pdf
```

Open it. A PDF that passes its tests and is unreadable is still a failure.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: export a panel drawing and circuit schedule as pdf"
```

---

### Task 5: Bill of materials export

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Documents/BomCsv.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/ExportEndpoints.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/BomExportTests.cs`

**Interfaces:**
- Produces: `BomCsv.Write(BillOfMaterials, string title) -> string`; `GET /submains/{id}/bom`, `GET /submains/{id}/bom.csv`, `GET /projects/{id}/bom`, `GET /projects/{id}/bom.csv`.

A house BOM aggregates every submain's latest revision, so it costs what was issued rather than what is being edited right now.

- [ ] **Step 1: Write the failing test**

Assertions: a submain BOM aggregates identical parts into one line; a house BOM sums across submains; CSV quotes a description containing a comma; CSV escapes an embedded quote by doubling it; and an unpriced BOM writes an empty cost cell with a `Not priced` note rather than `0.00`.

```csharp
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
```

- [ ] **Step 2: Run it to verify it fails**
- [ ] **Step 3: Write `BomCsv` and the endpoints**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: export bills of materials as json and csv"
```

---

### Task 6: Catalogue and ruleset write endpoints

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueAdminEndpoints.cs`
- Create: `src/PubInvest.HouseConfig.Api/Contracts/CatalogueContracts.cs`
- Test: `tests/PubInvest.HouseConfig.Api.Tests/CatalogueAdminTests.cs`

**Interfaces:**
- Produces: `POST`/`PATCH` on `/catalogue/device-types`, `/catalogue/enclosures`, `/catalogue/rulesets`, all `.RequireAuthorization("catalogue-admin")` when auth is enabled.

**Validation that matters:**
- `moduleWidth` must be ≥ 0, and > 0 for anything that is not an `Accessory` — a rail device with no width would pack infinitely.
- `channelCount` must be ≥ 0, and > 0 for `Dimmer240`, `Dimmer0_10V` and `Relay`.
- An enclosure's `slotsPerRow` must be a multiple of `DinUnits.PerModule`; a row that is a fraction of a module is not a real enclosure.
- A ruleset payload must reference only device types that exist and are active — the same check the shipped-seed test makes, enforced at write time so a bad ruleset can never be saved.
- Deactivating a device type that a default ruleset references is refused, naming the ruleset.

- [ ] **Step 1: Write the failing test** (one test per rule above, plus one asserting an anonymous caller gets `401` when auth is enabled)
- [ ] **Step 2: Run it to verify it fails**
- [ ] **Step 3: Write the contracts and endpoints**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add role-gated catalogue and ruleset writes"
```

---

### Task 7: BOM and issue in the UI

**Files:**
- Create: `ui/src/routes/BomPage.tsx`, `ui/src/panel/IssueButton.tsx`
- Modify: `ui/src/App.tsx`, `ui/src/routes/PanelPage.tsx`, `ui/src/routes/ProjectPage.tsx`
- Create: `ui/tests/BomPage.test.tsx`, `ui/tests/IssueButton.test.tsx`

**Behaviour:** the panel screen gains "Issue & download PDF", which issues a revision then opens the PDF. The submain and house both get a BOM screen listing parts with quantities, and a "Download CSV" link. An unpriced BOM shows "Not priced" where a total would go — the same rule as the PDF, for the same reason.

- [ ] **Step 1: Write the failing tests**

Assertions: the BOM page lists parts and quantities from a stubbed response; an unpriced BOM renders "Not priced" and no `£0.00`; the issue button disables while issuing and re-enables after; a failed issue shows the message rather than silently doing nothing.

- [ ] **Step 2: Run them to verify they fail**
- [ ] **Step 3: Write the components and routes**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add bom screen and issue-and-download to the ui"
```

---

### Task 8: Catalogue admin screens

**Files:**
- Create: `ui/src/routes/CataloguePage.tsx`, `ui/src/catalogue/DeviceTypeForm.tsx`, `ui/src/catalogue/EnclosureForm.tsx`
- Modify: `ui/src/App.tsx`
- Create: `ui/tests/DeviceTypeForm.test.tsx`

**Behaviour:** a list of device types and enclosures with an edit form each. Widths are entered and displayed **in DIN modules** and converted to slot units on save — an admin should never have to think in thirds. Terminal blocks are the exception and get a "blocks per module" field instead, since a third of a module is exactly what they are.

Ruleset editing is **not** a free-text JSON box: it is a form over the known fields — band order (drag to reorder), derating factor, preferred device per category, terminal rules. A JSON box would let an admin save a payload that fails only at generation time, on site.

- [ ] **Step 1: Write the failing test**

Assertions: entering `1` module saves `moduleWidth: 3`; an existing device showing `moduleWidth: 9` displays as `3`; a width of `0` is rejected for a dimmer with a message; the ruleset form refuses to save when a preferred device is unset.

- [ ] **Step 2: Run it to verify it fails**
- [ ] **Step 3: Write the screens**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add catalogue admin screens in din modules"
```

---

### Task 9: Price the catalogue and check a real job

**Blocked until:** real prices and the remaining `ASSUMED` values are supplied — PSU widths and wattages, enclosure models actually stocked, jumper bar ways, end stops per bank.

**Files:**
- Modify: `seed/catalogue.v1.json`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/ShippedSeedTests.cs`

- [ ] **Step 1: Replace every `ASSUMED` value and every `0.00` cost with a confirmed one**
- [ ] **Step 2: Add a test asserting the shipped seed has no `ASSUMED` part numbers and no zero costs**

```csharp
    [Fact]
    public void The_shipped_catalogue_is_fully_specified()
    {
        var seed = Load();

        Assert.DoesNotContain(seed.DeviceTypes, d => d.PartNumber.Contains("ASSUMED"));
        Assert.All(seed.DeviceTypes, d => Assert.True(d.Cost > 0m, $"{d.PartNumber} has no price"));
        Assert.All(seed.Enclosures, e => Assert.True(e.Cost > 0m, $"{e.Model} has no price"));
    }
```

That test is the thing that stops a half-specified catalogue reaching a real job. Until step 1 is done it fails, which is correct.

- [ ] **Step 3: Design one real submain end to end and check the drawing against the enclosure in front of you**
- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: price the catalogue from confirmed values"
```

---

## Done when

- `dotnet test` and `cd ui && npx vitest run` are green.
- Issuing a revision, then re-pricing a part, leaves the issued PDF and BOM unchanged.
- A PDF opens with a readable to-scale drawing on page 1 and a complete circuit schedule after it.
- A house BOM sums its submains' latest revisions, and says "Not priced" rather than "£0.00" while costs are missing.
- An admin can add a Shelly model and use it on the next design without a deploy.
- Every user-facing number is in DIN modules.

## Not in any plan

- Identifying physical devices (no printed QR or serial on Shelly Pro hardware) — dropped, see the spec.
- Offline use. A plant room with no signal cannot be worked in. If that turns out to matter, a service worker queuing edits is the remedy, deliberately added rather than half-built.
- Pushing configuration to the Shelly devices, and Home Assistant export.
- Cable and breaker calculations — protection lives upstream of these panels.
