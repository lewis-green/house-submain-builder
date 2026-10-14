import type { LayoutResponse } from '../api/types'
import { usePressDrag } from '../drag/usePressDrag'
import { isTargetFree } from './dragTargets'
import { SLOT_PX, xToSlot, yToRow } from './geometry'
import type { DragState } from './PanelSvg'

export { LONG_PRESS_MS } from '../drag/usePressDrag'

interface Options {
  layout: LayoutResponse
  onCommit: (deviceId: string, rowIndex: number, startSlot: number) => void
  /** Converts client coordinates into panel coordinates, undoing pan and zoom. */
  toPanelPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

/** The shared press-and-drag gesture, in the panel's terms: a row and a slot. */
export function useDeviceDrag({ layout, onCommit, toPanelPoint }: Options) {
  const find = (id: string) => layout.devices.find(d => d.id === id)

  const { dragging, bind } = usePressDrag<{ rowIndex: number; startSlot: number }>({
    idAttribute: 'data-device-id',
    start: id => {
      const device = find(id)
      return device ? { rowIndex: device.rowIndex, startSlot: device.startSlot } : null
    },
    locate: (id, point) => {
      const device = find(id)
      if (!device) return null
      const startSlot = xToSlot(point.x - (device.moduleWidth * SLOT_PX) / 2, device.moduleWidth, layout.slotsPerRow)
      const rowIndex = yToRow(point.y, layout.rows)
      return { target: { rowIndex, startSlot }, valid: isTargetFree(layout, id, rowIndex, startSlot) }
    },
    exists: id => find(id) !== undefined,
    onCommit: (id, target) => onCommit(id, target.rowIndex, target.startSlot),
    toPoint: toPanelPoint,
  })

  const state: DragState | null = dragging
    ? { deviceId: dragging.id, rowIndex: dragging.target.rowIndex, startSlot: dragging.target.startSlot, valid: dragging.valid }
    : null

  return { dragging: state, bind }
}
