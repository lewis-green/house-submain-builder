import type { LayoutResponse } from '../api/types'
import { SLOT_UNITS_PER_MODULE } from '../units'
import { deviceStyle } from './deviceStyle'
import { DEVICE_INSET, SLOT_PX, deviceRect, panelSize } from './geometry'
import type { ScrollExtent } from './useScrollExtent'

interface Props {
  layout: LayoutResponse
  /** The drawing's scale, to turn scroller pixels back into panel pixels. */
  zoom: number
  /** Where the drawing's origin sits inside the scroller's content, in CSS pixels. */
  originPx: number
  /** How much of the scroller's content the sticky row gutter covers, in CSS pixels. */
  gutterPx: number
  extent: ScrollExtent | null
  onPan: (scrollLeft: number) => void
}

/**
 * The whole panel in a strip the width of the screen, with the part now on
 * screen outlined — a map for a drawing that is four screens wide at the size
 * devices can be tapped. Tapping or dragging on the strip pans the drawing
 * there. It is the same server layout drawn smaller: nothing is laid out here.
 */
export function PanelOverview({ layout, zoom, originPx, gutterPx, extent, onPan }: Props) {
  const size = panelSize(layout)
  const modules = layout.slotsPerRow / SLOT_UNITS_PER_MODULE

  // The part of the drawing in view, in panel pixels. The sticky gutter sits
  // over the first strip of the viewport, so the view starts after it.
  const view = extent && {
    from: Math.max(0, (extent.left + gutterPx - originPx) / zoom),
    to: Math.min(size.width, (extent.left + extent.width - originPx) / zoom),
  }
  const all = view !== null && view.from <= 0 && view.to >= size.width
  const firstModule = view ? Math.floor(view.from / (SLOT_PX * SLOT_UNITS_PER_MODULE)) + 1 : 1
  const lastModule = view ? Math.min(modules, Math.ceil(view.to / (SLOT_PX * SLOT_UNITS_PER_MODULE))) : modules

  const caption = !view
    ? `${modules} modules wide. Scroll sideways to see more.`
    : all
      ? `All ${modules} modules shown.`
      : `Showing modules ${firstModule}–${lastModule} of ${modules}. Scroll sideways, or tap the strip, for the rest.`

  /** Pans so that the tapped point of the strip is in the middle of the screen. */
  const panTo = (e: React.PointerEvent<SVGSVGElement>) => {
    if (!extent) return
    const box = e.currentTarget.getBoundingClientRect()
    const panelX = ((e.clientX - box.left) / box.width) * size.width
    const visible = extent.width - gutterPx
    onPan(panelX * zoom + originPx - gutterPx - visible / 2)
  }

  return (
    <div className="mt-2">
      <svg
        viewBox={`0 0 ${size.width} ${size.height}`}
        preserveAspectRatio="none"
        aria-hidden="true"
        className="block h-11 w-full rounded border border-rule bg-rail"
        style={{ touchAction: 'none' }}
        // Captured so a finger that slides off the strip keeps panning; WebKit
        // throws for a pointer it does not consider active, and a tap is enough then.
        onPointerDown={e => {
          try { e.currentTarget.setPointerCapture(e.pointerId) } catch { /* not an active pointer */ }
          panTo(e)
        }}
        onPointerMove={e => { if (e.buttons > 0) panTo(e) }}
      >
        {layout.devices.map(device => {
          const rect = deviceRect(device)
          const style = deviceStyle(device.category)
          return (
            <rect
              key={device.id ?? `${device.rowIndex}-${device.startSlot}`}
              x={rect.x + DEVICE_INSET}
              y={rect.y + DEVICE_INSET}
              width={rect.width - DEVICE_INSET * 2}
              height={rect.height - DEVICE_INSET * 2}
              className={`${style.fill} ${style.stroke}`}
              strokeWidth={4}
            />
          )
        })}
        {view && !all && (
          <rect
            x={view.from}
            y={0}
            width={view.to - view.from}
            height={size.height}
            className="fill-ink/10 stroke-ink"
            strokeWidth={2}
            // Not scaled: the strip is squashed, and a scaled stroke would be hairline-thin vertically.
            vectorEffect="non-scaling-stroke"
          />
        )}
      </svg>
      <p className="mt-1 text-sm text-ink-2">{caption}</p>
    </div>
  )
}
