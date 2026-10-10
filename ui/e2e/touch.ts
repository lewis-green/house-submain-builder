import type { Locator } from '@playwright/test'

/**
 * Playwright's WebKit driver can tap but not drag with a finger, so the
 * gesture is replayed from inside the page as the event sequence Safari
 * itself fires: touchstart and pointerdown, a hold, touchmove and pointermove
 * steps, then touchend and pointerup. The touch events go through the page's
 * own non-passive listeners, so whether the drag would have cancelled the
 * scroll (defaultPrevented) can be read back — which is the thing that
 * decides whether a real finger's drag survives in Safari.
 */
export async function fingerDrag(
  target: Locator,
  to: { dx: number; dy: number },
  holdMs = 450,
): Promise<{ movePrevented: boolean; endPrevented: boolean }> {
  const box = await target.boundingBox()
  if (!box) throw new Error('target has no bounding box')
  const start = { x: box.x + box.width / 2, y: box.y + box.height / 2 }

  const handle = await target.elementHandle()
  if (!handle) throw new Error('target has no element handle')

  return handle.evaluate(async (element, { start, to, holdMs }) => {
    // WebKit has no Touch constructor, only the older createTouch and
    // createTouchList; the TouchEvent constructor then accepts those lists.
    const legacy = document as Document & {
      createTouch(view: Window, target: EventTarget, id: number, pageX: number, pageY: number, screenX: number, screenY: number): Touch
      createTouchList(...touches: Touch[]): TouchList
    }
    const touch = (type: string, x: number, y: number) => {
      const t = legacy.createTouch(window, element, 1, x, y, x, y)
      const list = legacy.createTouchList(t)
      const none = legacy.createTouchList()
      // The DOM typings want Touch[]; WebKit wants the lists it just made.
      const e = new TouchEvent(type, {
        touches: (type === 'touchend' ? none : list) as unknown as Touch[],
        changedTouches: list as unknown as Touch[],
        targetTouches: (type === 'touchend' ? none : list) as unknown as Touch[],
        bubbles: true,
        cancelable: true,
      })
      element.dispatchEvent(e)
      return e.defaultPrevented
    }
    const pointer = (type: string, x: number, y: number) => {
      element.dispatchEvent(new PointerEvent(type, {
        pointerId: 1, pointerType: 'touch', isPrimary: true, clientX: x, clientY: y, bubbles: true, cancelable: true,
      }))
    }
    const wait = (ms: number) => new Promise(r => setTimeout(r, ms))

    touch('touchstart', start.x, start.y)
    pointer('pointerdown', start.x, start.y)
    await wait(holdMs)

    let movePrevented = true
    const steps = 8
    for (let i = 1; i <= steps; i++) {
      const x = start.x + (to.dx * i) / steps
      const y = start.y + (to.dy * i) / steps
      movePrevented = touch('touchmove', x, y) && movePrevented
      pointer('pointermove', x, y)
      await wait(16)
    }

    const endPrevented = touch('touchend', start.x + to.dx, start.y + to.dy)
    pointer('pointerup', start.x + to.dx, start.y + to.dy)
    return { movePrevented, endPrevented }
  }, { start, to, holdMs })
}
