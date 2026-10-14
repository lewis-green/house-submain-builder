import type { RackItemResponse, RackLayoutResponse } from '../api/types'

/**
 * Whether an item could sit with its top at `topU`: inside the cabinet and off
 * every other item. Feedback only — the drag's red ghost, the "Move to" list.
 * The server checks again and has the last word, as it does for panels.
 */
export function isFree(layout: RackLayoutResponse, itemId: string, topU: number, heightU: number): boolean {
  const bottomU = topU - heightU + 1
  if (topU > layout.heightU || bottomU < 1) return false
  return layout.items.every(other =>
    other.id === itemId || other.topU < bottomU || other.topU - other.heightU + 1 > topU)
}

/** Every other top U the item could move to, highest first. */
export function freeTopUs(layout: RackLayoutResponse, item: RackItemResponse): number[] {
  const tops: number[] = []
  for (let u = layout.heightU; u >= item.heightU; u--) {
    if (u !== item.topU && isFree(layout, item.id, u, item.heightU)) tops.push(u)
  }
  return tops
}
