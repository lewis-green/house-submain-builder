/**
 * One rack unit on screen at the drawing's own size. A 1U item, once its
 * inset is taken off, is a 48px target under a thumb.
 */
export const U_PX = 56

/** Room on the left for the U numbers. */
export const GUTTER_PX = 44

/** The rack's own width: beside the gutter it fits a 375px phone without zooming. */
export const RACK_PX = 296

/** Space between an item and the U lines, so two neighbours read as two. */
export const ITEM_INSET = 4

/** The y of the top edge of U `u` in a rack `heightU` tall. U1 is at the bottom. */
export const uTopY = (u: number, heightU: number) => (heightU - u) * U_PX

export function itemRect(item: { topU: number; heightU: number }, rackHeightU: number) {
  return {
    x: GUTTER_PX + ITEM_INSET,
    y: uTopY(item.topU, rackHeightU) + ITEM_INSET,
    width: RACK_PX - ITEM_INSET * 2,
    height: item.heightU * U_PX - ITEM_INSET * 2,
  }
}

export const drawingSize = (heightU: number) => ({ width: GUTTER_PX + RACK_PX, height: heightU * U_PX })

/** The top U an item `heightU` tall would take with its middle at y, clamped inside the cabinet. */
export function yToTopU(y: number, heightU: number, rackHeightU: number) {
  const topEdge = y - (heightU * U_PX) / 2
  const topU = rackHeightU - Math.round(topEdge / U_PX)
  return Math.min(Math.max(topU, heightU), rackHeightU)
}
