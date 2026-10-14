import { describe, expect, it } from 'vitest'
import type { RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { freeTopUs, isFree } from '../src/racks/rackTargets'
import { frontPanel, portRows, poeCapable, poeTicked } from '../src/racks/ports'

const item = (id: string, topU: number, heightU = 1, extra: Partial<RackItemResponse> = {}): RackItemResponse => ({
  id, rackDeviceTypeId: 't', kind: 'Switch', model: 'Test', partNumber: 'T', label: id, topU, heightU,
  portGroups: [], poeBudgetW: null, ports: [], ...extra,
})

const layout = (items: RackItemResponse[], heightU = 6): RackLayoutResponse =>
  ({ rackId: 'r', heightU, freeU: 0, layoutVersion: 1, items })

describe('rack targets', () => {
  it('a U is free inside the cabinet and off every other item', () => {
    const l = layout([item('a', 6), item('b', 4, 2)])
    expect(isFree(l, 'a', 5, 1)).toBe(true)
    expect(isFree(l, 'a', 4, 1)).toBe(false)
    expect(isFree(l, 'a', 3, 1)).toBe(false)
    expect(isFree(l, 'a', 7, 1)).toBe(false)
    expect(isFree(l, 'b', 5, 2)).toBe(true)
  })

  it('lists every other free top U, highest first', () => {
    const l = layout([item('a', 6), item('b', 4, 2)])
    expect(freeTopUs(l, l.items[0])).toEqual([5, 2, 1])
    expect(freeTopUs(l, l.items[1])).toEqual([5, 3, 2])
  })
})

describe('ports', () => {
  const sw = item('sw', 6, 1, {
    portGroups: [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }],
    ports: [{ number: 2, label: 'Lounge TV', poe: true }],
  })

  it('makes a row for every port, filled from what is stored', () => {
    const rows = portRows(sw)
    expect(rows.map(r => r.number)).toEqual([1, 2, 3, 4, 5, 6])
    expect(rows[1]).toMatchObject({ label: 'Lounge TV', poe: true, group: { name: '1G' } })
    expect(rows[0]).toMatchObject({ label: '', poe: false })
  })

  it('counts PoE-capable and ticked ports', () => {
    expect(poeCapable(sw)).toBe(4)
    expect(poeTicked(sw)).toBe(1)
  })

  it('lays a switch out odd above even with uplinks apart, and a patch panel in one row', () => {
    expect(frontPanel(sw)).toEqual({ top: [1, 3], bottom: [2, 4], uplinks: [5, 6] })
    const panel = { ...sw, kind: 'PatchPanel' as const, portGroups: [{ name: 'RJ45', from: 1, to: 4, poeTick: true }] }
    expect(frontPanel(panel)).toEqual({ top: [1, 2, 3, 4], bottom: [], uplinks: [] })
  })
})
