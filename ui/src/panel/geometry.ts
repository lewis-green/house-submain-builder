import type { PlacedDeviceResponse } from '../api/types'

/** Pixels per slot unit. A DIN module is three of these, so a module is 24px. */
export const SLOT_PX = 8
export const ROW_PX = 72
export const ROW_GAP = 16

export const slotToX = (slot: number) => slot * SLOT_PX
export const rowToY = (row: number) => row * (ROW_PX + ROW_GAP)

export function deviceRect(device: Pick<PlacedDeviceResponse, 'rowIndex' | 'startSlot' | 'moduleWidth'>) {
  return {
    x: slotToX(device.startSlot),
    y: rowToY(device.rowIndex),
    width: slotToX(device.moduleWidth),
    height: ROW_PX,
  }
}

export function panelSize(layout: { rows: number; slotsPerRow: number }) {
  return {
    width: slotToX(layout.slotsPerRow),
    height: layout.rows * ROW_PX + Math.max(layout.rows - 1, 0) * ROW_GAP,
  }
}

/** Nearest slot for a pointer x, clamped so a drag can never leave the rail. */
export function xToSlot(x: number, moduleWidth: number, slotsPerRow: number) {
  const raw = Math.round(x / SLOT_PX)
  return Math.min(Math.max(raw, 0), slotsPerRow - moduleWidth)
}

/** Nearest row for a pointer y, clamped to the enclosure. */
export function yToRow(y: number, rows: number) {
  const raw = Math.round(y / (ROW_PX + ROW_GAP))
  return Math.min(Math.max(raw, 0), rows - 1)
}
