import type { RackLayoutResponse } from '../api/types'
import { usePressDrag } from '../drag/usePressDrag'
import type { RackDrag } from './RackDrawing'
import { yToTopU } from './rackGeometry'
import { isFree } from './rackTargets'

interface Options {
  layout: RackLayoutResponse
  onCommit: (itemId: string, topU: number) => void
  /** Converts client coordinates into drawing coordinates, undoing the drawing's scale. */
  toPoint: (clientX: number, clientY: number) => { x: number; y: number }
}

/** The panel's press-and-drag gesture, in the rack's terms: a top U. */
export function useRackDrag({ layout, onCommit, toPoint }: Options) {
  const find = (id: string) => layout.items.find(i => i.id === id)

  const { dragging, bind } = usePressDrag<number>({
    idAttribute: 'data-item-id',
    start: id => find(id)?.topU ?? null,
    locate: (id, point) => {
      const item = find(id)
      if (!item) return null
      const topU = yToTopU(point.y, item.heightU, layout.heightU)
      return { target: topU, valid: isFree(layout, id, topU, item.heightU) }
    },
    exists: id => find(id) !== undefined,
    // A drop back where it started is not a move, and is not sent.
    onCommit: (id, topU) => { if (find(id)?.topU !== topU) onCommit(id, topU) },
    toPoint,
  })

  const state: RackDrag | null = dragging ? { itemId: dragging.id, topU: dragging.target, valid: dragging.valid } : null
  return { dragging: state, bind }
}
