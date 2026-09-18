import type { LayoutResponse } from '../api/types'

/** The same overlap rule the server enforces, so the UI never promises a move the API will refuse. */
export function isTargetFree(
  layout: LayoutResponse,
  deviceId: string,
  rowIndex: number,
  startSlot: number,
): boolean {
  const device = layout.devices.find(d => d.id === deviceId)
  if (!device) return false

  const end = startSlot + device.moduleWidth
  if (rowIndex < 0 || rowIndex >= layout.rows) return false
  if (startSlot < 0 || end > layout.slotsPerRow) return false

  return !layout.devices.some(other =>
    other.id !== deviceId &&
    other.rowIndex === rowIndex &&
    other.startSlot < end &&
    startSlot < other.startSlot + other.moduleWidth)
}
