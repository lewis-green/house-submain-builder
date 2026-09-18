# House Panel Designer — Real Panel Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the generator's assumed wiring conventions with how these panels are actually built.

**Architecture:** Same shape as before — a pure generator, rules as data, server-authoritative. What changes is the rules themselves: one 3-tier terminal block per circuit instead of three banks, a two-pole isolator taking the incoming feed, LED drivers moved outside the panel, and a packer that merges bands into a row rather than always giving each its own.

**Spec:** `docs/superpowers/specs/2026-09-17-house-panel-designer-design.md` (updated by Task 7)
**Builds on:** all three previous plans, merged to `main`.

## What changes, and why

Five corrections, all from how the panels are really wired:

**1. One terminal block per circuit, not three.** The WAGO 2003-7646 is a 3-tier
block carrying L, N and E on a single slice. Today the generator builds three
separate banks — `L1–L7`, `N1–N7`, `E1–E7` — which is roughly three times as
many blocks as the panel actually needs, and makes the drawing lie about how much
rail is used.

**2. Earth commons through the DIN rail.** The PE tier grounds through the rail
foot, so earth needs no jumper bar and no separate PE part. Both come out.

**3. Neutrals commoned, live looped to the Shelly.** The neutral tier is bridged
with a bar; the line tier is per-circuit and loops out to its dimmer or relay
channel. Only the neutral bank contributes jumper bars now.

**4. LED drivers are not DIN mount.** They sit outside the panel. The generator
stops placing `Psu24V` devices on the rail entirely. What the panel does carry is
a **12-way WAGO for +24V and one for −24V**, one way per tape circuit, so a
second pair is needed past twelve. The driver is still *sized* from the tape load
and still appears on the BOM, flagged as external — you have to buy it, you just
don't mount it here.

**5. A band gets its own row only if the design fits that way.** Today every band
starts a fresh row, which costs a row whenever a band is small — a real panel came
out using 4 rows for 24 modules of kit. The new rule: try one band per row; if
that does not fit the enclosure, merge bands into shared rows, dimmers packing
from the left and relays from the right. The clean layout is preferred and the
dense one is the fallback, rather than either being forced.

## Global Constraints

- Widths stay in slot units (3 = 1 DIN module). Anything a person reads is in modules.
- The generator stays pure: no I/O, deterministic, diagnostics returned not thrown.
- Rules stay data. Nothing in this plan hard-codes a part number or a width.
- Plain xUnit `Assert`, `net10.0`, central package management.
- **Both golden files will change.** That is expected — regenerate under `GOLDEN_UPDATE=1`, then *look at* the new layout and SVG before committing them.

---

### Task 1: Catalogue categories for the new parts

**Files:**
- Modify: `src/PubInvest.HouseConfig.Domain/Catalogue/DeviceType.cs` (add categories)
- Modify: `src/PubInvest.HouseConfig.Domain/Rules/RuleSetPayload.cs` (rules for the new parts)
- Modify: `seed/catalogue.v1.json`
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/CatalogueFixture.cs`, `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/ShippedSeedTests.cs`

**Interfaces:**
- Produces: `DeviceCategory.Isolator`, `DeviceCategory.Dc24VPositive`, `DeviceCategory.Dc24VNegative`; `TerminalRules` reshaped (below); `PreferredDevices` gains `Isolator`, `Dc24VPositive`, `Dc24VNegative`, and its `Psu24V` list becomes `ExternalDriver`.

`TerminalRules` loses its three conductor rules and gains one:

```csharp
public sealed record TerminalRules(
    /// One 3-tier block per circuit: L, N and E on a single slice.
    Guid DeviceTypeId,
    int BlocksPerCircuit,
    /// The neutral tier is bridged. Earth commons through the DIN rail, so it
    /// needs no bar; line is per-circuit and loops out to the Shelly.
    Guid BridgeBarDeviceTypeId,
    int BridgeBarWays,
    Guid EndStopDeviceTypeId,
    int EndStopsPerBank);
```

- [ ] **Step 1: Write the failing test**

In `ShippedSeedTests`:

```csharp
    [Fact]
    public void The_seed_carries_the_parts_the_new_rules_need()
    {
        var seed = Load();
        var byCategory = seed.DeviceTypes.ToLookup(d => d.Category);

        Assert.NotEmpty(byCategory["Isolator"]);
        Assert.NotEmpty(byCategory["Dc24VPositive"]);
        Assert.NotEmpty(byCategory["Dc24VNegative"]);
    }

    [Fact]
    public void No_separate_earth_terminal_is_needed_any_more()
    {
        var seed = Load();
        var p = seed.RuleSets.Single(r => r.IsDefault).Payload;

        // One block per circuit carries all three conductors.
        Assert.Equal(1, p.Terminals.BlocksPerCircuit);
        Assert.Contains(seed.DeviceTypes, d => d.Id == p.Terminals.DeviceTypeId);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests --filter ShippedSeedTests`
Expected: FAIL — the categories and the reshaped rules do not exist.

- [ ] **Step 3: Add the categories and reshape the rules**

```csharp
public enum DeviceCategory
{
    Terminal240,
    Isolator,
    Dimmer240,
    Dimmer0_10V,
    Relay,
    /// 12-way +24V distribution block. The driver itself is not panel-mounted.
    Dc24VPositive,
    Dc24VNegative,
    /// Sized and costed, never placed: LED drivers live outside the panel.
    ExternalDriver,
    Accessory,
}
```

`Psu24V` is removed. Every `Psu24V` in the seed, fixtures and rulesets becomes
`ExternalDriver`, and those entries must no longer appear in `bandOrder`.

- [ ] **Step 4: Update the seed and both fixtures**

Add to `seed/catalogue.v1.json`, all marked `ASSUMED` until confirmed: a two-pole
isolator, a 12-way +24V block and a 12-way −24V block. Re-point the default
ruleset: `terminals.deviceTypeId` at the 2003-7646, drop the PE entry entirely,
and set `preferredDevice.isolator`, `.dc24VPositive`, `.dc24VNegative`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test`
Expected: the Domain and Api suites will now fail to compile in places — that is
Task 2 onwards. The Data suite must pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add isolator and 24V distribution categories"
```

---

### Task 2: One 3-tier terminal block per circuit

**Files:**
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/TerminalBandBuilder.cs`
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/TerminalBandBuilderTests.cs`

**Interfaces:**
- Produces: `TerminalBandBuilder.Build` unchanged in shape, changed in behaviour — one block per circuit, labelled `C1`, `C2`…, `TerminalRole.All`.

`TerminalRole` gains `All` and keeps `Line`/`Neutral`/`Earth` only for reading old
revisions. New designs use `All`.

**Rules:**
- Blocks = `circuits.Count × BlocksPerCircuit`. **No `+ 1`** — the incoming feed
  lands on the isolator now, not on a terminal block.
- One bank, not three. Jumper bars = `ceil(blocks / BridgeBarWays)` for the
  neutral tier alone. End stops = `EndStopsPerBank` once.
- Labels are `C1…Cn`, matching the circuit they serve, so the drawing and the
  schedule name the same thing.

- [ ] **Step 1: Rewrite the failing tests**

```csharp
    [Fact]
    public void One_block_serves_one_circuit_across_all_three_conductors()
    {
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(9, band.Devices.Count);
        Assert.All(band.Devices, d => Assert.Equal(TerminalRole.All, d.TerminalRole));
    }

    [Fact]
    public void Blocks_are_labelled_for_the_circuit_they_serve()
    {
        var band = TerminalBandBuilder.Build(Circuits(3), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(["C1", "C2", "C3"], band.Devices.Select(d => d.Label));
    }

    [Fact]
    public void Only_the_neutral_tier_needs_bars_and_the_earth_needs_none()
    {
        // 9 circuits, one bank of 9 blocks, 10-way bars -> one bar, one set of stops.
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(1, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.EndStopId).Quantity);
    }

    [Fact]
    public void The_incoming_feed_does_not_take_a_terminal_block()
    {
        var band = TerminalBandBuilder.Build(Circuits(4), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(4, band.Devices.Count);
    }
```

- [ ] **Step 2: Run them to verify they fail**
- [ ] **Step 3: Rewrite the builder** — one bank, one block per circuit, no incomer, bars for the neutral tier only
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: use one 3-tier terminal block per circuit"
```

---

### Task 3: The two-pole isolator

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/IsolatorBuilder.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/PanelGenerator.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/IsolatorBuilderTests.cs`

**Interfaces:**
- Produces: `IsolatorBuilder.Build(RuleSetPayload, DeviceCatalogue) -> (RequiredDevice? Device, IReadOnlyList<Diagnostic>)`.

Exactly one per submain, labelled `Isolator`, placed **first on the top row**,
ahead of the terminal band. A missing or inactive isolator part is a
`NO_PREFERRED_DEVICE` error, because a panel with no means of isolation is not a
panel you would install.

- [ ] **Step 1: Write the failing test** — one isolator always; it is the first device in the band order; a missing part errors
- [ ] **Step 2: Run it to verify it fails**
- [ ] **Step 3: Write the builder and put `Isolator` at the head of the default band order**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: put a two-pole isolator at the head of every panel"
```

---

### Task 4: LED tape — external driver, ± distribution blocks

**Files:**
- Rename: `PsuSizer.cs` → `src/PubInvest.HouseConfig.Domain/Generation/TapeSupplySizer.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/PanelGenerator.cs`, `BomBuilder.cs`
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/PsuSizerTests.cs` → `TapeSupplySizerTests.cs`

**Interfaces:**
- Produces: `TapeSupply(IReadOnlyList<RequiredDevice> Blocks, IReadOnlyList<AccessoryLine> ExternalParts, IReadOnlyList<Diagnostic> Diagnostics)` and `TapeSupplySizer.Size(...)`.

**Rules:**
- No tape circuits → nothing at all: no blocks, no driver.
- Blocks: `ceil(tapeCircuits / waysPerBlock)` pairs, one `Dc24VPositive` and one
  `Dc24VNegative` each, labelled `+24V 1` / `−24V 1`. `waysPerBlock` comes from
  the catalogue entry's `ChannelCount`, so a 12-way block is data, not a constant.
- Driver: same sizing as before (summed tape watts ÷ derating, largest-first from
  the ruleset's `ExternalDriver` list), emitted as `ExternalParts` — costed on the
  BOM, never placed on the rail.
- `TAPE_LOAD_MISSING` and the unsized-supply error keep their codes.

`BomBuilder` gains a flag so external parts are marked as such; `BomLine` gets
`bool PanelMounted` defaulting to `true`.

- [ ] **Step 1: Write the failing tests** — including one asserting no device of any
  kind lands on the rail for the driver itself, and one asserting 13 tape circuits
  produce two pairs of blocks
- [ ] **Step 2: Run them to verify they fail**
- [ ] **Step 3: Write the sizer and wire it into the generator**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: move led drivers outside the panel, add 24V distribution blocks"
```

---

### Task 5: Pack a band per row, merging only when it will not fit

**Files:**
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/BandPacker.cs`
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/BandPackerTests.cs`

**Interfaces:**
- Produces: `BandPacker.Pack` unchanged in shape. `RuleSetPayload.BandStartsNewRow` becomes `PackingPreference` — `"bandPerRow"` (try a row each, merge if that does not fit) or `"dense"` (always merge).

**The algorithm:**
1. Pack banded — each band on its own fresh row, as today.
2. If that fits the enclosure, use it. The clean layout is the preferred answer.
3. If it does not, re-pack merged: walk the bands in order, placing each into the
   first row with room, **alternating ends** — dimmers and their tape dimmers from
   the left, relays and distribution blocks from the right. A row is full when the
   left and right fronts would overlap.
4. If the merged pack still does not fit, report `ENCLOSURE_TOO_SMALL` against the
   merged attempt, since that is the best the enclosure could have done.

A merged layout is reported with an `Info` diagnostic naming which bands share a
row, so the engineer knows the drawing is dense on purpose rather than by mistake.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void A_design_that_fits_banded_keeps_one_band_per_row()
    {
        // Three small bands in a 6-row enclosure: no need to merge anything.
        var result = BandPacker.Pack(SmallBands(), CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.Equal(3, result.Layout.Devices.Select(d => d.RowIndex).Distinct().Count());
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BandsMerged);
    }

    [Fact]
    public void A_design_that_will_not_fit_banded_merges_rather_than_failing()
    {
        // The same devices in a 2-row enclosure: banded needs 3 rows, merged needs 2.
        var result = BandPacker.Pack(SmallBands(), TwoRowEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BandsMerged);
    }

    [Fact]
    public void Merged_rows_put_relays_at_the_right_hand_end()
    {
        var result = BandPacker.Pack(SmallBands(), TwoRowEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var dimmer = result.Layout.Devices.First(d => d.Category == DeviceCategory.Dimmer240);
        var relay = result.Layout.Devices.First(d => d.Category == DeviceCategory.Relay);

        Assert.True(relay.EndSlotExclusive > dimmer.EndSlotExclusive);
        Assert.Equal(result.Layout.SlotsPerRow, relay.EndSlotExclusive);
    }

    [Fact]
    public void Devices_never_overlap_even_when_packed_from_both_ends()
    {
        var result = BandPacker.Pack(ManyDevices(), TwoRowEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        foreach (var row in result.Layout.Devices.GroupBy(d => d.RowIndex))
        {
            var ordered = row.OrderBy(d => d.StartSlot).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].StartSlot >= ordered[i - 1].EndSlotExclusive,
                    $"{ordered[i].Label} overlaps {ordered[i - 1].Label} on row {row.Key}");
            }
        }
    }
```

The overlap test is the one that matters most: packing from two ends is exactly
where an off-by-one puts two devices in the same space, and the database's unique
`(SubmainId, RowIndex, StartSlot)` index would only catch some of those.

- [ ] **Step 2: Run them to verify they fail**
- [ ] **Step 3: Write the two-pass packer**
- [ ] **Step 4: Run the tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: merge bands into shared rows when a banded layout will not fit"
```

---

### Task 6: Regenerate the golden files and look at them

**Files:**
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/Golden/typical-submain.json`
- Modify: `tests/PubInvest.HouseConfig.Api.Tests/Golden/panel.svg`

- [ ] **Step 1: Regenerate both**

```bash
GOLDEN_UPDATE=1 dotnet test
```

- [ ] **Step 2: Read the new layout and check it by hand**

For the typical submain, confirm: one isolator first; one terminal block per
circuit and no more; no PSU anywhere on the rail; ± blocks present only if there
is tape; and the row count lower than before, since the terminal band is now a
third of its old size.

- [ ] **Step 3: Render the SVG and look at it**

```bash
qlmanage -t -s 900 -o . tests/PubInvest.HouseConfig.Api.Tests/Golden/panel.svg
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: refresh golden files for the real panel rules"
```

---

### Task 7: Carry the changes through the API, UI and spec

**Files:**
- Modify: `src/PubInvest.HouseConfig.Api/Rendering/PanelSvgRenderer.cs`, `Documents/PanelDocument.cs`
- Modify: `ui/src/panel/deviceStyle.ts`, `ui/src/wizard/PreviewSummary.tsx`, `ui/src/routes/BomPage.tsx`
- Modify: `docs/superpowers/specs/2026-09-17-house-panel-designer-design.md`

- [ ] **Step 1: Styles and labels for the new categories** — isolator, +24V, −24V in both renderers and in `deviceStyle.ts`; `Psu24V` gone
- [ ] **Step 2: Mark external parts in both BOM views** — PDF and screen show "external" against anything not panel-mounted, so nobody looks for it on the rail
- [ ] **Step 3: Update the spec** — the terminal decision, the isolator, LED tape (drivers external), and the banding rule, each replacing what is there now
- [ ] **Step 4: Run everything**

```bash
dotnet test && (cd ui && npx vitest run)
```

- [ ] **Step 5: Drive it in a browser and look at a real PDF** — a generated panel, then its issued PDF, checking the isolator is first, terminals are one per circuit, and no driver appears on the drawing
- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: carry the real panel rules through the api, ui and spec"
```

---

## Done when

- A submain with 6 circuits draws **6 terminal blocks**, not 21.
- Every panel starts with a two-pole isolator, and no terminal block is spent on the incoming feed.
- No LED driver appears on the rail; a panel with tape has ± distribution blocks, and the driver is on the BOM marked external.
- A design that fits one-band-per-row still looks like that; one that does not merges, packing relays from the right, and says so.
- No two devices ever occupy the same slot, from either end.
- Both golden files have been regenerated **and looked at**.

## Still outstanding after this

The catalogue values: PSU wattages become external driver wattages, and the
isolator and ± blocks arrive as fresh `ASSUMED` entries. Every price is still
£0.00. `The_shipped_catalogue_is_fully_specified` stays failing until that is
dealt with, which is the point of it.
