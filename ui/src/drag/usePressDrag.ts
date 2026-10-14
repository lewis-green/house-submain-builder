import { useCallback, useEffect, useRef, useState } from 'react'

/** Long-press before a drag begins, so an ordinary pan never picks an item up. */
export const LONG_PRESS_MS = 350

/**
 * Movement past this before the timer fires means a finger is panning, not
 * dragging. A mouse cannot pan by dragging, so for a mouse the same distance
 * means the opposite: the item has been picked up.
 */
const PAN_TOLERANCE_PX = 8

export interface PressDrag<T> {
  id: string
  target: T
  valid: boolean
}

export interface PressDragOptions<T> {
  /** The attribute on an item's element that carries its id, e.g. "data-device-id". */
  idAttribute: string
  /** Where the item is now, asked when the drag arms; null when it has gone. */
  start: (id: string) => T | null
  /** Where a pointer at this point would put the item and whether it may go there; null when it has gone. */
  locate: (id: string, point: { x: number; y: number }) => { target: T; valid: boolean } | null
  /** Whether the item is still there to be dropped. */
  exists: (id: string) => boolean
  onCommit: (id: string, target: T) => void
  /** Converts client coordinates into drawing coordinates, undoing pan and zoom. */
  toPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

/**
 * The press-and-drag gesture the panel and the rack share. On a touchscreen a
 * long press picks an item up, so an ordinary pan never does; with a mouse a
 * press that moves picks it up straight away. What a position means — a row
 * and a slot, or a rack unit — belongs to the caller, through start and locate.
 */
export function usePressDrag<T>(options: PressDragOptions<T>) {
  const [dragging, setDragging] = useState<PressDrag<T> | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const origin = useRef<{
    x: number
    y: number
    id: string
    mouse: boolean
    element: Element
    pointerId: number
  } | null>(null)
  const armed = useRef(false)
  const captured = useRef<{ element: Element; pointerId: number } | null>(null)
  const wrapper = useRef<HTMLElement | null>(null)

  // The caller's callbacks as they are now, not as they were at the press: a
  // reload can land during the hold — a save from the sheet, or a rebuild that
  // replaces every item — and the timer must not lift an item from a drawing
  // that is already gone.
  const latest = useRef(options)
  useEffect(() => { latest.current = options })

  const cancel = useCallback(() => {
    clearTimeout(timer.current)
    if (captured.current) {
      const { element, pointerId } = captured.current
      ;(element as Element & { releasePointerCapture?: (id: number) => void })
        .releasePointerCapture?.(pointerId)
      captured.current = null
    }
    origin.current = null
    armed.current = false
    setDragging(null)
  }, [])

  /**
   * Lifts the pressed item: from here on, moves place it and release drops it.
   * Capture starts only now — capturing on every pointerdown would retarget the
   * click that follows a plain tap, so the item's sheet would never open.
   */
  const arm = useCallback(() => {
    const start = origin.current
    if (!start) return false

    const target = latest.current.start(start.id)
    // Nothing to lift: the press ends as a press, and the page keeps scrolling as normal.
    if (target === null) {
      origin.current = null
      return false
    }

    armed.current = true
    ;(start.element as Element & { setPointerCapture?: (id: number) => void }).setPointerCapture?.(start.pointerId)
    captured.current = { element: start.element, pointerId: start.pointerId }
    setDragging({ id: start.id, target, valid: true })
    return true
  }, [])

  const onPointerDown = useCallback((e: React.PointerEvent) => {
    const attribute = latest.current.idAttribute
    const id = (e.target as Element).closest(`[${attribute}]`)?.getAttribute(attribute)
    if (!id) return

    const mouse = e.pointerType === 'mouse'
    // A right-click opens a menu; it never picks an item up.
    if (mouse && e.button !== 0) return

    origin.current = { x: e.clientX, y: e.clientY, id, mouse, element: e.currentTarget as Element, pointerId: e.pointerId }
    armed.current = false

    // A mouse is armed by moving instead (see onPointerMove): it has no scroll
    // to tell apart from a drag, so making it hold still first only reads as
    // a drawing that won't let go of its items.
    if (!mouse) timer.current = setTimeout(arm, LONG_PRESS_MS)
  }, [arm])

  const onPointerMove = useCallback((e: React.PointerEvent) => {
    const start = origin.current
    if (!start) return

    if (!armed.current) {
      const moved = Math.hypot(e.clientX - start.x, e.clientY - start.y)
      if (moved <= PAN_TOLERANCE_PX) return
      if (!start.mouse) {
        cancel()
        return
      }
      if (!arm()) return
    }

    const located = latest.current.locate(start.id, latest.current.toPoint(e.clientX, e.clientY))
    if (!located) return
    setDragging({ id: start.id, target: located.target, valid: located.valid })
  }, [cancel, arm])

  const onPointerUp = useCallback(() => {
    // Only an item still on the drawing can be moved: one replaced by a
    // reload mid-drag would be a request the server can only refuse.
    if (dragging?.valid && latest.current.exists(dragging.id)) {
      latest.current.onCommit(dragging.id, dragging.target)
    }
    cancel()
  }, [dragging, cancel])

  /**
   * WebKit decides at touchstart whether a touch may scroll, and a passive
   * listener cannot change its mind later. So this is registered non-passive
   * on the real element for both touchmove and touchend: once the long press
   * has armed, every touchmove is cancelled so neither the drawing nor the
   * page scrolls out from under the drag, and the touchend is cancelled so
   * Safari does not synthesise a click that would open the sheet on drop. It
   * only reads a ref, so one function serves the life of the hook and the ref
   * callback below can remove exactly what it added.
   */
  const cancelWhenArmed = useCallback((e: TouchEvent) => {
    if (armed.current) e.preventDefault()
  }, [])

  const ref = useCallback((element: HTMLElement | null) => {
    const previous = wrapper.current
    if (previous) {
      previous.removeEventListener('touchmove', cancelWhenArmed)
      previous.removeEventListener('touchend', cancelWhenArmed)
    }
    wrapper.current = element
    if (element) {
      element.addEventListener('touchmove', cancelWhenArmed, { passive: false })
      element.addEventListener('touchend', cancelWhenArmed, { passive: false })
    }
  }, [cancelWhenArmed])

  return {
    dragging,
    /** Spread onto the drawing's wrapper; its ref must reach the DOM element. */
    bind: {
      ref,
      onPointerDown,
      onPointerMove,
      onPointerUp,
      onPointerCancel: cancel,
    },
  }
}
