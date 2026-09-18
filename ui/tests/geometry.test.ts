import { describe, expect, it } from 'vitest'
import { ROW_GAP, ROW_PX, SLOT_PX, deviceRect, panelSize, rowToY, slotToX, xToSlot, yToRow } from '../src/panel/geometry'

const device = (rowIndex: number, startSlot: number, moduleWidth: number) => ({ rowIndex, startSlot, moduleWidth })

describe('panel geometry', () => {
  it('places a slot at a multiple of the slot width', () => {
    expect(slotToX(0)).toBe(0)
    expect(slotToX(3)).toBe(3 * SLOT_PX)
  })

  it('stacks rows with a gap between rails', () => {
    expect(rowToY(0)).toBe(0)
    expect(rowToY(2)).toBe(2 * (ROW_PX + ROW_GAP))
  })

  it('sizes a one-module device to three slot units', () => {
    const rect = deviceRect(device(1, 3, 3))

    expect(rect.x).toBe(3 * SLOT_PX)
    expect(rect.width).toBe(3 * SLOT_PX)
    expect(rect.y).toBe(ROW_PX + ROW_GAP)
    expect(rect.height).toBe(ROW_PX)
  })

  it('sizes the panel from rows and slots per row', () => {
    const size = panelSize({ rows: 4, slotsPerRow: 54 })

    expect(size.width).toBe(54 * SLOT_PX)
    expect(size.height).toBe(4 * ROW_PX + 3 * ROW_GAP)
  })

  it('snaps a pointer x to the nearest slot', () => {
    expect(xToSlot(SLOT_PX * 3 + 2, 3, 54)).toBe(3)
    expect(xToSlot(SLOT_PX * 3 - 2, 3, 54)).toBe(3)
  })

  it('clamps a snapped slot so a device never leaves the rail', () => {
    expect(xToSlot(-100, 3, 54)).toBe(0)
    expect(xToSlot(99999, 3, 54)).toBe(51)
  })

  it('clamps a snapped row to the enclosure', () => {
    expect(yToRow(-50, 4)).toBe(0)
    expect(yToRow(99999, 4)).toBe(3)
  })
})
