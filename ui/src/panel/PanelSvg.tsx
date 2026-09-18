import type { LayoutResponse, PlacedDeviceResponse } from '../api/types'
import { SLOT_UNITS_PER_MODULE } from '../units'
import { deviceStyle } from './deviceStyle'
import { ROW_PX, SLOT_PX, deviceRect, panelSize, rowToY, slotToX } from './geometry'

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

function describe(device: PlacedDeviceResponse): string {
  const named = device.channels.filter(c => c.circuitName).map(c => c.circuitName)
  const spare = device.channels.filter(c => c.isSpare).length

  const parts = [device.label, ...named]
  if (spare > 0) parts.push(`${spare} spare ${spare === 1 ? 'channel' : 'channels'}`)
  return parts.join(', ')
}

export function PanelSvg({ layout, selectedId, onSelect, dragging }: Props) {
  const size = panelSize(layout)
  const groups = groupTerminals(layout.devices)
  const others = layout.devices.filter(d => d.category !== 'Terminal240')

  return (
    <svg
      viewBox={`0 0 ${size.width} ${size.height}`}
      width={size.width}
      height={size.height}
      role="img"
      aria-label={`Panel, ${layout.rows} rows of ${layout.slotsPerRow / SLOT_UNITS_PER_MODULE} modules`}
      className="select-none"
    >
      {Array.from({ length: layout.rows }, (_, row) => (
        <g key={`row-${row}`}>
          <rect
            x={0}
            y={rowToY(row)}
            width={size.width}
            height={ROW_PX}
            rx={4}
            className="fill-slate-50 stroke-slate-200"
          />
          {Array.from({ length: layout.slotsPerRow / SLOT_UNITS_PER_MODULE + 1 }, (_, m) => (
            <line
              key={`tick-${row}-${m}`}
              x1={slotToX(m * SLOT_UNITS_PER_MODULE)}
              y1={rowToY(row)}
              x2={slotToX(m * SLOT_UNITS_PER_MODULE)}
              y2={rowToY(row) + ROW_PX}
              className="stroke-slate-200"
              strokeWidth={1}
            />
          ))}
        </g>
      ))}

      {groups.map(group => {
        const rect = deviceRect(group)
        const style = deviceStyle('Terminal240')
        return (
          <g
            key={group.key}
            role="button"
            tabIndex={0}
            aria-label={group.label}
            aria-pressed={group.members.some(m => m.id === selectedId)}
            onClick={() => group.members[0].id && onSelect(group.members[0].id)}
            onKeyDown={e => {
              if ((e.key === 'Enter' || e.key === ' ') && group.members[0].id) onSelect(group.members[0].id)
            }}
            className="cursor-pointer"
          >
            <rect {...rect} rx={3} className={`${style.fill} ${style.stroke}`} strokeWidth={1} />
            <text
              x={rect.x + rect.width / 2}
              y={rect.y + ROW_PX / 2}
              textAnchor="middle"
              dominantBaseline="middle"
              className={`${style.text} text-[10px]`}
            >
              {group.label}
            </text>
          </g>
        )
      })}

      {others.map(device => {
        const drag = dragging?.deviceId === device.id ? dragging : null
        const rect = deviceRect(drag ? { ...device, rowIndex: drag.rowIndex, startSlot: drag.startSlot } : device)
        const style = deviceStyle(device.category)
        const invalid = drag && !drag.valid

        return (
          <g
            key={device.id ?? `${device.rowIndex}-${device.startSlot}`}
            role="button"
            tabIndex={0}
            aria-label={describe(device)}
            aria-pressed={device.id === selectedId}
            data-device-id={device.id ?? undefined}
            onClick={() => device.id && onSelect(device.id)}
            onKeyDown={e => {
              if ((e.key === 'Enter' || e.key === ' ') && device.id) onSelect(device.id)
            }}
            className="cursor-pointer"
            opacity={drag ? 0.85 : 1}
          >
            <rect
              {...rect}
              rx={4}
              className={invalid ? 'fill-red-200 stroke-red-500' : `${style.fill} ${style.stroke}`}
              strokeWidth={device.id === selectedId ? 3 : 1.5}
            />
            <text
              x={rect.x + 6}
              y={rect.y + 20}
              className={`${style.text} text-[11px] font-medium`}
            >
              {device.label}
            </text>
            <text x={rect.x + 6} y={rect.y + 36} className="fill-slate-500 text-[9px]">
              {style.kind}
            </text>
            {device.channels.filter(c => c.circuitName).map((c, i) => (
              <text
                key={c.channelIndex}
                x={rect.x + 6}
                y={rect.y + 50 + i * 11}
                className="fill-slate-600 text-[9px]"
              >
                {c.circuitName}
              </text>
            ))}
          </g>
        )
      })}
    </svg>
  )
}
