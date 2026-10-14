# Network Racks — Design

**Date:** 2026-10-14
**Status:** Approved design, pending spec review

## Purpose

A house has network racks as well as electrical panels. An electrician adds a
rack to a house the way they add a submain, picks what goes in it from a list —
UniFi switches and patch panels — and the app places each item in the rack by
rack unit (U), draws the rack, and lets them label every port and mark which
ones carry PoE. A finished rack produces what a finished panel does: a drawing,
a port schedule and a parts list, issued as a PDF revision.

## Scope

**In scope:** rack cabinets and rack items in the catalogue; racks on a house;
server-decided placement by U, with moves; a label and a PoE tick on every
port; an optional "fed from" link to one of the house's submains; rack PDF
revisions with drawing, port schedule and parts list; rack lines on the rack
and house bills of materials.

**Out of scope (deliberately):**
- **Patching.** Which switch port a patch-panel port is patched to is not
  recorded. A PoE tick on a patch-panel port means "the cable on this outlet
  needs PoE", nothing more. Patching can be added later without changing what
  is stored here.
- **A supply circuit on the feeding submain.** "Fed from" is a recorded link.
  It does not add a circuit or a terminal to that submain's panel, and setting
  or changing it never rebuilds a panel.
- **PoE power budgeting.** The switch's PoE budget is shown for information;
  the app does not add up watts, because a tick carries no wattage.
- **Racks inside submains.** A rack sits beside submains under the house (see
  Decisions).

## Decisions taken

| Question | Decision |
|---|---|
| What a rack produces | Same as a panel: drawing, port schedule, parts list, issued as a PDF revision. |
| How the cabinet is chosen | From the catalogue, like panel enclosures, so it lands on the BOM. |
| How an item gets its U | Server stacks it into the next free U from the top, in the order added; the electrician can then move it. |
| What a port records | A label and a PoE tick. No patching. |
| What "fed from a submain" does | A recorded link only, shown on the rack and on the submain. |
| Where a rack lives | Beside submains, under the house, with an optional "fed from" submain. Not nested in a submain, and submains are not generalised into "enclosures". |
| U numbering | U1 is the bottom U, as racks are conventionally numbered. Items fill from the top down, so the first item in a 12U cabinet goes in U12. |
| Port numbering | Ports are numbered 1 to N across the whole item, as UniFi numbers them: a 48-port switch's SFP+ uplinks are ports 49–52. |

Why beside submains rather than inside one: the feed is a fact about the rack,
not ownership. A rack may be fed from the main board rather than any submain in
the app; its feed can change without the rack moving; and rebuilding or
renaming a submain must never touch a rack.

## Catalogue

Two new catalogue tables, separate from the DIN catalogue, so the panel
generator, rulesets and DIN device types are untouched. Both are editable in
the Catalogue screen and seeded from `seed/catalogue.v1.json`; the seeder adds
rows it has not seen by id, as it already does for device types and
enclosures, so existing databases receive them.

### Rack cabinets

`RackTypeRow`: `Id`, `Manufacturer`, `Model`, `PartNumber`, `HeightU`,
`Mounting` (`Wall` or `Floor`, for display).

Seeded, all **ASSUMED** until confirmed with the cabinet supplier: 6U, 9U and
12U wall cabinets; 22U and 42U floor cabinets.

### Rack items

`RackDeviceTypeRow`: `Id`, `Manufacturer`, `Model`, `PartNumber`, `Kind`
(`Switch`, `PatchPanel` or `Accessory`), `HeightU`, `PortGroups`, `PoeBudgetW`
(nullable, informational), `RequiredAccessoryId` (nullable), `Active`.

`PortGroups` is an ordered list of `{ Name, From, To, PoeTick }`: a run of
consecutively numbered ports with a name shown in the UI and schedule, and
whether the PoE tick is offered on them. Stored as JSON on the row; groups are
only ever read and written whole.

An `Accessory` has `HeightU` 0 and no ports. It is never placed in a rack; it
exists to be named by another item's `RequiredAccessoryId`, so that placing
that item puts the accessory on the BOM.

Seeded, all **ASSUMED** until checked against Ubiquiti's datasheets (retailer
listings disagree on some per-port details):

| Item | U | Ports | PoE tick | PoE budget |
|---|---|---|---|---|
| UniFi USW-Pro-Max-48-PoE | 1 | 1–32 "1G", 33–48 "2.5G", 49–52 "SFP+" | 1–48 | 720 W |
| UniFi USW-Pro-Max-24-PoE | 1 | 1–16 "1G", 17–24 "2.5G", 25–26 "SFP+" | 1–24 | 400 W |
| UniFi USW-Pro-Max-16-PoE | 1 | 1–12 "1G", 13–16 "2.5G", 17–18 "SFP+" | 1–16 | 180 W |
| 24-port patch panel, 1U | 1 | 1–24 "RJ45" | 1–24 (outlet needs PoE) | — |
| Rack-mount kit for USW-Pro-Max-16-PoE | 0 | — | — | — |

The 16-port switch is half-width and names the rack-mount kit as its required
accessory. The kit's part number is unknown and seeded as ASSUMED.

## Domain model

### Per house

- **`Rack`**: `Id`, `ProjectId`, `Name`, `RackTypeId`, `FedFromSubmainId`
  (nullable; must be a submain of the same house), `Notes`, `LayoutVersion`,
  `CreatedAt`.
- **`RackItem`**: `Id`, `RackId`, `RackDeviceTypeId`, `TopU` (the highest U
  the item occupies), `Sequence`, `Label`.
- **`RackPort`**: keyed by (`RackItemId`, `Number`); `Label` (nullable),
  `Poe`. A port is stored only once it has a label or a tick, so a new
  48-port switch writes no port rows. Removing an item removes its ports.
- **`RackRevision`**: `Id`, `RackId`, `Number` (assigned by the server),
  `IssuedAt`, `IssuedBy`, `SnapshotJson` — the rack, its cabinet, items,
  ports and the catalogue rows they used, as they were when issued.

`FedFromSubmainId` is set to null by the database if that submain is ever
deleted (`ON DELETE SET NULL`), so the link can never point at nothing.

### Placement (pure, in `PubInvest.HouseConfig.Domain/Racks`)

Like the panel packer: no EF, no I/O, fully unit-tested.

- An item of height *h* occupies U `TopU − h + 1` to `TopU`.
- **Adding** places the item at the highest `TopU` where all *h* U are free.
  When no run of *h* free U exists, the add is refused: "No room in this
  rack: n U free" — and nothing is saved.
- **Moving** is accepted when the item stays inside the cabinet (`TopU − h + 1
  ≥ 1`, `TopU ≤ HeightU`) and every U it would occupy is free or its own.
- **Changing the cabinet** is accepted only if every item still fits inside
  the new height at its current position; otherwise it is refused with the U
  range to clear: "Move or remove what's in U10–U12 first". Items are never
  shuffled to make room.

### Names

`Label` is assigned by the server per kind, in the order added: "Switch 1",
"Switch 2", "Patch panel 1". Labels are not renumbered when an item is
removed, and a removed item's name is never handed to a later one — the rack
counts how many of each kind it has ever named — so a name on a printed
drawing keeps meaning the same item.

### Concurrency

`Rack.LayoutVersion` is a concurrency token. Adding, moving and removing an
item bump it inside the same save, so two adds racing for the same U cannot
both land: the second fails its save and is placed again against the new
state.

## API surface

Minimal API endpoints, following the existing ones in style, validation
(`ValidationProblem` for field errors, 422 for refusals with a reason) and
authorization.

```
GET    /catalogue/rack-types
GET    /catalogue/rack-device-types
PATCH  /catalogue/rack-types/{id}             admin
PATCH  /catalogue/rack-device-types/{id}      admin

GET    /projects/{id}/racks
POST   /projects/{id}/racks                    { name, rackTypeId, fedFromSubmainId? }
GET    /racks/{id}
PATCH  /racks/{id}                             { name, rackTypeId, fedFromSubmainId? }
GET    /racks/{id}/layout                      items, positions, ports, free U, layoutVersion

POST   /racks/{id}/items                       { rackDeviceTypeId }  → placed by the server
PATCH  /rack-items/{id}/position               { topU, basedOnLayoutVersion }
DELETE /rack-items/{id}?basedOnLayoutVersion=n
PUT    /rack-items/{id}/ports/{number}         { label, poe }

POST   /racks/{id}/revisions
GET    /racks/{id}/revisions                   each with number and current
GET    /rack-revisions/{id}/export/pdf
GET    /racks/{id}/bom   and   /racks/{id}/bom.csv
```

- **Concurrency:** adding, moving and removing an item bump `LayoutVersion`;
  a move or removal based on an older version gets 409, which the UI answers
  by reloading, as panels do. Port edits do not bump it, as circuit names do
  not.
- **PoE:** a tick on a port whose group has `PoeTick` false (the SFP+ ports)
  is refused with a reason. A port number outside the item's groups is 404.
- **Accessories:** adding an `Accessory` to a rack is refused; accessories
  reach the BOM only through `RequiredAccessoryId`.
- **Feed:** a `fedFromSubmainId` from another house is a field error.
  `GET /submains/{id}/racks` lists the racks a submain feeds, by id and name
  (its own endpoint, so the submain response is unchanged).
- **Currency:** `current` on each revision is decided by comparing the
  revision's snapshot with what issuing now would embed — the same mechanism
  as panel revisions — so a changed label, tick, position, cabinet or feed
  marks the PDF out of date.
- **House BOM:** `GET /projects/{id}/bom` (and its CSV) adds the house's rack
  lines to its panel lines, grouped by part number.

## UI

Phone first, using the components and patterns the panel screens use
(`AppShell`, the bottom sheet, steppers, `ConfirmButton`, the status region,
skeletons, single-flight guards). The UI never computes a placement: it draws
what `/racks/{id}/layout` returns.

### House page

Two lists, each with its own Add button: **Submains** (as now) and **Network
racks**. A rack row shows its name, cabinet, U used of total, and "Fed from
<submain>" when set.

### Add rack

A short form: name, cabinet (from the catalogue), fed from (the house's
submains, or "Not fed from a submain"). Saving creates the rack and opens it.

### Rack screen

```
‹ House                 Bill of materials
Comms cupboard
12U wall cabinet · fed from Ground floor
┌─────┬──────────────────────────────┐
│ U12 │ Patch panel 1   ▪▪▪▫▫▫▫▫▫▫▫▫ │
│ U11 │ Switch 1  48p   ▪▪⚡▪▫▫▫▫▫▫▫▫ │
│ U10 │ empty                        │
│ ... │                              │
└─────┴──────────────────────────────┘
[ Add to rack ]  [ Issue PDF revision ]
```

- The rack is drawn top to bottom, U numbers on the left, every U shown. Each
  item shows its name and a strip of its ports: filled once labelled, ⚡ when
  PoE is ticked.
- **Add to rack** opens a sheet listing the active switches and patch panels
  (name, height, ports) — never accessories, which are not placed. Choosing
  one adds it; a refusal ("No room…") is shown in the sheet.
- **Moving:** drag an item to another U — on a touchscreen hold it first, with
  a mouse drag straight away, exactly as panel devices move — or pick its U in
  the item sheet from a "Move to" list that offers only the free positions.
  The drag gesture is the panel's, shared rather than copied.
- Rack settings (name, cabinet, fed from) are edited in a collapsed section
  below the drawing, as install options are on the panel screen.

### Item sheet

```
Switch 1 · USW-Pro-Max-48-PoE      ×
U11 · 1U · PoE ticked on 6 of 48 ports
 Front panel:  1 3 5 7 … 47
               2 4 6 8 … 48   49–52
 1G · ports 1–32
  1  [ Kitchen AP          ] ☑ PoE
  2  [ Lounge TV           ] ☐ PoE
  …
 SFP+ · ports 49–52 (no PoE)
  49 [ Uplink to router    ]
 [ Remove from rack ]
```

- **Front panel:** a compact picture of the item's ports laid out as on the
  real front panel — a switch's copper ports odd on the top row and even
  below, its uplinks to the right; a patch panel's ports in one row. Tapping
  a port scrolls to and focuses its row.
- **Rows** are where ports are labelled: one per port, grouped under the
  item's port groups. A label saves on blur; **Return saves and moves focus to
  the next port's label** (`enterkeyhint="next"`), so a whole switch can be
  labelled in one run with the keyboard up. The PoE tick saves when changed.
  Groups without the tick show none and say why.
- The header shows the PoE count and, for a switch, its PoE budget for
  information.
- **Remove from rack** asks first, naming how many port labels go with it.

### Submain screen

When a submain feeds racks, its screen says so: "Feeds: Comms cupboard", each
a link to the rack.

## Outputs

**PDF** via QuestPDF, rendered from a `RackRevision`:
- **Page 1, the rack drawing:** every U to scale, each item with its name and
  port strip; a title block with house, rack, cabinet, fed from, revision
  number and date.
- **Page 2 onwards, the port schedule:** a table per item listing every port —
  number, group, label, PoE. Unlabelled ports print as "—", so the schedule
  doubles as a patching sheet on site.
- **Last, the parts list.**

**Bill of materials** per rack and in the house BOM: the cabinet, each item,
and each item's required accessory, with quantities and **no prices**, as the
panel BOM. Exportable as CSV.

The drawing, the schedule and the parts list all come from the same stored
layout, so they cannot disagree with each other or with the screen.

## Error handling

The patterns the panel screens already use:
- Designed loading, empty and error states with **Try again**; a failed port
  save keeps the typed label and can be retried with the same text.
- Refusals (no room, invalid move, smaller cabinet, PoE on SFP+, feed from
  another house) shown in plain words where the action was taken.
- A version conflict reloads with "This rack changed elsewhere — reloading".
- Single-flight guards on Add rack, Add to rack, Remove, and Issue.

## Testing

- **Domain (xUnit):** next free U from the top; taller items; no room; moves
  inside and outside the cabinet, onto free, own and occupied U; cabinet change
  accepted and refused with the right range.
- **API (xUnit + Testcontainers):** every endpoint; PoE refused on SFP+; port
  number out of range; feed from another house refused; 409 on a stale move;
  revision numbering and `current` going stale after a label edit and back;
  PDF starts `%PDF` and its text matches the stored labels; rack and house BOM
  include the cabinet, items and the required accessory; seeding adds the rack
  catalogue to an existing database.
- **UI (Vitest):** house page lists racks; add rack form; rack screen renders
  the layout it is given; item sheet groups rows, Return moves to the next
  port, no tick on SFP+ rows, remove asks first; error and retry states.
- **e2e (Playwright, WebKit as an iPhone, plus the mouse group):** create a
  rack, add a switch and a patch panel, label ports with Return, tick PoE,
  drag an item to another U and see it persist, issue a PDF and fetch it.

## Risks and open items

- **Catalogue specs are ASSUMED.** Port splits, PoE classes and the rack kit's
  part number need checking against Ubiquiti's datasheets; the cabinets need
  real models from the supplier. They are listed in the README's "not
  confirmed yet" section and editable in the Catalogue screen.
- **Port numbering follows UniFi** (uplinks continue the copper numbering).
  If the team labels uplinks separately on site, the schedule's group column
  still says "SFP+", but the numbers would differ.
- **New EF migration.** `dotnet ef` is not installed on this machine; the
  plan adds it as a local tool (`.config/dotnet-tools.json`) so the migration
  can be generated the same way the existing ones were.

## Sequence of work

1. Catalogue: rack tables, seed rows, seeder, catalogue endpoints.
2. Domain placement, with its tests.
3. Racks, items and ports: entities, migration, endpoints, API tests.
4. UI: house page, add rack, rack screen, item sheet.
5. Revisions, PDF, rack and house BOM.
6. e2e, README.
