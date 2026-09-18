import { useCallback, useRef, useState } from 'react'
import type { LayoutResponse } from '../api/types'
import { isTargetFree } from './dragTargets'
import { xToSlot, yToRow } from './geometry'
import type { DragState } from './PanelSvg'

/** Long-press before a drag begins, so an ordinary pan never picks a device up. */
export const LONG_PRESS_MS = 350

/** Movement past this before the timer fires means the user is panning, not dragging. */
export const PAN_TOLERANCE_PX = 8

interface Options {
  layout: LayoutResponse
  onCommit: (deviceId: string, rowIndex: number, startSlot: number) => void
  /** Converts client coordinates into panel coordinates, undoing pan and zoom. */
  toPanelPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

export function useDeviceDrag({ layout, onCommit, toPanelPoint }: Options) {
  const [dragging, setDragging] = useState<DragState | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const origin = useRef<{ x: number; y: number; deviceId: string } | null>(null)
  const armed = useRef(false)
  const captured = useRef<{ element: Element; pointerId: number } | null>(null)

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

  const onPointerDown = useCallback((e: React.PointerEvent) => {
    const target = (e.target as Element).closest('[data-device-id]')
    const deviceId = target?.getAttribute('data-device-id')
    if (!deviceId) return

    origin.current = { x: e.clientX, y: e.clientY, deviceId }
    armed.current = false

    // Capture only once the drag actually arms. Capturing on every pointerdown
    // retargets the following click to the capturing element, so a plain tap
    // would never reach the device and the sheet would never open.
    const element = e.currentTarget as Element
    const { pointerId } = e

    timer.current = setTimeout(() => {
      armed.current = true
      const device = layout.devices.find(d => d.id === deviceId)
      if (!device) return

      ;(element as Element & { setPointerCapture?: (id: number) => void }).setPointerCapture?.(pointerId)
      captured.current = { element, pointerId }

      setDragging({
        deviceId,
        rowIndex: device.rowIndex,
        startSlot: device.startSlot,
        valid: true,
      })
    }, LONG_PRESS_MS)
  }, [layout])

  const onPointerMove = useCallback((e: React.PointerEvent) => {
    const start = origin.current
    if (!start) return

    if (!armed.current) {
      const moved = Math.hypot(e.clientX - start.x, e.clientY - start.y)
      if (moved > PAN_TOLERANCE_PX) cancel()
      return
    }

    const device = layout.devices.find(d => d.id === start.deviceId)
    if (!device) return

    const point = toPanelPoint(e.clientX, e.clientY)
    const startSlot = xToSlot(point.x - (device.moduleWidth * 8) / 2, device.moduleWidth, layout.slotsPerRow)
    const rowIndex = yToRow(point.y, layout.rows)

    setDragging({
      deviceId: start.deviceId,
      rowIndex,
      startSlot,
      valid: isTargetFree(layout, start.deviceId, rowIndex, startSlot),
    })
  }, [layout, toPanelPoint, cancel])

  const onPointerUp = useCallback(() => {
    if (dragging?.valid) {
      onCommit(dragging.deviceId, dragging.rowIndex, dragging.startSlot)
    }
    cancel()
  }, [dragging, onCommit, cancel])

  return {
    dragging,
    /** Spread onto the SVG wrapper. touch-action is none only while a drag is live. */
    bind: {
      onPointerDown,
      onPointerMove,
      onPointerUp,
      onPointerCancel: cancel,
      style: dragging ? ({ touchAction: 'none' } as const) : undefined,
    },
  }
}
