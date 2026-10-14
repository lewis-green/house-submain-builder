import type { RackItemResponse, RackLayoutResponse } from '../api/types'
import { frontPanel } from './ports'
import { GUTTER_PX, RACK_PX, U_PX, drawingSize, itemRect, uTopY } from './rackGeometry'

export interface RackDrag {
  itemId: string
  topU: number
  valid: boolean
}

interface Props {
  layout: RackLayoutResponse
  selectedId: string | null
  onSelect: (itemId: string) => void
  dragging?: RackDrag | null
}

function describe(item: RackItemResponse) {
  const total = item.portGroups.reduce((n, g) => n + g.to - g.from + 1, 0)
  const labelled = item.ports.filter(p => p.label).length
  return `${item.label}, ${item.model}, U${item.topU}${total ? `, ${labelled} of ${total} ports labelled` : ''}`
}

/**
 * The rack top to bottom, every U drawn and numbered on the left. Drawn
 * exactly where the server put each item; a dragged item is drawn where the
 * pointer would drop it, dashed and marked when it would not fit.
 */
export function RackDrawing({ layout, selectedId, onSelect, dragging }: Props) {
  const size = drawingSize(layout.heightU)
  // The dragged item is drawn last, so its ghost sits on top of anything it is
  // over: an invalid drop has to be seen, not hidden under the item it hits.
  const items = [...layout.items].sort((a, b) =>
    Number(a.id === dragging?.itemId) - Number(b.id === dragging?.itemId))

  return (
    <svg
      viewBox={`0 0 ${size.width} ${size.height}`}
      width="100%"
      // Not role="img": that would hide every item button inside it from VoiceOver.
      role="group"
      aria-label={`Rack, ${layout.heightU}U, ${layout.freeU}U free`}
      className="block select-none"
    >
      {Array.from({ length: layout.heightU }, (_, i) => layout.heightU - i).map(u => (
        <g key={u}>
          <rect x={GUTTER_PX} y={uTopY(u, layout.heightU)} width={RACK_PX} height={U_PX} className="fill-rail stroke-rule" strokeWidth={1} />
          <text
            x={GUTTER_PX - 8}
            y={uTopY(u, layout.heightU) + U_PX / 2}
            textAnchor="end"
            dominantBaseline="middle"
            className="fill-ink-2 text-[12px] font-medium"
          >
            U{u}
          </text>
        </g>
      ))}

      {items.map(item => {
        const drag = dragging?.itemId === item.id ? dragging : null
        const rect = itemRect(drag ? { ...item, topU: drag.topU } : item, layout.heightU)
        const invalid = drag !== null && !drag.valid
        const selected = item.id === selectedId

        return (
          <g
            key={item.id}
            role="button"
            tabIndex={0}
            data-item-id={item.id}
            aria-label={describe(item)}
            aria-pressed={selected}
            onClick={() => onSelect(item.id)}
            onKeyDown={e => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                onSelect(item.id)
              }
            }}
            className="cursor-pointer"
            opacity={drag ? 0.9 : 1}
          >
            <rect
              {...rect}
              rx={4}
              className={invalid ? 'fill-danger-soft stroke-danger' : 'fill-sheet stroke-ink'}
              strokeWidth={selected ? 3 : 1.5}
              strokeDasharray={invalid ? '6 4' : undefined}
            />
            <text x={rect.x + 8} y={rect.y + 19} className="fill-ink text-[13px] font-bold">{item.label}</text>
            {/* Colour is never the only sign: an invalid drop says so in words. */}
            <text x={rect.x + 8} y={rect.y + 35} className={invalid ? 'fill-danger text-[11px] font-semibold' : 'fill-ink-2 text-[11px]'}>
              {invalid ? 'No room here' : item.model.replace(/^Ubiquiti /, '')}
            </text>
            <PortStrip item={item} x={rect.x + 150} y={rect.y + 10} width={rect.width - 158} />
          </g>
        )
      })}
    </svg>
  )
}

/** The front panel at a glance: filled once labelled, an accent dot for PoE. The detail is in the item's sheet. */
function PortStrip({ item, x, y, width }: { item: RackItemResponse; x: number; y: number; width: number }) {
  const { top, bottom, uplinks } = frontPanel(item)
  const copperColumns = Math.max(top.length, bottom.length)
  const columns = copperColumns + (uplinks.length ? Math.ceil(uplinks.length / 2) + 1 : 0)
  if (columns === 0) return null

  const cell = Math.min(10, width / columns)
  const stored = new Map(item.ports.map(p => [p.number, p]))

  const square = (number: number, cx: number, cy: number) => {
    const port = stored.get(number)
    return (
      <g key={number}>
        <rect x={cx} y={cy} width={cell - 1.5} height={cell - 1.5} className={port?.label ? 'fill-ink' : 'fill-sheet stroke-ink-2'} strokeWidth={0.75} />
        {port?.poe && <circle cx={cx + cell - 3} cy={cy + 1.5} r={1.6} className="fill-accent" />}
      </g>
    )
  }

  return (
    <g aria-hidden="true">
      {top.map((n, i) => square(n, x + i * cell, y))}
      {bottom.map((n, i) => square(n, x + i * cell, y + cell))}
      {uplinks.map((n, i) => square(n, x + (copperColumns + 1 + Math.floor(i / 2)) * cell, y + (i % 2) * cell))}
    </g>
  )
}
