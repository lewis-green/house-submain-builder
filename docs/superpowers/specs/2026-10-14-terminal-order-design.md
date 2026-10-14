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
- **Reorder is in place, count is a regenerate.** Reordering only relabels.
  Changing a count adds or removes terminals, which changes geometry.

## 1. Data

- `CircuitRow.TerminalPosition` (`int?`), with a migration. Null means "use
  `Sequence`", so existing projects keep their current order.
- Domain `Circuit` gains `int? TerminalPosition`. Effective terminal order is
  `TerminalPosition ?? Sequence`, ties broken by `Sequence`.
- `CircuitRow.TerminalCount` (`int`, default 1, valid range 1–12), in the
  same migration. Existing rows get 1. Domain `Circuit` and `CircuitInput` /
  `CircuitSnapshot` gain it too. In snapshots it is nullable, read as 1.
- `TerminalRules.BlocksPerCircuit` is removed from the ruleset payload.
  Deserialization ignores the old field, so stored rulesets still load.
- `TerminalBandBuilder` orders by effective terminal order and gives each
  circuit `TerminalCount` adjacent blocks. They are labelled `C{n}` when the
  count is 1, or `C{n}.1`…`C{n}.{k}` otherwise, where `n` is the 1-based
  circuit position. So circuits keep consecutive numbers whatever their
  counts.
- Each terminal `RequiredDevice` carries one channel (index 0) with the
  circuit's id, so the persisted `DeviceInstance` links terminal to circuit.
  Terminals have `channelCount: 0` in the catalogue. This channel is a link,
  not a Shelly output. It must not show as a spare channel, and it must not
  count towards capacity, the BOM or orphan reporting.
- `CircuitSnapshot` in revisions gains `TerminalPosition` and
  `TerminalCount`. Both are nullable, so old snapshots still deserialize.

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
- `SubmainEndpoints.ApplyCircuits` keeps `TerminalPosition` on circuits that
  survive an edit. New circuits get null, which puts them at the end in
  `Sequence` order.

## 2. Reorder endpoint

`PUT /submains/{id}/terminal-order`, body `{ "circuitIds": [guid, …] }`.

- 404 if the submain is unknown.
- 400 (validation problem on `circuitIds`) unless the list holds every circuit
  of the submain exactly once.
- Sets `TerminalPosition = index + 1` on each circuit.
- If the submain has a generated layout, it relabels in place. It takes the
  terminal devices (`Category == Terminal240`) sorted by row, then slot, and
  walks them in step with the new order, `TerminalCount` blocks per
  circuit. For
  each terminal it rewrites `Label` and the channel's `CircuitId`. No
  geometry changes, so `LayoutVersion` is neither checked nor bumped,
  matching circuit rename.
- If the number of terminals on the panel doesn't match the sum of
  `TerminalCount` (a stale layout), it saves the order and skips the relabel. The response
  says `relabelled: false`, so the UI can tell the user to regenerate.
- Returns `{ relabelled, terminals: [{ circuitId, label }] }`.

## 2b. Terminal count endpoint

Changing counts changes geometry, so it goes through regenerate.

`PUT /submains/{id}/terminal-counts`, body
`{ "layoutVersion": n, "counts": [{ "circuitId": guid, "terminalCount": k }] }`.

- 400 if a count is outside 1–12 or an id is not one of the submain's
  circuits. Circuits left out keep their count.
- 409 on a stale `layoutVersion`, the same as the other layout edits.
- Saves the counts, then runs the same generate as `DesignService.GenerateAsync`.
  That covers the atomic save, position overrides, orphan reporting and the
  version bump. It returns the normal design response, including diagnostics
  such as the rail being full.
- If generation has errors, nothing is saved, the same as generate today.

## 3. UI

**Drawing (`PanelSvg.tsx`)**
- The run stays one block, with a thin divider line between terminals.
- A circuit with several terminals gets a bar drawn along the top edge of its
  blocks, so the bridged group reads as one circuit.
- When a terminal's drawn width fits the rotated label (zoom-dependent), each
  terminal shows its own `C#` on its side, and the run label is dropped.
  Otherwise the run keeps its `C1–C44` label.
- The run becomes a button (`aria-label="Terminals C1–C44, change order"`)
  that opens the terminal sheet. This replaces today's `role="img"`.

**Terminal sheet (new `panel/TerminalSheet.tsx`, built on `Sheet`)**
- One row per circuit in terminal order: `C3 · Hall lights · Hall · Dimmed`.
- Each row has a "cables" stepper (1–12). When it is above 1, the row label
  shows the range: `C3.1–C3.6`.
- Up and down buttons on each row, at least 44px, disabled at the ends. Labels
  renumber live as rows move.
- Save calls the reorder endpoint when only the order changed. It calls the
  counts endpoint (then the reorder endpoint, if the order also changed) when
  any count changed. Then it refreshes the design. A 409 shows the usual
  "panel changed, reload" message. Cancel throws the changes
  away. Save is disabled until something changes. Uses `useSingleFlight` as
  the other sheets do.
- If `relabelled: false`, it shows a note: "Order saved. Regenerate the
  panel to apply it."

## 4. PDF

- `PanelDocument` schedule gets a leading **Terminal** column. It comes from
  the terminal device whose channel carries that circuit (joined on
  `CircuitId`), and is blank for spare channels. A bridged circuit shows its
  range, `C3.1–C3.6`.
- `PanelSvgRenderer` draws the same dividers between terminals in a run, and
  the per-terminal rotated `C#` when it fits at PDF scale, plus the bar over
  each bridged group.

## 5. Tests

- **Domain:** `TerminalBandBuilder` orders by `TerminalPosition`, falls back
  to `Sequence`, mixes the two (positioned first, the rest after), labels
  `C3.1`…`C3.6` for a count of 6 with the next circuit still `C4`, adds
  `ceil(N/ways)` line bars per bridged circuit (6 → 1, 12 → 2 at 10-way, none
  for count 1), and gives each terminal a channel
  with its circuit id. Shelly channel assignment is unchanged when only
  `TerminalPosition` changes.
- **API:** reorder relabels in place. Device ids, positions and
  `LayoutVersion` stay the same, and Shelly channels stay the same. It
  rejects missing, duplicate or foreign ids, saves without relabelling on a
  stale layout, keeps the order across regenerate, and keeps it across a
  circuit edit. The counts endpoint regenerates with the extra terminals and
  line bars in the BOM, returns 409 on a stale version, rejects counts outside
  1–12, and saves nothing when generation fails. The PDF schedule shows the
  Terminal column, with ranges for bridged circuits.
- **UI unit:** the sheet moves rows, renumbers, disables the end buttons,
  sends the right id list, and calls the counts endpoint only when a count
  changed.
- **E2E:** open the panel, open the terminal sheet, move one circuit up,
  save, and see the new order in the sheet after a reload. Then set a
  circuit to 3 cables, save, and see `C{n}.1–C{n}.3` in the sheet.

## Out of scope

- Dragging terminals on the drawing.
- Keeping a terminal number fixed to a circuit.
- Reordering anything other than terminals.
- Reusing jumper bar offcuts across circuits.
- Bridging terminals across circuits (that would be a wiring fault).
