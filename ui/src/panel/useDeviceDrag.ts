import { useCallback, useEffect, useRef, useState } from 'react'
import type { LayoutResponse } from '../api/types'
import { isTargetFree } from './dragTargets'
import { SLOT_PX, xToSlot, yToRow } from './geometry'
import type { DragState } from './PanelSvg'

/** Long-press before a drag begins, so an ordinary pan never picks a device up. */
export const LONG_PRESS_MS = 350

/**
 * Movement past this before the timer fires means a finger is panning, not
 * dragging. A mouse cannot pan by dragging, so for a mouse the same distance
 * means the opposite: the device has been picked up.
 */
const PAN_TOLERANCE_PX = 8

interface Options {
  layout: LayoutResponse
  onCommit: (deviceId: string, rowIndex: number, startSlot: number) => void
  /** Converts client coordinates into panel coordinates, undoing pan and zoom. */
  toPanelPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

export function useDeviceDrag({ layout, onCommit, toPanelPoint }: Options) {
  const [dragging, setDragging] = useState<DragState | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const origin = useRef<{
    x: number
    y: number
    deviceId: string
    mouse: boolean
    element: Element
    pointerId: number
  } | null>(null)
  const armed = useRef(false)
  const captured = useRef<{ element: Element; pointerId: number } | null>(null)
  const wrapper = useRef<HTMLElement | null>(null)

  // The layout as it is when the long press fires, not as it was at the press:
  // a reload can land during the hold — a save from the sheet, or a rebuild
  // that replaces every device — and the timer below must not lift a device
  // from a drawing that is already gone.
  const latestLayout = useRef(layout)
  useEffect(() => { latestLayout.current = layout }, [layout])

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
   * Lifts the pressed device: from here on, moves place it and release drops it.
   * Capture starts only now — capturing on every pointerdown would retarget the
   * click that follows a plain tap, so the sheet would never open.
   */
  const arm = useCallback(() => {
    const start = origin.current
    if (!start) return false

    const device = latestLayout.current.devices.find(d => d.id === start.deviceId)
    // Nothing to lift: the press ends as a press, and the page keeps scrolling as normal.
    if (!device) {
      origin.current = null
      return false
    }

    armed.current = true
    ;(start.element as Element & { setPointerCapture?: (id: number) => void }).setPointerCapture?.(start.pointerId)
    captured.current = { element: start.element, pointerId: start.pointerId }

    setDragging({
      deviceId: start.deviceId,
      rowIndex: device.rowIndex,
      startSlot: device.startSlot,
      valid: true,
    })
    return true
  }, [])

  const onPointerDown = useCallback((e: React.PointerEvent) => {
    const target = (e.target as Element).closest('[data-device-id]')
    const deviceId = target?.getAttribute('data-device-id')
    if (!deviceId) return

    const mouse = e.pointerType === 'mouse'
    // A right-click opens a menu; it never picks a device up.
    if (mouse && e.button !== 0) return

    origin.current = { x: e.clientX, y: e.clientY, deviceId, mouse, element: e.currentTarget as Element, pointerId: e.pointerId }
    armed.current = false

    // A mouse is armed by moving instead (see onPointerMove): it has no scroll
    // to tell apart from a drag, so making it hold still first only reads as
    // a drawing that won't let go of its devices.
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

    const device = layout.devices.find(d => d.id === start.deviceId)
    if (!device) return

    const point = toPanelPoint(e.clientX, e.clientY)
    const startSlot = xToSlot(point.x - (device.moduleWidth * SLOT_PX) / 2, device.moduleWidth, layout.slotsPerRow)
    const rowIndex = yToRow(point.y, layout.rows)

    setDragging({
      deviceId: start.deviceId,
      rowIndex,
      startSlot,
      valid: isTargetFree(layout, start.deviceId, rowIndex, startSlot),
    })
  }, [layout, toPanelPoint, cancel, arm])

  const onPointerUp = useCallback(() => {
    // Only a device still on the drawing can be moved: one replaced by a
    // rebuild mid-drag would be a request the server can only answer with a 404.
    if (dragging?.valid && layout.devices.some(d => d.id === dragging.deviceId)) {
      onCommit(dragging.deviceId, dragging.rowIndex, dragging.startSlot)
    }
    cancel()
  }, [dragging, layout, onCommit, cancel])

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
    /** Spread onto the SVG wrapper; its ref must reach the DOM element. */
    bind: {
      ref,
      onPointerDown,
      onPointerMove,
      onPointerUp,
      onPointerCancel: cancel,
    },
  }
}
