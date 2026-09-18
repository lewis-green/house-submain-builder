# House Panel Designer — Design

**Date:** 2026-09-17
**Status:** Approved design, pending spec review

## Purpose

A mobile-friendly web app for designing the electrical panels of a house, one
submain at a time. An engineer enters how many dimmed lighting circuits,
switched circuits and LED tape circuits a submain carries; the app works out
how many Shelly Pro devices that needs, lays them out in a DIN enclosure
according to house best practice, and draws the result. On site, the engineer
taps a device to name its channels. The finished design produces a printable
panel drawing with a circuit schedule, and a bill of materials.

## Scope

**In scope:** project and submain management; device and enclosure catalogue;
rule-driven panel generation; to-scale panel drawing with drag-to-rearrange;
channel naming; PDF drawing and circuit schedule; bill of materials.

**Out of scope (deliberately):** device identification — Shelly Pro units carry
no printed QR code or serial, so there is nothing to photograph, and matching a
physical unit to its slot is deferred until there is a reason to solve it; photo
capture and storage, which existed only to serve that; label printing (the
existing `label-api` / `label-ui` can consume design data later if wanted);
pushing configuration to the Shelly devices themselves; Home Assistant export;
offline operation; cable and breaker calculations — protection lives upstream of
these panels.

## Decisions taken

| Question | Decision |
|---|---|
| Persistence | Full server app: API, Postgres, Keycloak auth |
| Catalogue and rules | Postgres rows, seeded from versioned JSON, admin-editable in-app |
| Hierarchy | House (Project) → Submain → one Panel, 1:1 |
| Device identification | Deferred — no printed QR or serial exists on the hardware |
| Panel drawing | To-scale SVG, drag-to-rearrange, touch-friendly |
| Outputs | PDF panel drawing + circuit schedule; bill of materials |
| LED tape | 24V constant-voltage drivers in-panel, dimmed 0/1-10V |
| Layout rules | Function-banded rows; no MCB/RCBO band; 240V circuit terminals on top |
| Row zones | Termination on top (terminals then ±24V from the left, isolator hard right); Shelly kit below (dimmers left, relays right) |
| Terminals | One WAGO 2003-7646 per circuit, carrying L, N and E on a single slice |
| Earth | Commons through the DIN rail: no bar, no separate PE part |
| Neutral | Commoned with a jumper bar; live loops out to the Shelly |
| Incoming feed | Lands on a two-pole isolator at the head of the top row |
| LED drivers | Not DIN mount: sized and costed but never placed. The panel carries 12-way +24V and −24V blocks, one way per tape circuit |
| Row packing | Layouts are tried finest first — a row per kind of device, then dimmers together, then everything together — and the first that fits the enclosure wins |
| Architecture | Server-authoritative generator; client renders and edits optimistically |

## Architecture

Monorepo, following the `label-api` pattern:

```
house-config/
  src/
    PubInvest.HouseConfig.Domain/   pure generator, no EF dependency
    PubInvest.HouseConfig.Data/     EF Core, migrations, seeding
    PubInvest.HouseConfig.Api/      minimal API, auth, PDF, BOM
  tests/
    ...Domain.Tests/                unit + golden-file layout tests
    ...Api.Tests/                   integration tests, Testcontainers Postgres
  ui/                               React 19 + Vite + TypeScript + Tailwind
  docs/
  docker-compose.yml                Postgres + API
```

**Stack:** .NET 9 minimal API, EF Core 9, Postgres 17, Keycloak bearer auth
(`Keycloak.AuthServices.Authentication`), QuestPDF for documents. UI is
React 19 + Vite + TypeScript + Tailwind, consistent with `crm-ui` and
`dashboard`.

**Why server-authoritative:** the generator is the only thing that decides what
a panel looks like, so the drawing, the schedule and the BOM cannot drift apart,
and an admin's rule edit changes behaviour with no redeploy. The cost is a round
trip per what-if, which is immaterial for a sub-millisecond pure function.

## Domain model

### Catalogue (admin-editable, seeded from versioned JSON)

**`DeviceType`** — `id`, `manufacturer`, `model`, `partNumber`, `category`,
`moduleWidth` (T-slots), `channelCount`, `maxLoadPerChannelW`, `maxTotalLoadW`, `active`.

`category` is one of: `Terminal240`, `Isolator`, `Dimmer240`, `Dimmer0_10V`,
`Relay`, `Dc24VPositive`, `Dc24VNegative`, `ExternalDriver`, `Accessory`.

`ExternalDriver` and `Accessory` are never placed on the rail, so they carry a
`moduleWidth` of zero and appear only on the bill of materials.

**`EnclosureType`** — `id`, `manufacturer`, `model`, `rows`, `slotsPerRow`,
`ipRating`.

**`RuleSet`** — `id`, `name`, `version`, `payload` (JSON), `isDefault`. The
payload is validated on save — including that every device it references exists
and is active — and carries:

```jsonc
{
  "layouts": [
    { "zones": [
      { "fromLeft": ["Terminal240", "Dc24VPositive", "Dc24VNegative"], "fromRight": ["Isolator"] },
      { "fromLeft": ["Dimmer240"], "fromRight": [] },
      { "fromLeft": ["Dimmer0_10V"], "fromRight": [] },
      { "fromLeft": [], "fromRight": ["Relay"] }
    ] },
    { "zones": [
      { "fromLeft": ["Terminal240", "Dc24VPositive", "Dc24VNegative"], "fromRight": ["Isolator"] },
      { "fromLeft": ["Dimmer240", "Dimmer0_10V"], "fromRight": [] },
      { "fromLeft": [], "fromRight": ["Relay"] }
    ] },
    { "zones": [
      { "fromLeft": ["Terminal240", "Dc24VPositive", "Dc24VNegative"], "fromRight": ["Isolator"] },
      { "fromLeft": ["Dimmer240", "Dimmer0_10V"], "fromRight": ["Relay"] }
    ] }
  ],
  "psuDeratingFactor": 0.8,
  "preferredDevice": {
    "Dimmer240":  "<deviceTypeId>",
    "Dimmer0_10V":"<deviceTypeId>",
    "Relay":      "<deviceTypeId>",
    "isolator":      "<deviceTypeId>",
    "dc24VPositive": "<deviceTypeId>",
    "dc24VNegative": "<deviceTypeId>",
    "externalDriver": ["<small>", "<medium>", "<large>"],
    "Terminal240":"<deviceTypeId>"
  },
  "terminals": {
    "deviceTypeId": "<wago-2003-7646>",
    "blocksPerCircuit": 1,
    "bridgeBarDeviceTypeId": "<wago-jumper-bar>",
    "bridgeBarWays": 10,
    "endStopDeviceTypeId": "<wago-end-stop>"
  },
}
```

Layouts are tried in order and the first that fits the enclosure wins, so a spare
row is spent on separation rather than left spare. Within a layout, zones run top
to bottom and each starts on a fresh row, so the Shelly kit never shares a rail
with the terminations. `ExternalDriver` appears in no zone, because
it is never placed. Zones are expressed by device category, so adding a category
later is a data change rather than a code change; a category no zone mentions is
still placed, in a zone of its own at the bottom, rather than silently vanishing
from the drawing.

### Design

**`Project`** — the house. `id`, `name`, `address`, `notes`, audit fields.

**`Submain`** — `id`, `projectId`, `name`, `reference`, supply details (feed
cable size, origin breaker rating, phase), `enclosureTypeId`, `notes`, and
`layoutVersion`, an integer bumped on every change to the layout (generation,
device move, channel assignment) and used for optimistic concurrency. One
submain is exactly one panel, so the enclosure lives here; there is no separate
Panel table.

**`Circuit`** — the demand, and a child of the submain rather than of a device.
`id`, `submainId`, `type` (`DimmedLighting` | `Switched` | `LedTape`), `name`,
`room`, `sequence`, and for LED tape `wattsPerMetre` and `lengthMetres`.

The wizard's counts create these rows immediately with placeholder names
(`Lighting 1`, `Switched 3`, `Tape 2`), so a count and a named circuit are the
same record from the start; naming later is an edit, not a creation. Because a
circuit exists independently of any device, the generator can re-run and
re-assign channels without destroying names.

**`DeviceInstance`** — a placed device. `id`, `submainId`, `deviceTypeId`,
`rowIndex`, `startSlot`, `moduleWidth`, `label`, `terminalRole`.

**`DeviceChannel`** — `deviceInstanceId`, `channelIndex`, `circuitId` (nullable),
`isSpare`. A channel is either assigned to a circuit or explicitly spare.

**`PanelRevision`** — an immutable JSON snapshot of a submain's layout, taken
whenever a design is issued. Records the `layoutVersion` it was taken from, the
`ruleSet` payload as used, the resolved catalogue entries for every placed
device, and `issuedAt` / `issuedBy`. Embedding the rules and catalogue rather
than referencing them means a later admin edit cannot retrospectively change
what an issued drawing meant. PDFs render from a revision, so a drawing in a
folder can always be traced to the exact rules that produced it.

## The generator

A pure function in `Domain`, with no database access:

```
(circuits, enclosureType, ruleSet, catalogue) → Layout + Diagnostics + BillOfMaterials
```

Identical inputs always produce an identical panel. This is the main reason it
lives server-side in C#: it is directly unit-testable, and golden-file tests
catch any rule change that silently moves a device.

### Steps

1. **Classify** circuits by type.
2. **Size control devices.** Dimmed lighting → `Dimmer240` channels; switched →
   `Relay` channels; LED tape → `Dimmer0_10V` channels. Device count per category
   is `ceil(circuits / channelCount)` for the preferred model. Because each
   circuit type maps to its own device category, circuit types never share a
   device — that convention needs no rule of its own. One two-pole isolator is
   added ahead of everything else; a panel with no means of isolation is an error,
   not a warning.
3. **Size the 24V supply.** Sum LED tape watts (`wattsPerMetre × lengthMetres`
   per circuit) and divide by `psuDeratingFactor`. The driver that covers it is
   chosen largest-first from the catalogue and put on the BOM, but **never placed**:
   LED drivers are not DIN mount and live outside the panel. What the panel does
   carry is a 12-way +24V block and a 12-way −24V block, one way per tape circuit,
   so a second pair appears past twelve.
4. **Build the terminal band.** One block per outgoing circuit. The 2003-7646
   carries line, neutral and earth on a single slice, so a circuit needs one
   block, not one in each of three banks. Earth commons through the DIN rail and
   needs no bar; the neutral tier is bridged, contributing
   `ceil(blocks / bridgeBarWays)` jumper bars and one set of end stops; line is
   per-circuit and loops out to its Shelly channel. **No block is spent on the
   incoming feed** — it lands on the isolator. Bars and stops add no width but do
   appear in the BOM.
5. **Pack.** Try each layout in `layouts` in turn and keep the first that fits
   the enclosure's rows; if none do, keep the densest and report the overflow
   against it. Within a layout, walk the zones top to bottom, each starting on a
   fresh row. Right-hand devices are placed first so the isolator is guaranteed
   the top-right corner even when the terminals run onto a second row; then
   left-hand devices fill from the left. A row is full when the two fronts would
   meet, and the next row of the same zone takes the overflow.
6. **Validate.** Emit `Diagnostics`, never exceptions. "Needs 5 rows, this
   enclosure has 4" is an ordinary result, shown in the UI with the smallest
   catalogue enclosure that would fit.

### Re-running on an existing design

Circuits live on the submain, not on devices, so circuit names and rooms survive
re-generation untouched — the generator simply re-assigns them to channels.
Anything that cannot be re-assigned — a circuit deleted from the submain, or one
whose device category no longer exists — is reported as a diagnostic rather than
vanishing silently.

Once the engineer can drag devices, those manual positions become state worth
preserving across a re-run, and re-generation becomes a genuine merge. Until
then there is nothing held on a device worth carrying forward, so re-generation
replaces the device rows and reports orphaned circuits.

### Diagnostics

Each diagnostic carries `severity` (`Error` | `Warning` | `Info`), `code`,
`message`, and optional `suggestion`. Errors block `generate`; warnings do not.

- `ENCLOSURE_TOO_SMALL` — rows or slots exceeded; suggests a larger enclosure.
- `NO_PREFERRED_DEVICE` — ruleset names a device type that is inactive or absent,
  including a missing isolator or 24V distribution block.
- `PSU_UNSIZED` — LED tape load exceeds the largest catalogue PSU.
- `ORPHANED_ASSIGNMENT` — a previously assigned circuit has no home in the new layout.
- `TAPE_LOAD_MISSING` — an LED tape circuit has no watts/length, so PSU sizing is a guess.

## API surface

.NET minimal API, Keycloak bearer auth, roles `engineer` and `catalogue-admin`.

**Projects and submains**
```
GET    /projects
POST   /projects
GET    /projects/{id}
GET    /projects/{id}/submains
POST   /projects/{id}/submains
GET    /submains/{id}
PATCH  /submains/{id}
```

**Design**
```
POST   /submains/{id}/design/preview    runs generator, persists nothing
POST   /submains/{id}/design/generate   runs generator, commits with merge rules
GET    /submains/{id}/layout
POST   /submains/{id}/revisions         issue an immutable snapshot
```

`preview` is what drives the live readout in the wizard ("3 dimmers, 2 relays,
1 × 150W PSU, 26 of 48 slots") as counts are typed.

**Editing**
```
PATCH  /devices/{id}/position           { rowIndex, startSlot, basedOnLayoutVersion }
PATCH  /devices/{id}/channels/{index}   { circuitId | isSpare }
PATCH  /circuits/{id}                   { name, room }
```

**Outputs**
```
GET    /submains/{id}/export/pdf
GET    /submains/{id}/bom
GET    /projects/{id}/bom
```

**Catalogue admin** (role `catalogue-admin`)
```
GET|POST|PATCH  /catalogue/device-types
GET|POST|PATCH  /catalogue/enclosures
GET|POST        /catalogue/rulesets
```

### Concurrency

Position and channel edits carry the `layoutVersion` they were based on. A
stale edit returns `409 Conflict` with the current layout rather than silently
clobbering. `PanelRevision` is a separate concept — an issued snapshot for the
record — and plays no part in concurrency control.
Two engineers on one panel is unlikely; a phone left open in a pocket is not.

## UI

Mobile-first. React 19 + Vite + TypeScript + Tailwind.

**Projects → submains list.** Each submain shows its state at a glance:
not designed, or designed with its device and circuit counts.

**New submain wizard.** Supply details → enclosure pick → circuit counts, with
LED tape taking watts-per-metre and length per circuit. The preview readout
updates as values change, showing device counts, PSU sizing and slot usage
against the chosen enclosure, plus any diagnostics.

**Panel view.** The to-scale SVG. Rows stack vertically at true DIN
proportions; pinch-zoom and pan. Tap a device to open a bottom sheet with its
channels. Long-press to pick up, drag to a new slot. Dragging is optimistic and
snaps to slot boundaries; invalid targets (no room, wrong band) are greyed
before release, and the server revalidates on drop, reverting the optimistic
move on `409`. `touch-action: none` is applied only while a drag is live, so
normal panning is unaffected.

**Device sheet.** Channel list with editable circuit names and rooms, and the
device's type and position.

## Outputs

**PDF** via QuestPDF, rendered server-side from a `PanelRevision`:
page 1 the to-scale panel drawing, page 2+ the circuit schedule — submain,
device, channel, circuit name, room.

**Bill of materials** aggregated per submain and per house — Shelly units,
enclosure, terminal blocks, jumper bars, end stops, 24V joints and the external
driver — exportable as CSV. It is a parts list and carries **no prices**:
costing happens wherever your pricing actually lives. Accessories are counted
even though they consume no DIN slots, and anything not mounted on the rail is
marked so nobody hunts for it in the panel.

Both derive from the same layout model the screen draws, so the drawing, the
schedule and the parts list cannot disagree.

## Testing

Test-driven throughout.

- **Domain (xUnit):** packing edge cases (exact fit, one slot short, device
  wider than a row); PSU sizing boundaries including the derating factor;
  orphaned-circuit reporting on re-generation; every diagnostic code; golden-file
  layout tests that fail loudly when a rule change moves a device.
- **API (xUnit + Testcontainers Postgres):** auth and role enforcement,
  generate round trips, `409` on stale edits.
- **UI (Vitest + Testing Library):** wizard state and preview, device sheet
  editing, optimistic move and revert.
- **E2E (Playwright):** the drag interaction, which is the part most likely to
  break silently under touch.

## Error handling

- Generation never throws for a domain condition; it returns diagnostics the UI
  presents with suggestions.
- Stale edits return `409` with current state; the client reverts and reloads.
- Catalogue or ruleset references that go missing surface as diagnostics at
  generation time, not as 500s.

## Risks and open items

**Open — the seed catalogue is unverified.** The values below are the author's
best recollection and **must be confirmed against datasheets before any code
depends on them**. Wrong module widths would make every generated panel wrong in
a way the drawing would hide.

| Device | Category | Channels | Module width (T-slots) | Notes |
|---|---|---|---|---|
| Shelly Pro Dimmer 2PM | `Dimmer240` | 2 | ? | leading/trailing edge, per-channel W limit unverified |
| Shelly Pro Dimmer 0/1-10V PM | `Dimmer0_10V` | 2 | ? | drives 24V CV drivers |
| Shelly Pro 4PM | `Relay` | 4 | ? | 16A per channel unverified |
| Shelly Pro 2PM | `Relay` | 2 | ? | alternative relay model |
| 24V LED driver (model TBC) | `ExternalDriver` | — | 0 | not panel-mounted; need the wattages actually stocked |
| Two-pole isolator (model TBC) | `Isolator` | — | ? | part number and width to confirm |
| WAGO 12-way +24V block (part TBC) | `Dc24VPositive` | — | ? | part number, width and ways to confirm |
| WAGO 12-way −24V block (part TBC) | `Dc24VNegative` | — | ? | part number, width and ways to confirm |
| WAGO TOPJOB S 2003-7646 | `Terminal240` | — | 1 | **confirmed**: 3 per DIN module, carrying L/N/E on one slice |
| WAGO jumper bar (part TBC) | `Accessory` | — | 0 | `bridgeBarWays` to confirm; no slot width |
| WAGO end stop (part TBC) | `Accessory` | — | 0 | quantity per bank to confirm |

Also open: the jumper bar part number and its `bridgeBarWays`, and the end-stop
quantity for the neutral bank.

**Risk — no offline capability.** A full server app cannot be used in a plant
room with no signal. If dead spots turn out to be a real problem, the remedy is a
service worker queuing edits, added deliberately later rather than half-built
now.

**Risk — drag on touch.** Slot-snapped dragging inside a zoomable SVG is the
fiddliest part of the build. The Playwright test exists specifically to keep it
honest.

## Sequence of work

1. Domain project and generator, TDD, with a hand-written catalogue fixture.
2. Data layer, migrations, catalogue seeding from JSON.
3. API: projects, submains, preview, generate.
4. UI: projects, submains, wizard with live preview.
5. UI: panel SVG rendering (read-only first).
6. Editing: drag-to-rearrange, channel naming.
7. PDF and BOM.
8. Catalogue admin screens.
