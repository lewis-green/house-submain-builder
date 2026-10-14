import { describe, expect, it } from 'vitest'
import { GUTTER_PX, ITEM_INSET, U_PX, drawingSize, itemRect, uTopY, yToTopU } from '../src/racks/rackGeometry'

describe('rack geometry', () => {
  it('puts U1 at the bottom of the drawing', () => {
    expect(uTopY(12, 12)).toBe(0)
    expect(uTopY(1, 12)).toBe(11 * U_PX)
    expect(drawingSize(12).height).toBe(12 * U_PX)
  })

  it('draws an item from its top U down, inset so neighbours read as two', () => {
    expect(itemRect({ topU: 11, heightU: 2 }, 12)).toEqual({
      x: GUTTER_PX + ITEM_INSET, y: U_PX + ITEM_INSET, width: expect.any(Number), height: 2 * U_PX - 2 * ITEM_INSET,
    })
  })

  it('turns a pointer y into the top U it would drop on, clamped to the cabinet', () => {
    expect(yToTopU(U_PX / 2, 1, 12)).toBe(12)
    expect(yToTopU(U_PX * 1.5, 1, 12)).toBe(11)
    expect(yToTopU(-500, 1, 12)).toBe(12)
    expect(yToTopU(99999, 2, 12)).toBe(2)
  })
})
