import type { LayoutResponse, PlacedDeviceResponse } from '../api/types'
import { SLOT_UNITS_PER_MODULE } from '../units'
import { deviceStyle } from './deviceStyle'
import { DEVICE_INSET, ROW_PX, deviceRect, panelSize, rowToY, slotToX } from './geometry'

export interface DragState {
  deviceId: string
  rowIndex: number
  startSlot: number
  valid: boolean
}

interface Props {
  layout: LayoutResponse
  selectedId: string | null
  onSelect: (deviceId: string) => void
  dragging?: DragState | null
  /** Drawn size over the layout's own pixels. Labels scale with it. */
  zoom?: number
  /** Opens the terminal order. Without it the terminal run is only a picture. */
  onOpenTerminals?: () => void
}

/** A run of adjacent terminals drawn as one block. Grouping is for drawing only. */
interface TerminalGroup {
  key: string
  rowIndex: number
  startSlot: number
  moduleWidth: number
  label: string
  members: PlacedDeviceResponse[]
}

/** Below this a label no longer fits across the device, so it is turned on its side. */
const NARROW_PX = 100

function groupTerminals(devices: PlacedDeviceResponse[]): TerminalGroup[] {
  const terminals = devices
    .filter(d => d.category === 'Terminal240')
    .sort((a, b) => a.rowIndex - b.rowIndex || a.startSlot - b.startSlot)

  const groups: TerminalGroup[] = []

  for (const device of terminals) {
    const last = groups.at(-1)
    const contiguous =
      last &&
      last.rowIndex === device.rowIndex &&
      last.startSlot + last.moduleWidth === device.startSlot &&
      last.members[0].terminalRole === device.terminalRole

    if (contiguous) {
      last.moduleWidth += device.moduleWidth
      last.members.push(device)
      last.label = `${last.members[0].label}–${device.label}`
    } else {
      groups.push({
        key: device.id ?? `${device.rowIndex}-${device.startSlot}`,
        rowIndex: device.rowIndex,
        startSlot: device.startSlot,
        moduleWidth: device.moduleWidth,
        label: device.label,
        members: [device],
      })
    }
  }

  return groups
}

/** Each run of adjacent terminals carrying one circuit, two or more long, as first and last member. */
function bridgedRuns(members: PlacedDeviceResponse[]) {
  const runs: PlacedDeviceResponse[][] = []
  for (const m of members) {
    const run = runs.at(-1)
    const prev = run?.at(-1)
    if (run && prev && m.circuitId && prev.circuitId === m.circuitId && prev.startSlot + prev.moduleWidth === m.startSlot) {
      run.push(m)
    } else {
      runs.push([m])
    }
  }
  return runs
    .filter(run => run.length > 1 && run[0].circuitId)
    .map(run => ({ first: run[0], last: run.at(-1)! }))
}

function describe(device: PlacedDeviceResponse): string {
  const named = device.channels.filter(c => c.circuitName).map(c => c.circuitName)
  const spare = device.channels.filter(c => c.isSpare).length
  const { kind } = deviceStyle(device.category)

  // The label as the server gives it, then what the thing is when the label
  // does not already say so: "Cover 1, Blinds, Snug blind".
  const parts = [device.label, ...(kind !== device.label ? [kind] : []), ...named]
  if (spare > 0) parts.push(`${spare} spare ${spare === 1 ? 'channel' : 'channels'}`)
  return parts.join(', ')
}

/** The visible block sits inside its slot span, leaving a gutter to the neighbours. */
const inset = (rect: { x: number; y: number; width: number; height: number }) => ({
  x: rect.x + DEVICE_INSET,
  y: rect.y + DEVICE_INSET,
  width: rect.width - DEVICE_INSET * 2,
  height: rect.height - DEVICE_INSET * 2,
})

export function PanelSvg({ layout, selectedId, onSelect, dragging, zoom = 1, onOpenTerminals }: Props) {
  const size = panelSize(layout)
  const groups = groupTerminals(layout.devices)
  // The device being dragged is drawn last, so its ghost — the dashed stroke
  // and "No room" — sits on top of whatever it is dropped over. Drawn in list
  // order it could land under a device that came later and vanish.
  const others = layout.devices
    .filter(d => d.category !== 'Terminal240')
    .sort((a, b) => Number(a.id === dragging?.deviceId) - Number(b.id === dragging?.deviceId))
  const modules = layout.slotsPerRow / SLOT_UNITS_PER_MODULE

  return (
    <svg
      viewBox={`0 0 ${size.width} ${size.height}`}
      width={size.width * zoom}
      height={size.height * zoom}
      // Not role="img": that makes the SVG a leaf in the accessibility tree and
      // hides every device button inside it from assistive tech and keyboard users.
      role="group"
      aria-label={`Panel, ${layout.rows} rows of ${modules} modules`}
      className="block select-none"
    >
      {/* Clip paths live in defs: a clipPath inside a device's <g> has no
          rendered box, but it still stretches that group's geometric bounding
          box back to the origin, which throws off hit-testing. */}
      <defs>
        {groups.map(group => {
          const rect = inset(deviceRect(group))
          return (
            <clipPath key={group.key} id={`clip-${group.key}`}>
              <rect x={rect.x} y={rect.y} width={rect.width} height={rect.height} rx={3} />
            </clipPath>
          )
        })}
        {others.map(device => {
          const drag = dragging?.deviceId === device.id ? dragging : null
          const rect = inset(deviceRect(drag ? { ...device, rowIndex: drag.rowIndex, startSlot: drag.startSlot } : device))
          const key = device.id ?? `${device.rowIndex}-${device.startSlot}`
          return (
            <clipPath key={key} id={`clip-${key}`}>
              <rect x={rect.x} y={rect.y} width={rect.width} height={rect.height} rx={4} />
            </clipPath>
          )
        })}
      </defs>

      {Array.from({ length: layout.rows }, (_, row) => (
        <g key={`row-${row}`} aria-hidden="true">
          <rect x={0} y={rowToY(row)} width={size.width} height={ROW_PX} className="fill-rail" />
          {/* The DIN rail itself, drawn through the middle the way a schedule does. */}
          <rect
            x={0}
            y={rowToY(row) + ROW_PX / 2 - 10}
            width={size.width}
            height={20}
            className="fill-rule-2 stroke-rule"
            strokeWidth={1}
          />
          {Array.from({ length: modules + 1 }, (_, m) => (
            <line
              key={`tick-${row}-${m}`}
              x1={slotToX(m * SLOT_UNITS_PER_MODULE)}
              y1={rowToY(row)}
              x2={slotToX(m * SLOT_UNITS_PER_MODULE)}
              y2={rowToY(row) + ROW_PX}
              className="stroke-rule"
              strokeWidth={m % 6 === 0 ? 1.5 : 0.75}
            />
          ))}
        </g>
      ))}

      {/* A run of terminals is one block, each terminal labelled on its side,
          with a bar over each circuit bridged across several. The run opens
          the terminal order when the page offers it. Unlike the devices it
          keeps the global focus ring: it has no selected stroke to show focus. */}
      {groups.map(group => {
        const rect = inset(deviceRect(group))
        const style = deviceStyle('Terminal240')
        const interactive = Boolean(onOpenTerminals)
        return (
          <g
            key={group.key}
            role={interactive ? 'button' : 'img'}
            tabIndex={interactive ? 0 : undefined}
            aria-label={interactive ? `Terminals ${group.label}, change order` : `Terminals ${group.label}`}
            onClick={onOpenTerminals}
            onKeyDown={interactive
              ? e => { if (e.key === 'Enter' || e.key === ' ') onOpenTerminals!() }
              : undefined}
            className={interactive ? 'cursor-pointer' : undefined}
          >
            <rect {...rect} rx={3} className={`${style.fill} ${style.stroke}`} strokeWidth={1} />
            <g clipPath={`url(#clip-${group.key})`} className="pointer-events-none">
              {group.members.map((member, i) => {
                const x = slotToX(member.startSlot)
                const cx = x + slotToX(member.moduleWidth) / 2
                const bottom = rect.y + rect.height - 8
                return (
                  <g key={member.id ?? `${member.rowIndex}-${member.startSlot}`}>
                    {i > 0 && (
                      <line x1={x} y1={rect.y} x2={x} y2={rect.y + rect.height} className={style.stroke} strokeWidth={0.75} />
                    )}
                    <text
                      x={cx}
                      y={bottom}
                      transform={`rotate(-90 ${cx} ${bottom})`}
                      dominantBaseline="middle"
                      className={`${style.text} text-[12px] font-semibold`}
                    >
                      {member.label}
                    </text>
                  </g>
                )
              })}
              {bridgedRuns(group.members).map(({ first, last }) => (
                <rect
                  key={`bridge-${first.label}`}
                  data-bridge={`${first.label}–${last.label}`}
                  x={slotToX(first.startSlot) + 2}
                  y={rect.y + 3}
                  width={slotToX(last.startSlot + last.moduleWidth) - slotToX(first.startSlot) - 4}
                  height={4}
                  rx={2}
                  className="fill-ink-2"
                />
              ))}
            </g>
          </g>
        )
      })}

      {others.map(device => {
        const drag = dragging?.deviceId === device.id ? dragging : null
        const rect = inset(deviceRect(drag ? { ...device, rowIndex: drag.rowIndex, startSlot: drag.startSlot } : device))
        const style = deviceStyle(device.category)
        const invalid = drag && !drag.valid
        const selected = device.id === selectedId
        const key = device.id ?? `${device.rowIndex}-${device.startSlot}`
        const clipId = `clip-${key}`
        const named = device.channels.filter(c => c.circuitName)

        // A 1T device is 46px wide — no horizontal text fits. Narrow devices get a
        // rotated label, the way a real panel schedule does; the detail lives in
        // the device sheet.
        const narrow = rect.width < NARROW_PX
        const cx = rect.x + rect.width / 2
        const bottom = rect.y + rect.height - 8
        const label = device.label

        return (
          <g
            key={key}
            role="button"
            tabIndex={0}
            aria-label={describe(device)}
            aria-pressed={selected}
            data-device-id={device.id ?? undefined}
            onClick={() => device.id && onSelect(device.id)}
            onKeyDown={e => {
              if ((e.key === 'Enter' || e.key === ' ') && device.id) onSelect(device.id)
            }}
            className="cursor-pointer outline-none"
            opacity={drag ? 0.9 : 1}
          >
            {/* Lifted while dragging: a shadow says "picked up" without colour. */}
            {drag && (
              <rect {...rect} x={rect.x + 3} y={rect.y + 5} rx={4} className="fill-ink/30" />
            )}
            {/* The selected device is outlined in the accent on its own rect rather
                than a larger one around it, so its hit box never grows into the
                neighbour's 8px gutter. */}
            <rect
              {...rect}
              rx={4}
              className={invalid
                ? 'fill-danger-soft stroke-danger'
                : `${style.fill} ${selected ? 'stroke-accent' : style.stroke}`}
              strokeWidth={selected ? 3 : 1}
              strokeDasharray={invalid ? '6 4' : undefined}
            />

            {/* Clipped so a long name can never bleed over the device next door. */}
            <g clipPath={`url(#${clipId})`} className="pointer-events-none">
              {invalid ? (
                <text
                  x={cx}
                  y={bottom}
                  transform={narrow ? `rotate(-90 ${cx} ${bottom})` : undefined}
                  textAnchor={narrow ? 'start' : 'middle'}
                  className="fill-danger text-[13px] font-bold"
                >
                  No room
                </text>
              ) : narrow ? (
                <>
                  <text
                    x={cx - 7}
                    y={bottom}
                    transform={`rotate(-90 ${cx - 7} ${bottom})`}
                    className={`${style.text} text-[14px] font-bold`}
                  >
                    {label}
                  </text>
                  {/* The first circuit, or the kind — unless the kind is the label, as on a +24V block. */}
                  {(named[0]?.circuitName ?? (style.kind !== label ? style.kind : null)) && (
                    <text
                      x={cx + 9}
                      y={bottom}
                      transform={`rotate(-90 ${cx + 9} ${bottom})`}
                      className={`${style.text} text-[12px]`}
                    >
                      {named[0]?.circuitName ?? style.kind}
                    </text>
                  )}
                </>
              ) : (
                <>
                  <text x={rect.x + 8} y={rect.y + 20} className={`${style.text} text-[14px] font-bold`}>
                    {label}
                  </text>
                  <text x={rect.x + 8} y={rect.y + 36} className={`${style.text} text-[12px]`}>
                    {style.kind}
                  </text>
                  {named.slice(0, 2).map((c, i) => (
                    <text
                      key={c.channelIndex}
                      x={rect.x + 8}
                      y={rect.y + 56 + i * 16}
                      className={`${style.text} text-[13px] font-medium`}
                    >
                      {c.circuitName}
                    </text>
                  ))}
                </>
              )}
            </g>
          </g>
        )
      })}
    </svg>
  )
}
