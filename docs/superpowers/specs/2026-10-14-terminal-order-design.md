# Terminal order — design

## Problem

Each circuit lands on one WAGO 2003-7646 terminal, labelled `C1`, `C2`… in
circuit `Sequence` order (`TerminalBandBuilder`). The drawing, on screen and
in the PDF, merges a contiguous run into one block labelled `C1–C44`. Nobody
can see which circuit is on which terminal, and nobody can choose the order.

The user wants to break the run down: see each terminal's circuit, set the
order of circuits along the rail, and give one circuit several terminals
bridged together (say 6 cables landing for one dimmer channel).

## Decisions

- **Number = position.** `C1` is always the first terminal on the rail.
  Moving a circuit renumbers the terminals after it.
- **Reorder in a terminal sheet**, not by dragging on the drawing. A terminal
  is one slot unit (a third of a module) wide, too narrow to drag on a phone.
- **Terminal order is separate from `Sequence`.** `Sequence` decides Shelly
  channel assignment (`DeviceDemandCalculator`) and tape supply sizing
  (`TapeSupplySizer`). Reusing it would reshuffle devices on every reorder.
- **Terminal count is per circuit.** It replaces the ruleset's global
  `BlocksPerCircuit`. A circuit with more than one terminal has its line tier
  bridged with a cut-to-length jumper bar.
- **One save, always a rebuild.** Order and counts are saved together and
  the panel is rebuilt in the same transaction, the way install options are.
  Every terminal is the same width, so a reorder alone gives the same
  geometry. There is no separate in-place relabel path.
- **The terminal-to-circuit link is a field on the device, not a channel.**
  A terminal channel carrying a circuit id would trip the "already wired to
  another channel" check on channel edits, and would hide real orphans from
  `OrphanReporter`.

## 1. Data

- `CircuitRow.TerminalPosition` (`int?`), with a migration. Null means "use
  `Sequence`", so existing projects keep their current order.
- Domain `Circuit` gains `int? TerminalPosition`. Terminal order puts the
  circuits that have a position first, in position order, then the rest in
  `Sequence` order. With no positions at all this is today's order.
- `CircuitRow.TerminalCount` (`int`, default 1, valid range 1–12), in the
  same migration. Existing rows get 1. Domain `Circuit` and `CircuitInput` /
  `CircuitSnapshot` gain it too. In snapshots it is an `int` defaulting to 1, so a revision issued before it existed reads as one terminal per circuit and stays current.
- `TerminalRules.BlocksPerCircuit` is removed from the ruleset payload.
  Deserialization ignores the old field, so stored rulesets still load.
- `TerminalBandBuilder` orders by effective terminal order and gives each
  circuit `TerminalCount` adjacent blocks. They are labelled `C{n}` when the
  count is 1, or `C{n}.1`…`C{n}.{k}` otherwise, where `n` is the 1-based
  circuit position. So circuits keep consecutive numbers whatever their
  counts.
- `RequiredDevice`, `PlacedDevice`, `DeviceInstance` and
  `PlacedDeviceResponse` gain a nullable `CircuitId`, set only on terminals.
  Terminals keep no channels.
- `CircuitSnapshot` in revisions gains `TerminalPosition` and
  `TerminalCount`. `TerminalPosition` is nullable and `TerminalCount` defaults to 1, so old snapshots still deserialize and compare equal.

### Line-tier bridging

- New ruleset fields `TerminalRules.LineBridgeBarDeviceTypeId` and
  `LineBridgeBarWays`. The seed points at a new ASSUMED catalogue entry,
  "Line jumper bar 10-way (ASSUMED)", category `Accessory`, width 0, the same
  pattern as the existing neutral bar.
- For each circuit with `TerminalCount = N > 1`, the BOM adds
  `ceil(N / LineBridgeBarWays)` line bars. Offcuts are not shared between
  circuits, so it over-orders slightly rather than under-ordering.
- `TerminalBand.Accessories` carries the line bars next to the neutral bars
  and end stops. The neutral bar count still uses the total number of blocks.
- `SubmainEndpoints.ApplyCircuits` keeps `TerminalPosition` and
  `TerminalCount` on circuits that survive an edit. New circuits get null and
  1, which puts them at the end in `Sequence` order.
- When a generate or preview request carries its own circuit list, circuits
  whose id matches a stored circuit take that circuit's `TerminalPosition`
  and `TerminalCount`.

## 2. Endpoints

`GET /submains/{id}/terminals` returns
`{ layoutVersion, circuits: [{ circuitId, name, room, type, terminalCount }] }`
in terminal order. It returns 404 for an unknown submain.

`PUT /submains/{id}/terminals`, body
`{ "layoutVersion": n, "circuits": [{ "circuitId": guid, "terminalCount": k }, …] }`,
in the new terminal order.

- 404 if the submain is unknown.
- 409 `ConflictResponse` on a stale `layoutVersion`.
- 400 (validation problem on `circuits`) unless the list holds every circuit
  of the submain exactly once, and every `terminalCount` is 1–12.
- In one transaction, it sets `TerminalPosition = index + 1` and
  `TerminalCount`, then runs `DesignService.GenerateAsync`. That covers
  position overrides, orphan reporting and the version bump. If generation
  refuses (a 422 with diagnostics, e.g. the rail is full), the transaction
  rolls back and nothing is saved, the same as an install-option rebuild.
- On success it returns the same body as the GET.

## 3. UI

**Drawing (`PanelSvg.tsx`)**
- The run stays one block, with a thin divider line between terminals.
- A circuit with several terminals gets a bar drawn along the top edge of its
  blocks, so the bridged group reads as one circuit.
- Each terminal shows its own label on its side, at 12px. That fits the 18px
  terminal at every zoom, because labels scale with the drawing. The run's
  `C1–C44` stays as its accessible name.
- The run becomes a button (`aria-label="Terminals C1–C44, change order"`)
  that opens the terminal sheet. This replaces today's `role="img"`.

**Terminal sheet (new `panel/TerminalSheet.tsx`, built on `Sheet`)**
- One row per circuit in terminal order: `C3 · Hall lights · Hall · Dimmed`.
- Each row has a "cables" stepper (1–12). When it is above 1, the row label
  shows the range: `C3.1–C3.6`.
- Up and down buttons on each row, at least 44px, disabled at the ends. Labels
  renumber live as rows move.
- Save sends one `PUT`, then reloads the panel. A 409 shows the usual "panel
  changed elsewhere — reloading" message. A 422 keeps the sheet open, shows
  the generator's reason and saves nothing. Cancel throws the changes
  away. Save is disabled until something changes. Uses `useSingleFlight` as
  the other sheets do.

## 4. PDF

- `PanelDocument` schedule gets a leading **Terminal** column. It comes from
  the terminal devices whose `CircuitId` is the circuit on that Shelly
  channel, and is blank for spare channels. A bridged circuit shows its
  range, `C3.1–C3.6`.
- `PanelSvgRenderer` draws the same dividers between terminals in a run, and
  the per-terminal rotated label, plus the bar over each bridged group.

## 4b. Agreed during build

- **Terminals can't be dragged.** `PATCH /devices/{id}/position` refuses a
  terminal (400). Generation drops any stored override on a terminal with a
  warning that points at the terminal order. This keeps a bridged circuit's
  blocks together, and makes physical order equal generation order, which the
  schedule's first–last range relies on.
- **A missing line bar is warned, never dropped silently.** If a circuit
  bridges but the ruleset has no line bar ways, or names a part not in the
  catalogue, generation adds one warning and no line bar line. Ruleset save
  refuses line bar ways with no line bar part.
- **The seeder keeps an admin's default ruleset.** A shipped ruleset upgrade
  replaces its payload, name and version but no longer touches `IsDefault`.
- **Deploying ruleset v4 marks existing revisions on the shipped ruleset out
  of date.** A revision embeds its ruleset, and the upgrade adds the line bar
  fields, so the comparison fails although nothing printed changes. Every
  earlier ruleset bump behaved the same. Accepted. Re-issue after deploy where
  a current revision matters.
- **Panels built before deploy have no terminal `CircuitId` until rebuilt**,
  so their PDF Terminal column is blank until then.

## 5. Tests

- **Domain:** `TerminalBandBuilder` orders by `TerminalPosition`, falls back
  to `Sequence`, mixes the two (positioned first, the rest after), labels
  `C3.1`…`C3.6` for a count of 6 with the next circuit still `C4`, adds
  `ceil(N/ways)` line bars per bridged circuit (6 → 1, 12 → 2 at 10-way, none
  for count 1), and sets each terminal's `CircuitId`. Shelly channel assignment is unchanged when only
  `TerminalPosition` changes.
- **API:** GET lists circuits in terminal order. PUT reorders, so the
  terminal labels follow the new order and the Shelly channels don't change.
  It adds blocks and a line bar for a count of 6, returns 409 on a stale
  version, rejects missing, duplicate or foreign ids and counts outside 1–12,
  and saves nothing when generation refuses. The order survives a circuit
  edit and a rebuild. The stored layout and generate both report terminal
  `circuitId`. The PDF schedule shows the Terminal column, with ranges for
  bridged circuits.
- **UI unit:** the sheet moves rows, renumbers, disables the end buttons,
  and sends the circuits in their new order with their counts.
- **E2E:** open the panel, open the terminal sheet, move one circuit up,
  save, and see the new order in the sheet after a reload. Then set a
  circuit to 3 cables, save, and see `C{n}.1–C{n}.3` in the sheet.

## Out of scope

- Dragging terminals on the drawing.
- Keeping a terminal number fixed to a circuit.
- Reordering anything other than terminals.
- Reusing jumper bar offcuts across circuits.
- Bridging terminals across circuits (that would be a wiring fault).
- Keeping a bridged circuit's blocks on one row. The packer places terminals
  one block at a time, so a long run can wrap. The drawing shows two bars and
  the BOM still covers both pieces. Follow-up.
