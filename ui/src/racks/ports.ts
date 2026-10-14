import type { PortGroup, RackItemResponse } from '../api/types'

export interface PortRowData {
  number: number
  group: PortGroup
  label: string
  poe: boolean
}

const numbers = (group: PortGroup) => Array.from({ length: group.to - group.from + 1 }, (_, i) => group.from + i)

/** A row for every port the item has, filled in from the few the server stores. */
export function portRows(item: RackItemResponse): PortRowData[] {
  const stored = new Map(item.ports.map(p => [p.number, p]))
  return item.portGroups.flatMap(group => numbers(group).map(number => {
    const port = stored.get(number)
    return { number, group, label: port?.label ?? '', poe: port?.poe ?? false }
  }))
}

export const poeCapable = (item: RackItemResponse) =>
  item.portGroups.filter(g => g.poeTick).reduce((n, g) => n + g.to - g.from + 1, 0)

export const poeTicked = (item: RackItemResponse) => item.ports.filter(p => p.poe).length

/**
 * The ports as they sit on the real front panel: a switch's copper ports odd
 * on the top row and even below, its uplinks apart on the right; a patch
 * panel's in one row. Groups without the PoE tick are the uplinks.
 */
export function frontPanel(item: RackItemResponse): { top: number[]; bottom: number[]; uplinks: number[] } {
  const copper = item.portGroups.filter(g => g.poeTick).flatMap(numbers)
  const uplinks = item.portGroups.filter(g => !g.poeTick).flatMap(numbers)
  if (item.kind === 'PatchPanel') return { top: copper, bottom: [], uplinks }
  return { top: copper.filter(n => n % 2 === 1), bottom: copper.filter(n => n % 2 === 0), uplinks }
}
