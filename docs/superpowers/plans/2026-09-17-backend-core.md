# House Panel Designer — Backend Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure panel generator, the Postgres-backed data layer, and the design API that turns circuit counts into a laid-out DIN panel.

**Architecture:** A pure C# function in a `Domain` project with no EF dependency takes circuits, an enclosure, a ruleset and a catalogue, and returns a layout, diagnostics and a bill of materials. Identical inputs always produce an identical panel, which makes it unit-testable and golden-file-testable. The `Api` project persists the result; circuits are owned by the submain, so re-generation re-assigns them to channels and reports any that no longer have a home.

**Tech Stack:** .NET 9, EF Core 9, Postgres 17, Keycloak bearer auth, xUnit, Testcontainers, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-09-17-house-panel-designer-design.md`

## Amendments during execution (2026-09-17)

Two corrections found when the toolchain was checked, before Task 1:

1. **`net10.0`, not `net9.0`.** No .NET 9 SDK or runtime is installed on this
   machine (6.0, 8.0 and 10.0 are), and `label-api` — the project this
   architecture follows — is already on `net10.0`. Package versions match it:
   EF Core and ASP.NET Core `10.0.9`, `Npgsql.EntityFrameworkCore.PostgreSQL`
   `10.0.2`, `Testcontainers.PostgreSql` `4.12.0`, `xunit` `2.9.3`,
   `xunit.runner.visualstudio` `3.1.4`.
2. **Plain xUnit `Assert`, not FluentAssertions.** No sibling project uses an
   assertion library, and FluentAssertions v8+ requires a paid commercial
   licence. The test code in the tasks below is written in FluentAssertions
   style; implement each assertion with its plain `Assert` equivalent. The
   assertions' *meaning* is the specification — the syntax is not.

Everything else in this plan stands as written.

## Global Constraints

- Target framework `net10.0`; `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` on every project.
- Central package management: all versions in `Directory.Packages.props`, `PackageReference` entries carry no `Version` attribute.
- `PubInvest.HouseConfig.Domain` must have **zero** package references beyond the BCL. No EF Core, no ASP.NET, no JSON attributes on domain types. Any task adding a dependency there is wrong.
- The generator must be deterministic: no `Guid.NewGuid()`, no `DateTime.Now`, no dictionary-order-dependent output inside `Domain`. Device identity in a layout is `(RowIndex, StartSlot)`; database ids are assigned by the persistence layer.
- Assembly names follow `PubInvest.HouseConfig.<Project>`; root namespaces match.
- Band order, derating, terminal rules and preferred devices are **data** (`RuleSetPayload`), never constants in code.
- Diagnostics are returned, never thrown, for any domain condition.
- Auth is toggled by config `HouseConfig:AuthEnabled` (default `true`; integration tests set `false`), matching the `label-api` pattern.
- **Blocking prerequisite for Task 10 only:** the seed catalogue values (Shelly module widths and channel counts, WAGO 2003-7646 width, jumper bar ways, end stops per bank) must be confirmed from datasheets before that task runs. Tasks 1-9 use a test fixture catalogue with arbitrary but internally consistent values and are not blocked.

## File Structure

```
house-config/
  Directory.Packages.props            central package versions
  HouseConfig.slnx                    solution
  docker-compose.yml                  postgres + api
  seed/catalogue.v1.json              versioned catalogue seed data
  src/
    PubInvest.HouseConfig.Domain/
      Catalogue/DeviceType.cs         device catalogue record + DeviceCategory
      Catalogue/EnclosureType.cs      enclosure record
      Catalogue/DeviceCatalogue.cs    id lookup over device types
      Circuits/Circuit.cs             circuit record + CircuitType
      Rules/RuleSetPayload.cs         ruleset, preferred devices, terminal rules
      Layout/PlacedDevice.cs          placed device, channel assignment, TerminalRole
      Layout/PanelLayout.cs           rows, slots, placed devices
      Diagnostics/Diagnostic.cs       diagnostic record, severity, codes
      Bom/BillOfMaterials.cs          bom line + totals
      Generation/RequiredDevice.cs    pre-packing device + accessory line
      Generation/DeviceDemandCalculator.cs
      Generation/PsuSizer.cs
      Generation/TerminalBandBuilder.cs
      Generation/BandPacker.cs
      Generation/BomBuilder.cs
      Generation/PanelGenerator.cs    orchestration, GenerationRequest/Result
      Generation/OrphanReporter.cs    circuits left without a channel
    PubInvest.HouseConfig.Data/
      Entities/*.cs                   EF entities, one file each
      HouseConfigDbContext.cs
      Configurations/*.cs             IEntityTypeConfiguration per entity
      Seeding/CatalogueSeeder.cs      reads seed/catalogue.v1.json
      Mapping/DomainMapper.cs         entity <-> domain record conversion
      Migrations/
    PubInvest.HouseConfig.Api/
      Program.cs                      host, auth, problem details
      Endpoints/ProjectEndpoints.cs
      Endpoints/SubmainEndpoints.cs
      Endpoints/CatalogueEndpoints.cs
      Endpoints/DesignEndpoints.cs    preview + generate
      Contracts/*.cs                  request/response DTOs
      Services/DesignService.cs       loads inputs, calls generator, persists
  tests/
    PubInvest.HouseConfig.Domain.Tests/
      CatalogueFixture.cs             shared test catalogue + ruleset
      DeviceDemandCalculatorTests.cs
      PsuSizerTests.cs
      TerminalBandBuilderTests.cs
      BandPackerTests.cs
      BomBuilderTests.cs
      PanelGeneratorTests.cs
      Golden/typical-submain.json
      OrphanReporterTests.cs
    PubInvest.HouseConfig.Api.Tests/
      HouseConfigApiFactory.cs        WebApplicationFactory + Testcontainers
      ProjectEndpointTests.cs
      SubmainEndpointTests.cs
      DesignEndpointTests.cs
```

Files split by responsibility rather than by layer: each generator stage is one file with one public static entry point, so a stage can be understood and tested without reading its neighbours.

---

### Task 1: Solution scaffolding and Domain core types

**Files:**
- Create: `Directory.Packages.props`, `HouseConfig.slnx`, `.editorconfig`
- Create: `src/PubInvest.HouseConfig.Domain/PubInvest.HouseConfig.Domain.csproj`
- Create: `src/PubInvest.HouseConfig.Domain/Catalogue/DeviceType.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Catalogue/EnclosureType.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Catalogue/DeviceCatalogue.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Circuits/Circuit.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Diagnostics/Diagnostic.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/PubInvest.HouseConfig.Domain.Tests.csproj`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/CircuitTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DeviceCategory`, `CircuitType`, `DiagnosticSeverity`, `TerminalRole` enums; `DeviceType`, `EnclosureType`, `Circuit`, `Diagnostic` records; `DeviceCatalogue` class with `Find(Guid) -> DeviceType?` and `All -> IReadOnlyCollection<DeviceType>`; `DiagnosticCodes` string constants.

- [ ] **Step 1: Create the solution and projects**

```bash
cd /Users/lewis/src/pubinvest/house-config
dotnet new sln -n HouseConfig --format slnx
dotnet new classlib -o src/PubInvest.HouseConfig.Domain -f net9.0
rm src/PubInvest.HouseConfig.Domain/Class1.cs
dotnet new xunit -o tests/PubInvest.HouseConfig.Domain.Tests -f net9.0
rm tests/PubInvest.HouseConfig.Domain.Tests/UnitTest1.cs
dotnet sln add src/PubInvest.HouseConfig.Domain tests/PubInvest.HouseConfig.Domain.Tests
dotnet add tests/PubInvest.HouseConfig.Domain.Tests reference src/PubInvest.HouseConfig.Domain
```

- [ ] **Step 2: Add central package management**

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="FluentAssertions" Version="7.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
</Project>
```

Then strip every `Version=` attribute from the generated `.csproj` files, and add `FluentAssertions` to the test project:

```bash
dotnet add tests/PubInvest.HouseConfig.Domain.Tests package FluentAssertions
```

Set both `.csproj` files to have `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.

- [ ] **Step 3: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/CircuitTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class CircuitTests
{
    [Fact]
    public void LoadWatts_multiplies_tape_watts_per_metre_by_length()
    {
        var circuit = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Kitchen plinth", "Kitchen", 1,
            WattsPerMetre: 14.4m, LengthMetres: 6.5m);

        circuit.LoadWatts.Should().Be(93.6m);
    }

    [Fact]
    public void LoadWatts_is_zero_when_tape_dimensions_are_missing()
    {
        var circuit = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Unmeasured", null, 2,
            WattsPerMetre: null, LengthMetres: null);

        circuit.LoadWatts.Should().Be(0m);
    }

    [Fact]
    public void DeviceCatalogue_returns_null_for_an_unknown_device_type()
    {
        var catalogue = new DeviceCatalogue([]);

        catalogue.Find(Guid.NewGuid()).Should().BeNull();
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: FAIL — compile errors, `Circuit` and `DeviceCatalogue` do not exist.

- [ ] **Step 5: Write the domain types**

`src/PubInvest.HouseConfig.Domain/Circuits/Circuit.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Circuits;

public enum CircuitType { DimmedLighting, Switched, LedTape }

public sealed record Circuit(
    Guid Id,
    CircuitType Type,
    string Name,
    string? Room,
    int Sequence,
    decimal? WattsPerMetre,
    decimal? LengthMetres)
{
    public decimal LoadWatts => (WattsPerMetre ?? 0m) * (LengthMetres ?? 0m);

    public bool HasTapeLoad => WattsPerMetre is > 0m && LengthMetres is > 0m;
}
```

`src/PubInvest.HouseConfig.Domain/Catalogue/DeviceType.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Catalogue;

public enum DeviceCategory { Terminal240, Dimmer240, Dimmer0_10V, Relay, Psu24V, Accessory }

public sealed record DeviceType(
    Guid Id,
    string Manufacturer,
    string Model,
    string PartNumber,
    DeviceCategory Category,
    int ModuleWidth,
    int ChannelCount,
    int? MaxLoadPerChannelW,
    int? MaxTotalLoadW,
    decimal Cost,
    bool Active)
{
    public string Description => $"{Manufacturer} {Model}";
}
```

`src/PubInvest.HouseConfig.Domain/Catalogue/EnclosureType.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Catalogue;

public sealed record EnclosureType(
    Guid Id,
    string Manufacturer,
    string Model,
    int Rows,
    int SlotsPerRow,
    string IpRating,
    decimal Cost)
{
    public int TotalSlots => Rows * SlotsPerRow;

    public string Description => $"{Manufacturer} {Model}";
}
```

`src/PubInvest.HouseConfig.Domain/Catalogue/DeviceCatalogue.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Catalogue;

public sealed class DeviceCatalogue
{
    private readonly Dictionary<Guid, DeviceType> _byId;

    public DeviceCatalogue(IEnumerable<DeviceType> deviceTypes)
        => _byId = deviceTypes.ToDictionary(d => d.Id);

    public DeviceType? Find(Guid id) => _byId.GetValueOrDefault(id);

    public DeviceType? FindActive(Guid id)
        => _byId.TryGetValue(id, out var d) && d.Active ? d : null;

    public IReadOnlyCollection<DeviceType> All => _byId.Values;
}
```

`src/PubInvest.HouseConfig.Domain/Diagnostics/Diagnostic.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Diagnostics;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Suggestion = null);

public static class DiagnosticCodes
{
    public const string EnclosureTooSmall = "ENCLOSURE_TOO_SMALL";
    public const string NoPreferredDevice = "NO_PREFERRED_DEVICE";
    public const string PsuUnsized = "PSU_UNSIZED";
    public const string OrphanedAssignment = "ORPHANED_ASSIGNMENT";
    public const string TapeLoadMissing = "TAPE_LOAD_MISSING";
    public const string DeviceWiderThanRow = "DEVICE_WIDER_THAN_ROW";
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution and domain core types"
```

---

### Task 2: Rules, layout and BOM types with the shared test fixture

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Rules/RuleSetPayload.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Layout/PlacedDevice.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Layout/PanelLayout.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Bom/BillOfMaterials.cs`
- Create: `src/PubInvest.HouseConfig.Domain/Generation/RequiredDevice.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/CatalogueFixture.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/LayoutTests.cs`

**Interfaces:**
- Consumes: `DeviceType`, `DeviceCategory`, `EnclosureType`, `DeviceCatalogue` from Task 1.
- Produces: `RuleSetPayload`, `PreferredDevices`, `TerminalRules`, `TerminalConductorRule`; `TerminalRole` enum; `ChannelAssignment`, `PlacedDevice`, `PanelLayout`; `BomLine`, `BillOfMaterials`; `RequiredDevice`, `AccessoryLine`; and `CatalogueFixture` static test data used by every later Domain test.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/LayoutTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class LayoutTests
{
    private static PlacedDevice Device(int row, int startSlot, int width) =>
        new(Guid.NewGuid(), DeviceCategory.Relay, row, startSlot, width, "Relay 1", [], TerminalRole.None);

    [Fact]
    public void DevicesInRow_returns_only_that_row_ordered_by_slot()
    {
        var layout = new PanelLayout(2, 12, [Device(0, 4, 2), Device(1, 0, 4), Device(0, 0, 4)]);

        layout.DevicesInRow(0).Select(d => d.StartSlot).Should().Equal(0, 4);
    }

    [Fact]
    public void SlotsUsed_sums_module_widths_across_all_rows()
    {
        var layout = new PanelLayout(2, 12, [Device(0, 0, 4), Device(0, 4, 2), Device(1, 0, 4)]);

        layout.SlotsUsed.Should().Be(10);
    }

    [Fact]
    public void BillOfMaterials_total_is_the_sum_of_line_totals()
    {
        var bom = new BillOfMaterials([
            new BomLine(Guid.NewGuid(), "SPDM-002PE", "Shelly Pro Dimmer 2PM", 3, 60.00m),
            new BomLine(Guid.NewGuid(), "2003-7646", "WAGO TOPJOB S", 24, 1.50m)
        ]);

        bom.Total.Should().Be(216.00m);
    }
}
```

Add `using PubInvest.HouseConfig.Domain.Bom;` to the file for `BillOfMaterials`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter LayoutTests`
Expected: FAIL — compile errors, `PanelLayout`, `PlacedDevice`, `BillOfMaterials` do not exist.

- [ ] **Step 3: Write the layout and BOM types**

`src/PubInvest.HouseConfig.Domain/Layout/PlacedDevice.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Layout;

public enum TerminalRole { None, Line, Neutral, Earth }

public sealed record ChannelAssignment(int ChannelIndex, Guid? CircuitId, bool IsSpare);

public sealed record PlacedDevice(
    Guid DeviceTypeId,
    DeviceCategory Category,
    int RowIndex,
    int StartSlot,
    int ModuleWidth,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels,
    TerminalRole TerminalRole)
{
    public int EndSlotExclusive => StartSlot + ModuleWidth;
}
```

`src/PubInvest.HouseConfig.Domain/Layout/PanelLayout.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Layout;

public sealed record PanelLayout(int Rows, int SlotsPerRow, IReadOnlyList<PlacedDevice> Devices)
{
    public IEnumerable<PlacedDevice> DevicesInRow(int rowIndex)
        => Devices.Where(d => d.RowIndex == rowIndex).OrderBy(d => d.StartSlot);

    public int SlotsUsed => Devices.Sum(d => d.ModuleWidth);

    public int TotalSlots => Rows * SlotsPerRow;
}
```

`src/PubInvest.HouseConfig.Domain/Bom/BillOfMaterials.cs`:

```csharp
namespace PubInvest.HouseConfig.Domain.Bom;

public sealed record BomLine(
    Guid CatalogueId,
    string PartNumber,
    string Description,
    int Quantity,
    decimal UnitCost)
{
    public decimal LineTotal => Quantity * UnitCost;
}

public sealed record BillOfMaterials(IReadOnlyList<BomLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
}
```

- [ ] **Step 4: Write the rules and pre-packing types**

`src/PubInvest.HouseConfig.Domain/Rules/RuleSetPayload.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Rules;

public sealed record TerminalConductorRule(Guid DeviceTypeId, int BlocksPerCircuit, bool Bridged);

public sealed record TerminalRules(
    TerminalConductorRule Line,
    TerminalConductorRule Neutral,
    TerminalConductorRule Earth,
    Guid BridgeBarDeviceTypeId,
    int BridgeBarWays,
    Guid EndStopDeviceTypeId,
    int EndStopsPerBank);

public sealed record PreferredDevices(
    Guid Dimmer240,
    Guid Dimmer0_10V,
    Guid Relay,
    IReadOnlyList<Guid> Psu24V);

public sealed record RuleSetPayload(
    IReadOnlyList<DeviceCategory> BandOrder,
    bool BandStartsNewRow,
    decimal PsuDeratingFactor,
    PreferredDevices PreferredDevice,
    TerminalRules Terminals,
    string Packing);
```

Terminals are **not** listed in `PreferredDevice`; their device types come from `TerminalRules`, so there is exactly one place a terminal part number is configured.

`src/PubInvest.HouseConfig.Domain/Generation/RequiredDevice.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

/// A device the generator has decided is needed, before it has been given a slot.
public sealed record RequiredDevice(
    Guid DeviceTypeId,
    DeviceCategory Category,
    int ModuleWidth,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels,
    TerminalRole TerminalRole = TerminalRole.None);

/// A part counted in the BOM that occupies no DIN slots (jumper bars, end stops).
public sealed record AccessoryLine(Guid DeviceTypeId, int Quantity);
```

- [ ] **Step 5: Write the shared test fixture**

`tests/PubInvest.HouseConfig.Domain.Tests/CatalogueFixture.cs`. These values are deliberately arbitrary but internally consistent — the real catalogue is seeded separately in Task 11 and must never be inferred from this file.

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Tests;

public static class CatalogueFixture
{
    public static readonly Guid DimmerId   = new("11111111-0000-0000-0000-000000000001");
    public static readonly Guid TapeDimId  = new("11111111-0000-0000-0000-000000000002");
    public static readonly Guid RelayId    = new("11111111-0000-0000-0000-000000000003");
    public static readonly Guid Psu100Id   = new("11111111-0000-0000-0000-000000000004");
    public static readonly Guid Psu240Id   = new("11111111-0000-0000-0000-000000000005");
    public static readonly Guid TerminalId = new("11111111-0000-0000-0000-000000000006");
    public static readonly Guid EarthId    = new("11111111-0000-0000-0000-000000000007");
    public static readonly Guid BridgeId   = new("11111111-0000-0000-0000-000000000008");
    public static readonly Guid EndStopId  = new("11111111-0000-0000-0000-000000000009");
    public static readonly Guid SmallBoxId = new("22222222-0000-0000-0000-000000000001");
    public static readonly Guid LargeBoxId = new("22222222-0000-0000-0000-000000000002");

    public static DeviceCatalogue Catalogue() => new(
    [
        new DeviceType(DimmerId,   "Shelly", "Pro Dimmer 2PM",     "TEST-DIM2",  DeviceCategory.Dimmer240,   2, 2, 200, 400,  60.00m, true),
        new DeviceType(TapeDimId,  "Shelly", "Pro Dimmer 0/1-10V", "TEST-DIM10", DeviceCategory.Dimmer0_10V, 2, 2, null, null, 55.00m, true),
        new DeviceType(RelayId,    "Shelly", "Pro 4PM",            "TEST-REL4",  DeviceCategory.Relay,       4, 4, 3680, 7360, 95.00m, true),
        new DeviceType(Psu100Id,   "Mean Well", "DR-100-24",       "TEST-PSU100",DeviceCategory.Psu24V,      3, 0, null, 100,  45.00m, true),
        new DeviceType(Psu240Id,   "Mean Well", "DR-240-24",       "TEST-PSU240",DeviceCategory.Psu24V,      6, 0, null, 240,  85.00m, true),
        new DeviceType(TerminalId, "WAGO", "TOPJOB S 2.5",         "TEST-TB",    DeviceCategory.Terminal240, 1, 0, null, null,  1.50m, true),
        new DeviceType(EarthId,    "WAGO", "TOPJOB S 2.5 PE",      "TEST-TBPE",  DeviceCategory.Terminal240, 1, 0, null, null,  2.10m, true),
        new DeviceType(BridgeId,   "WAGO", "Jumper bar 10-way",    "TEST-BAR",   DeviceCategory.Accessory,   0, 0, null, null,  3.00m, true),
        new DeviceType(EndStopId,  "WAGO", "End stop",             "TEST-STOP",  DeviceCategory.Accessory,   0, 0, null, null,  0.80m, true)
    ]);

    public static EnclosureType SmallEnclosure() =>
        new(SmallBoxId, "Hager", "Test 2x12", 2, 12, "IP30", 90.00m);

    public static EnclosureType LargeEnclosure() =>
        new(LargeBoxId, "Hager", "Test 6x24", 6, 24, "IP30", 220.00m);

    public static IReadOnlyList<EnclosureType> AllEnclosures() =>
        [SmallEnclosure(), LargeEnclosure()];

    public static RuleSetPayload Rules() => new(
        BandOrder: [DeviceCategory.Terminal240, DeviceCategory.Dimmer240, DeviceCategory.Relay, DeviceCategory.Psu24V],
        BandStartsNewRow: true,
        PsuDeratingFactor: 0.8m,
        PreferredDevice: new PreferredDevices(DimmerId, TapeDimId, RelayId, [Psu240Id, Psu100Id]),
        Terminals: new TerminalRules(
            Line:    new TerminalConductorRule(TerminalId, 1, Bridged: false),
            Neutral: new TerminalConductorRule(TerminalId, 1, Bridged: true),
            Earth:   new TerminalConductorRule(EarthId,    1, Bridged: true),
            BridgeBarDeviceTypeId: BridgeId,
            BridgeBarWays: 10,
            EndStopDeviceTypeId: EndStopId,
            EndStopsPerBank: 2),
        Packing: "firstFit");
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add rules, layout and bom types with shared test fixture"
```

---

### Task 3: DeviceDemandCalculator

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/DeviceDemandCalculator.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/DeviceDemandCalculatorTests.cs`

**Interfaces:**
- Consumes: `Circuit`, `RuleSetPayload`, `DeviceCatalogue`, `RequiredDevice`, `ChannelAssignment`, `Diagnostic` from Tasks 1-2.
- Produces: `DeviceDemand(IReadOnlyList<RequiredDevice> Devices, IReadOnlyList<Diagnostic> Diagnostics)` and `DeviceDemandCalculator.Calculate(IReadOnlyList<Circuit>, RuleSetPayload, DeviceCatalogue) -> DeviceDemand`. Device labels produced here are `"Dimmer 1"`, `"Tape Dimmer 1"`, `"Relay 1"` — later tasks match devices across regeneration by this label, so the format must not change.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/DeviceDemandCalculatorTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class DeviceDemandCalculatorTests
{
    private static Circuit Lighting(int n) =>
        new(Guid.NewGuid(), CircuitType.DimmedLighting, $"Lighting {n}", null, n, null, null);

    private static Circuit Switched(int n) =>
        new(Guid.NewGuid(), CircuitType.Switched, $"Switched {n}", null, n, null, null);

    private static Circuit Tape(int n) =>
        new(Guid.NewGuid(), CircuitType.LedTape, $"Tape {n}", null, n, 14.4m, 5m);

    [Fact]
    public void Five_dimmed_circuits_need_three_two_channel_dimmers()
    {
        var circuits = Enumerable.Range(1, 5).Select(Lighting).ToList();

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var dimmers = demand.Devices.Where(d => d.Category == DeviceCategory.Dimmer240).ToList();
        dimmers.Should().HaveCount(3);
        dimmers.Select(d => d.Label).Should().Equal("Dimmer 1", "Dimmer 2", "Dimmer 3");
    }

    [Fact]
    public void The_last_device_carries_spare_channels_rather_than_being_dropped()
    {
        var circuits = Enumerable.Range(1, 5).Select(Lighting).ToList();

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var last = demand.Devices.Last(d => d.Category == DeviceCategory.Dimmer240);
        last.Channels.Should().HaveCount(2);
        last.Channels[0].CircuitId.Should().NotBeNull();
        last.Channels[1].IsSpare.Should().BeTrue();
        last.Channels[1].CircuitId.Should().BeNull();
    }

    [Fact]
    public void Circuits_are_assigned_in_sequence_order()
    {
        var circuits = new List<Circuit> { Lighting(3), Lighting(1), Lighting(2) };

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        var assigned = demand.Devices
            .Where(d => d.Category == DeviceCategory.Dimmer240)
            .SelectMany(d => d.Channels)
            .Where(c => c.CircuitId is not null)
            .Select(c => circuits.Single(x => x.Id == c.CircuitId).Sequence);

        assigned.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Each_circuit_type_gets_its_own_device_category()
    {
        var circuits = new List<Circuit> { Lighting(1), Switched(2), Tape(3) };

        var demand = DeviceDemandCalculator.Calculate(circuits, CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        demand.Devices.Select(d => d.Category).Should().BeEquivalentTo(
            [DeviceCategory.Dimmer240, DeviceCategory.Relay, DeviceCategory.Dimmer0_10V]);
    }

    [Fact]
    public void No_circuits_of_a_type_means_no_devices_of_that_category()
    {
        var demand = DeviceDemandCalculator.Calculate([Lighting(1)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        demand.Devices.Should().OnlyContain(d => d.Category == DeviceCategory.Dimmer240);
    }

    [Fact]
    public void An_inactive_preferred_device_produces_an_error_diagnostic()
    {
        var catalogue = new DeviceCatalogue(CatalogueFixture.Catalogue().All
            .Select(d => d.Id == CatalogueFixture.RelayId ? d with { Active = false } : d));

        var demand = DeviceDemandCalculator.Calculate([Switched(1)], CatalogueFixture.Rules(), catalogue);

        demand.Devices.Should().BeEmpty();
        demand.Diagnostics.Should().ContainSingle()
            .Which.Should().Match<Diagnostic>(d =>
                d.Code == DiagnosticCodes.NoPreferredDevice && d.Severity == DiagnosticSeverity.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter DeviceDemandCalculatorTests`
Expected: FAIL — `DeviceDemandCalculator` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/DeviceDemandCalculator.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record DeviceDemand(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<Diagnostic> Diagnostics);

public static class DeviceDemandCalculator
{
    public static DeviceDemand Calculate(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var devices = new List<RequiredDevice>();
        var diagnostics = new List<Diagnostic>();

        Build(CircuitType.DimmedLighting, rules.PreferredDevice.Dimmer240, "Dimmer");
        Build(CircuitType.Switched, rules.PreferredDevice.Relay, "Relay");
        Build(CircuitType.LedTape, rules.PreferredDevice.Dimmer0_10V, "Tape Dimmer");

        return new DeviceDemand(devices, diagnostics);

        void Build(CircuitType type, Guid preferredId, string labelPrefix)
        {
            var ofType = circuits.Where(c => c.Type == type).OrderBy(c => c.Sequence).ToList();
            if (ofType.Count == 0) return;

            var deviceType = catalogue.FindActive(preferredId);
            if (deviceType is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for {type} circuits (preferred id {preferredId}).",
                    "Choose an active device in the ruleset, or re-activate the catalogue entry."));
                return;
            }

            if (deviceType.ChannelCount <= 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"{deviceType.Description} has no channels and cannot carry {type} circuits."));
                return;
            }

            var deviceCount = (int)Math.Ceiling(ofType.Count / (double)deviceType.ChannelCount);

            for (var i = 0; i < deviceCount; i++)
            {
                var channels = new List<ChannelAssignment>(deviceType.ChannelCount);
                for (var ch = 0; ch < deviceType.ChannelCount; ch++)
                {
                    var circuitIndex = i * deviceType.ChannelCount + ch;
                    channels.Add(circuitIndex < ofType.Count
                        ? new ChannelAssignment(ch, ofType[circuitIndex].Id, IsSpare: false)
                        : new ChannelAssignment(ch, null, IsSpare: true));
                }

                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    $"{labelPrefix} {i + 1}",
                    channels));
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: calculate device demand from circuit counts"
```

---

### Task 4: PsuSizer

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/PsuSizer.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/PsuSizerTests.cs`

**Interfaces:**
- Consumes: `Circuit`, `RuleSetPayload`, `DeviceCatalogue`, `RequiredDevice`, `Diagnostic`.
- Produces: `PsuSizing(IReadOnlyList<RequiredDevice> Devices, IReadOnlyList<Diagnostic> Diagnostics)` and `PsuSizer.Size(IReadOnlyList<Circuit>, RuleSetPayload, DeviceCatalogue) -> PsuSizing`. PSU labels are `"PSU 1"`, `"PSU 2"`.

**Note on `PSU_UNSIZED`:** the spec describes this as "load exceeds the largest catalogue PSU". Because several PSUs can be fitted, that condition cannot arise; the diagnostic is raised instead when the ruleset's PSU list resolves to no usable catalogue entry. This is a deliberate refinement of the spec wording.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/PsuSizerTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Rules;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PsuSizerTests
{
    private static Circuit Tape(int n, decimal wPerM, decimal metres) =>
        new(Guid.NewGuid(), CircuitType.LedTape, $"Tape {n}", null, n, wPerM, metres);

    [Fact]
    public void No_tape_circuits_means_no_psus()
    {
        var lighting = new Circuit(Guid.NewGuid(), CircuitType.DimmedLighting, "Lighting 1", null, 1, null, null);

        var sizing = PsuSizer.Size([lighting], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        sizing.Devices.Should().BeEmpty();
        sizing.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Load_is_derated_before_psus_are_chosen()
    {
        // 100W of tape derated by 0.8 needs 125W, which the 100W unit cannot cover alone.
        var sizing = PsuSizer.Size([Tape(1, 10m, 10m)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        sizing.Devices.Should().ContainSingle()
            .Which.DeviceTypeId.Should().Be(CatalogueFixture.Psu240Id);
    }

    [Fact]
    public void Large_loads_are_covered_by_several_psus_largest_first()
    {
        // 400W derated by 0.8 = 500W required: 240 + 240 + 100.
        var sizing = PsuSizer.Size([Tape(1, 20m, 20m)], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        sizing.Devices.Select(d => d.DeviceTypeId).Should().Equal(
            CatalogueFixture.Psu240Id, CatalogueFixture.Psu240Id, CatalogueFixture.Psu100Id);
        sizing.Devices.Select(d => d.Label).Should().Equal("PSU 1", "PSU 2", "PSU 3");
    }

    [Fact]
    public void A_tape_circuit_without_dimensions_warns_but_does_not_block()
    {
        var unmeasured = new Circuit(Guid.NewGuid(), CircuitType.LedTape, "Tape 1", null, 1, null, null);

        var sizing = PsuSizer.Size([unmeasured], CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        sizing.Diagnostics.Should().ContainSingle()
            .Which.Should().Match<Diagnostic>(d =>
                d.Code == DiagnosticCodes.TapeLoadMissing && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void An_empty_psu_list_produces_an_error()
    {
        var rules = CatalogueFixture.Rules();
        var noPsus = rules with
        {
            PreferredDevice = rules.PreferredDevice with { Psu24V = [] }
        };

        var sizing = PsuSizer.Size([Tape(1, 10m, 10m)], noPsus, CatalogueFixture.Catalogue());

        sizing.Devices.Should().BeEmpty();
        sizing.Diagnostics.Should().ContainSingle()
            .Which.Should().Match<Diagnostic>(d =>
                d.Code == DiagnosticCodes.PsuUnsized && d.Severity == DiagnosticSeverity.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter PsuSizerTests`
Expected: FAIL — `PsuSizer` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/PsuSizer.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record PsuSizing(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<Diagnostic> Diagnostics);

public static class PsuSizer
{
    public static PsuSizing Size(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var diagnostics = new List<Diagnostic>();
        var tape = circuits.Where(c => c.Type == CircuitType.LedTape).OrderBy(c => c.Sequence).ToList();
        if (tape.Count == 0) return new PsuSizing([], diagnostics);

        foreach (var circuit in tape.Where(c => !c.HasTapeLoad))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.TapeLoadMissing,
                $"'{circuit.Name}' has no watts-per-metre and length, so it is excluded from PSU sizing.",
                "Enter the tape wattage and run length."));
        }

        var totalWatts = tape.Sum(c => c.LoadWatts);
        if (totalWatts <= 0m) return new PsuSizing([], diagnostics);

        var factor = rules.PsuDeratingFactor <= 0m ? 1m : rules.PsuDeratingFactor;
        var requiredWatts = totalWatts / factor;

        var candidates = rules.PreferredDevice.Psu24V
            .Select(catalogue.FindActive)
            .OfType<DeviceType>()
            .Where(d => d.MaxTotalLoadW is > 0)
            .OrderByDescending(d => d.MaxTotalLoadW)
            .ThenBy(d => d.PartNumber, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.PsuUnsized,
                $"{requiredWatts:0.#}W of 24V supply is needed but the ruleset lists no usable PSU.",
                "Add at least one active 24V PSU with a rated wattage to the ruleset."));
            return new PsuSizing([], diagnostics);
        }

        var devices = new List<RequiredDevice>();
        var remaining = requiredWatts;

        while (remaining > 0m)
        {
            // Prefer the smallest unit that finishes the job; otherwise take the largest.
            var chosen = candidates.LastOrDefault(d => d.MaxTotalLoadW >= remaining) ?? candidates[0];
            devices.Add(new RequiredDevice(
                chosen.Id,
                chosen.Category,
                chosen.ModuleWidth,
                $"PSU {devices.Count + 1}",
                []));
            remaining -= chosen.MaxTotalLoadW!.Value;
        }

        return new PsuSizing(devices, diagnostics);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 17 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: size 24V psus from derated led tape load"
```

---

### Task 5: TerminalBandBuilder

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/TerminalBandBuilder.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/TerminalBandBuilderTests.cs`

**Interfaces:**
- Consumes: `Circuit`, `RuleSetPayload`, `TerminalRules`, `DeviceCatalogue`, `RequiredDevice`, `AccessoryLine`, `TerminalRole`.
- Produces: `TerminalBand(IReadOnlyList<RequiredDevice> Devices, IReadOnlyList<AccessoryLine> Accessories, IReadOnlyList<Diagnostic> Diagnostics)` and `TerminalBandBuilder.Build(IReadOnlyList<Circuit>, RuleSetPayload, DeviceCatalogue) -> TerminalBand`. Terminal labels are `"L1"`, `"N1"`, `"E1"` … numbered from 1 within each conductor bank.

**Sizing rule:** each conductor bank holds `circuits.Count * BlocksPerCircuit + 1` blocks — the `+ 1` is the incoming twin-and-earth. Bridging changes wiring, not block count, so all three banks are the same size; bridged banks additionally contribute `ceil(blocks / BridgeBarWays)` jumper bars and `EndStopsPerBank` end stops to the BOM. Accessories have zero module width and are never placed in the layout.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/TerminalBandBuilderTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class TerminalBandBuilderTests
{
    private static IReadOnlyList<Circuit> Circuits(int count) =>
        Enumerable.Range(1, count)
            .Select(n => new Circuit(Guid.NewGuid(), CircuitType.Switched, $"Switched {n}", null, n, null, null))
            .ToList();

    [Fact]
    public void Each_conductor_gets_one_block_per_circuit_plus_one_for_the_incomer()
    {
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        band.Devices.Count(d => d.TerminalRole == TerminalRole.Line).Should().Be(10);
        band.Devices.Count(d => d.TerminalRole == TerminalRole.Neutral).Should().Be(10);
        band.Devices.Count(d => d.TerminalRole == TerminalRole.Earth).Should().Be(10);
    }

    [Fact]
    public void Earth_blocks_use_the_pe_part_and_line_blocks_the_standard_part()
    {
        var band = TerminalBandBuilder.Build(Circuits(2), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        band.Devices.Where(d => d.TerminalRole == TerminalRole.Earth)
            .Should().OnlyContain(d => d.DeviceTypeId == CatalogueFixture.EarthId);
        band.Devices.Where(d => d.TerminalRole == TerminalRole.Line)
            .Should().OnlyContain(d => d.DeviceTypeId == CatalogueFixture.TerminalId);
    }

    [Fact]
    public void Blocks_are_labelled_by_conductor_and_numbered_from_one()
    {
        var band = TerminalBandBuilder.Build(Circuits(2), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        band.Devices.Where(d => d.TerminalRole == TerminalRole.Line).Select(d => d.Label)
            .Should().Equal("L1", "L2", "L3");
    }

    [Fact]
    public void Bridged_banks_contribute_jumper_bars_and_end_stops_but_unbridged_lines_do_not()
    {
        // 9 circuits -> 10 blocks per bank; 10-way bars -> 1 bar per bridged bank; 2 bridged banks.
        var band = TerminalBandBuilder.Build(Circuits(9), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity.Should().Be(2);
        band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.EndStopId).Quantity.Should().Be(4);
    }

    [Fact]
    public void Jumper_bars_round_up_when_a_bank_exceeds_one_bar()
    {
        // 11 circuits -> 12 blocks per bank -> ceil(12/10) = 2 bars per bridged bank, 2 banks.
        var band = TerminalBandBuilder.Build(Circuits(11), CatalogueFixture.Rules(), CatalogueFixture.Catalogue());

        band.Accessories.Single(a => a.DeviceTypeId == CatalogueFixture.BridgeId).Quantity.Should().Be(4);
    }

    [Fact]
    public void A_missing_terminal_part_produces_an_error_diagnostic()
    {
        var catalogue = new DeviceCatalogue(CatalogueFixture.Catalogue().All
            .Where(d => d.Id != CatalogueFixture.EarthId));

        var band = TerminalBandBuilder.Build(Circuits(1), CatalogueFixture.Rules(), catalogue);

        band.Devices.Should().NotContain(d => d.TerminalRole == TerminalRole.Earth);
        band.Diagnostics.Should().ContainSingle()
            .Which.Should().Match<Diagnostic>(d =>
                d.Code == DiagnosticCodes.NoPreferredDevice && d.Severity == DiagnosticSeverity.Error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter TerminalBandBuilderTests`
Expected: FAIL — `TerminalBandBuilder` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/TerminalBandBuilder.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record TerminalBand(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<AccessoryLine> Accessories,
    IReadOnlyList<Diagnostic> Diagnostics);

public static class TerminalBandBuilder
{
    public static TerminalBand Build(
        IReadOnlyList<Circuit> circuits,
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var devices = new List<RequiredDevice>();
        var diagnostics = new List<Diagnostic>();
        var bridgedBankBlocks = new List<int>();

        AddBank(TerminalRole.Line, rules.Terminals.Line, "L");
        AddBank(TerminalRole.Neutral, rules.Terminals.Neutral, "N");
        AddBank(TerminalRole.Earth, rules.Terminals.Earth, "E");

        var accessories = BuildAccessories(rules.Terminals, bridgedBankBlocks);

        return new TerminalBand(devices, accessories, diagnostics);

        void AddBank(TerminalRole role, TerminalConductorRule rule, string labelPrefix)
        {
            var deviceType = catalogue.FindActive(rule.DeviceTypeId);
            if (deviceType is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for the {role} terminal bank (id {rule.DeviceTypeId}).",
                    "Point the ruleset's terminal rules at an active catalogue entry."));
                return;
            }

            // + 1 block per conductor for the incoming twin-and-earth.
            var blocks = circuits.Count * Math.Max(rule.BlocksPerCircuit, 1) + 1;

            for (var i = 0; i < blocks; i++)
            {
                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    $"{labelPrefix}{i + 1}",
                    [],
                    role));
            }

            if (rule.Bridged) bridgedBankBlocks.Add(blocks);
        }
    }

    private static IReadOnlyList<AccessoryLine> BuildAccessories(
        TerminalRules terminals,
        IReadOnlyList<int> bridgedBankBlocks)
    {
        if (bridgedBankBlocks.Count == 0) return [];

        var bars = terminals.BridgeBarWays <= 0
            ? 0
            : bridgedBankBlocks.Sum(b => (int)Math.Ceiling(b / (double)terminals.BridgeBarWays));

        var endStops = bridgedBankBlocks.Count * terminals.EndStopsPerBank;

        var accessories = new List<AccessoryLine>();
        if (bars > 0) accessories.Add(new AccessoryLine(terminals.BridgeBarDeviceTypeId, bars));
        if (endStops > 0) accessories.Add(new AccessoryLine(terminals.EndStopDeviceTypeId, endStops));
        return accessories;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 23 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: build terminal band with bridged neutral and earth banks"
```

---

### Task 6: BandPacker

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/BandPacker.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/BandPackerTests.cs`

**Interfaces:**
- Consumes: `RequiredDevice`, `EnclosureType`, `RuleSetPayload`, `PanelLayout`, `PlacedDevice`, `Diagnostic`.
- Produces: `PackResult(PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics)` and `BandPacker.Pack(IReadOnlyList<RequiredDevice>, EnclosureType, RuleSetPayload, IReadOnlyList<EnclosureType>) -> PackResult`.

**Rules implemented here:**
- `Dimmer0_10V` devices are packed inside the `Dimmer240` band; every other category is its own band. Categories absent from `BandOrder` are packed last, in the order they first appear in the input.
- Each band starts on a fresh row when `BandStartsNewRow` is true.
- First-fit within a row; a device never straddles two rows.
- `PanelLayout.Rows` is always the enclosure's row count. If packing needs more, devices are still placed (with `RowIndex >= Rows`) and `ENCLOSURE_TOO_SMALL` is raised carrying the smallest enclosure from `allEnclosures` that would fit.
- Devices with a module width of zero or less are skipped; accessories never reach this stage.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/BandPackerTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class BandPackerTests
{
    private static RequiredDevice Device(DeviceCategory category, int width, string label) =>
        new(Guid.NewGuid(), category, width, label, []);

    [Fact]
    public void Bands_are_laid_out_in_ruleset_order_each_starting_a_new_row()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Relay, 4, "Relay 1"),
            Device(DeviceCategory.Terminal240, 1, "L1"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        result.Layout.Devices.Single(d => d.Label == "L1").RowIndex.Should().Be(0);
        result.Layout.Devices.Single(d => d.Label == "Dimmer 1").RowIndex.Should().Be(1);
        result.Layout.Devices.Single(d => d.Label == "Relay 1").RowIndex.Should().Be(2);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Devices_in_a_band_are_placed_left_to_right_without_gaps()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 2"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 3")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        result.Layout.DevicesInRow(0).Select(d => d.StartSlot).Should().Equal(0, 2, 4);
    }

    [Fact]
    public void A_device_that_would_straddle_the_row_end_moves_to_the_next_row()
    {
        // Small enclosure is 12 slots per row; five 3-slot PSUs cannot share one row.
        var devices = Enumerable.Range(1, 5)
            .Select(n => Device(DeviceCategory.Psu24V, 3, $"PSU {n}"))
            .ToList();

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        var fifth = result.Layout.Devices.Single(d => d.Label == "PSU 5");
        fifth.RowIndex.Should().Be(1);
        fifth.StartSlot.Should().Be(0);
    }

    [Fact]
    public void Tape_dimmers_share_the_mains_dimmer_band()
    {
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1"),
            Device(DeviceCategory.Dimmer0_10V, 2, "Tape Dimmer 1")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        result.Layout.Devices.Select(d => d.RowIndex).Should().AllBeEquivalentTo(0);
    }

    [Fact]
    public void Overflowing_the_enclosure_raises_an_error_and_suggests_a_bigger_one()
    {
        // Small enclosure has 2 rows; three bands alone need three rows.
        var devices = new List<RequiredDevice>
        {
            Device(DeviceCategory.Terminal240, 1, "L1"),
            Device(DeviceCategory.Dimmer240, 2, "Dimmer 1"),
            Device(DeviceCategory.Relay, 4, "Relay 1")
        };

        var result = BandPacker.Pack(devices, CatalogueFixture.SmallEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        result.Layout.Rows.Should().Be(2);
        result.Layout.Devices.Should().Contain(d => d.RowIndex >= result.Layout.Rows);
        var diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be(DiagnosticCodes.EnclosureTooSmall);
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.Suggestion.Should().Contain("Test 6x24");
    }

    [Fact]
    public void A_device_wider_than_a_row_is_reported_and_skipped()
    {
        var devices = new List<RequiredDevice> { Device(DeviceCategory.Relay, 30, "Huge") };

        var result = BandPacker.Pack(devices, CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(), CatalogueFixture.AllEnclosures());

        result.Layout.Devices.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(DiagnosticCodes.DeviceWiderThanRow);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter BandPackerTests`
Expected: FAIL — `BandPacker` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/BandPacker.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record PackResult(PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics);

public static class BandPacker
{
    public static PackResult Pack(
        IReadOnlyList<RequiredDevice> devices,
        EnclosureType enclosure,
        RuleSetPayload rules,
        IReadOnlyList<EnclosureType> allEnclosures)
    {
        var diagnostics = new List<Diagnostic>();
        var placed = Place(devices, enclosure.SlotsPerRow, rules, diagnostics);
        var rowsUsed = placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;

        if (rowsUsed > enclosure.Rows)
        {
            var suggestion = SuggestEnclosure(devices, rules, allEnclosures, enclosure);
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.EnclosureTooSmall,
                $"This design needs {rowsUsed} rows of {enclosure.SlotsPerRow} slots; " +
                $"{enclosure.Description} has {enclosure.Rows}.",
                suggestion is null
                    ? "No catalogue enclosure is large enough; split the submain."
                    : $"Use {suggestion.Description} ({suggestion.Rows} x {suggestion.SlotsPerRow})."));
        }

        return new PackResult(new PanelLayout(enclosure.Rows, enclosure.SlotsPerRow, placed), diagnostics);
    }

    private static List<PlacedDevice> Place(
        IReadOnlyList<RequiredDevice> devices,
        int slotsPerRow,
        RuleSetPayload rules,
        List<Diagnostic>? diagnostics)
    {
        var placed = new List<PlacedDevice>();
        var row = 0;
        var slot = 0;

        foreach (var band in BandsInOrder(devices, rules))
        {
            var inBand = devices.Where(d => BandOf(d.Category) == band).ToList();
            if (inBand.Count == 0) continue;

            if (rules.BandStartsNewRow && slot > 0)
            {
                row++;
                slot = 0;
            }

            foreach (var device in inBand)
            {
                if (device.ModuleWidth <= 0) continue;

                if (device.ModuleWidth > slotsPerRow)
                {
                    diagnostics?.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.DeviceWiderThanRow,
                        $"'{device.Label}' is {device.ModuleWidth} slots wide but a row holds {slotsPerRow}.",
                        "Choose a wider enclosure or a narrower device."));
                    continue;
                }

                if (slot + device.ModuleWidth > slotsPerRow)
                {
                    row++;
                    slot = 0;
                }

                placed.Add(new PlacedDevice(
                    device.DeviceTypeId,
                    device.Category,
                    row,
                    slot,
                    device.ModuleWidth,
                    device.Label,
                    device.Channels,
                    device.TerminalRole));

                slot += device.ModuleWidth;
            }
        }

        return placed;
    }

    private static EnclosureType? SuggestEnclosure(
        IReadOnlyList<RequiredDevice> devices,
        RuleSetPayload rules,
        IReadOnlyList<EnclosureType> allEnclosures,
        EnclosureType current)
        => allEnclosures
            .Where(e => e.Id != current.Id)
            .OrderBy(e => e.TotalSlots)
            .ThenBy(e => e.Description, StringComparer.Ordinal)
            .FirstOrDefault(e =>
            {
                var trial = Place(devices, e.SlotsPerRow, rules, diagnostics: null);
                var rows = trial.Count == 0 ? 0 : trial.Max(d => d.RowIndex) + 1;
                return trial.Count == devices.Count(d => d.ModuleWidth > 0) && rows <= e.Rows;
            });

    private static DeviceCategory BandOf(DeviceCategory category)
        => category == DeviceCategory.Dimmer0_10V ? DeviceCategory.Dimmer240 : category;

    private static IEnumerable<DeviceCategory> BandsInOrder(
        IReadOnlyList<RequiredDevice> devices,
        RuleSetPayload rules)
    {
        var seen = new HashSet<DeviceCategory>();
        foreach (var band in rules.BandOrder)
        {
            if (seen.Add(BandOf(band))) yield return BandOf(band);
        }

        foreach (var device in devices)
        {
            var band = BandOf(device.Category);
            if (seen.Add(band)) yield return band;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 29 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: pack devices into function-banded rows"
```

---

### Task 7: BomBuilder

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/BomBuilder.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/BomBuilderTests.cs`

**Interfaces:**
- Consumes: `PanelLayout`, `AccessoryLine`, `EnclosureType`, `DeviceCatalogue`, `BomLine`, `BillOfMaterials`.
- Produces: `BomBuilder.Build(PanelLayout, IReadOnlyList<AccessoryLine>, EnclosureType, DeviceCatalogue) -> BillOfMaterials`. Lines are ordered by `PartNumber` (ordinal) so output is deterministic; the enclosure is always one line, using its own id as `CatalogueId`.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/BomBuilderTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class BomBuilderTests
{
    private static PlacedDevice Placed(Guid deviceTypeId, DeviceCategory category, int row, int slot, int width, string label) =>
        new(deviceTypeId, category, row, slot, width, label, [], TerminalRole.None);

    [Fact]
    public void Identical_devices_are_aggregated_into_one_line()
    {
        var layout = new PanelLayout(6, 24, [
            Placed(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 0, 0, 2, "Dimmer 1"),
            Placed(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 0, 2, 2, "Dimmer 2"),
            Placed(CatalogueFixture.RelayId, DeviceCategory.Relay, 1, 0, 4, "Relay 1")
        ]);

        var bom = BomBuilder.Build(layout, [], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.DimmerId).Quantity.Should().Be(2);
        bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.RelayId).Quantity.Should().Be(1);
    }

    [Fact]
    public void The_enclosure_appears_exactly_once()
    {
        var layout = new PanelLayout(6, 24, []);

        var bom = BomBuilder.Build(layout, [], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        bom.Lines.Should().ContainSingle()
            .Which.Should().Match<BomLine>(l =>
                l.CatalogueId == CatalogueFixture.LargeBoxId && l.Quantity == 1);
    }

    [Fact]
    public void Accessories_are_included_even_though_they_occupy_no_slots()
    {
        var layout = new PanelLayout(6, 24, []);
        var accessories = new[] { new AccessoryLine(CatalogueFixture.BridgeId, 2) };

        var bom = BomBuilder.Build(layout, accessories, CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        bom.Lines.Single(l => l.CatalogueId == CatalogueFixture.BridgeId).Quantity.Should().Be(2);
    }

    [Fact]
    public void Lines_are_ordered_by_part_number_and_total_is_the_sum()
    {
        var layout = new PanelLayout(6, 24, [
            Placed(CatalogueFixture.RelayId, DeviceCategory.Relay, 0, 0, 4, "Relay 1"),
            Placed(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 0, 4, 2, "Dimmer 1")
        ]);

        var bom = BomBuilder.Build(layout, [], CatalogueFixture.LargeEnclosure(), CatalogueFixture.Catalogue());

        bom.Lines.Select(l => l.PartNumber).Should().BeInAscendingOrder(StringComparer.Ordinal);
        bom.Total.Should().Be(60.00m + 95.00m + 220.00m);
    }
}
```

The enclosure's part number is its `Description`, since `EnclosureType` carries no part number field.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter BomBuilderTests`
Expected: FAIL — `BomBuilder` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/BomBuilder.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

public static class BomBuilder
{
    public static BillOfMaterials Build(
        PanelLayout layout,
        IReadOnlyList<AccessoryLine> accessories,
        EnclosureType enclosure,
        DeviceCatalogue catalogue)
    {
        var quantities = new Dictionary<Guid, int>();

        foreach (var device in layout.Devices)
        {
            quantities[device.DeviceTypeId] = quantities.GetValueOrDefault(device.DeviceTypeId) + 1;
        }

        foreach (var accessory in accessories)
        {
            quantities[accessory.DeviceTypeId] =
                quantities.GetValueOrDefault(accessory.DeviceTypeId) + accessory.Quantity;
        }

        var lines = new List<BomLine>
        {
            new(enclosure.Id, enclosure.Description, enclosure.Description, 1, enclosure.Cost)
        };

        foreach (var (deviceTypeId, quantity) in quantities)
        {
            var deviceType = catalogue.Find(deviceTypeId);
            if (deviceType is null) continue;

            lines.Add(new BomLine(
                deviceType.Id,
                deviceType.PartNumber,
                deviceType.Description,
                quantity,
                deviceType.Cost));
        }

        return new BillOfMaterials(
            lines.OrderBy(l => l.PartNumber, StringComparer.Ordinal).ToList());
    }
}
```

A device type missing from the catalogue is silently skipped here because the stage that chose it has already raised `NO_PREFERRED_DEVICE`; raising it twice would clutter the UI.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 33 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: build bill of materials from layout and accessories"
```

---

### Task 8: PanelGenerator orchestration and golden-file test

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/PanelGenerator.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/PanelGeneratorTests.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/Golden/typical-submain.json` (generated in step 4)
- Modify: `tests/PubInvest.HouseConfig.Domain.Tests/PubInvest.HouseConfig.Domain.Tests.csproj` (copy golden files to output)

**Interfaces:**
- Consumes: every generator stage from Tasks 3-7.
- Produces: `GenerationRequest(IReadOnlyList<Circuit> Circuits, EnclosureType Enclosure, RuleSetPayload Rules, DeviceCatalogue Catalogue, IReadOnlyList<EnclosureType> AllEnclosures)`, `GenerationResult(PanelLayout Layout, IReadOnlyList<Diagnostic> Diagnostics, BillOfMaterials Bom)` with `HasErrors`, and `PanelGenerator.Generate(GenerationRequest) -> GenerationResult`. This is the only entry point the API calls.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/PanelGeneratorTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class PanelGeneratorTests
{
    private static readonly JsonSerializerOptions GoldenJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// Fixed ids keep the golden file stable across runs.
    private static Circuit Circuit(int n, CircuitType type, decimal? wPerM = null, decimal? metres = null) =>
        new(new Guid($"33333333-0000-0000-0000-{n:D12}"), type, $"{type} {n}", null, n, wPerM, metres);

    private static GenerationRequest TypicalSubmain()
    {
        var circuits = new List<Circuit>();
        for (var n = 1; n <= 5; n++) circuits.Add(Circuit(n, CircuitType.DimmedLighting));
        for (var n = 6; n <= 9; n++) circuits.Add(Circuit(n, CircuitType.Switched));
        for (var n = 10; n <= 11; n++) circuits.Add(Circuit(n, CircuitType.LedTape, 14.4m, 5m));

        return new GenerationRequest(
            circuits,
            CatalogueFixture.LargeEnclosure(),
            CatalogueFixture.Rules(),
            CatalogueFixture.Catalogue(),
            CatalogueFixture.AllEnclosures());
    }

    [Fact]
    public void A_typical_submain_generates_without_errors()
    {
        var result = PanelGenerator.Generate(TypicalSubmain());

        result.HasErrors.Should().BeFalse();
        result.Layout.Devices.Should().NotBeEmpty();
    }

    [Fact]
    public void Every_circuit_is_assigned_exactly_one_channel()
    {
        var request = TypicalSubmain();

        var result = PanelGenerator.Generate(request);

        var assigned = result.Layout.Devices
            .SelectMany(d => d.Channels)
            .Where(c => c.CircuitId is not null)
            .Select(c => c.CircuitId!.Value)
            .ToList();

        assigned.Should().OnlyHaveUniqueItems();
        assigned.Should().BeEquivalentTo(request.Circuits.Select(c => c.Id));
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var first = JsonSerializer.Serialize(PanelGenerator.Generate(TypicalSubmain()), GoldenJson);
        var second = JsonSerializer.Serialize(PanelGenerator.Generate(TypicalSubmain()), GoldenJson);

        second.Should().Be(first);
    }

    [Fact]
    public void A_typical_submain_matches_the_golden_layout()
    {
        var actual = JsonSerializer.Serialize(PanelGenerator.Generate(TypicalSubmain()), GoldenJson);

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "typical-submain.json");
        if (!File.Exists(goldenPath))
        {
            throw new InvalidOperationException(
                $"Golden file missing. Review and write it with:\n{actual}");
        }

        actual.Should().Be(File.ReadAllText(goldenPath).ReplaceLineEndings());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter PanelGeneratorTests`
Expected: FAIL — `PanelGenerator` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/PanelGenerator.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

public sealed record GenerationRequest(
    IReadOnlyList<Circuit> Circuits,
    EnclosureType Enclosure,
    RuleSetPayload Rules,
    DeviceCatalogue Catalogue,
    IReadOnlyList<EnclosureType> AllEnclosures);

public sealed record GenerationResult(
    PanelLayout Layout,
    IReadOnlyList<Diagnostic> Diagnostics,
    BillOfMaterials Bom)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

public static class PanelGenerator
{
    public static GenerationResult Generate(GenerationRequest request)
    {
        var diagnostics = new List<Diagnostic>();

        var terminals = TerminalBandBuilder.Build(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(terminals.Diagnostics);

        var demand = DeviceDemandCalculator.Calculate(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(demand.Diagnostics);

        var psus = PsuSizer.Size(request.Circuits, request.Rules, request.Catalogue);
        diagnostics.AddRange(psus.Diagnostics);

        var allDevices = terminals.Devices
            .Concat(demand.Devices)
            .Concat(psus.Devices)
            .ToList();

        var packed = BandPacker.Pack(allDevices, request.Enclosure, request.Rules, request.AllEnclosures);
        diagnostics.AddRange(packed.Diagnostics);

        var bom = BomBuilder.Build(packed.Layout, terminals.Accessories, request.Enclosure, request.Catalogue);

        return new GenerationResult(packed.Layout, diagnostics, bom);
    }
}
```

- [ ] **Step 4: Create the golden file**

Configure the test project to copy golden files to the output directory, by adding to `PubInvest.HouseConfig.Domain.Tests.csproj`:

```xml
<ItemGroup>
  <None Update="Golden\**\*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Run the golden test once; it throws with the serialized layout in the message. **Read that layout and check it by hand** — terminals on row 0, dimmers next, then relays, then PSUs, every circuit assigned once — then save it verbatim to `tests/PubInvest.HouseConfig.Domain.Tests/Golden/typical-submain.json`. A golden file accepted without reading it is worthless.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 37 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: orchestrate panel generation with a golden layout test"
```

---

### Task 9: OrphanReporter

**Files:**
- Create: `src/PubInvest.HouseConfig.Domain/Generation/OrphanReporter.cs`
- Test: `tests/PubInvest.HouseConfig.Domain.Tests/OrphanReporterTests.cs`

**Interfaces:**
- Consumes: `PanelLayout`, `ChannelAssignment`, `Diagnostic`.
- Produces: `ExistingAssignment(Guid DeviceId, string Label, IReadOnlyList<ChannelAssignment> Channels)` and `OrphanReporter.Report(PanelLayout newLayout, IReadOnlyList<ExistingAssignment> existing) -> IReadOnlyList<Diagnostic>`.

**Why this is small.** Circuit names and rooms live on `Circuit` rows owned by the submain, so they survive re-generation without any help. Nothing is currently stored on a device that is worth carrying across a re-run, so re-generation replaces device rows outright. The one thing that must not happen silently is a circuit that *was* wired to a channel losing its home — that is what this reports. When Plan 2 adds engineer-dragged positions, those become state worth preserving and this grows into a real merge.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Domain.Tests/OrphanReporterTests.cs`:

```csharp
using FluentAssertions;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using Xunit;

namespace PubInvest.HouseConfig.Domain.Tests;

public class OrphanReporterTests
{
    private static readonly Guid CircuitA = new("44444444-0000-0000-0000-000000000001");
    private static readonly Guid CircuitB = new("44444444-0000-0000-0000-000000000002");

    private static PlacedDevice Dimmer(string label, params Guid?[] circuits) =>
        new(CatalogueFixture.DimmerId, DeviceCategory.Dimmer240, 1, 0, 2, label,
            circuits.Select((c, i) => new ChannelAssignment(i, c, c is null)).ToList(),
            TerminalRole.None);

    private static ExistingAssignment Existing(string label, params Guid?[] circuits) =>
        new(Guid.NewGuid(), label,
            circuits.Select((c, i) => new ChannelAssignment(i, c, c is null)).ToList());

    [Fact]
    public void Nothing_is_reported_when_every_circuit_still_has_a_channel()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, CircuitB)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_is_reported_when_a_circuit_simply_moves_to_another_device()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, null), Dimmer("Dimmer 2", CircuitB, null)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_circuit_that_loses_its_channel_is_reported_as_a_warning()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, null)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        diagnostics.Should().ContainSingle()
            .Which.Should().Match<Diagnostic>(d =>
                d.Code == DiagnosticCodes.OrphanedAssignment
                && d.Severity == DiagnosticSeverity.Warning
                && d.Message.Contains(CircuitB.ToString()));
    }

    [Fact]
    public void Several_orphans_are_reported_in_a_stable_order()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", null, null)]);

        var diagnostics = OrphanReporter.Report(layout, [Existing("Dimmer 1", CircuitA, CircuitB)]);

        diagnostics.Should().HaveCount(2);
        diagnostics[0].Message.Should().Contain(CircuitA.ToString());
        diagnostics[1].Message.Should().Contain(CircuitB.ToString());
    }

    [Fact]
    public void A_first_generation_with_no_previous_devices_reports_nothing()
    {
        var layout = new PanelLayout(6, 24, [Dimmer("Dimmer 1", CircuitA, null)]);

        OrphanReporter.Report(layout, []).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests --filter OrphanReporterTests`
Expected: FAIL — `OrphanReporter` does not exist.

- [ ] **Step 3: Write the implementation**

`src/PubInvest.HouseConfig.Domain/Generation/OrphanReporter.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

/// A device as it was stored before re-generation, with the circuits it carried.
public sealed record ExistingAssignment(
    Guid DeviceId,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels);

public static class OrphanReporter
{
    public static IReadOnlyList<Diagnostic> Report(
        PanelLayout newLayout,
        IReadOnlyList<ExistingAssignment> existing)
    {
        if (existing.Count == 0) return [];

        var stillAssigned = newLayout.Devices
            .SelectMany(d => d.Channels)
            .Select(c => c.CircuitId)
            .OfType<Guid>()
            .ToHashSet();

        return existing
            .SelectMany(d => d.Channels)
            .Select(c => c.CircuitId)
            .OfType<Guid>()
            .Distinct()
            .Where(id => !stillAssigned.Contains(id))
            .OrderBy(id => id)
            .Select(id => new Diagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.OrphanedAssignment,
                $"Circuit {id} was wired to a channel but has no channel in the new layout.",
                "Re-add the circuit to the submain, or delete it if it is no longer needed."))
            .ToList();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Domain.Tests`
Expected: PASS, 42 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: report circuits orphaned by re-generation"
```

---

### Task 10: Data layer, entities and the initial migration

**Files:**
- Create: `src/PubInvest.HouseConfig.Data/PubInvest.HouseConfig.Data.csproj`
- Create: `src/PubInvest.HouseConfig.Data/Entities/Project.cs`, `Submain.cs`, `CircuitRow.cs`, `DeviceInstance.cs`, `DeviceChannelRow.cs`, `PanelRevision.cs`, `DeviceTypeRow.cs`, `EnclosureTypeRow.cs`, `RuleSetRow.cs`
- Create: `src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs`
- Create: `src/PubInvest.HouseConfig.Data/Mapping/DomainMapper.cs`
- Create: `docker-compose.yml`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/PubInvest.HouseConfig.Data.Tests.csproj`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/PostgresFixture.cs`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/PersistenceTests.cs`

**Interfaces:**
- Consumes: the Domain records (for mapping only — Domain gains no reference to Data).
- Produces: `HouseConfigDbContext` with `DbSet`s `Projects`, `Submains`, `Circuits`, `DeviceInstances`, `DeviceChannels`, `PanelRevisions`, `DeviceTypes`, `Enclosures`, `RuleSets`; `DomainMapper.ToDomain(DeviceTypeRow) -> DeviceType`, `ToDomain(EnclosureTypeRow) -> EnclosureType`, `ToDomain(CircuitRow) -> Circuit`, `ToDomain(RuleSetRow) -> RuleSetPayload`, `ToExisting(DeviceInstance) -> ExistingAssignment`.

- [ ] **Step 1: Create the project and add packages**

```bash
cd /Users/lewis/src/pubinvest/house-config
dotnet new classlib -o src/PubInvest.HouseConfig.Data -f net9.0
rm src/PubInvest.HouseConfig.Data/Class1.cs
dotnet new xunit -o tests/PubInvest.HouseConfig.Data.Tests -f net9.0
rm tests/PubInvest.HouseConfig.Data.Tests/UnitTest1.cs
dotnet sln add src/PubInvest.HouseConfig.Data tests/PubInvest.HouseConfig.Data.Tests
dotnet add src/PubInvest.HouseConfig.Data reference src/PubInvest.HouseConfig.Domain
dotnet add tests/PubInvest.HouseConfig.Data.Tests reference src/PubInvest.HouseConfig.Data
```

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.4" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.4" />
<PackageVersion Include="Testcontainers.PostgreSql" Version="4.1.0" />
```

Reference `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design` from the Data project, and `Testcontainers.PostgreSql` plus `FluentAssertions` from the Data test project.

- [ ] **Step 2: Write the entities**

One file each under `src/PubInvest.HouseConfig.Data/Entities/`. All ids are `Guid`, all navigation collections are `List<T>`.

```csharp
namespace PubInvest.HouseConfig.Data.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<Submain> Submains { get; set; } = [];
}

public class Submain
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = "";
    public string? Reference { get; set; }
    public string? FeedCableSize { get; set; }
    public int? OriginBreakerAmps { get; set; }
    public string? Phase { get; set; }
    public Guid? EnclosureTypeId { get; set; }
    public Guid? RuleSetId { get; set; }
    public string? Notes { get; set; }
    /// Bumped on every layout change; used for optimistic concurrency.
    public int LayoutVersion { get; set; }
    public List<CircuitRow> Circuits { get; set; } = [];
    public List<DeviceInstance> Devices { get; set; } = [];
}

public class CircuitRow
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public string Type { get; set; } = "";       // CircuitType name
    public string Name { get; set; } = "";
    public string? Room { get; set; }
    public int Sequence { get; set; }
    public decimal? WattsPerMetre { get; set; }
    public decimal? LengthMetres { get; set; }
}

public class DeviceInstance
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public Guid DeviceTypeId { get; set; }
    public string Category { get; set; } = "";   // DeviceCategory name
    public int RowIndex { get; set; }
    public int StartSlot { get; set; }
    public int ModuleWidth { get; set; }
    public string Label { get; set; } = "";
    public string TerminalRole { get; set; } = "None";
    public List<DeviceChannelRow> Channels { get; set; } = [];
}

public class DeviceChannelRow
{
    public Guid Id { get; set; }
    public Guid DeviceInstanceId { get; set; }
    public int ChannelIndex { get; set; }
    public Guid? CircuitId { get; set; }
    public bool IsSpare { get; set; }
}

public class PanelRevision
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public int LayoutVersion { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public string IssuedBy { get; set; } = "";
}

public class DeviceTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Category { get; set; } = "";
    public int ModuleWidth { get; set; }
    public int ChannelCount { get; set; }
    public int? MaxLoadPerChannelW { get; set; }
    public int? MaxTotalLoadW { get; set; }
    public decimal Cost { get; set; }
    public bool Active { get; set; }
}

public class EnclosureTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public int Rows { get; set; }
    public int SlotsPerRow { get; set; }
    public string IpRating { get; set; } = "";
    public decimal Cost { get; set; }
}

public class RuleSetRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string PayloadJson { get; set; } = "";
    public bool IsDefault { get; set; }
}
```

Enums are stored as their string names so a database dump stays readable and adding an enum member never renumbers existing rows.

- [ ] **Step 3: Write the DbContext**

`src/PubInvest.HouseConfig.Data/HouseConfigDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Data;

public class HouseConfigDbContext(DbContextOptions<HouseConfigDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Submain> Submains => Set<Submain>();
    public DbSet<CircuitRow> Circuits => Set<CircuitRow>();
    public DbSet<DeviceInstance> DeviceInstances => Set<DeviceInstance>();
    public DbSet<DeviceChannelRow> DeviceChannels => Set<DeviceChannelRow>();
    public DbSet<PanelRevision> PanelRevisions => Set<PanelRevision>();
    public DbSet<DeviceTypeRow> DeviceTypes => Set<DeviceTypeRow>();
    public DbSet<EnclosureTypeRow> Enclosures => Set<EnclosureTypeRow>();
    public DbSet<RuleSetRow> RuleSets => Set<RuleSetRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>().HasMany(p => p.Submains).WithOne(s => s.Project!)
            .HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Submain>().HasMany(s => s.Circuits).WithOne()
            .HasForeignKey(c => c.SubmainId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Submain>().HasMany(s => s.Devices).WithOne()
            .HasForeignKey(d => d.SubmainId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DeviceInstance>().HasMany(d => d.Channels).WithOne()
            .HasForeignKey(c => c.DeviceInstanceId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DeviceInstance>()
            .HasIndex(d => new { d.SubmainId, d.RowIndex, d.StartSlot }).IsUnique();

        b.Entity<DeviceChannelRow>()
            .HasIndex(c => new { c.DeviceInstanceId, c.ChannelIndex }).IsUnique();

        b.Entity<DeviceChannelRow>().HasIndex(c => c.CircuitId);

        b.Entity<DeviceTypeRow>().HasIndex(d => d.PartNumber).IsUnique();
        b.Entity<RuleSetRow>().HasIndex(r => new { r.Name, r.Version }).IsUnique();
        b.Entity<PanelRevision>().HasIndex(r => new { r.SubmainId, r.LayoutVersion });

        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(12);
            property.SetScale(2);
        }
    }
}
```

`WattsPerMetre` needs more than 2 decimal places in practice; override it explicitly after the loop with `b.Entity<CircuitRow>().Property(c => c.WattsPerMetre).HasPrecision(8, 3);`.

- [ ] **Step 4: Write the failing persistence test**

`tests/PubInvest.HouseConfig.Data.Tests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace PubInvest.HouseConfig.Data.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public HouseConfigDbContext CreateContext()
        => new(new DbContextOptionsBuilder<HouseConfigDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

`tests/PubInvest.HouseConfig.Data.Tests/PersistenceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;
using Xunit;

namespace PubInvest.HouseConfig.Data.Tests;

[Collection("postgres")]
public class PersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_submain_with_circuits_and_devices_round_trips()
    {
        var projectId = Guid.NewGuid();
        var submainId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = "Test House",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "test",
                Submains =
                [
                    new Submain
                    {
                        Id = submainId,
                        Name = "Ground Floor West",
                        LayoutVersion = 1,
                        Circuits = [new CircuitRow { Id = Guid.NewGuid(), Type = "DimmedLighting", Name = "Lighting 1", Sequence = 1 }],
                        Devices =
                        [
                            new DeviceInstance
                            {
                                Id = Guid.NewGuid(),
                                DeviceTypeId = Guid.NewGuid(),
                                Category = "Dimmer240",
                                RowIndex = 1, StartSlot = 0, ModuleWidth = 2,
                                Label = "Dimmer 1",
                                Channels = [new DeviceChannelRow { Id = Guid.NewGuid(), ChannelIndex = 0, IsSpare = true }]
                            }
                        ]
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var submain = await db.Submains
                .Include(s => s.Circuits)
                .Include(s => s.Devices).ThenInclude(d => d.Channels)
                .SingleAsync(s => s.Id == submainId);

            submain.Circuits.Should().ContainSingle();
            submain.Devices.Should().ContainSingle()
                .Which.Channels.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Two_devices_cannot_occupy_the_same_slot_in_one_submain()
    {
        var projectId = Guid.NewGuid();
        var submainId = Guid.NewGuid();
        var deviceTypeId = Guid.NewGuid();

        await using var db = fixture.CreateContext();
        db.Projects.Add(new Project
        {
            Id = projectId, Name = "Clash House", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test",
            Submains =
            [
                new Submain
                {
                    Id = submainId, Name = "Clash", LayoutVersion = 1,
                    Devices =
                    [
                        new DeviceInstance { Id = Guid.NewGuid(), DeviceTypeId = deviceTypeId, Category = "Relay", RowIndex = 0, StartSlot = 0, ModuleWidth = 4, Label = "Relay 1" },
                        new DeviceInstance { Id = Guid.NewGuid(), DeviceTypeId = deviceTypeId, Category = "Relay", RowIndex = 0, StartSlot = 0, ModuleWidth = 4, Label = "Relay 2" }
                    ]
                }
            ]
        });

        var save = async () => await db.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests`
Expected: FAIL — no migration exists, `MigrateAsync` throws.

- [ ] **Step 6: Create the migration and docker-compose**

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate \
  --project src/PubInvest.HouseConfig.Data \
  --startup-project src/PubInvest.HouseConfig.Data \
  --output-dir Migrations
```

The Data project needs a design-time factory for this to work — create `src/PubInvest.HouseConfig.Data/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PubInvest.HouseConfig.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HouseConfigDbContext>
{
    public HouseConfigDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<HouseConfigDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=HouseConfig;Username=postgres;Password=postgres")
            .Options);
}
```

`docker-compose.yml`:

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: "HouseConfig"
      POSTGRES_USER: "postgres"
      POSTGRES_PASSWORD: "postgres"
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d HouseConfig"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  postgres-data:
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests`
Expected: PASS, 2 tests. Docker must be running.

- [ ] **Step 8: Write the domain mapper**

`src/PubInvest.HouseConfig.Data/Mapping/DomainMapper.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Mapping;

public static class DomainMapper
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DeviceType ToDomain(DeviceTypeRow row) => new(
        row.Id, row.Manufacturer, row.Model, row.PartNumber,
        Enum.Parse<DeviceCategory>(row.Category),
        row.ModuleWidth, row.ChannelCount, row.MaxLoadPerChannelW, row.MaxTotalLoadW,
        row.Cost, row.Active);

    public static EnclosureType ToDomain(EnclosureTypeRow row) => new(
        row.Id, row.Manufacturer, row.Model, row.Rows, row.SlotsPerRow, row.IpRating, row.Cost);

    public static Circuit ToDomain(CircuitRow row) => new(
        row.Id, Enum.Parse<CircuitType>(row.Type), row.Name, row.Room, row.Sequence,
        row.WattsPerMetre, row.LengthMetres);

    public static RuleSetPayload ToDomain(RuleSetRow row)
        => JsonSerializer.Deserialize<RuleSetPayload>(row.PayloadJson, Json)
           ?? throw new InvalidOperationException($"Ruleset '{row.Name}' v{row.Version} has an unreadable payload.");

    public static ExistingAssignment ToExisting(DeviceInstance row) => new(
        row.Id,
        row.Label,
        row.Channels
            .OrderBy(c => c.ChannelIndex)
            .Select(c => new ChannelAssignment(c.ChannelIndex, c.CircuitId, c.IsSpare))
            .ToList());
}
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add postgres data layer with initial migration"
```

---

### Task 11: Catalogue seeding from versioned JSON

**Blocked until:** the real module widths, channel counts and costs are confirmed from datasheets (see the spec's "Risks and open items"). Do not start this task with guessed numbers — a wrong `moduleWidth` produces panels that look right and do not fit.

**Files:**
- Create: `seed/catalogue.v1.json`
- Create: `src/PubInvest.HouseConfig.Data/Seeding/CatalogueSeeder.cs`
- Create: `src/PubInvest.HouseConfig.Data/Seeding/SeedDocument.cs`
- Test: `tests/PubInvest.HouseConfig.Data.Tests/CatalogueSeederTests.cs`

**Interfaces:**
- Consumes: `HouseConfigDbContext`, `DomainMapper.Json`.
- Produces: `SeedDocument`, `SeedDeviceType`, `SeedEnclosure`, `SeedRuleSet` records, and `CatalogueSeeder.SeedAsync(HouseConfigDbContext, SeedDocument, CancellationToken) -> Task<int>` returning the number of rows inserted. Seeding is **additive and idempotent**: a row whose `id` already exists is left untouched, so an admin's edit is never overwritten by a redeploy.

- [ ] **Step 1: Confirm the catalogue values and write the seed file**

Ask the user for, or read from datasheets: for each Shelly Pro model the DIN module width and channel count; for the WAGO 2003-7646 the module width; the PE block part number; the jumper bar part number and its ways; the end stop part number and how many per bank; the enclosure models in use with their rows and slots per row; and current costs.

Write `seed/catalogue.v1.json` in this shape. **The numbers below are illustrative placeholders for structure only — replace every one with a confirmed value before running the seeder.**

```json
{
  "version": 1,
  "deviceTypes": [
    {
      "id": "b1a1f1c0-0000-4000-8000-000000000001",
      "manufacturer": "Shelly",
      "model": "Pro Dimmer 2PM",
      "partNumber": "SPDM-002PE00EU01",
      "category": "Dimmer240",
      "moduleWidth": 2,
      "channelCount": 2,
      "maxLoadPerChannelW": 200,
      "maxTotalLoadW": 400,
      "cost": 0.00,
      "active": true
    }
  ],
  "enclosures": [
    {
      "id": "c1a1f1c0-0000-4000-8000-000000000001",
      "manufacturer": "Hager",
      "model": "VML418",
      "rows": 4,
      "slotsPerRow": 18,
      "ipRating": "IP30",
      "cost": 0.00
    }
  ],
  "ruleSets": [
    {
      "id": "d1a1f1c0-0000-4000-8000-000000000001",
      "name": "House default",
      "version": 1,
      "isDefault": true,
      "payload": {
        "bandOrder": ["Terminal240", "Dimmer240", "Relay", "Psu24V"],
        "bandStartsNewRow": true,
        "psuDeratingFactor": 0.8,
        "preferredDevice": {
          "dimmer240": "b1a1f1c0-0000-4000-8000-000000000001",
          "dimmer0_10V": "b1a1f1c0-0000-4000-8000-000000000002",
          "relay": "b1a1f1c0-0000-4000-8000-000000000003",
          "psu24V": ["b1a1f1c0-0000-4000-8000-000000000004"]
        },
        "terminals": {
          "line":    { "deviceTypeId": "b1a1f1c0-0000-4000-8000-000000000005", "blocksPerCircuit": 1, "bridged": false },
          "neutral": { "deviceTypeId": "b1a1f1c0-0000-4000-8000-000000000005", "blocksPerCircuit": 1, "bridged": true },
          "earth":   { "deviceTypeId": "b1a1f1c0-0000-4000-8000-000000000006", "blocksPerCircuit": 1, "bridged": true },
          "bridgeBarDeviceTypeId": "b1a1f1c0-0000-4000-8000-000000000007",
          "bridgeBarWays": 10,
          "endStopDeviceTypeId": "b1a1f1c0-0000-4000-8000-000000000008",
          "endStopsPerBank": 2
        },
        "packing": "firstFit"
      }
    }
  ]
}
```

Every id referenced inside `payload` must exist in `deviceTypes`, or generation will raise `NO_PREFERRED_DEVICE` at runtime. The test in step 2 enforces that.

- [ ] **Step 2: Write the failing test**

`tests/PubInvest.HouseConfig.Data.Tests/CatalogueSeederTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Rules;
using Xunit;

namespace PubInvest.HouseConfig.Data.Tests;

[Collection("postgres")]
public class CatalogueSeederTests(PostgresFixture fixture)
{
    private static SeedDocument LoadShippedSeed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "seed", "catalogue.v1.json");
        return JsonSerializer.Deserialize<SeedDocument>(File.ReadAllText(path), DomainMapper.Json)!;
    }

    [Fact]
    public void The_shipped_seed_references_only_device_types_it_defines()
    {
        var seed = LoadShippedSeed();
        var known = seed.DeviceTypes.Select(d => d.Id).ToHashSet();

        foreach (var ruleSet in seed.RuleSets)
        {
            var p = ruleSet.Payload;
            var referenced = new List<Guid>
            {
                p.PreferredDevice.Dimmer240, p.PreferredDevice.Dimmer0_10V, p.PreferredDevice.Relay,
                p.Terminals.Line.DeviceTypeId, p.Terminals.Neutral.DeviceTypeId, p.Terminals.Earth.DeviceTypeId,
                p.Terminals.BridgeBarDeviceTypeId, p.Terminals.EndStopDeviceTypeId
            };
            referenced.AddRange(p.PreferredDevice.Psu24V);

            referenced.Should().OnlyContain(id => known.Contains(id));
        }
    }

    [Fact]
    public async Task Seeding_inserts_rows_and_running_it_again_changes_nothing()
    {
        await using var db = fixture.CreateContext();
        var seed = LoadShippedSeed();

        var firstRun = await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);
        var secondRun = await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        firstRun.Should().BeGreaterThan(0);
        secondRun.Should().Be(0);
        (await db.DeviceTypes.CountAsync()).Should().Be(seed.DeviceTypes.Count);
    }

    [Fact]
    public async Task Seeding_does_not_overwrite_an_admin_edit()
    {
        await using var db = fixture.CreateContext();
        var seed = LoadShippedSeed();
        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        var row = await db.DeviceTypes.FirstAsync();
        row.Cost = 999.99m;
        await db.SaveChangesAsync();

        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);

        (await db.DeviceTypes.FirstAsync(d => d.Id == row.Id)).Cost.Should().Be(999.99m);
    }

    [Fact]
    public void The_default_ruleset_payload_deserialises_into_the_domain_type()
    {
        var seed = LoadShippedSeed();

        var payload = seed.RuleSets.Single(r => r.IsDefault).Payload;

        payload.Should().BeOfType<RuleSetPayload>();
        payload.PsuDeratingFactor.Should().BeGreaterThan(0m);
        payload.BandOrder.Should().NotBeEmpty();
    }
}
```

Add to `PubInvest.HouseConfig.Data.Tests.csproj` so the seed file is available to the test:

```xml
<ItemGroup>
  <None Include="..\..\seed\catalogue.v1.json" Link="seed\catalogue.v1.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests --filter CatalogueSeederTests`
Expected: FAIL — `SeedDocument` and `CatalogueSeeder` do not exist.

- [ ] **Step 4: Write the seed document and seeder**

`src/PubInvest.HouseConfig.Data/Seeding/SeedDocument.cs`:

```csharp
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Data.Seeding;

public sealed record SeedDocument(
    int Version,
    IReadOnlyList<SeedDeviceType> DeviceTypes,
    IReadOnlyList<SeedEnclosure> Enclosures,
    IReadOnlyList<SeedRuleSet> RuleSets);

public sealed record SeedDeviceType(
    Guid Id, string Manufacturer, string Model, string PartNumber, string Category,
    int ModuleWidth, int ChannelCount, int? MaxLoadPerChannelW, int? MaxTotalLoadW,
    decimal Cost, bool Active);

public sealed record SeedEnclosure(
    Guid Id, string Manufacturer, string Model, int Rows, int SlotsPerRow, string IpRating, decimal Cost);

public sealed record SeedRuleSet(
    Guid Id, string Name, int Version, bool IsDefault, RuleSetPayload Payload);
```

`src/PubInvest.HouseConfig.Data/Seeding/CatalogueSeeder.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;

namespace PubInvest.HouseConfig.Data.Seeding;

public static class CatalogueSeeder
{
    public static async Task<int> SeedAsync(
        HouseConfigDbContext db,
        SeedDocument seed,
        CancellationToken cancellationToken)
    {
        var inserted = 0;

        var existingDeviceIds = await db.DeviceTypes.Select(d => d.Id).ToListAsync(cancellationToken);
        foreach (var d in seed.DeviceTypes.Where(d => !existingDeviceIds.Contains(d.Id)))
        {
            db.DeviceTypes.Add(new DeviceTypeRow
            {
                Id = d.Id, Manufacturer = d.Manufacturer, Model = d.Model, PartNumber = d.PartNumber,
                Category = d.Category, ModuleWidth = d.ModuleWidth, ChannelCount = d.ChannelCount,
                MaxLoadPerChannelW = d.MaxLoadPerChannelW, MaxTotalLoadW = d.MaxTotalLoadW,
                Cost = d.Cost, Active = d.Active
            });
            inserted++;
        }

        var existingEnclosureIds = await db.Enclosures.Select(e => e.Id).ToListAsync(cancellationToken);
        foreach (var e in seed.Enclosures.Where(e => !existingEnclosureIds.Contains(e.Id)))
        {
            db.Enclosures.Add(new EnclosureTypeRow
            {
                Id = e.Id, Manufacturer = e.Manufacturer, Model = e.Model,
                Rows = e.Rows, SlotsPerRow = e.SlotsPerRow, IpRating = e.IpRating, Cost = e.Cost
            });
            inserted++;
        }

        var existingRuleSetIds = await db.RuleSets.Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var r in seed.RuleSets.Where(r => !existingRuleSetIds.Contains(r.Id)))
        {
            db.RuleSets.Add(new RuleSetRow
            {
                Id = r.Id, Name = r.Name, Version = r.Version, IsDefault = r.IsDefault,
                PayloadJson = JsonSerializer.Serialize(r.Payload, DomainMapper.Json)
            });
            inserted++;
        }

        if (inserted > 0) await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Data.Tests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: seed device catalogue and default ruleset from versioned json"
```

---

### Task 12: API host, auth and the integration test harness

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/PubInvest.HouseConfig.Api.csproj`, `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Dockerfile`
- Create: `tests/PubInvest.HouseConfig.Api.Tests/PubInvest.HouseConfig.Api.Tests.csproj`
- Create: `tests/PubInvest.HouseConfig.Api.Tests/HouseConfigApiFactory.cs`
- Create: `tests/PubInvest.HouseConfig.Api.Tests/HealthTests.cs`
- Modify: `docker-compose.yml` (add the `api` service)

**Interfaces:**
- Consumes: `HouseConfigDbContext`, `CatalogueSeeder`.
- Produces: a running host exposing `GET /health`; `HouseConfigApiFactory : WebApplicationFactory<Program>` with `CreateClientAsync()` and `Services`, used by every later API test. `Program` must be reachable from the test project — add `public partial class Program;` at the end of `Program.cs`.

- [ ] **Step 1: Create the projects**

```bash
cd /Users/lewis/src/pubinvest/house-config
dotnet new web -o src/PubInvest.HouseConfig.Api -f net9.0
dotnet new xunit -o tests/PubInvest.HouseConfig.Api.Tests -f net9.0
rm tests/PubInvest.HouseConfig.Api.Tests/UnitTest1.cs
dotnet sln add src/PubInvest.HouseConfig.Api tests/PubInvest.HouseConfig.Api.Tests
dotnet add src/PubInvest.HouseConfig.Api reference src/PubInvest.HouseConfig.Data src/PubInvest.HouseConfig.Domain
dotnet add tests/PubInvest.HouseConfig.Api.Tests reference src/PubInvest.HouseConfig.Api
```

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="Keycloak.AuthServices.Authentication" Version="2.7.0" />
<PackageVersion Include="Keycloak.AuthServices.Authorization" Version="2.7.0" />
<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.4" />
<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.4" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.4" />
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="9.0.4" />
```

Reference the Keycloak, JwtBearer and OpenApi packages from the Api project; `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql` and `FluentAssertions` from the Api test project.

- [ ] **Step 2: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/HouseConfigApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubInvest.HouseConfig.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace PubInvest.HouseConfig.Api.Tests;

public class HouseConfigApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HouseConfig", _postgres.GetConnectionString());
        builder.UseSetting("HouseConfig:AuthEnabled", "false");
        builder.UseSetting("HouseConfig:SeedOnStartup", "false");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HouseConfigDbContext>().Database.MigrateAsync();
    }

    public HouseConfigDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<HouseConfigDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<HouseConfigApiFactory>;
```

`builder.UseSetting` needs `using Microsoft.AspNetCore.Hosting;`.

`tests/PubInvest.HouseConfig.Api.Tests/HealthTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class HealthTests(HouseConfigApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: FAIL — no `/health` endpoint, and `Program` is not accessible.

- [ ] **Step 4: Write the host**

`src/PubInvest.HouseConfig.Api/Program.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Keycloak.AuthServices.Authentication;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);

var authEnabled = builder.Configuration.GetValue("HouseConfig:AuthEnabled", true);

builder.Services.AddDbContext<HouseConfigDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("HouseConfig")));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<HouseConfigDbContext>();
builder.Services.AddOpenApi();

if (authEnabled)
{
    builder.Services.AddKeycloakWebApiAuthentication(builder.Configuration);
    builder.Services.AddAuthorization();
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("HouseConfig:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapHealthChecks("/health");
app.MapOpenApi();

if (builder.Configuration.GetValue("HouseConfig:SeedOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HouseConfigDbContext>();
    await db.Database.MigrateAsync();

    var seedPath = Path.Combine(AppContext.BaseDirectory, "seed", "catalogue.v1.json");
    if (File.Exists(seedPath))
    {
        var seed = JsonSerializer.Deserialize<SeedDocument>(
            await File.ReadAllTextAsync(seedPath),
            PubInvest.HouseConfig.Data.Mapping.DomainMapper.Json)!;
        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);
    }
}

app.Run();

public partial class Program;
```

`appsettings.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": { "HouseConfig": "" },
  "HouseConfig": {
    "AuthEnabled": true,
    "SeedOnStartup": true,
    "AllowedOrigins": []
  },
  "Keycloak": {
    "realm": "pubinvest",
    "auth-server-url": "",
    "resource": "house-config-api",
    "verify-token-audience": true
  }
}
```

Add the seed file to the Api project output so `SeedOnStartup` works in a container:

```xml
<ItemGroup>
  <None Include="..\..\seed\catalogue.v1.json" Link="seed\catalogue.v1.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: PASS, 1 test.

- [ ] **Step 6: Add the api service to docker-compose**

```yaml
  api:
    build:
      context: .
      dockerfile: src/PubInvest.HouseConfig.Api/Dockerfile
    environment:
      ConnectionStrings__HouseConfig: "Host=postgres;Port=5432;Database=HouseConfig;Username=postgres;Password=postgres"
      HouseConfig__AuthEnabled: "false"
      HouseConfig__SeedOnStartup: "true"
      HouseConfig__AllowedOrigins__0: "http://localhost:5173"
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy
```

Write a standard multi-stage `Dockerfile` for `src/PubInvest.HouseConfig.Api` using `mcr.microsoft.com/dotnet/sdk:9.0` to build and `mcr.microsoft.com/dotnet/aspnet:9.0` to run, exposing 8080.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add api host with keycloak auth and integration test harness"
```

---

### Task 13: Project and submain endpoints

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Contracts/ProjectContracts.cs`, `SubmainContracts.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/ProjectEndpoints.cs`, `SubmainEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs` (map the endpoint groups)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/ProjectEndpointTests.cs`

**Interfaces:**
- Consumes: `HouseConfigDbContext`, entities from Task 10.
- Produces: `ProjectResponse`, `CreateProjectRequest`, `SubmainResponse`, `CreateSubmainRequest`, `UpdateSubmainRequest`, `CircuitRequest`; extension methods `IEndpointRouteBuilder.MapProjectEndpoints()` and `MapSubmainEndpoints()`.

**Endpoints:** `GET /projects`, `POST /projects`, `GET /projects/{id}`, `GET /projects/{id}/submains`, `POST /projects/{id}/submains`, `GET /submains/{id}`, `PATCH /submains/{id}`. `PATCH` replaces the submain's circuit list wholesale, since the wizard always sends the full set; circuits keep their ids when the client sends them, so names survive.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/ProjectEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class ProjectEndpointTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id, string Name, string? Address);
    private record SubmainDto(Guid Id, string Name, int LayoutVersion, int CircuitCount);

    [Fact]
    public async Task A_project_can_be_created_and_read_back()
    {
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/projects", new { name = "Willow House", address = "1 Test Lane" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var project = await created.Content.ReadFromJsonAsync<ProjectDto>();
        project!.Name.Should().Be("Willow House");

        var fetched = await client.GetFromJsonAsync<ProjectDto>($"/projects/{project.Id}");
        fetched!.Address.Should().Be("1 Test Lane");
    }

    [Fact]
    public async Task A_submain_is_created_with_its_circuits_and_reports_the_count()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Circuit House" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var response = await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Ground Floor West",
            circuits = new object[]
            {
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
                new { type = "Switched", name = "Switched 1", sequence = 2 },
                new { type = "LedTape", name = "Tape 1", sequence = 3, wattsPerMetre = 14.4, lengthMetres = 5.0 }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var submain = await response.Content.ReadFromJsonAsync<SubmainDto>();
        submain!.CircuitCount.Should().Be(3);
        submain.LayoutVersion.Should().Be(0);
    }

    [Fact]
    public async Task Patching_a_submain_replaces_its_circuits_and_keeps_supplied_ids()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "Patch House" }))
            .Content.ReadFromJsonAsync<ProjectDto>();
        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Loft",
            circuits = new object[] { new { type = "Switched", name = "Switched 1", sequence = 1 } }
        })).Content.ReadFromJsonAsync<SubmainDto>();

        await using var db = factory.NewDbContext();
        var keptId = (await db.Circuits.SingleAsync(c => c.SubmainId == submain!.Id)).Id;

        var patched = await client.PatchAsJsonAsync($"/submains/{submain!.Id}", new
        {
            name = "Loft",
            circuits = new object[]
            {
                new { id = keptId, type = "Switched", name = "Immersion", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 2 }
            }
        });

        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var check = factory.NewDbContext();
        var circuits = await check.Circuits.Where(c => c.SubmainId == submain.Id).ToListAsync();
        circuits.Should().HaveCount(2);
        circuits.Single(c => c.Id == keptId).Name.Should().Be("Immersion");
    }

    [Fact]
    public async Task An_unknown_project_returns_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/projects/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter ProjectEndpointTests`
Expected: FAIL — all four return 404, no endpoints are mapped.

- [ ] **Step 3: Write the contracts**

`src/PubInvest.HouseConfig.Api/Contracts/ProjectContracts.cs`:

```csharp
namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record CreateProjectRequest(string Name, string? Address, string? Notes);

public sealed record ProjectResponse(Guid Id, string Name, string? Address, string? Notes, int SubmainCount);
```

`src/PubInvest.HouseConfig.Api/Contracts/SubmainContracts.cs`:

```csharp
namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record CircuitRequest(
    Guid? Id, string Type, string Name, string? Room, int Sequence,
    decimal? WattsPerMetre, decimal? LengthMetres);

public sealed record CreateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    IReadOnlyList<CircuitRequest>? Circuits);

public sealed record UpdateSubmainRequest(
    string Name, string? Reference, string? FeedCableSize, int? OriginBreakerAmps, string? Phase,
    Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    IReadOnlyList<CircuitRequest>? Circuits);

public sealed record SubmainResponse(
    Guid Id, Guid ProjectId, string Name, string? Reference, string? FeedCableSize,
    int? OriginBreakerAmps, string? Phase, Guid? EnclosureTypeId, Guid? RuleSetId, string? Notes,
    int LayoutVersion, int CircuitCount, int DeviceCount);
```

- [ ] **Step 4: Write the endpoints**

`src/PubInvest.HouseConfig.Api/Endpoints/ProjectEndpoints.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/projects").WithTags("Projects");

        group.MapGet("/", async (HouseConfigDbContext db, CancellationToken ct) =>
            await db.Projects
                .OrderBy(p => p.Name)
                .Select(p => new ProjectResponse(p.Id, p.Name, p.Address, p.Notes, p.Submains.Count))
                .ToListAsync(ct));

        group.MapGet("/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects
                .Where(p => p.Id == id)
                .Select(p => new ProjectResponse(p.Id, p.Name, p.Address, p.Notes, p.Submains.Count))
                .SingleOrDefaultAsync(ct);

            return project is null ? Results.NotFound() : Results.Ok(project);
        });

        group.MapPost("/", async (CreateProjectRequest request, HouseConfigDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["A project name is required."]
                });

            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Address = request.Address,
                Notes = request.Notes,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = http.User.Identity?.Name ?? "anonymous"
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/projects/{project.Id}",
                new ProjectResponse(project.Id, project.Name, project.Address, project.Notes, 0));
        });

        return app;
    }
}
```

`src/PubInvest.HouseConfig.Api/Endpoints/SubmainEndpoints.cs`:

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Domain.Circuits;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class SubmainEndpoints
{
    public static IEndpointRouteBuilder MapSubmainEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/{projectId:guid}/submains",
            async (Guid projectId, HouseConfigDbContext db, CancellationToken ct) =>
                await db.Submains
                    .Where(s => s.ProjectId == projectId)
                    .OrderBy(s => s.Name)
                    .Select(ToResponse)
                    .ToListAsync(ct))
            .WithTags("Submains");

        app.MapPost("/projects/{projectId:guid}/submains",
            async (Guid projectId, CreateSubmainRequest request, HouseConfigDbContext db, CancellationToken ct) =>
        {
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return Results.NotFound();

            var problems = ValidateCircuits(request.Circuits);
            if (problems.Count > 0) return Results.ValidationProblem(problems);

            var submain = new Submain
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = request.Name.Trim(),
                Reference = request.Reference,
                FeedCableSize = request.FeedCableSize,
                OriginBreakerAmps = request.OriginBreakerAmps,
                Phase = request.Phase,
                EnclosureTypeId = request.EnclosureTypeId,
                RuleSetId = request.RuleSetId,
                Notes = request.Notes,
                LayoutVersion = 0,
                Circuits = ToRows(request.Circuits)
            };

            db.Submains.Add(submain);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/submains/{submain.Id}", await Load(db, submain.Id, ct));
        }).WithTags("Submains");

        app.MapGet("/submains/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
        {
            var submain = await Load(db, id, ct);
            return submain is null ? Results.NotFound() : Results.Ok(submain);
        }).WithTags("Submains");

        app.MapPatch("/submains/{id:guid}",
            async (Guid id, UpdateSubmainRequest request, HouseConfigDbContext db, CancellationToken ct) =>
        {
            var submain = await db.Submains.Include(s => s.Circuits).SingleOrDefaultAsync(s => s.Id == id, ct);
            if (submain is null) return Results.NotFound();

            var problems = ValidateCircuits(request.Circuits);
            if (problems.Count > 0) return Results.ValidationProblem(problems);

            submain.Name = request.Name.Trim();
            submain.Reference = request.Reference;
            submain.FeedCableSize = request.FeedCableSize;
            submain.OriginBreakerAmps = request.OriginBreakerAmps;
            submain.Phase = request.Phase;
            submain.EnclosureTypeId = request.EnclosureTypeId;
            submain.RuleSetId = request.RuleSetId;
            submain.Notes = request.Notes;

            if (request.Circuits is not null)
            {
                var incoming = ToRows(request.Circuits);
                var incomingIds = incoming.Select(c => c.Id).ToHashSet();

                db.Circuits.RemoveRange(submain.Circuits.Where(c => !incomingIds.Contains(c.Id)));

                foreach (var row in incoming)
                {
                    var existing = submain.Circuits.SingleOrDefault(c => c.Id == row.Id);
                    if (existing is null)
                    {
                        row.SubmainId = submain.Id;
                        db.Circuits.Add(row);
                    }
                    else
                    {
                        existing.Type = row.Type;
                        existing.Name = row.Name;
                        existing.Room = row.Room;
                        existing.Sequence = row.Sequence;
                        existing.WattsPerMetre = row.WattsPerMetre;
                        existing.LengthMetres = row.LengthMetres;
                    }
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(await Load(db, id, ct));
        }).WithTags("Submains");

        return app;
    }

    private static Dictionary<string, string[]> ValidateCircuits(IReadOnlyList<CircuitRequest>? circuits)
    {
        var problems = new Dictionary<string, string[]>();
        if (circuits is null) return problems;

        foreach (var (circuit, index) in circuits.Select((c, i) => (c, i)))
        {
            if (!Enum.TryParse<CircuitType>(circuit.Type, out _))
            {
                problems[$"circuits[{index}].type"] =
                    [$"'{circuit.Type}' is not a circuit type. Use DimmedLighting, Switched or LedTape."];
            }

            if (string.IsNullOrWhiteSpace(circuit.Name))
            {
                problems[$"circuits[{index}].name"] = ["A circuit name is required."];
            }
        }

        return problems;
    }

    private static List<CircuitRow> ToRows(IReadOnlyList<CircuitRequest>? circuits)
        => (circuits ?? []).Select(c => new CircuitRow
        {
            Id = c.Id ?? Guid.NewGuid(),
            Type = c.Type,
            Name = c.Name.Trim(),
            Room = c.Room,
            Sequence = c.Sequence,
            WattsPerMetre = c.WattsPerMetre,
            LengthMetres = c.LengthMetres
        }).ToList();

    private static async Task<SubmainResponse?> Load(HouseConfigDbContext db, Guid id, CancellationToken ct)
        => await db.Submains.Where(s => s.Id == id).Select(ToResponse).SingleOrDefaultAsync(ct);

    /// An Expression, not a method: EF Core cannot translate a method call inside Select.
    private static readonly Expression<Func<Submain, SubmainResponse>> ToResponse = s => new SubmainResponse(
        s.Id, s.ProjectId, s.Name, s.Reference, s.FeedCableSize, s.OriginBreakerAmps, s.Phase,
        s.EnclosureTypeId, s.RuleSetId, s.Notes, s.LayoutVersion,
        s.Circuits.Count, s.Devices.Count);
}
```

`ToResponse` must stay an `Expression<Func<...>>` field. Writing it as a plain static method and calling it inside `Select` compiles but throws at runtime, because EF Core cannot translate a method call into SQL.

- [ ] **Step 5: Map the endpoints in Program.cs**

Immediately before `app.Run();`:

```csharp
app.MapProjectEndpoints();
app.MapSubmainEndpoints();
```

with `using PubInvest.HouseConfig.Api.Endpoints;` at the top. When `authEnabled` is true, add `.RequireAuthorization()` to both group registrations by chaining it on the returned builders inside each endpoint file's `MapGroup` call.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add project and submain endpoints"
```

---

### Task 14: Catalogue endpoints and design preview

**Files:**
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueEndpoints.cs`
- Create: `src/PubInvest.HouseConfig.Api/Contracts/DesignContracts.cs`
- Create: `src/PubInvest.HouseConfig.Api/Services/DesignService.cs`
- Create: `src/PubInvest.HouseConfig.Api/Endpoints/DesignEndpoints.cs`
- Modify: `src/PubInvest.HouseConfig.Api/Program.cs` (map the new groups, register `DesignService`)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/DesignPreviewTests.cs`

**Interfaces:**
- Consumes: `PanelGenerator`, `DomainMapper`, `HouseConfigDbContext`.
- Produces: `DesignService.LoadAsync(Guid submainId, PreviewRequest?, CancellationToken) -> Task<(DesignInputs? Inputs, string? Error)>` and the static `DesignService.ToResponse(...)`; contracts `PreviewRequest`, `DesignResponse`, `LayoutResponse`, `PlacedDeviceResponse`, `ChannelResponse`, `DiagnosticResponse`, `BomLineResponse`, `DesignSummary`; `MapCatalogueEndpoints()` and `MapDesignEndpoints()`.

**Endpoints:** `GET /catalogue/device-types`, `GET /catalogue/enclosures`, `GET /catalogue/rulesets`, `POST /submains/{id}/design/preview`.

`PreviewRequest` lets the wizard override the stored submain before anything is saved — `enclosureTypeId`, `ruleSetId` and a full `circuits` list are all optional and fall back to what is stored. That single endpoint powers the live "3 dimmers, 2 relays, 26 of 48 slots" readout.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/DesignPreviewTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PubInvest.HouseConfig.Data.Seeding;
using Xunit;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class DesignPreviewTests(HouseConfigApiFactory factory)
{
    private record ProjectDto(Guid Id);
    private record SubmainDto(Guid Id);
    private record SummaryDto(int RowsUsed, int SlotsUsed, int TotalSlots, int DeviceCount);
    private record DiagnosticDto(string Severity, string Code, string Message, string? Suggestion);
    private record DeviceDto(Guid DeviceTypeId, string Category, int RowIndex, int StartSlot, int ModuleWidth, string Label);
    private record LayoutDto(int Rows, int SlotsPerRow, DeviceDto[] Devices);
    private record BomLineDto(string PartNumber, int Quantity, decimal LineTotal);
    private record DesignDto(LayoutDto Layout, DiagnosticDto[] Diagnostics, BomLineDto[] Bom, SummaryDto Summary);

    private async Task<Guid> SeededSubmain(HttpClient client, object[] circuits)
    {
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        var project = await (await client.PostAsJsonAsync("/projects", new { name = $"Preview {Guid.NewGuid()}" }))
            .Content.ReadFromJsonAsync<ProjectDto>();

        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Preview submain",
            enclosureTypeId = TestSeed.EnclosureId,
            ruleSetId = TestSeed.RuleSetId,
            circuits
        })).Content.ReadFromJsonAsync<SubmainDto>();

        return submain!.Id;
    }

    [Fact]
    public async Task Preview_returns_a_banded_layout_and_a_summary()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client,
        [
            new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
            new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
            new { type = "Switched", name = "Switched 1", sequence = 3 }
        ]);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        design!.Layout.Devices.Should().NotBeEmpty();
        design.Layout.Devices.Where(d => d.Category == "Terminal240").Should().OnlyContain(d => d.RowIndex == 0);
        design.Summary.DeviceCount.Should().Be(design.Layout.Devices.Length);
        design.Bom.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Preview_persists_nothing()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client, [new { type = "Switched", name = "Switched 1", sequence = 1 }]);

        await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new { });

        await using var db = factory.NewDbContext();
        db.DeviceInstances.Should().NotContain(d => d.SubmainId == submainId);
    }

    [Fact]
    public async Task Preview_honours_an_overridden_circuit_list()
    {
        var client = factory.CreateClient();
        var submainId = await SeededSubmain(client, [new { type = "Switched", name = "Switched 1", sequence = 1 }]);

        var response = await client.PostAsJsonAsync($"/submains/{submainId}/design/preview", new
        {
            circuits = new object[]
            {
                new { type = "DimmedLighting", name = "Lighting 1", sequence = 1 },
                new { type = "DimmedLighting", name = "Lighting 2", sequence = 2 },
                new { type = "DimmedLighting", name = "Lighting 3", sequence = 3 }
            }
        });

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        design!.Layout.Devices.Should().Contain(d => d.Category == "Dimmer240");
        design.Layout.Devices.Should().NotContain(d => d.Category == "Relay");
    }

    [Fact]
    public async Task A_submain_with_no_enclosure_is_a_validation_problem_not_a_500()
    {
        var client = factory.CreateClient();
        var project = await (await client.PostAsJsonAsync("/projects", new { name = "No enclosure" }))
            .Content.ReadFromJsonAsync<ProjectDto>();
        var submain = await (await client.PostAsJsonAsync($"/projects/{project!.Id}/submains", new
        {
            name = "Bare", circuits = new object[] { new { type = "Switched", name = "Switched 1", sequence = 1 } }
        })).Content.ReadFromJsonAsync<SubmainDto>();

        var response = await client.PostAsJsonAsync($"/submains/{submain!.Id}/design/preview", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Catalogue_endpoints_list_seeded_data()
    {
        var client = factory.CreateClient();
        await using (var db = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(db, TestSeed.Document(), CancellationToken.None);
        }

        (await client.GetAsync("/catalogue/device-types")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/catalogue/enclosures")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/catalogue/rulesets")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

Also create `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs`, which builds a `SeedDocument` in code from the same values as `CatalogueFixture` in the Domain tests (fixed ids, one dimmer, one 0/1-10V dimmer, one relay, two PSUs, two terminal parts, bar, end stop, one 6×24 enclosure, one default ruleset). Exposing `TestSeed.EnclosureId` and `TestSeed.RuleSetId`. Building it in code rather than reading `seed/catalogue.v1.json` keeps API tests independent of the real, still-unconfirmed catalogue.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter DesignPreviewTests`
Expected: FAIL — the preview and catalogue routes return 404.

- [ ] **Step 3: Write the design contracts**

`src/PubInvest.HouseConfig.Api/Contracts/DesignContracts.cs`:

```csharp
namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record PreviewRequest(
    Guid? EnclosureTypeId,
    Guid? RuleSetId,
    IReadOnlyList<CircuitRequest>? Circuits);

public sealed record ChannelResponse(int ChannelIndex, Guid? CircuitId, string? CircuitName, bool IsSpare);

public sealed record PlacedDeviceResponse(
    Guid? Id,
    Guid DeviceTypeId,
    string Category,
    int RowIndex,
    int StartSlot,
    int ModuleWidth,
    string Label,
    string TerminalRole,
    IReadOnlyList<ChannelResponse> Channels);

public sealed record LayoutResponse(int Rows, int SlotsPerRow, IReadOnlyList<PlacedDeviceResponse> Devices);

public sealed record DiagnosticResponse(string Severity, string Code, string Message, string? Suggestion);

public sealed record BomLineResponse(
    Guid CatalogueId, string PartNumber, string Description, int Quantity, decimal UnitCost, decimal LineTotal);

public sealed record DesignSummary(
    int RowsUsed, int SlotsUsed, int TotalSlots, int DeviceCount, int SpareChannels, decimal BomTotal);

public sealed record DesignResponse(
    Guid SubmainId,
    int LayoutVersion,
    LayoutResponse Layout,
    IReadOnlyList<DiagnosticResponse> Diagnostics,
    IReadOnlyList<BomLineResponse> Bom,
    DesignSummary Summary);
```

- [ ] **Step 4: Write the design service**

`src/PubInvest.HouseConfig.Api/Services/DesignService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Services;

public sealed record DesignInputs(
    Data.Entities.Submain Submain,
    GenerationRequest Request,
    IReadOnlyList<Circuit> Circuits);

public sealed class DesignService(HouseConfigDbContext db)
{
    /// Loads everything the generator needs. Returns an error string for anything
    /// the user can fix (no enclosure, no ruleset), never an exception.
    public async Task<(DesignInputs? Inputs, string? Error)> LoadAsync(
        Guid submainId,
        PreviewRequest? overrides,
        CancellationToken ct)
    {
        var submain = await db.Submains
            .Include(s => s.Circuits)
            .SingleOrDefaultAsync(s => s.Id == submainId, ct);

        if (submain is null) return (null, null);

        var enclosureId = overrides?.EnclosureTypeId ?? submain.EnclosureTypeId;
        if (enclosureId is null)
            return (null, "This submain has no enclosure. Choose one before generating a design.");

        var enclosureRow = await db.Enclosures.SingleOrDefaultAsync(e => e.Id == enclosureId, ct);
        if (enclosureRow is null)
            return (null, $"Enclosure {enclosureId} is not in the catalogue.");

        var ruleSetId = overrides?.RuleSetId ?? submain.RuleSetId;
        var ruleSetRow = ruleSetId is null
            ? await db.RuleSets.FirstOrDefaultAsync(r => r.IsDefault, ct)
            : await db.RuleSets.SingleOrDefaultAsync(r => r.Id == ruleSetId, ct);

        if (ruleSetRow is null)
            return (null, "No ruleset is available. Seed or select one before generating a design.");

        var circuits = overrides?.Circuits is { Count: > 0 }
            ? overrides.Circuits.Select(c => new Circuit(
                c.Id ?? Guid.NewGuid(), Enum.Parse<CircuitType>(c.Type), c.Name, c.Room, c.Sequence,
                c.WattsPerMetre, c.LengthMetres)).ToList()
            : submain.Circuits.Select(DomainMapper.ToDomain).ToList();

        var catalogue = new DeviceCatalogue(
            (await db.DeviceTypes.ToListAsync(ct)).Select(DomainMapper.ToDomain));

        var allEnclosures = (await db.Enclosures.ToListAsync(ct))
            .Select(DomainMapper.ToDomain).ToList();

        var request = new GenerationRequest(
            circuits,
            DomainMapper.ToDomain(enclosureRow),
            DomainMapper.ToDomain(ruleSetRow),
            catalogue,
            allEnclosures);

        return (new DesignInputs(submain, request, circuits), null);
    }

    public static DesignResponse ToResponse(
        Guid submainId,
        int layoutVersion,
        GenerationResult result,
        IReadOnlyList<Circuit> circuits,
        IReadOnlyDictionary<string, Guid>? persisted = null)
    {
        var circuitNames = circuits.ToDictionary(c => c.Id, c => c.Name);

        var devices = result.Layout.Devices
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .Select(d =>
            {
                Guid? storedId =
                    persisted is not null && persisted.TryGetValue(d.Label, out var found) ? found : null;

                return new PlacedDeviceResponse(
                    storedId,
                    d.DeviceTypeId,
                    d.Category.ToString(),
                    d.RowIndex,
                    d.StartSlot,
                    d.ModuleWidth,
                    d.Label,
                    d.TerminalRole.ToString(),
                    d.Channels.Select(c => new ChannelResponse(
                        c.ChannelIndex,
                        c.CircuitId,
                        c.CircuitId is not null && circuitNames.TryGetValue(c.CircuitId.Value, out var name) ? name : null,
                        c.IsSpare)).ToList());
            })
            .ToList();

        var rowsUsed = result.Layout.Devices.Count == 0 ? 0 : result.Layout.Devices.Max(d => d.RowIndex) + 1;

        return new DesignResponse(
            submainId,
            layoutVersion,
            new LayoutResponse(result.Layout.Rows, result.Layout.SlotsPerRow, devices),
            result.Diagnostics.Select(d => new DiagnosticResponse(
                d.Severity.ToString(), d.Code, d.Message, d.Suggestion)).ToList(),
            result.Bom.Lines.Select(l => new BomLineResponse(
                l.CatalogueId, l.PartNumber, l.Description, l.Quantity, l.UnitCost, l.LineTotal)).ToList(),
            new DesignSummary(
                rowsUsed,
                result.Layout.SlotsUsed,
                result.Layout.TotalSlots,
                result.Layout.Devices.Count,
                result.Layout.Devices.SelectMany(d => d.Channels).Count(c => c.IsSpare),
                result.Bom.Total));
    }
}
```

`persisted` is null for a preview (nothing is stored yet) and populated by the generate endpoint in Task 15, which is how a saved design's device ids reach the client through the same response shape.

- [ ] **Step 5: Write the endpoints**

`src/PubInvest.HouseConfig.Api/Endpoints/DesignEndpoints.cs`:

```csharp
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Services;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class DesignEndpoints
{
    public static IEndpointRouteBuilder MapDesignEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/submains/{id:guid}/design/preview",
            async (Guid id, PreviewRequest? request, DesignService service, CancellationToken ct) =>
            {
                var (inputs, error) = await service.LoadAsync(id, request, ct);
                if (error is not null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
                if (inputs is null) return Results.NotFound();

                var result = PanelGenerator.Generate(inputs.Request);

                return Results.Ok(DesignService.ToResponse(
                    id, inputs.Submain.LayoutVersion, result, inputs.Circuits));
            })
            .WithTags("Design");

        return app;
    }
}
```

`src/PubInvest.HouseConfig.Api/Endpoints/CatalogueEndpoints.cs` exposes three read-only `MapGet` handlers projecting `DeviceTypeRow`, `EnclosureTypeRow` and `RuleSetRow` (the ruleset returning `Id`, `Name`, `Version`, `IsDefault` and the parsed payload via `DomainMapper.ToDomain`), grouped under `/catalogue` with `.WithTags("Catalogue")`.

- [ ] **Step 6: Register in Program.cs**

```csharp
builder.Services.AddScoped<DesignService>();
```

and before `app.Run();`:

```csharp
app.MapCatalogueEndpoints();
app.MapDesignEndpoints();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests`
Expected: PASS, 10 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add catalogue endpoints and design preview"
```

---

### Task 15: Design generate endpoint with merge and layout versioning

**Files:**
- Modify: `src/PubInvest.HouseConfig.Api/Services/DesignService.cs` (add `GenerateAsync`)
- Modify: `src/PubInvest.HouseConfig.Api/Endpoints/DesignEndpoints.cs` (add the route)
- Test: `tests/PubInvest.HouseConfig.Api.Tests/DesignGenerateTests.cs`

**Interfaces:**
- Consumes: `OrphanReporter`, `DomainMapper.ToExisting`, everything from Task 14.
- Produces: `DesignService.GenerateAsync(Guid submainId, PreviewRequest?, CancellationToken) -> Task<(DesignResponse? Response, string? Error, bool HasErrors)>`, and `POST /submains/{id}/design/generate`.

**Behaviour:** generation commits the layout, bumps `LayoutVersion`, and reports any circuit left without a channel via `OrphanReporter`. A result with errors is **not** persisted — it comes back with `422 Unprocessable Entity` and its diagnostics so the engineer can pick a bigger enclosure. Circuits sent in the request body replace the stored circuit list first, so the wizard's "generate" is a single call.

- [ ] **Step 1: Write the failing test**

`tests/PubInvest.HouseConfig.Api.Tests/DesignGenerateTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Seeding;
using Xunit;

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
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        design!.LayoutVersion.Should().Be(1);
        design.Layout.Devices.Should().OnlyContain(d => d.Id != null);

        await using var db = factory.NewDbContext();
        (await db.DeviceInstances.CountAsync(d => d.SubmainId == submainId))
            .Should().Be(design.Layout.Devices.Length);
    }

    [Fact]
    public async Task Regenerating_keeps_circuit_names_and_re_assigns_them()
    {
        var client = factory.CreateClient();
        var submainId = await NewSubmain(client, TestSeed.EnclosureId,
        [
            new { type = "DimmedLighting", name = "Kitchen ceiling", sequence = 1 }
        ]);

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
        design!.LayoutVersion.Should().Be(2);
        design.Diagnostics.Should().NotContain(d => d.Code == "ORPHANED_ASSIGNMENT");
        design.Layout.Devices
            .SelectMany(d => d.Channels)
            .Should().Contain(c => c.CircuitId == keptCircuitId && c.CircuitName == "Kitchen ceiling");
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
        design!.Diagnostics.Should().Contain(d =>
            d.Code == "ORPHANED_ASSIGNMENT"
            && d.Severity == "Warning"
            && d.Message.Contains(droppedCircuitId.ToString()));
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

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var design = await response.Content.ReadFromJsonAsync<DesignDto>();
        design!.Diagnostics.Should().Contain(d => d.Code == "ENCLOSURE_TOO_SMALL" && d.Severity == "Error");
        design.Diagnostics.Should().Contain(d => d.Suggestion != null);

        await using var db = factory.NewDbContext();
        (await db.DeviceInstances.CountAsync(d => d.SubmainId == submainId)).Should().Be(0);
    }
}
```

`TestSeed` gains a second, deliberately small enclosure (`TestSeed.TinyEnclosureId`, 1 row × 6 slots) so the overflow case is reachable.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PubInvest.HouseConfig.Api.Tests --filter DesignGenerateTests`
Expected: FAIL — `/design/generate` returns 404.

- [ ] **Step 3: Write GenerateAsync**

Add to `DesignService`:

```csharp
public async Task<(DesignResponse? Response, string? Error, bool HasErrors)> GenerateAsync(
    Guid submainId,
    PreviewRequest? request,
    CancellationToken ct)
{
    var (inputs, error) = await LoadAsync(submainId, request, ct);
    if (error is not null) return (null, error, false);
    if (inputs is null) return (null, null, false);

    var result = PanelGenerator.Generate(inputs.Request);

    if (result.HasErrors)
    {
        return (ToResponse(submainId, inputs.Submain.LayoutVersion, result, inputs.Circuits), null, true);
    }

    var submain = inputs.Submain;

    // The request's circuit list, if any, becomes the stored one.
    if (request?.Circuits is { Count: > 0 })
    {
        var incomingIds = inputs.Circuits.Select(c => c.Id).ToHashSet();
        db.Circuits.RemoveRange(submain.Circuits.Where(c => !incomingIds.Contains(c.Id)));

        foreach (var circuit in inputs.Circuits)
        {
            var existing = submain.Circuits.SingleOrDefault(c => c.Id == circuit.Id);
            if (existing is null)
            {
                db.Circuits.Add(new Data.Entities.CircuitRow
                {
                    Id = circuit.Id, SubmainId = submain.Id, Type = circuit.Type.ToString(),
                    Name = circuit.Name, Room = circuit.Room, Sequence = circuit.Sequence,
                    WattsPerMetre = circuit.WattsPerMetre, LengthMetres = circuit.LengthMetres
                });
            }
            else
            {
                existing.Type = circuit.Type.ToString();
                existing.Name = circuit.Name;
                existing.Room = circuit.Room;
                existing.Sequence = circuit.Sequence;
                existing.WattsPerMetre = circuit.WattsPerMetre;
                existing.LengthMetres = circuit.LengthMetres;
            }
        }
    }

    var existingDevices = await db.DeviceInstances
        .Include(d => d.Channels)
        .Where(d => d.SubmainId == submainId)
        .ToListAsync(ct);

    var orphans = OrphanReporter.Report(
        result.Layout,
        existingDevices.Select(DomainMapper.ToExisting).ToList());

    db.DeviceInstances.RemoveRange(existingDevices);
    await db.SaveChangesAsync(ct);

    var persisted = new Dictionary<string, Guid>();

    foreach (var placed in result.Layout.Devices)
    {
        var row = new Data.Entities.DeviceInstance
        {
            Id = Guid.NewGuid(),
            SubmainId = submainId,
            DeviceTypeId = placed.DeviceTypeId,
            Category = placed.Category.ToString(),
            RowIndex = placed.RowIndex,
            StartSlot = placed.StartSlot,
            ModuleWidth = placed.ModuleWidth,
            Label = placed.Label,
            TerminalRole = placed.TerminalRole.ToString(),
            Channels = placed.Channels.Select(c => new Data.Entities.DeviceChannelRow
            {
                Id = Guid.NewGuid(),
                ChannelIndex = c.ChannelIndex,
                CircuitId = c.CircuitId,
                IsSpare = c.IsSpare
            }).ToList()
        };

        db.DeviceInstances.Add(row);
        persisted[row.Label] = row.Id;
    }

    submain.LayoutVersion++;
    await db.SaveChangesAsync(ct);

    var diagnostics = result.Diagnostics.Concat(orphans).ToList();
    var mergedResult = new GenerationResult(result.Layout, diagnostics, result.Bom);

    return (ToResponse(submainId, submain.LayoutVersion, mergedResult, inputs.Circuits, persisted), null, false);
}
```

Deleting and re-inserting device rows keeps the unique `(SubmainId, RowIndex, StartSlot)` index from tripping on a rearrangement; the `SaveChangesAsync` between the delete and the insert is what makes that safe, so do not collapse the two saves into one. Replacing rows outright is only correct while nothing is stored on a device that the engineer created by hand — Plan 2's dragged positions change that, and will need a real merge here.

- [ ] **Step 4: Add the endpoint**

```csharp
app.MapPost("/submains/{id:guid}/design/generate",
    async (Guid id, PreviewRequest? request, DesignService service, CancellationToken ct) =>
    {
        var (response, error, hasErrors) = await service.GenerateAsync(id, request, ct);
        if (error is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
        if (response is null) return Results.NotFound();

        return hasErrors
            ? Results.Json(response, statusCode: StatusCodes.Status422UnprocessableEntity)
            : Results.Ok(response);
    })
    .WithTags("Design");
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS across all four test projects.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: persist generated designs with merge and layout versioning"
```

---

## Done when

- `dotnet test` is green across Domain, Data and Api test projects.
- `docker compose up` brings up Postgres and the API, and `GET /health` returns `Healthy`.
- `POST /projects`, `POST /projects/{id}/submains`, `POST /submains/{id}/design/preview` and `.../generate` can be driven end to end with curl, producing a banded layout whose terminals are on row 0.
- Re-generating a submain keeps its circuit names and re-assigns them to channels, and warns about any circuit that no longer has one.

---

## Appendix A: files referenced above in prose

These belong to the tasks that mention them; they are collected here so no task contains a "write something like…" instruction.

### `src/PubInvest.HouseConfig.Api/Endpoints/CatalogueEndpoints.cs` (Task 14, step 5)

```csharp
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/catalogue").WithTags("Catalogue");

        group.MapGet("/device-types", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.DeviceTypes.OrderBy(d => d.PartNumber).ToListAsync(ct))
                .Select(DomainMapper.ToDomain));

        group.MapGet("/enclosures", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.Enclosures.OrderBy(e => e.Model).ToListAsync(ct))
                .Select(DomainMapper.ToDomain));

        group.MapGet("/rulesets", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.RuleSets.OrderBy(r => r.Name).ThenBy(r => r.Version).ToListAsync(ct))
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.Version,
                    r.IsDefault,
                    Payload = DomainMapper.ToDomain(r)
                }));

        return app;
    }
}
```

### `tests/PubInvest.HouseConfig.Api.Tests/TestSeed.cs` (Tasks 14 and 15)

Built in code, not read from `seed/catalogue.v1.json`, so API tests never depend on the real catalogue's still-unconfirmed values.

```csharp
using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Api.Tests;

public static class TestSeed
{
    public static readonly Guid DimmerId   = new("aaaa0000-0000-4000-8000-000000000001");
    public static readonly Guid TapeDimId  = new("aaaa0000-0000-4000-8000-000000000002");
    public static readonly Guid RelayId    = new("aaaa0000-0000-4000-8000-000000000003");
    public static readonly Guid Psu240Id   = new("aaaa0000-0000-4000-8000-000000000004");
    public static readonly Guid Psu100Id   = new("aaaa0000-0000-4000-8000-000000000005");
    public static readonly Guid TerminalId = new("aaaa0000-0000-4000-8000-000000000006");
    public static readonly Guid EarthId    = new("aaaa0000-0000-4000-8000-000000000007");
    public static readonly Guid BridgeId   = new("aaaa0000-0000-4000-8000-000000000008");
    public static readonly Guid EndStopId  = new("aaaa0000-0000-4000-8000-000000000009");

    public static readonly Guid EnclosureId     = new("bbbb0000-0000-4000-8000-000000000001");
    public static readonly Guid TinyEnclosureId = new("bbbb0000-0000-4000-8000-000000000002");
    public static readonly Guid RuleSetId       = new("cccc0000-0000-4000-8000-000000000001");

    public static SeedDocument Document() => new(
        Version: 1,
        DeviceTypes:
        [
            new SeedDeviceType(DimmerId,   "Shelly", "Test Dimmer 2",    "T-DIM2",  "Dimmer240",   2, 2, 200, 400,  60.00m, true),
            new SeedDeviceType(TapeDimId,  "Shelly", "Test Dimmer 10V",  "T-DIM10", "Dimmer0_10V", 2, 2, null, null, 55.00m, true),
            new SeedDeviceType(RelayId,    "Shelly", "Test Relay 4",     "T-REL4",  "Relay",       4, 4, 3680, 7360, 95.00m, true),
            new SeedDeviceType(Psu240Id,   "Test",   "PSU 240",          "T-PSU240","Psu24V",      6, 0, null, 240,  85.00m, true),
            new SeedDeviceType(Psu100Id,   "Test",   "PSU 100",          "T-PSU100","Psu24V",      3, 0, null, 100,  45.00m, true),
            new SeedDeviceType(TerminalId, "Test",   "Terminal",         "T-TB",    "Terminal240", 1, 0, null, null,  1.50m, true),
            new SeedDeviceType(EarthId,    "Test",   "Terminal PE",      "T-TBPE",  "Terminal240", 1, 0, null, null,  2.10m, true),
            new SeedDeviceType(BridgeId,   "Test",   "Jumper bar",       "T-BAR",   "Accessory",   0, 0, null, null,  3.00m, true),
            new SeedDeviceType(EndStopId,  "Test",   "End stop",         "T-STOP",  "Accessory",   0, 0, null, null,  0.80m, true)
        ],
        Enclosures:
        [
            new SeedEnclosure(EnclosureId,     "Test", "Box 6x24", 6, 24, "IP30", 220.00m),
            new SeedEnclosure(TinyEnclosureId, "Test", "Box 1x6",  1,  6, "IP30",  40.00m)
        ],
        RuleSets:
        [
            new SeedRuleSet(RuleSetId, "Test rules", 1, true, new RuleSetPayload(
                BandOrder: [DeviceCategory.Terminal240, DeviceCategory.Dimmer240, DeviceCategory.Relay, DeviceCategory.Psu24V],
                BandStartsNewRow: true,
                PsuDeratingFactor: 0.8m,
                PreferredDevice: new PreferredDevices(DimmerId, TapeDimId, RelayId, [Psu240Id, Psu100Id]),
                Terminals: new TerminalRules(
                    Line:    new TerminalConductorRule(TerminalId, 1, false),
                    Neutral: new TerminalConductorRule(TerminalId, 1, true),
                    Earth:   new TerminalConductorRule(EarthId,    1, true),
                    BridgeBarDeviceTypeId: BridgeId,
                    BridgeBarWays: 10,
                    EndStopDeviceTypeId: EndStopId,
                    EndStopsPerBank: 2),
                Packing: "firstFit"))
        ]);
}
```

### `src/PubInvest.HouseConfig.Api/Dockerfile` (Task 12, step 6)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Packages.props ./
COPY src/ ./src/
COPY seed/ ./seed/
RUN dotnet publish src/PubInvest.HouseConfig.Api/PubInvest.HouseConfig.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "PubInvest.HouseConfig.Api.dll"]
```

## Appendix B: deliberately deferred to later plans

Named here so nobody assumes they were forgotten:

- **Device position and channel-name edits** (`PATCH /devices/{id}/position`, `PATCH /devices/{id}/channels/{index}`) and the `409` stale-`layoutVersion` handling — Plan 2, alongside the drag-and-drop UI that is their only consumer.
- **Device identification** — dropped from the design entirely: Shelly Pro units carry no printed QR code or serial, so there is nothing to photograph. If matching a physical unit to its slot becomes necessary, LAN discovery via mDNS and `Shelly.GetDeviceInfo` is the route to revisit, and it would be its own plan.
- **`POST /submains/{id}/revisions`**, PDF export and the BOM export endpoints — Plan 3. The `PanelRevision` table is created in Task 10 so no migration is needed later.
- **Catalogue write endpoints** and the admin screens — Plan 3. Reads land in Task 14 because preview needs them.
