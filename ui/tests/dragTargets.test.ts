import { describe, expect, it } from 'vitest'
import { isTargetFree } from '../src/panel/dragTargets'
import type { LayoutResponse, PlacedDeviceResponse } from '../src/api/types'

const device = (id: string, rowIndex: number, startSlot: number, moduleWidth: number): PlacedDeviceResponse => ({
  id, deviceTypeId: 'x', category: 'Dimmer240', rowIndex, startSlot, moduleWidth,
  label: id, terminalRole: 'None', channels: [],
})

const layout: LayoutResponse = {
  rows: 4, slotsPerRow: 54,
  devices: [device('a', 1, 0, 3), device('b', 1, 3, 3), device('c', 2, 0, 9)],
}

describe('isTargetFree', () => {
  it('accepts empty space in the same row', () => {
    expect(isTargetFree(layout, 'b', 1, 30)).toBe(true)
  })

  it('rejects overlapping another device', () => {
    expect(isTargetFree(layout, 'b', 1, 1)).toBe(false)
  })

  it('accepts a device landing exactly where it already is', () => {
    expect(isTargetFree(layout, 'b', 1, 3)).toBe(true)
  })

  it('rejects running off the end of the row', () => {
    expect(isTargetFree(layout, 'b', 1, 52)).toBe(false)
  })

  it('rejects a row outside the enclosure', () => {
    expect(isTargetFree(layout, 'b', 9, 0)).toBe(false)
  })

  it('accepts an empty row', () => {
    expect(isTargetFree(layout, 'b', 3, 0)).toBe(true)
  })

  it('rejects a device that is not in the layout', () => {
    expect(isTargetFree(layout, 'nope', 3, 0)).toBe(false)
  })
})
