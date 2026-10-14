# Terminal Order Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the engineer choose the order of circuits along the terminal rail and give one circuit several bridged terminals, then show each terminal's circuit on the drawing, in a terminal sheet and in the PDF.

**Architecture:** Two new circuit fields, `TerminalPosition` and `TerminalCount`, drive `TerminalBandBuilder`. It lays terminals in terminal order, gives a circuit `TerminalCount` adjacent blocks, stamps each block with its `CircuitId`, and adds line-tier jumper bars for bridged circuits. `GET/PUT /submains/{id}/terminals` reads and saves the order and counts, rebuilding the panel in the same transaction. The UI draws each terminal individually and opens a `TerminalSheet` from the terminal run.

**Tech Stack:** .NET 10 minimal API, EF Core + Npgsql, xUnit + Testcontainers, QuestPDF; React 19 + TypeScript + Tailwind 4, Vitest + Testing Library, Playwright (WebKit).

**Spec:** `docs/superpowers/specs/2026-10-14-terminal-order-design.md`

**Commands used throughout**
- Domain tests: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
- Data tests (Docker): `dotnet test tests/PubInvest.HouseConfig.Data.Tests`
- API tests (Docker): `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
- One test: add `--filter "FullyQualifiedName~<ClassName>"`
- UI: `cd ui && npx vitest run tests/<File>.test.tsx`, `npm run build`, `npm run lint`

---

## File map

| File | Change |
|---|---|
| `src/PubInvest.HouseConfig.Domain/Circuits/Circuit.cs` | + `TerminalPosition`, `TerminalCount` |
| `src/PubInvest.HouseConfig.Domain/Rules/RuleSetPayload.cs` | `TerminalRules`: − `BlocksPerCircuit`, + line bar fields |
| `src/PubInvest.HouseConfig.Domain/Generation/RequiredDevice.cs` | + `CircuitId` |
| `src/PubInvest.HouseConfig.Domain/Layout/PlacedDevice.cs` | + `CircuitId` |
| `src/PubInvest.HouseConfig.Domain/Generation/PanelPacker.cs` | carry `CircuitId` |
| `src/PubInvest.HouseConfig.Domain/Generation/TerminalBandBuilder.cs` | order, counts, ids, line bars |
| `seed/catalogue.v1.json` | line bar part, ruleset v4 |
| `src/PubInvest.HouseConfig.Data/Entities/CircuitRow.cs`, `DeviceInstance.cs` | new columns |
| `src/PubInvest.HouseConfig.Data/Mapping/DomainMapper.cs` | map new fields |
| `src/PubInvest.HouseConfig.Data/Migrations/*_CircuitTerminals.cs` | new migration |
| `src/PubInvest.HouseConfig.Api/Contracts/DesignContracts.cs` | `PlacedDeviceResponse.CircuitId` |
| `src/PubInvest.HouseConfig.Api/Contracts/TerminalContracts.cs` | **new** request/response records |
| `src/PubInvest.HouseConfig.Api/Endpoints/TerminalEndpoints.cs` | **new** GET/PUT |
| `src/PubInvest.HouseConfig.Api/Program.cs` | map the endpoints |
| `src/PubInvest.HouseConfig.Api/Services/DesignService.cs` | persist/return `CircuitId`, merge stored terminal fields |
| `src/PubInvest.HouseConfig.Api/Endpoints/DesignEndpoints.cs` | return `CircuitId` on stored layout |
| `src/PubInvest.HouseConfig.Api/Revisions/*` | carry `CircuitId` and the new circuit fields |
| `src/PubInvest.HouseConfig.Api/Documents/PanelDocument.cs` | Terminal column |
| `src/PubInvest.HouseConfig.Api/Rendering/PanelSvgRenderer.cs` | per-terminal drawing |
| `ui/src/api/types.ts`, `ui/src/api/client.ts` | types, `put` |
| `ui/src/panel/terminalLabels.ts` | **new** label helper |
| `ui/src/panel/PanelSvg.tsx` | per-terminal drawing, run is a button |
| `ui/src/panel/TerminalSheet.tsx` | **new** sheet |
| `ui/src/routes/PanelPage.tsx` | wire the sheet |
| `ui/e2e/panel.spec.ts` | e2e |
| `README.md` | document the feature |

---

### Task 1: Domain types carry the new fields

**Files:**
- Modify: `src/PubInvest.HouseConfig.Domain/Circuits/Circuit.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Rules/RuleSetPayload.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/RequiredDevice.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Layout/PlacedDevice.cs`
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/PanelPacker.cs` (the `Place` method near line 176)
- Modify: every `new TerminalRules(` call: `tests/PubInvest.HouseConfig.Domain.Tests/CatalogueFixture.cs`, `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs`, `tests/PubInvest.HouseConfig.Data.Tests/CatalogueSeederTests.cs`, `tests/PubInvest.HouseConfig.Api.Tests/PanelDocumentTests.cs`
- Modify: `tests/PubInvest.HouseConfig.Data.Tests/ShippedSeedTests.cs` (around line 161)
- Modify: `seed/catalogue.v1.json` (remove `blocksPerCircuit` only; the rest comes in Task 3)

This task is a pure refactor. Behaviour is unchanged and the existing tests are the check.

- [ ] **Step 1: Add the circuit fields**

Replace the `Circuit` record in `Circuit.cs`:

```csharp
public sealed record Circuit(
    Guid Id,
    CircuitType Type,
    string Name,
    string? Room,
    int Sequence,
    decimal? WattsPerMetre,
    decimal? LengthMetres,
    /// Where the circuit lands along the terminal rail, counted from 1. Null
    /// for a circuit nobody has placed: it follows the placed ones, in
    /// Sequence order. Separate from Sequence because Sequence decides which
    /// Shelly channel a circuit gets, and moving a terminal must not rewire one.
    int? TerminalPosition = null,
    /// How many terminal blocks the circuit lands on. Above one, their line
    /// tiers are bridged so several cables share one channel.
    int TerminalCount = 1)
{
    public decimal LoadWatts => (WattsPerMetre ?? 0m) * (LengthMetres ?? 0m);

    public bool HasTapeLoad => WattsPerMetre is > 0m && LengthMetres is > 0m;
}
```

- [ ] **Step 2: Change `TerminalRules`**

Replace the record and its doc comment in `RuleSetPayload.cs`:

```csharp
/// One 3-tier block per terminal: L, N and E on a single slice. A circuit has
/// one block unless it asks for more (Circuit.TerminalCount).
///
/// The neutral tier is bridged across the whole bank with a bar. Earth commons
/// through the DIN rail, so it needs none. Line is per-circuit and loops out to
/// the Shelly. Only a circuit with several blocks bridges its line tier, across
/// its own blocks, with a cut-to-length line bar.
///
/// The line bar fields are defaulted so a ruleset stored before they existed
/// still reads. With no line bar ways, no line bars are counted.
public sealed record TerminalRules(
    Guid DeviceTypeId,
    Guid BridgeBarDeviceTypeId,
    int BridgeBarWays,
    Guid EndStopDeviceTypeId,
    int EndStopsPerBank,
    Guid LineBridgeBarDeviceTypeId = default,
    int LineBridgeBarWays = 0);
```

- [ ] **Step 3: Add `CircuitId` to `RequiredDevice` and `PlacedDevice`**

In `RequiredDevice.cs`:

```csharp
/// A device the generator has decided is needed, before it has been given a slot.
/// CircuitId is set on terminals only: the circuit whose cables land on it.
public sealed record RequiredDevice(
    Guid DeviceTypeId,
    DeviceCategory Category,
    int ModuleWidth,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels,
    TerminalRole TerminalRole = TerminalRole.None,
    Guid? CircuitId = null);
```

In `PlacedDevice.cs`:

```csharp
public sealed record PlacedDevice(
    Guid DeviceTypeId,
    DeviceCategory Category,
    int RowIndex,
    int StartSlot,
    int ModuleWidth,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels,
    TerminalRole TerminalRole,
    /// Terminals only: the circuit landing on this block. Not a channel, so it
    /// never counts as a circuit being wired to a Shelly.
    Guid? CircuitId = null)
{
    public int EndSlotExclusive => StartSlot + ModuleWidth;
}
```

In `PanelPacker.cs`, the `Place` method becomes:

```csharp
    private static PlacedDevice Place(RequiredDevice device, int row, int slot) => new(
        device.DeviceTypeId,
        device.Category,
        row,
        slot,
        device.ModuleWidth,
        device.Label,
        device.Channels,
        device.TerminalRole,
        device.CircuitId);
```

- [ ] **Step 4: Drop `BlocksPerCircuit` from the builder for now**

In `TerminalBandBuilder.cs`, replace `var perCircuit = Math.Max(terminals.BlocksPerCircuit, 1);` with `var perCircuit = 1;`. Task 2 rewrites this loop.

- [ ] **Step 5: Fix every `TerminalRules` construction**

In `CatalogueFixture.cs`, `TestSeed.cs` and `CatalogueSeederTests.cs`, delete the line `BlocksPerCircuit: 1,`.

In `PanelDocumentTests.cs` line 56, change `new TerminalRules(DimmerType, 1, DimmerType, 10, DimmerType, 2)` to `new TerminalRules(DimmerType, DimmerType, 10, DimmerType, 2)`.

In `ShippedSeedTests.cs`, delete `Assert.Equal(1, p.Terminals.BlocksPerCircuit);`.

In `seed/catalogue.v1.json`, delete the line `"blocksPerCircuit": 1,`.

- [ ] **Step 6: Build and run the domain tests**

Run: `dotnet build && dotnet test tests/PubInvest.HouseConfig.Domain.Tests`

Expected: the build succeeds and everything passes except `A_typical_submain_matches_the_golden_layout`. That one fails because the golden JSON now has `"circuitId": null` on every device. Check the diff is only that field:

```bash
GOLDEN_UPDATE=1 dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter "FullyQualifiedName~golden_layout"
git diff --stat tests/PubInvest.HouseConfig.Domain.Tests/Golden
git diff tests/PubInvest.HouseConfig.Domain.Tests/Golden | grep '^[+-] ' | grep -v circuitId
```

Expected: the last command prints nothing.

- [ ] **Step 7: Commit**

```bash
git add -A src/PubInvest.HouseConfig.Domain tests seed
git commit -m "refactor: give circuits a terminal position and count, and terminals a circuit id"
```

---

### Task 2: `TerminalBandBuilder` lays terminals in terminal order, with counts and line bars

**Files:**
- Modify: `src/PubInvest.HouseConfig.Domain/Generation/TerminalBandBuilder.cs`
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/CatalogueFixture.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/TerminalBandBuilderTests.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/DeviceDemandCalculatorTests.cs`

- [ ] **Step 1: Add a line bar to the test fixture**

In `CatalogueFixture.cs`, add the id after `EndStopId`:

```csharp
    public static readonly Guid LineBridgeId = new("11111111-0000-0000-0000-000000000010");
```

Add this to the `Catalogue()` list, after the end stop entry. Add the comma to the end stop line.

```csharp
        new DeviceType(LineBridgeId, "WAGO",    "Line jumper bar 10-way", "TEST-LBAR", DeviceCategory.Accessory, 0, 0, null, null, true)
```

In `Rules()`, the `TerminalRules` becomes:

```csharp
        Terminals: new TerminalRules(
            DeviceTypeId: TerminalId,
            BridgeBarDeviceTypeId: BridgeId,
            BridgeBarWays: 10,
            EndStopDeviceTypeId: EndStopId,
            EndStopsPerBank: 2,
            LineBridgeBarDeviceTypeId: LineBridgeId,
            LineBridgeBarWays: 10));
```

- [ ] **Step 2: Write the failing tests**

Append to `TerminalBandBuilderTests.cs`, inside the class:

```csharp
    private static Circuit Switched(int sequence, int? position = null, int count = 1) =>
        new(Guid.NewGuid(), CircuitType.Switched, $"Switched {sequence}", null, sequence, null, null,
            position, count);

    private static TerminalBand Build(params Circuit[] circuits) =>
        TerminalBandBuilder.Build(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

    [Fact]
    public void Circuits_are_laid_in_their_terminal_position_not_their_sequence()
    {
        var first = Switched(1, position: 3);
        var second = Switched(2, position: 1);
        var third = Switched(3, position: 2);

        var band = Build(first, second, third);

        Assert.Equal([second.Id, third.Id, first.Id], band.Devices.Select(d => d.CircuitId!.Value));
        Assert.Equal(["C1", "C2", "C3"], band.Devices.Select(d => d.Label));
    }

    [Fact]
    public void Circuits_nobody_has_placed_follow_the_placed_ones_in_sequence_order()
    {
        // Added after the order was chosen: they go on the end, not in the middle.
        var placed = Switched(3, position: 1);
        var newA = Switched(1);
        var newB = Switched(2);

        var band = Build(newA, placed, newB);

        Assert.Equal([placed.Id, newA.Id, newB.Id], band.Devices.Select(d => d.CircuitId!.Value));
    }

    [Fact]
    public void With_no_positions_at_all_the_order_is_the_sequence()
    {
        var a = Switched(2);
        var b = Switched(1);

        var band = Build(a, b);

        Assert.Equal([b.Id, a.Id], band.Devices.Select(d => d.CircuitId!.Value));
    }

    [Fact]
    public void A_circuit_on_several_terminals_gets_adjacent_blocks_and_the_next_keeps_its_number()
    {
        var a = Switched(1);
        var bridged = Switched(2, count: 6);
        var c = Switched(3);

        var band = Build(a, bridged, c);

        Assert.Equal(
            ["C1", "C2.1", "C2.2", "C2.3", "C2.4", "C2.5", "C2.6", "C3"],
            band.Devices.Select(d => d.Label));
        Assert.All(band.Devices.Skip(1).Take(6), d => Assert.Equal(bridged.Id, d.CircuitId));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(12, 2)]
    public void A_bridged_circuit_gets_line_bars_for_its_own_blocks(int count, int bars)
    {
        var band = Build(Switched(1, count: count));

        var line = band.Accessories.SingleOrDefault(a => a.DeviceTypeId == CatalogueFixture.LineBridgeId);
        Assert.Equal(bars, line?.Quantity ?? 0);
    }

    [Fact]
    public void Line_bars_are_not_shared_between_circuits()
    {
        // Two groups of six would fit one 10-way bar's length between them, but
        // an offcut is not a bar: each group gets its own.
        var band = Build(Switched(1, count: 6), Switched(2, count: 6));

        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.LineBridgeId).Quantity);
    }

    [Fact]
    public void The_neutral_bar_spans_every_block_including_bridged_ones()
    {
        // 6 + 5 = 11 blocks: two 10-way neutral bars.
        var band = Build(Switched(1, count: 6), Switched(2), Switched(3), Switched(4), Switched(5), Switched(6));

        Assert.Equal(11, band.Devices.Count);
        Assert.Equal(2, band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity);
    }

    [Fact]
    public void A_ruleset_without_line_bars_counts_none()
    {
        var rules = CatalogueFixture.Rules() with
        {
            Terminals = CatalogueFixture.Rules().Terminals with { LineBridgeBarWays = 0 }
        };

        var band = TerminalBandBuilder.Build([Switched(1, count: 4)], rules, CatalogueFixture.Catalogue());

        Assert.DoesNotContain(band.Accessories, a => a.DeviceTypeId == CatalogueFixture.LineBridgeId);
    }

    [Fact]
    public void A_count_beyond_the_limit_is_held_to_it()
    {
        var band = Build(Switched(1, count: 40));

        Assert.Equal(TerminalBandBuilder.MaxTerminalsPerCircuit, band.Devices.Count);
    }
```

Append to `DeviceDemandCalculatorTests.cs`, inside the class. Check the existing tests there for the exact `Calculate` arguments; they are `(circuits, rules, catalogue)`.

```csharp
    [Fact]
    public void Moving_a_circuit_along_the_terminal_rail_does_not_rewire_its_channel()
    {
        var circuits = Enumerable.Range(1, 4)
            .Select(n => new Circuit(Guid.NewGuid(), CircuitType.DimmedLighting, $"L{n}", null, n, null, null))
            .ToList();
        var reversed = circuits.Select((c, i) => c with { TerminalPosition = 4 - i }).ToList();

        var before = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());
        var after = DeviceDemandCalculator.Calculate(reversed, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        Assert.Equal(
            before.Devices.SelectMany(d => d.Channels).Select(c => c.CircuitId),
            after.Devices.SelectMany(d => d.Channels).Select(c => c.CircuitId));
    }
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter "FullyQualifiedName~TerminalBandBuilderTests|FullyQualifiedName~DeviceDemandCalculatorTests"`

Expected: a compile error, because `MaxTerminalsPerCircuit` doesn't exist. Once Step 4 adds it, the ordering, label and bar tests fail. The demand test passes already; it guards against a regression.

- [ ] **Step 4: Implement**

Replace the body of `TerminalBandBuilder` from `public static class TerminalBandBuilder` to the end of the file:

```csharp
public static class TerminalBandBuilder
{
    /// The most blocks one circuit may land on.
    public const int MaxTerminalsPerCircuit = 12;

    /// Placed circuits first, in position order, then the rest in Sequence
    /// order. With no positions at all this is plain Sequence order.
    public static IReadOnlyList<Circuit> InTerminalOrder(IEnumerable<Circuit> circuits) =>
        circuits
            .OrderBy(c => c.TerminalPosition is null)
            .ThenBy(c => c.TerminalPosition)
            .ThenBy(c => c.Sequence)
            .ToList();

    public static TerminalBand Build(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var terminals = rules.Terminals;
        var deviceType = catalogue.FindActive(terminals.DeviceTypeId);

        if (deviceType is null)
        {
            return new TerminalBand([], [], [
                new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for the terminal blocks (id {terminals.DeviceTypeId}).",
                    "Point the ruleset's terminal rules at an active catalogue entry.")
            ]);
        }

        if (circuits.Count == 0) return new TerminalBand([], [], []);

        var ordered = InTerminalOrder(circuits);
        var devices = new List<RequiredDevice>();
        var lineBars = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var circuit = ordered[i];
            var count = Math.Clamp(circuit.TerminalCount, 1, MaxTerminalsPerCircuit);

            for (var n = 0; n < count; n++)
            {
                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    // Numbered by place on the rail, so C1 is always the first
                    // terminal. A bridged circuit's blocks share its number.
                    count == 1 ? $"C{i + 1}" : $"C{i + 1}.{n + 1}",
                    [],
                    TerminalRole.All,
                    circuit.Id));
            }

            // Cut to length per circuit. An offcut is not counted as a bar.
            if (count > 1 && terminals.LineBridgeBarWays > 0)
            {
                lineBars += (int)Math.Ceiling(count / (double)terminals.LineBridgeBarWays);
            }
        }

        return new TerminalBand(devices, BuildAccessories(terminals, devices.Count, lineBars), []);
    }

    /// One bank, so one neutral bar run and one set of stops; line bars only
    /// for the circuits that bridge.
    private static IReadOnlyList<AccessoryLine> BuildAccessories(TerminalRules terminals, int blocks, int lineBars)
    {
        if (blocks == 0) return [];

        var accessories = new List<AccessoryLine>();

        var bars = terminals.BridgeBarWays <= 0
            ? 0
            : (int)Math.Ceiling(blocks / (double)terminals.BridgeBarWays);

        if (bars > 0) accessories.Add(new AccessoryLine(terminals.BridgeBarDeviceTypeId, bars));
        if (lineBars > 0) accessories.Add(new AccessoryLine(terminals.LineBridgeBarDeviceTypeId, lineBars));
        if (terminals.EndStopsPerBank > 0)
        {
            accessories.Add(new AccessoryLine(terminals.EndStopDeviceTypeId, terminals.EndStopsPerBank));
        }

        return accessories;
    }
}
```

Update the class doc comment above it. Replace "One 3-tier terminal block per circuit." with "One 3-tier terminal block per circuit, or several bridged on the line tier when a circuit asks for them (Circuit.TerminalCount). Laid in terminal order (InTerminalOrder), not Sequence."

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`

Expected: all pass. If the golden layout test fails, check the diff with the commands from Task 1, Step 6. The terminals' `circuitId` values are now set, so expect only `circuitId` lines on terminal devices to change. Then refresh it with `GOLDEN_UPDATE=1`.

- [ ] **Step 6: Commit**

```bash
git add -A src/PubInvest.HouseConfig.Domain tests/PubInvest.HouseConfig.Domain.Tests
git commit -m "feat: lay terminals in the chosen order and bridge a circuit across several"
```

---

### Task 3: Ship the line bar in the seed

**Files:**
- Modify: `seed/catalogue.v1.json`
- Modify: `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/ShippedSeedTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `ShippedSeedTests.cs`, next to the test that used to assert `BlocksPerCircuit` (around line 155). Load the seed the same way that test does.

```csharp
    [Fact]
    public void The_default_ruleset_bridges_line_tiers_with_a_catalogued_bar()
    {
        var seed = Load();
        var terminals = seed.RuleSets.Single(r => r.IsDefault).Payload.Terminals;

        Assert.True(terminals.LineBridgeBarWays > 0);
        var bar = Assert.Single(seed.DeviceTypes, d => d.Id == terminals.LineBridgeBarDeviceTypeId);
        Assert.Equal("Accessory", bar.Category);
    }
```

If the neighbouring tests call their loader something other than `Load()`, use that name.

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests --filter "FullyQualifiedName~ShippedSeedTests"`

Expected: FAIL, `LineBridgeBarWays` is 0.

- [ ] **Step 3: Add the part and bump the ruleset**

In `seed/catalogue.v1.json`, add this after the `d1000000-0000-4000-8000-000000000009` end stop entry:

```json
    {
      "id": "d1000000-0000-4000-8000-00000000000d",
      "manufacturer": "WAGO",
      "model": "Line jumper bar 10-way, cut to length (ASSUMED)",
      "partNumber": "ASSUMED-LINE-JUMPER-BAR-10",
      "category": "Accessory",
      "moduleWidth": 0,
      "channelCount": 0,
      "maxLoadPerChannelW": null,
      "maxTotalLoadW": null,
      "active": true
    },
```

In the default ruleset, change `"version": 3` to `"version": 4`. The seeder replaces a stored ruleset only when the shipped version is higher. Add this to `"terminals"`:

```json
          "endStopsPerBank": 2,
          "lineBridgeBarDeviceTypeId": "d1000000-0000-4000-8000-00000000000d",
          "lineBridgeBarWays": 10
```

In `TestSeed.cs`, add `public static readonly Guid LineBridgeId = new("aaaa0000-0000-4000-8000-000000000010");` next to `EndStopId`. Add `new SeedDeviceType(LineBridgeId, "Test", "Line jumper bar", "T-LBAR", "Accessory", 0, 0, null, null, true),` after the end stop. Add `LineBridgeBarDeviceTypeId: LineBridgeId, LineBridgeBarWays: 10` to its `TerminalRules` (add a comma after `EndStopsPerBank: 2`).

- [ ] **Step 4: Run the Data tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests`

Expected: all pass. If a test asserts the default ruleset is version 3, change it to 4.

- [ ] **Step 5: Commit**

```bash
git add seed tests
git commit -m "feat: ship an assumed line jumper bar and point the ruleset at it"
```

---

### Task 4: Store the new fields

**Files:**
- Modify: `src/PubInvest.HouseConfig.Data/Entities/CircuitRow.cs`
- Modify: `src/PubInvest.HouseConfig.Data/Entities/DeviceInstance.cs`
- Modify: `src/PubInvest.HouseConfig.Data/Mapping/DomainMapper.cs`
- Create: `src/PubInvest.HouseConfig.Data/Migrations/<timestamp>_CircuitTerminals.cs` (generated)
- Test: `tests/PubInvest.HouseConfig.Data.Tests/PersistenceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PersistenceTests.cs`, following `A_submain_with_circuits_and_devices_round_trips`. Copy how that test creates a project, submain and context; only the asserted fields are new.

```csharp
    [Fact]
    public async Task A_circuits_terminal_place_and_a_terminals_circuit_round_trip()
    {
        // Arrange a project and submain exactly as the round-trip test above does.
        var circuitId = Guid.NewGuid();
        submain.Circuits.Add(new CircuitRow
        {
            Id = circuitId, Type = "Switched", Name = "Hall", Sequence = 1,
            TerminalPosition = 4, TerminalCount = 6
        });
        db.DeviceInstances.Add(new DeviceInstance
        {
            Id = Guid.NewGuid(), SubmainId = submain.Id, DeviceTypeId = Guid.NewGuid(),
            Category = "Terminal240", RowIndex = 0, StartSlot = 0, ModuleWidth = 1,
            Label = "C4.1", TerminalRole = "All", CircuitId = circuitId
        });
        await db.SaveChangesAsync();

        await using var fresh = fixture.NewDbContext();
        var circuit = await fresh.Circuits.SingleAsync(c => c.Id == circuitId);
        var terminal = await fresh.DeviceInstances.SingleAsync(d => d.SubmainId == submain.Id);

        Assert.Equal(4, circuit.TerminalPosition);
        Assert.Equal(6, circuit.TerminalCount);
        Assert.Equal(circuitId, terminal.CircuitId);
    }

    [Fact]
    public void A_new_circuit_row_lands_on_one_terminal()
    {
        Assert.Equal(1, new CircuitRow().TerminalCount);
        Assert.Null(new CircuitRow().TerminalPosition);
    }
```

Name the fixture's context factory however `PostgresFixture` names it.

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests --filter "FullyQualifiedName~PersistenceTests"`

Expected: compile errors, because the properties don't exist.

- [ ] **Step 3: Add the properties and mapping**

`CircuitRow.cs`, after `LengthMetres`:

```csharp
    public int? TerminalPosition { get; set; }
    public int TerminalCount { get; set; } = 1;
```

`DeviceInstance.cs`, after `TerminalRole`:

```csharp
    /// Terminals only: the circuit landing on this block.
    public Guid? CircuitId { get; set; }
```

`DomainMapper.ToDomain(CircuitRow row)`:

```csharp
    public static Circuit ToDomain(CircuitRow row) => new(
        row.Id, Enum.Parse<CircuitType>(row.Type), row.Name, row.Room, row.Sequence,
        row.WattsPerMetre, row.LengthMetres, row.TerminalPosition, row.TerminalCount);
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add CircuitTerminals --project src/PubInvest.HouseConfig.Data
```

Open the generated `*_CircuitTerminals.cs`. In the `AddColumn<int>` for `TerminalCount`, change `defaultValue: 0` to `defaultValue: 1`, so existing circuits land on one terminal. Check that it adds three columns: `Circuits.TerminalPosition` (nullable), `Circuits.TerminalCount` and `DeviceInstances.CircuitId` (nullable uuid). Check that it changes nothing else.

- [ ] **Step 5: Run the Data tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests`

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/PubInvest.HouseConfig.Data tests/PubInvest.HouseConfig.Data.Tests
git commit -m "feat: store a circuit's terminal place and count, and a terminal's circuit"
```

---

### Task 5: Generate, layout and revisions carry `CircuitId` and the terminal fields

**Files:**
- Modify: `src/PubInvest.HouseConfig.Api/Contracts/DesignContracts.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Services/DesignService.cs` (BuildAsync circuit list near line 83; device persistence near line 180; `ToResponse` near line 231)
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/DesignEndpoints.cs` (near line 101)
- Modify: `src/PubInvest.HouseConfig.Api/Revisions/RevisionSnapshot.cs`, `RevisionService.cs` (near lines 123 and 161)
- Test: create `tests/PubInvest.HouseConfig.Api.Tests/TerminalEndpointTests.cs` (Task 6 adds to it)

- [ ] **Step 1: Write the failing tests**

Create `tests/PubInvest.HouseConfig.Api.Tests/TerminalEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// The order circuits land along the terminal rail, and how many terminals each
/// lands on, are the engineer's to choose — without rewiring any Shelly.
[Collection("api")]
public class TerminalEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record ChannelDto(int ChannelIndex, Guid? CircuitId, string? CircuitName, string? CircuitRoom, bool IsSpare);
    private record DeviceDto(Guid? Id, string Category, string Label, Guid? CircuitId, ChannelDto[] Channels);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record BomLineDto(Guid CatalogueId, int Quantity);
    private record DesignDto(int LayoutVersion, LayoutDto Layout, BomLineDto[] Bom);
    private record TerminalCircuitDto(Guid CircuitId, string Name, string? Room, string Type, int TerminalCount);
    private record TerminalsDto(int LayoutVersion, TerminalCircuitDto[] Circuits);

    private static readonly object[] FourCircuits =
    [
        new { type = "DimmedLighting", name = "Kitchen", sequence = 1 },
        new { type = "DimmedLighting", name = "Hall", sequence = 2 },
        new { type = "Switched", name = "Landing", sequence = 3 },
        new { type = "Switched", name = "Porch", sequence = 4 },
    ];

    private async Task<(Guid SubmainId, DesignDto Design)> Generated(HttpClient client, object[]? circuits = null)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Terminals {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Terminals",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits = circuits ?? FourCircuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var design = await (await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();

        return (submain.Id, design!);
    }

    private static string[] TerminalLabels(DesignDto design, Guid circuitId) =>
        design.Layout.Devices.Where(d => d.Category == "Terminal240" && d.CircuitId == circuitId)
            .Select(d => d.Label).ToArray();

    private static Guid CircuitOnChannel(DesignDto design, string name) =>
        design.Layout.Devices.SelectMany(d => d.Channels).Single(c => c.CircuitName == name).CircuitId!.Value;

    [Fact]
    public async Task Generate_and_the_stored_layout_name_the_circuit_on_each_terminal()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client);
        var kitchen = CircuitOnChannel(design, "Kitchen");

        Assert.Equal(["C1"], TerminalLabels(design, kitchen));

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C1"], TerminalLabels(stored!, kitchen));
    }

    [Fact]
    public async Task A_terminal_is_not_a_channel_so_a_circuit_can_still_be_moved_between_channels()
    {
        var client = factory.CreateClient();
        var (submainId, design) = await Generated(client);
        var kitchen = CircuitOnChannel(design, "Kitchen");
        var dimmer = design.Layout.Devices.First(d => d.Channels.Any(c => c.CircuitId == kitchen));
        var index = dimmer.Channels.Single(c => c.CircuitId == kitchen).ChannelIndex;

        // Free it, then wire it back: the terminal must not count as "wired elsewhere".
        var freed = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/channels/{index}",
            new { circuitId = (Guid?)null, isSpare = true, basedOnLayoutVersion = design.LayoutVersion });
        freed.EnsureSuccessStatusCode();
        var version = (await freed.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["layoutVersion"];

        var rewired = await client.PatchAsJsonAsync($"/devices/{dimmer.Id}/channels/{index}",
            new { circuitId = kitchen, isSpare = false, basedOnLayoutVersion = version });

        Assert.Equal(HttpStatusCode.OK, rewired.StatusCode);
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~TerminalEndpointTests"`

Expected: `Generate_and_the_stored_layout…` fails, because `CircuitId` is null in the responses. The channel test passes already; it guards the decision not to use channels.

- [ ] **Step 3: Add `CircuitId` to the response contract**

In `DesignContracts.cs`:

```csharp
public sealed record PlacedDeviceResponse(
    Guid? Id,
    Guid DeviceTypeId,
    string Category,
    int RowIndex,
    int StartSlot,
    int ModuleWidth,
    string Label,
    string TerminalRole,
    IReadOnlyList<ChannelResponse> Channels,
    /// Terminals only: the circuit landing on this block.
    Guid? CircuitId = null);
```

- [ ] **Step 4: Fill it in everywhere a device is built**

`DesignService.GenerateAsync`: in `new Data.Entities.DeviceInstance { … }`, add `CircuitId = placed.CircuitId,` after `TerminalRole = …`.

`DesignService.ToResponse`: after the channel list argument of `new PlacedDeviceResponse(`, add `, d.CircuitId`. The call now ends `}).ToList(), d.CircuitId);`.

`DesignEndpoints.cs` (GET layout): the same. After the channels `.ToList()`, add `, d.CircuitId`, so it ends `}).ToList(), d.CircuitId)).ToList();`.

`RevisionService.cs`, in `new PlacedDevice(`: after `Enum.Parse<TerminalRole>(d.TerminalRole)`, add `, d.CircuitId`.

- [ ] **Step 5: Carry the terminal fields through revisions and request-supplied circuits**

`RevisionSnapshot.cs`:

```csharp
/// TerminalPosition and TerminalCount are nullable so revisions issued before
/// they existed still read.
public sealed record CircuitSnapshot(
    Guid Id, string Type, string Name, string? Room, int Sequence,
    int? TerminalPosition = null, int? TerminalCount = null);
```

`RevisionService.cs`, the snapshot circuits:

```csharp
                .Select(c => new CircuitSnapshot(
                    c.Id, c.Type, c.Name, c.Room, c.Sequence, c.TerminalPosition, c.TerminalCount))
```

`DesignService.BuildAsync`: replace the `var circuits = …` expression. A circuit list in the request (the wizard, or an edit) knows nothing of terminal places, so it takes them from the stored circuit with the same id:

```csharp
        var stored = (submain?.Circuits ?? []).ToDictionary(c => c.Id);

        var circuits = overrides?.Circuits is { Count: > 0 }
            ? overrides.Circuits.Select(c =>
            {
                var id = c.Id ?? Guid.NewGuid();
                var known = stored.GetValueOrDefault(id);
                return new Circuit(
                    id, Enum.Parse<CircuitType>(c.Type), c.Name, c.Room, c.Sequence,
                    c.WattsPerMetre, c.LengthMetres,
                    known?.TerminalPosition, known?.TerminalCount ?? 1);
            }).ToList()
            : (submain?.Circuits ?? []).Select(DomainMapper.ToDomain).ToList();
```

Leave `SubmainEndpoints.ApplyCircuits` as it is. It copies named fields onto an existing row, so `TerminalPosition` and `TerminalCount` survive an edit already. A new `CircuitRow` gets null and 1.

- [ ] **Step 6: Run the API tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`

Expected: all pass. If `PanelSvgRendererTests` fails its golden check, leave it for Task 7.

- [ ] **Step 7: Commit**

```bash
git add src/PubInvest.HouseConfig.Api tests/PubInvest.HouseConfig.Api.Tests
git commit -m "feat: report which circuit lands on each terminal"
```

---

### Task 6: `GET` and `PUT /submains/{id}/terminals`

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Contracts/TerminalContracts.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/TerminalEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs` (next to `app.MapCircuitEndpoints()`)
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/SubmainEndpoints.cs` (make `Refusal` `internal`)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/TerminalEndpointTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `TerminalEndpointTests`:

```csharp
    private static object Put(int layoutVersion, params (Guid Id, int Count)[] circuits) => new
    {
        layoutVersion,
        circuits = circuits.Select(c => new { circuitId = c.Id, terminalCount = c.Count }).ToArray()
    };

    private async Task<(Guid SubmainId, DesignDto Design, TerminalsDto Terminals)> WithTerminals(HttpClient client)
    {
        var (submainId, design) = await Generated(client);
        var terminals = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        return (submainId, design, terminals!);
    }

    [Fact]
    public async Task Get_lists_the_circuits_in_terminal_order()
    {
        var client = factory.CreateClient();
        var (_, design, terminals) = await WithTerminals(client);

        Assert.Equal(["Kitchen", "Hall", "Landing", "Porch"], terminals.Circuits.Select(c => c.Name));
        Assert.All(terminals.Circuits, c => Assert.Equal(1, c.TerminalCount));
        Assert.Equal(design.LayoutVersion, terminals.LayoutVersion);
    }

    [Fact]
    public async Task Get_for_an_unknown_submain_is_not_found()
    {
        var response = await factory.CreateClient().GetAsync($"/submains/{Guid.NewGuid()}/terminals");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reordering_relabels_the_terminals_and_leaves_every_channel_alone()
    {
        var client = factory.CreateClient();
        var (submainId, design, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();
        var channelsBefore = design.Layout.Devices.Where(d => d.Category != "Terminal240")
            .Select(d => (d.Label, string.Join(",", d.Channels.Select(c => c.CircuitId))));

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[3], 1), (ids[0], 1), (ids[1], 1), (ids[2], 1)));
        response.EnsureSuccessStatusCode();

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C1"], TerminalLabels(stored!, ids[3]));
        Assert.Equal(["C2"], TerminalLabels(stored!, ids[0]));
        Assert.Equal(channelsBefore, stored!.Layout.Devices.Where(d => d.Category != "Terminal240")
            .Select(d => (d.Label, string.Join(",", d.Channels.Select(c => c.CircuitId)))));

        var after = await response.Content.ReadFromJsonAsync<TerminalsDto>();
        Assert.Equal(["Porch", "Kitchen", "Hall", "Landing"], after!.Circuits.Select(c => c.Name));
        Assert.True(after.LayoutVersion > terminals.LayoutVersion);
    }

    [Fact]
    public async Task Six_cables_on_one_circuit_gives_six_bridged_terminals_and_a_line_bar()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[0], 1), (ids[1], 6), (ids[2], 1), (ids[3], 1)));
        response.EnsureSuccessStatusCode();

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C2.1", "C2.2", "C2.3", "C2.4", "C2.5", "C2.6"], TerminalLabels(stored!, ids[1]));
        Assert.Equal(["C3"], TerminalLabels(stored!, ids[2]));

        // The BOM comes from generating, so ask for one.
        var design = await (await client.PostAsJsonAsync($"/submains/{submainId}/design/generate", new { }))
            .Content.ReadFromJsonAsync<DesignDto>();
        Assert.Equal(1, design!.Bom.Single(l => l.CatalogueId == TestSeed.LineBridgeId).Quantity);
    }

    [Fact]
    public async Task A_stale_layout_version_is_a_conflict()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion - 1, ids.Select(id => (id, 1)).ToArray()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    public static TheoryData<string> BadLists => ["missing", "duplicate", "foreign", "zero", "thirteen"];

    [Theory]
    [MemberData(nameof(BadLists))]
    public async Task A_list_that_is_not_every_circuit_once_with_a_sane_count_is_refused(string fault)
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        var circuits = fault switch
        {
            "missing" => ids.Skip(1).Select(id => (id, 1)),
            "duplicate" => ids.Append(ids[0]).Select(id => (id, 1)),
            "foreign" => ids.Skip(1).Append(Guid.NewGuid()).Select(id => (id, 1)),
            "zero" => ids.Select((id, i) => (id, i == 0 ? 0 : 1)),
            _ => ids.Select((id, i) => (id, i == 0 ? 13 : 1)),
        };

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, circuits.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.Equal(terminals.LayoutVersion, after!.LayoutVersion);
    }

    [Fact]
    public async Task A_count_that_overfills_the_panel_saves_nothing()
    {
        // The test box is 6 rows of 24 slot units: 144 in all, and a terminal is
        // one. Twelve circuits on twelve terminals each is 144 terminals before
        // the relays and isolator, so it cannot fit.
        var client = factory.CreateClient();
        var twelve = Enumerable.Range(1, 12)
            .Select(n => (object)new { type = "Switched", name = $"S{n}", sequence = n }).ToArray();
        var (submainId, _) = await Generated(client, twelve);
        var terminals = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");

        var response = await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals!.LayoutVersion, terminals.Circuits.Select(c => (c.CircuitId, 12)).ToArray()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.All(after!.Circuits, c => Assert.Equal(1, c.TerminalCount));
        Assert.Equal(terminals.LayoutVersion, after.LayoutVersion);
    }

    [Fact]
    public async Task The_chosen_order_survives_an_edit_to_the_circuits_and_a_rebuild()
    {
        var client = factory.CreateClient();
        var (submainId, _, terminals) = await WithTerminals(client);
        var ids = terminals.Circuits.Select(c => c.CircuitId).ToArray();

        (await client.PutAsJsonAsync($"/submains/{submainId}/terminals",
            Put(terminals.LayoutVersion, (ids[3], 2), (ids[0], 1), (ids[1], 1), (ids[2], 1)))).EnsureSuccessStatusCode();

        // Rename one circuit through the submain edit, which resends the whole list, and rebuild.
        (await client.PatchAsJsonAsync($"/submains/{submainId}", new
        {
            name = "Terminals",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits = terminals.Circuits.Select((c, i) => new
            {
                id = c.CircuitId, type = c.Type, name = c.Name == "Kitchen" ? "Kitchen spots" : c.Name, sequence = i + 1
            }).ToArray(),
            rebuild = true
        })).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<TerminalsDto>($"/submains/{submainId}/terminals");
        Assert.Equal(["Porch", "Kitchen spots", "Hall", "Landing"], after!.Circuits.Select(c => c.Name));
        Assert.Equal(2, after.Circuits[0].TerminalCount);

        var stored = await client.GetFromJsonAsync<DesignDto>($"/submains/{submainId}/layout");
        Assert.Equal(["C1.1", "C1.2"], TerminalLabels(stored!, ids[3]));
    }
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~TerminalEndpointTests"`

Expected: the new tests fail with 404 or 405, because the route doesn't exist.

- [ ] **Step 3: Add the contracts**

Create `src/PubInvest.HouseConfig.Api/Contracts/TerminalContracts.cs`:

```csharp
namespace PubInvest.HouseConfig.Api.Contracts;

/// One circuit as the terminal sheet shows it, in terminal order.
public sealed record TerminalCircuitResponse(Guid CircuitId, string Name, string? Room, string Type, int TerminalCount);

public sealed record TerminalsResponse(int LayoutVersion, IReadOnlyList<TerminalCircuitResponse> Circuits);

public sealed record TerminalCircuitRequest(Guid CircuitId, int TerminalCount);

/// Every circuit of the submain, once, in the order they should land on the rail.
public sealed record UpdateTerminalsRequest(int LayoutVersion, IReadOnlyList<TerminalCircuitRequest>? Circuits);
```

- [ ] **Step 4: Add the endpoints**

In `SubmainEndpoints.cs`, change `private static IResult? Refusal(` to `internal static IResult? Refusal(`.

Create `src/PubInvest.HouseConfig.Api/Endpoints/TerminalEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Services;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class TerminalEndpoints
{
    public static IEndpointRouteBuilder MapTerminalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/submains/{id:guid}/terminals", async (
                Guid id,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var response = await Load(db, id, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithTags("Terminals");

        // Order and counts are saved together and the panel is rebuilt in the
        // same transaction, as an install option is: extra terminals take rail
        // space, and a saved order over an unchanged drawing is a state nobody
        // can see. A refused rebuild rolls the save back.
        app.MapPut("/submains/{id:guid}/terminals", async (
                Guid id,
                UpdateTerminalsRequest request,
                HouseConfigDbContext db,
                DesignService designs,
                CancellationToken ct) =>
            {
                var submain = await db.Submains
                    .Include(s => s.Circuits)
                    .SingleOrDefaultAsync(s => s.Id == id, ct);
                if (submain is null) return Results.NotFound();

                if (submain.LayoutVersion != request.LayoutVersion)
                {
                    return Results.Json(
                        new ConflictResponse(
                            "This panel changed since you loaded it. Reload and try again.",
                            submain.LayoutVersion),
                        statusCode: StatusCodes.Status409Conflict);
                }

                var problem = Validate(request.Circuits, submain.Circuits.Select(c => c.Id).ToHashSet());
                if (problem is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["circuits"] = [problem] });
                }

                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                var byId = submain.Circuits.ToDictionary(c => c.Id);
                foreach (var (entry, index) in request.Circuits!.Select((c, i) => (c, i)))
                {
                    byId[entry.CircuitId].TerminalPosition = index + 1;
                    byId[entry.CircuitId].TerminalCount = entry.TerminalCount;
                }

                await db.SaveChangesAsync(ct);

                var refusal = SubmainEndpoints.Refusal(await designs.GenerateAsync(id, null, ct));
                if (refusal is not null) return refusal;

                await transaction.CommitAsync(ct);
                return Results.Ok(await Load(db, id, ct));
            })
            .WithTags("Terminals");

        return app;
    }

    private static string? Validate(IReadOnlyList<TerminalCircuitRequest>? circuits, HashSet<Guid> known)
    {
        if (circuits is null || circuits.Count != known.Count
            || circuits.Select(c => c.CircuitId).Distinct().Count() != circuits.Count
            || circuits.Any(c => !known.Contains(c.CircuitId)))
        {
            return "List every circuit on this submain exactly once.";
        }

        if (circuits.Any(c => c.TerminalCount is < 1 or > TerminalBandBuilder.MaxTerminalsPerCircuit))
        {
            return $"A circuit lands on 1 to {TerminalBandBuilder.MaxTerminalsPerCircuit} terminals.";
        }

        return null;
    }

    private static async Task<TerminalsResponse?> Load(HouseConfigDbContext db, Guid id, CancellationToken ct)
    {
        var submain = await db.Submains
            .AsNoTracking()
            .Include(s => s.Circuits)
            .SingleOrDefaultAsync(s => s.Id == id, ct);
        if (submain is null) return null;

        var rows = submain.Circuits.ToDictionary(c => c.Id);
        var ordered = TerminalBandBuilder.InTerminalOrder(submain.Circuits.Select(DomainMapper.ToDomain));

        return new TerminalsResponse(
            submain.LayoutVersion,
            ordered.Select(c => new TerminalCircuitResponse(
                c.Id, c.Name, c.Room, rows[c.Id].Type, c.TerminalCount)).ToList());
    }
}
```

In `Program.cs`, add `app.MapTerminalEndpoints();` next to `app.MapCircuitEndpoints();`. If the endpoints are mapped onto a group (for example one with `RequireAuthorization`), map it on the same group.

- [ ] **Step 5: Run the API tests**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/PubInvest.HouseConfig.Api tests/PubInvest.HouseConfig.Api.Tests
git commit -m "feat: read and save the terminal order and counts, rebuilding the panel"
```

---

### Task 7: The PDF names each terminal

**Files:**
- Modify: `src/PubInvest.HouseConfig.Api/Documents/PanelDocument.cs` (`Schedule`, `ScheduleRow`, `Rows`)
- Modify: `src/PubInvest.HouseConfig.Api/Rendering/PanelSvgRenderer.cs` (`RenderTerminalGroups`, `Group`, `GroupTerminals`)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/PanelDocumentTests.cs`, `PanelSvgRendererTests.cs`
- Golden: `tests/PubInvest.HouseConfig.Api.Tests/Golden/panel.svg`

- [ ] **Step 1: Write the failing tests**

In `PanelDocumentTests.cs`, find how an existing test extracts text from the PDF with PdfPig. Copy its helper usage, then add:

```csharp
    [Fact]
    public void The_schedule_says_which_terminal_each_circuit_lands_on()
    {
        var terminalType = Guid.NewGuid();
        var snapshot = Snapshot(
        [
            new(terminalType, DeviceCategory.Terminal240, 0, 0, 1, "C1", [], TerminalRole.All, CircuitB),
            new(terminalType, DeviceCategory.Terminal240, 0, 1, 1, "C2.1", [], TerminalRole.All, CircuitA),
            new(terminalType, DeviceCategory.Terminal240, 0, 2, 1, "C2.2", [], TerminalRole.All, CircuitA),
            new(DimmerType, DeviceCategory.Dimmer240, 1, 0, 3, "Dimmer 1",
                [new ChannelAssignment(0, CircuitA, false), new ChannelAssignment(1, null, true)], TerminalRole.None),
            new(RelayType, DeviceCategory.Relay, 2, 0, 9, "Relay 1",
                [new ChannelAssignment(0, CircuitB, false)], TerminalRole.None),
        ]);

        var text = TextOf(snapshot);

        Assert.Contains("Terminal", text);
        Assert.Contains("C2.1–C2.2", text);
        Assert.Contains("C1", text);
    }
```

`TextOf` stands for whatever the file's existing tests use to render and extract text. If there is no such helper, extract one from the nearest test that renders the document and reads `page.Text`.

In `PanelSvgRendererTests.cs`, add:

```csharp
    [Fact]
    public void Each_terminal_in_a_run_is_labelled_and_a_bridged_circuit_gets_a_bar()
    {
        var circuit = Guid.NewGuid();
        var type = Guid.NewGuid();
        var layout = new PanelLayout(1, 12,
        [
            new(type, DeviceCategory.Terminal240, 0, 0, 1, "C1", [], TerminalRole.All, Guid.NewGuid()),
            new(type, DeviceCategory.Terminal240, 0, 1, 1, "C2.1", [], TerminalRole.All, circuit),
            new(type, DeviceCategory.Terminal240, 0, 2, 1, "C2.2", [], TerminalRole.All, circuit),
        ]);

        var svg = PanelSvgRenderer.Render(layout, new Dictionary<Guid, string>());

        Assert.Contains(">C1<", svg);
        Assert.Contains(">C2.1<", svg);
        Assert.Contains(">C2.2<", svg);
        Assert.Contains("data-bridge=\"C2.1–C2.2\"", svg);
    }
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~PanelDocumentTests|FullyQualifiedName~PanelSvgRendererTests"`

Expected: the new tests fail.

- [ ] **Step 3: Add the schedule's Terminal column**

In `PanelDocument.cs`:

```csharp
    private sealed record ScheduleRow(string Terminal, string Circuit, string Room, string Type, string Device, string Channel, string Position);
```

At the top of `Rows()`, add:

```csharp
        // A circuit's terminal labels as one cell: "C3", or "C3.1–C3.6" when bridged.
        var terminals = snapshot.Layout.Devices
            .Where(d => d.Category == DeviceCategory.Terminal240 && d.CircuitId is not null)
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .GroupBy(d => d.CircuitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Count() == 1 ? g.First().Label : $"{g.First().Label}–{g.Last().Label}");
```

The `yield return` becomes:

```csharp
                yield return new ScheduleRow(
                    circuit is not null && terminals.TryGetValue(circuit.Id, out var terminal) ? terminal : "",
                    circuit?.Name ?? "— spare —",
                    circuit?.Room ?? "",
                    circuit is null ? "" : Wording.CircuitType(circuit.Type),
                    device.Label,
                    (channel.ChannelIndex + 1).ToString(CultureInfo.InvariantCulture),
                    $"row {device.RowIndex + 1}, module {ModulePosition(device.StartSlot)}");
```

In `Schedule`, add `columns.RelativeColumn(2);   // terminal` as the first column definition. Make the headings `new[] { "Terminal", "Circuit", "Room", "Type", "Device", "Ch", "Position" }`. Add `table.Cell().Element(BodyCell).Text(row.Terminal);` before the circuit cell.

- [ ] **Step 4: Draw each terminal in the PDF drawing**

In `PanelSvgRenderer.cs`, the group keeps its members:

```csharp
    private sealed record Group(int RowIndex, int StartSlot, int Slots, string Label, TerminalRole Role, List<PlacedDevice> Members);
```

In `GroupTerminals`, for the merge branch use `Members = [.. last.Members, device]`. Construct the else branch with `[device]` as the last argument.

Replace the text part of `RenderTerminalGroups`, after the group `<rect>`:

```csharp
            // Each terminal on its own: a divider between neighbours, and its
            // label on its side, as the screen draws it.
            foreach (var (member, i) in group.Members.Select((m, i) => (m, i)))
            {
                var mx = PanelGeometry.SlotToX(member.StartSlot);
                var mw = PanelGeometry.SlotToX(member.ModuleWidth);

                if (i > 0)
                {
                    svg.Append(CultureInfo.InvariantCulture,
                        $"<line x1=\"{N(mx)}\" y1=\"{N(y)}\" x2=\"{N(mx)}\" y2=\"{N(y + h)}\" ")
                       .Append("stroke=\"#94a3b8\" stroke-width=\"0.2\"/>");
                }

                var cx = mx + mw / 2;
                var cy = y + h - 2;
                svg.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{N(cx)}\" y=\"{N(cy)}\" transform=\"rotate(-90 {N(cx)} {N(cy)})\" ")
                   .Append("dominant-baseline=\"middle\" font-size=\"2.6\" fill=\"#1e293b\">")
                   .Append(SecurityElement.Escape(member.Label))
                   .Append("</text>");
            }

            // A bar along the top of each circuit bridged across several blocks.
            foreach (var bridged in group.Members
                         .Where(m => m.CircuitId is not null)
                         .GroupBy(m => m.CircuitId)
                         .Where(g => g.Count() > 1))
            {
                var first = bridged.First();
                var last = bridged.Last();
                var bx = PanelGeometry.SlotToX(first.StartSlot) + 0.6;
                var bw = PanelGeometry.SlotToX(last.EndSlotExclusive) - PanelGeometry.SlotToX(first.StartSlot) - 1.2;

                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect data-bridge=\"{SecurityElement.Escape($"{first.Label}–{last.Label}")}\" ")
                   .Append(CultureInfo.InvariantCulture,
                       $"x=\"{N(bx)}\" y=\"{N(y + 1)}\" width=\"{N(bw)}\" height=\"1.2\" rx=\"0.4\" fill=\"#475569\"/>");
            }
```

Delete the old centred `<text>` that wrote `group.Label`. The `Label` field and its merging code in `GroupTerminals` are no longer used, so remove them too.

- [ ] **Step 5: Run the tests and refresh the golden SVG**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~PanelDocumentTests|FullyQualifiedName~PanelSvgRendererTests"`

The new tests pass and the golden test fails. Refresh it, then read the diff:

```bash
GOLDEN_UPDATE=1 dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter "FullyQualifiedName~PanelSvgRendererTests"
git diff tests/PubInvest.HouseConfig.Api.Tests/Golden/panel.svg
```

Expected: only terminal groups change. The run label is replaced by per-terminal labels and dividers. Then run the whole API suite: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`. Everything should pass.

- [ ] **Step 6: Commit**

```bash
git add src/PubInvest.HouseConfig.Api tests/PubInvest.HouseConfig.Api.Tests
git commit -m "feat: name each terminal in the PDF drawing and schedule"
```

---

### Task 8: UI types, client and terminal labels

**Files:**
- Modify: `ui/src/api/types.ts`
- Modify: `ui/src/api/client.ts` (the `api` object at the bottom)
- Create: `ui/src/panel/terminalLabels.ts`
- Test: create `ui/tests/terminalLabels.test.ts`

- [ ] **Step 1: Write the failing test**

Create `ui/tests/terminalLabels.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { terminalLabel } from '../src/panel/terminalLabels'

describe('terminalLabel', () => {
  it('is the bare number for a circuit on one terminal', () => {
    expect(terminalLabel(3, 1)).toBe('C3')
  })

  it('is the range for a bridged circuit', () => {
    expect(terminalLabel(2, 6)).toBe('C2.1–C2.6')
  })
})
```

- [ ] **Step 2: Run to confirm it fails**

Run: `cd ui && npx vitest run tests/terminalLabels.test.ts`

Expected: FAIL, module not found.

- [ ] **Step 3: Implement the helper, the types and the client method**

Create `ui/src/panel/terminalLabels.ts`:

```ts
/**
 * A circuit's terminals as the server labels them: "C3" on one block, or
 * "C3.1–C3.6" across six bridged ones. `position` counts from 1.
 */
export function terminalLabel(position: number, count: number): string {
  return count <= 1 ? `C${position}` : `C${position}.1–C${position}.${count}`
}
```

In `ui/src/api/types.ts`, add to `PlacedDeviceResponse` after `channels`:

```ts
  /** Terminals only: the circuit landing on this block. Absent from older responses. */
  circuitId?: string | null
```

At the end of the file, add:

```ts
/** One circuit as the terminal sheet shows it, in terminal order. */
export interface TerminalCircuit {
  circuitId: string
  name: string
  room: string | null
  type: CircuitType
  terminalCount: number
}

export interface TerminalsResponse {
  layoutVersion: number
  circuits: TerminalCircuit[]
}

/** The most terminals one circuit may land on. Mirrors TerminalBandBuilder.MaxTerminalsPerCircuit. */
export const MAX_TERMINALS_PER_CIRCUIT = 12
```

In `ui/src/api/client.ts`, add `put` to the `api` object:

```ts
export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
}
```

- [ ] **Step 4: Run the tests and typecheck**

Run: `cd ui && npx vitest run tests/terminalLabels.test.ts && npm run build`

Expected: PASS, and the build succeeds.

- [ ] **Step 5: Commit**

```bash
git add ui/src ui/tests
git commit -m "feat(ui): types and a label helper for terminal order"
```

---

### Task 9: `PanelSvg` draws each terminal, and the run opens the sheet

**Files:**
- Modify: `ui/src/panel/PanelSvg.tsx`
- Test: `ui/tests/PanelSvg.test.tsx`

- [ ] **Step 1: Write the failing tests**

In `ui/tests/PanelSvg.test.tsx`, replace the test `'groups a run of terminals into one labelled block'` with:

```tsx
  it('draws a run of terminals as one block with each terminal labelled', () => {
    const { container } = render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    // Without a handler the run is still a picture: nothing to open.
    expect(screen.getByRole('img', { name: /L1.*L3/ })).toBeInTheDocument()
    const labels = [...container.querySelectorAll('text')].map(t => t.textContent)
    expect(labels).toEqual(expect.arrayContaining(['L1', 'L2', 'L3']))
  })

  it('opens the terminal order from the run when it can', async () => {
    const onOpenTerminals = vi.fn()
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} onOpenTerminals={onOpenTerminals} />)

    await userEvent.click(screen.getByRole('button', { name: /terminals L1–L3/i }))

    expect(onOpenTerminals).toHaveBeenCalled()
  })

  it('marks a circuit bridged across several terminals with a bar', () => {
    const bridged: LayoutResponse = {
      rows: 1, slotsPerRow: 54,
      devices: [
        { ...terminal(1, 0), label: 'C1', circuitId: 'a' },
        { ...terminal(2, 1), label: 'C2.1', circuitId: 'b' },
        { ...terminal(3, 2), label: 'C2.2', circuitId: 'b' },
      ],
    }
    const { container } = render(<PanelSvg layout={bridged} selectedId={null} onSelect={() => {}} />)

    expect(container.querySelector('[data-bridge="C2.1–C2.2"]')).not.toBeNull()
    expect(container.querySelectorAll('[data-bridge]')).toHaveLength(1)
  })
```

- [ ] **Step 2: Run to confirm they fail**

Run: `cd ui && npx vitest run tests/PanelSvg.test.tsx`

Expected: the three new tests fail.

- [ ] **Step 3: Implement**

In `PanelSvg.tsx`, add this to `Props`:

```ts
  /** Opens the terminal order. Without it the terminal run is only a picture. */
  onOpenTerminals?: () => void
```

Destructure it: `export function PanelSvg({ layout, selectedId, onSelect, dragging, zoom = 1, onOpenTerminals }: Props) {`.

Add this helper above `PanelSvg`:

```ts
/** Each circuit landing on more than one terminal in a run, as first and last member. */
function bridgedGroups(members: PlacedDeviceResponse[]) {
  const byCircuit = new Map<string, PlacedDeviceResponse[]>()
  for (const m of members) {
    if (!m.circuitId) continue
    byCircuit.set(m.circuitId, [...(byCircuit.get(m.circuitId) ?? []), m])
  }
  return [...byCircuit.values()]
    .filter(group => group.length > 1)
    .map(group => ({ first: group[0], last: group.at(-1)! }))
}
```

Replace the terminal `groups.map(...)` block, including its comment, with:

```tsx
      {/* A run of terminals is one block, each terminal labelled on its side,
          with a bar over each circuit bridged across several. The run opens
          the terminal order when the page offers it. */}
      {groups.map(group => {
        const rect = inset(deviceRect(group))
        const style = deviceStyle('Terminal240')
        const interactive = Boolean(onOpenTerminals)
        return (
          <g
            key={group.key}
            role={interactive ? 'button' : 'img'}
            tabIndex={interactive ? 0 : undefined}
            aria-label={interactive ? `Terminals ${group.label}, change order` : `Terminals ${group.label}`}
            onClick={onOpenTerminals}
            onKeyDown={interactive
              ? e => { if (e.key === 'Enter' || e.key === ' ') onOpenTerminals!() }
              : undefined}
            className={interactive ? 'cursor-pointer outline-none' : undefined}
          >
            <rect {...rect} rx={3} className={`${style.fill} ${style.stroke}`} strokeWidth={1} />
            <g clipPath={`url(#clip-${group.key})`} className="pointer-events-none">
              {group.members.map((member, i) => {
                const x = slotToX(member.startSlot)
                const cx = x + slotToX(member.moduleWidth) / 2
                const bottom = rect.y + rect.height - 8
                return (
                  <g key={member.id ?? `${member.rowIndex}-${member.startSlot}`}>
                    {i > 0 && (
                      <line x1={x} y1={rect.y} x2={x} y2={rect.y + rect.height} className={style.stroke} strokeWidth={0.75} />
                    )}
                    <text
                      x={cx}
                      y={bottom}
                      transform={`rotate(-90 ${cx} ${bottom})`}
                      dominantBaseline="middle"
                      className={`${style.text} text-[12px] font-semibold`}
                    >
                      {member.label}
                    </text>
                  </g>
                )
              })}
              {bridgedGroups(group.members).map(({ first, last }) => (
                <rect
                  key={`bridge-${first.label}`}
                  data-bridge={`${first.label}–${last.label}`}
                  x={slotToX(first.startSlot) + 2}
                  y={rect.y + 3}
                  width={slotToX(last.startSlot + last.moduleWidth) - slotToX(first.startSlot) - 4}
                  height={4}
                  rx={2}
                  className="fill-ink-2"
                />
              ))}
            </g>
          </g>
        )
      })}
```

If `fill-ink-2` isn't a defined colour token in `ui/src/index.css`, use the token that `text-ink-2` comes from, with the `fill-` prefix.

- [ ] **Step 4: Run the tests**

Run: `cd ui && npx vitest run tests/PanelSvg.test.tsx && npm run build && npm run lint`

Expected: all pass, including `'draws every label at a size that can be read at default zoom'` (the minimum is 12). The build and lint are clean.

- [ ] **Step 5: Commit**

```bash
git add ui/src ui/tests
git commit -m "feat(ui): draw each terminal, bar bridged circuits, open order from the run"
```

---

### Task 10: `TerminalSheet`

**Files:**
- Create: `ui/src/panel/TerminalSheet.tsx`
- Test: create `ui/tests/TerminalSheet.test.tsx`

The sheet only presents. The page fetches the circuits and does the save.

- [ ] **Step 1: Write the failing tests**

Create `ui/tests/TerminalSheet.test.tsx`:

```tsx
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TerminalSheet } from '../src/panel/TerminalSheet'
import type { TerminalCircuit } from '../src/api/types'

const circuits: TerminalCircuit[] = [
  { circuitId: 'a', name: 'Kitchen', room: 'Kitchen', type: 'DimmedLighting', terminalCount: 1 },
  { circuitId: 'b', name: 'Hall', room: null, type: 'DimmedLighting', terminalCount: 1 },
  { circuitId: 'c', name: 'Porch', room: null, type: 'Switched', terminalCount: 1 },
]

function show(onSave = vi.fn().mockResolvedValue(undefined)) {
  render(<TerminalSheet circuits={circuits} onSave={onSave} onClose={() => {}} />)
  return onSave
}

const rows = () => screen.getAllByRole('listitem')

describe('TerminalSheet', () => {
  it('lists each circuit with the terminal it lands on', () => {
    show()

    expect(rows().map(r => within(r).getByTestId('terminal').textContent)).toEqual(['C1', 'C2', 'C3'])
    expect(within(rows()[0]).getByText('Kitchen', { selector: 'strong' })).toBeInTheDocument()
  })

  it('moves a circuit up and renumbers as it goes', async () => {
    show()

    await userEvent.click(screen.getByRole('button', { name: 'Move Porch up' }))

    expect(rows().map(r => within(r).getByText(/Kitchen|Hall|Porch/, { selector: 'strong' }).textContent))
      .toEqual(['Kitchen', 'Porch', 'Hall'])
    expect(within(rows()[1]).getByTestId('terminal').textContent).toBe('C2')
  })

  it('cannot move the first circuit up or the last one down', () => {
    show()

    expect(screen.getByRole('button', { name: 'Move Kitchen up' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Move Porch down' })).toBeDisabled()
  })

  it('shows the range when a circuit lands on several terminals', async () => {
    show()

    const stepper = within(rows()[1]).getByRole('spinbutton')
    await userEvent.clear(stepper)
    await userEvent.type(stepper, '6')

    expect(within(rows()[1]).getByTestId('terminal').textContent).toBe('C2.1–C2.6')
  })

  it('saves nothing until something has changed', () => {
    show()

    expect(screen.getByRole('button', { name: 'Save order' })).toBeDisabled()
  })

  it('saves the circuits in their new order with their counts', async () => {
    const onSave = show()

    await userEvent.click(screen.getByRole('button', { name: 'Move Hall down' }))
    await userEvent.click(within(rows()[0]).getByRole('button', { name: /more|increase|\+/i }))
    await userEvent.click(screen.getByRole('button', { name: 'Save order' }))

    expect(onSave).toHaveBeenCalledWith([
      { circuitId: 'a', terminalCount: 2 },
      { circuitId: 'c', terminalCount: 1 },
      { circuitId: 'b', terminalCount: 1 },
    ])
  })
})
```

Check what `Stepper` renders: its input role (`spinbutton` for `type="number"`, or `textbox`) and the accessible name of its "+" button. Then adjust the two queries above to match. Look at `ui/src/components/Stepper.tsx` and `ui/tests/controls.test.tsx`.

- [ ] **Step 2: Run to confirm they fail**

Run: `cd ui && npx vitest run tests/TerminalSheet.test.tsx`

Expected: FAIL, module not found.

- [ ] **Step 3: Implement**

Create `ui/src/panel/TerminalSheet.tsx`:

```tsx
import { useState } from 'react'
import { MAX_TERMINALS_PER_CIRCUIT, type TerminalCircuit } from '../api/types'
import { Button } from '../components/Button'
import { Sheet } from '../components/Sheet'
import { Stepper } from '../components/Stepper'
import { useSingleFlight } from '../components/useSingleFlight'
import { terminalLabel } from './terminalLabels'

interface Props {
  circuits: TerminalCircuit[]
  /** Rejects when the save was refused; the sheet stays open with the edit intact. */
  onSave: (circuits: { circuitId: string; terminalCount: number }[]) => Promise<void>
  onClose: () => void
}

const sameAs = (a: TerminalCircuit[], b: TerminalCircuit[]) =>
  a.length === b.length && a.every((c, i) => c.circuitId === b[i].circuitId && c.terminalCount === b[i].terminalCount)

/**
 * The order circuits land along the terminal rail, and how many terminals each
 * lands on. Up and down buttons rather than drag: a terminal is a third of a
 * module, too small to pick up under a thumb, and a list reorders the same way
 * on every screen. Numbers follow place, so C1 is always the first terminal.
 */
export function TerminalSheet({ circuits, onSave, onClose }: Props) {
  const [rows, setRows] = useState(circuits)
  const [saving, setSaving] = useState(false)
  const once = useSingleFlight()
  const changed = !sameAs(rows, circuits)

  const move = (index: number, by: -1 | 1) => setRows(current => {
    const next = [...current]
    ;[next[index], next[index + by]] = [next[index + by], next[index]]
    return next
  })

  const setCount = (index: number, terminalCount: number) =>
    setRows(current => current.map((c, i) => (i === index ? { ...c, terminalCount } : c)))

  const save = () => once(async () => {
    setSaving(true)
    try {
      await onSave(rows.map(({ circuitId, terminalCount }) => ({ circuitId, terminalCount })))
    } catch {
      // The page has said why; keep the edit so it can be tried again.
    } finally {
      setSaving(false)
    }
  })

  return (
    <Sheet title="Terminal order" subtitle="C1 is the first terminal on the rail. Saving rebuilds the panel." onClose={onClose}>
      <ol className="space-y-2">
        {rows.map((circuit, index) => (
          <li key={circuit.circuitId} className="rounded-lg border border-rule p-3">
            <div className="flex items-center gap-2">
              <span data-testid="terminal" className="min-w-16 font-mono font-semibold">
                {terminalLabel(index + 1, circuit.terminalCount)}
              </span>
              <span className="min-w-0 flex-1 truncate">
                <strong className="font-medium">{circuit.name}</strong>
                {circuit.room && <span className="text-ink-2"> · {circuit.room}</span>}
              </span>
              <Button
                variant="secondary"
                aria-label={`Move ${circuit.name} up`}
                disabled={index === 0 || saving}
                onClick={() => move(index, -1)}
              >↑</Button>
              <Button
                variant="secondary"
                aria-label={`Move ${circuit.name} down`}
                disabled={index === rows.length - 1 || saving}
                onClick={() => move(index, 1)}
              >↓</Button>
            </div>
            <div className="mt-2">
              <Stepper
                label={`Cables for ${circuit.name}`}
                hint="Above one, the terminals are bridged on the line."
                value={circuit.terminalCount}
                min={1}
                max={MAX_TERMINALS_PER_CIRCUIT}
                disabled={saving}
                onChange={value => setCount(index, value)}
              />
            </div>
          </li>
        ))}
      </ol>
      <div className="mt-4 flex justify-end gap-2">
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="primary" disabled={!changed || saving} onClick={() => void save()}>
          {saving ? 'Saving…' : 'Save order'}
        </Button>
      </div>
    </Sheet>
  )
}
```

Check that `Button`'s `variant` names (`primary`, `secondary`, `ghost`) match `ui/src/components/Button.tsx`. They're used that way in `PanelPage.tsx`.

- [ ] **Step 4: Run the tests**

Run: `cd ui && npx vitest run tests/TerminalSheet.test.tsx && npm run build && npm run lint`

Expected: all pass, and the build and lint are clean.

- [ ] **Step 5: Commit**

```bash
git add ui/src ui/tests
git commit -m "feat(ui): a sheet to order terminals and set cables per circuit"
```

---

### Task 11: Wire the sheet into the panel page, and an end-to-end test

**Files:**
- Modify: `ui/src/routes/PanelPage.tsx`
- Modify: `ui/e2e/panel.spec.ts`
- Test: `ui/tests/PanelPage.test.tsx`

- [ ] **Step 1: Write the failing e2e test**

In `ui/e2e/panel.spec.ts`, change the first test's last line from `getByRole('img', { name: /C1/ })` to `getByRole('button', { name: /Terminals C1/ })`. Then add:

```ts
test('the terminal order can be changed and a circuit given several cables', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  await page.goto(`/submains/${submainId}/panel`)

  await page.getByRole('button', { name: /Terminals C1/ }).click()
  const sheet = page.getByRole('dialog', { name: 'Terminal order' })
  await expect(sheet).toBeVisible()

  await sheet.getByRole('button', { name: 'Move Hall up' }).click()
  await sheet.getByRole('button', { name: 'Save order' }).click()
  await expect(sheet).toBeHidden()

  await page.reload()
  await page.getByRole('button', { name: /Terminals C1/ }).click()
  const items = page.getByRole('dialog', { name: 'Terminal order' }).getByRole('listitem')
  await expect(items.nth(1)).toContainText('Hall')
  await expect(items.nth(1)).toContainText('C2')
})
```

`threeDimmed` is Kitchen ceiling, Kitchen island, Hall, Immersion. Moving Hall up puts it second. If `Sheet` doesn't render `role="dialog"` with an `aria-label` or `aria-labelledby` equal to its title, check `ui/src/components/Sheet.tsx` and use the locator that `DeviceSheet`'s e2e checks use.

- [ ] **Step 2: Wire it up in `PanelPage.tsx`**

Add these imports:

```ts
import type { TerminalCircuit, TerminalsResponse } from '../api/types'
import { TerminalSheet } from '../panel/TerminalSheet'
```

(Merge the type import into the existing `import type { … } from '../api/types'`.)

Add this state next to `selectedId`:

```ts
  // The circuits in terminal order while the sheet is open; null when it is shut.
  const [terminals, setTerminals] = useState<TerminalCircuit[] | null>(null)
```

Add these functions after `freeChannel`:

```ts
  async function openTerminals() {
    try {
      const response = await api.get<TerminalsResponse>(`/submains/${submainId}/terminals`)
      setTerminals(response.circuits)
    } catch (e) {
      announce(`Terminals not loaded: ${(e as ApiError).message}`, 'error')
    }
  }

  /**
   * One request: the order and counts are saved and the panel rebuilt together,
   * so a refused rebuild (no room for the extra terminals) saves nothing.
   * Rethrows a refusal so the sheet keeps the edit.
   */
  async function saveTerminals(circuits: { circuitId: string; terminalCount: number }[]) {
    if (!design) return
    try {
      await api.put(`/submains/${submainId}/terminals`, { layoutVersion: design.layoutVersion, circuits })
      setTerminals(null)
      await load()
      announce('Terminal order saved, panel rebuilt', 'ok')
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        announce('This panel changed elsewhere — reloading')
        setTerminals(null)
        await load()
        return
      }
      announce(`Terminal order not saved: ${error.message}`, 'error')
      throw e
    }
  }

  const closeTerminals = useCallback(() => setTerminals(null), [])
```

Pass the opener to the drawing: `<PanelSvg … zoom={zoom} onOpenTerminals={() => void openTerminals()} />`.

Render the sheet next to `DeviceSheet`, before `</AppShell>`:

```tsx
      {terminals && (
        <TerminalSheet circuits={terminals} onSave={saveTerminals} onClose={closeTerminals} />
      )}
```

Update the hint `'Tap a device to name its circuits. Drag it to move it — on a touchscreen, hold it first.'` to `'Tap a device to name its circuits, or the terminals to change their order. Drag a device to move it — on a touchscreen, hold it first.'`.

- [ ] **Step 3: Add a page-level unit test**

In `ui/tests/PanelPage.test.tsx`, follow how the existing tests stub `fetch` or the API for `/layout` and `/submains/:id`. Add a test where the layout has a terminal (`category: 'Terminal240'`, `label: 'C1'`). Stub `GET /submains/s1/terminals` to return one circuit. Click `getByRole('button', { name: /Terminals C1/ })` and expect `getByRole('heading', { name: 'Terminal order' })`. If the existing tests stub at the `fetch` level, add a branch for the `/terminals` URL.

- [ ] **Step 4: Run everything for the UI**

```bash
cd ui && npm test && npm run build && npm run lint && npm run test:e2e
```

Expected: all pass. Any existing unit or e2e test that looked for `role="img"` on terminals should now look for the button.

- [ ] **Step 5: Commit**

```bash
git add ui
git commit -m "feat(ui): open the terminal order from the panel and save it with a rebuild"
```

---

### Task 12: Document it and verify the whole thing

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the README**

Make three changes in `README.md`:

- Where the panel rules describe terminals (search for `2003-7646`), add: "Circuits land on the terminal rail in the order the engineer chooses (tap the terminals on the panel screen). C1 is always the first terminal. A circuit can land on up to 12 terminals, for several cables to one channel. Their line tiers are bridged with a cut-to-length line jumper bar, counted per circuit in the BOM (the part number is ASSUMED until confirmed)."
- In the API section, if there is one listing endpoints, add `GET/PUT /submains/{id}/terminals`.
- Update the test counts on the `dotnet test` and `npm test` lines to the new totals reported in Step 2.

- [ ] **Step 2: Run the full verification**

```bash
dotnet build && dotnet test
cd ui && npm test && npm run build && npm run lint && npm run test:e2e
```

Expected: everything passes. Record the test totals for the README.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: README for choosing the terminal order and bridged terminals"
```
