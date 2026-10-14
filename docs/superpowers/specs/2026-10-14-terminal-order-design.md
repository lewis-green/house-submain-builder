# Terminal order — design

## Problem

Each circuit lands on one WAGO 2003-7646 terminal, labelled `C1`, `C2`… in
circuit `Sequence` order (`TerminalBandBuilder`). The drawing, on screen and
in the PDF, merges a contiguous run into one block labelled `C1–C44`. Nobody
can see which circuit is on which terminal, and nobody can choose the order.

The user wants to break the run down: see each terminal's circuit, and set
the order of circuits along the rail.

## Decisions

- **Number = position.** `C1` is always the first terminal on the rail.
  Moving a circuit renumbers the terminals after it.
- **Reorder in a terminal sheet**, not by dragging on the drawing. A terminal
  is one slot unit (a third of a module) wide, too narrow to drag on a phone.
- **Terminal order is separate from `Sequence`.** `Sequence` decides Shelly
  channel assignment (`DeviceDemandCalculator`) and tape supply sizing
  (`TapeSupplySizer`). Reusing it would reshuffle devices on every reorder.

## 1. Data

- `CircuitRow.TerminalPosition` (`int?`), with a migration. Null means "use
  `Sequence`", so existing projects keep their current order.
- Domain `Circuit` gains `int? TerminalPosition`. Effective terminal order is
  `TerminalPosition ?? Sequence`, ties broken by `Sequence`.
- `TerminalBandBuilder` orders by effective terminal order. Labels stay
  `C{n}`, or `C{n}.{k}` when `BlocksPerCircuit > 1`, where `n` is the
  1-based position.
- Each terminal `RequiredDevice` carries one channel (index 0) with the
  circuit's id, so the persisted `DeviceInstance` links terminal to circuit.
  Terminals have `channelCount: 0` in the catalogue. This channel is a link,
  not a Shelly output. It must not show as a spare channel, and it must not
  count towards capacity, the BOM or orphan reporting.
- `CircuitSnapshot` in revisions gains `TerminalPosition`. It is nullable, so
  old snapshots still deserialize.
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
  walks them in step with the new order, `BlocksPerCircuit` per circuit. For
  each terminal it rewrites `Label` and the channel's `CircuitId`. No
  geometry changes, so `LayoutVersion` is neither checked nor bumped,
  matching circuit rename.
- If the terminal count on the panel doesn't match `circuits × BlocksPerCircuit`
  (a stale layout), it saves the order and skips the relabel. The response
  says `relabelled: false`, so the UI can tell the user to regenerate.
- Returns `{ relabelled, terminals: [{ circuitId, label }] }`.

## 3. UI

**Drawing (`PanelSvg.tsx`)**
- The run stays one block, with a thin divider line between terminals.
- When a terminal's drawn width fits the rotated label (zoom-dependent), each
  terminal shows its own `C#` on its side, and the run label is dropped.
  Otherwise the run keeps its `C1–C44` label.
- The run becomes a button (`aria-label="Terminals C1–C44, change order"`)
  that opens the terminal sheet. This replaces today's `role="img"`.

**Terminal sheet (new `panel/TerminalSheet.tsx`, built on `Sheet`)**
- One row per circuit in terminal order: `C3 · Hall lights · Hall · Dimmed`.
- Up and down buttons on each row, at least 44px, disabled at the ends. Labels
  renumber live as rows move.
- Save calls the endpoint and refreshes the design. Cancel throws the changes
  away. Save is disabled until something changes. Uses `useSingleFlight` as
  the other sheets do.
- If `relabelled: false`, it shows a note: "Order saved. Regenerate the
  panel to apply it."

## 4. PDF

- `PanelDocument` schedule gets a leading **Terminal** column. It comes from
  the terminal device whose channel carries that circuit (joined on
  `CircuitId`), and is blank for spare channels.
- `PanelSvgRenderer` draws the same dividers between terminals in a run, and
  the per-terminal rotated `C#` when it fits at PDF scale.

## 5. Tests

- **Domain:** `TerminalBandBuilder` orders by `TerminalPosition`, falls back
  to `Sequence`, mixes the two (positioned first, the rest after), labels
  correctly with `BlocksPerCircuit > 1`, and gives each terminal a channel
  with its circuit id. Shelly channel assignment is unchanged when only
  `TerminalPosition` changes.
- **API:** reorder relabels in place. Device ids, positions and
  `LayoutVersion` stay the same, and Shelly channels stay the same. It
  rejects missing, duplicate or foreign ids, saves without relabelling on a
  stale layout, keeps the order across regenerate, and keeps it across a
  circuit edit. The PDF schedule shows the Terminal column.
- **UI unit:** the sheet moves rows, renumbers, disables the end buttons, and
  sends the right id list.
- **E2E:** open the panel, open the terminal sheet, move one circuit up,
  save, and see the new order in the sheet after a reload.

## Out of scope

- Dragging terminals on the drawing.
- Keeping a terminal number fixed to a circuit.
- Reordering anything other than terminals.
