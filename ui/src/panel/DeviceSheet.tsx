import { useRef, useState } from 'react'
import type { PlacedDeviceResponse } from '../api/types'
import { Button } from '../components/Button'
import { Sheet } from '../components/Sheet'
import { deviceStyle } from './deviceStyle'
import { formatModules } from '../units'

interface Props {
  device: PlacedDeviceResponse
  /** Rooms already named in this house, offered as suggestions. */
  rooms: string[]
  onSaveCircuit: (circuitId: string, name: string, room: string | null) => void | Promise<void>
  onFreeChannel: (channelIndex: number) => void | Promise<void>
  onClose: () => void
}

export function DeviceSheet({ device, rooms, onSaveCircuit, onFreeChannel, onClose }: Props) {
  const style = deviceStyle(device.category)

  return (
    <Sheet title={device.label} onClose={onClose}>
      <div className="mb-4">
        <h2 className="text-lg font-semibold">{device.label}</h2>
        <p className="text-sm text-slate-500">
          {style.kind} · {formatModules(device.moduleWidth)} · row {device.rowIndex + 1}
        </p>
      </div>

      <datalist id="known-rooms">
        {rooms.map(room => <option key={room} value={room} />)}
      </datalist>

      <ul className="space-y-3">
        {device.channels.map(channel => (
          <li key={channel.channelIndex} className="rounded-lg border border-slate-200 p-3">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">
              Channel {channel.channelIndex + 1}
            </p>
            {channel.circuitId ? (
              <div className="space-y-2">
                <CircuitFields
                  // Remounts when the channel is pointed at a different circuit,
                  // so the inputs never show the previous circuit's values.
                  key={channel.circuitId}
                  initialName={channel.circuitName ?? ''}
                  initialRoom={channel.circuitRoom ?? ''}
                  onCommit={(name, room) => onSaveCircuit(channel.circuitId!, name, room)}
                />
                <Button variant="ghost" onClick={() => onFreeChannel(channel.channelIndex)}>
                  Free this channel
                </Button>
              </div>
            ) : (
              <p className="text-sm text-slate-500">Spare</p>
            )}
          </li>
        ))}
        {device.channels.length === 0 && (
          <li className="text-sm text-slate-500">This device has no channels.</li>
        )}
      </ul>
    </Sheet>
  )
}

/**
 * Room and circuit name are one edit, committed together on blur: the API
 * replaces both fields, so sending a name alone would wipe the room.
 */
function CircuitFields({ initialName, initialRoom, onCommit }: {
  initialName: string
  initialRoom: string
  onCommit: (name: string, room: string | null) => void | Promise<void>
}) {
  const [name, setName] = useState(initialName)
  const [room, setRoom] = useState(initialRoom)

  // Both inputs commit on blur, so tabbing from room to name would otherwise
  // save the pair twice. Tracked here rather than against the props, which only
  // catch up once the reload lands.
  const saved = useRef({ name: initialName, room: initialRoom })

  function commit() {
    // A blank name is not an edit — the API rejects it, and clearing the field
    // to retype should not fire a failing request on the way past.
    if (!name.trim()) return
    if (name === saved.current.name && room === saved.current.room) return

    saved.current = { name, room }
    void onCommit(name.trim(), room.trim() || null)
  }

  return (
    <div className="space-y-2">
      <label className="block">
        <span className="mb-1 block text-xs text-slate-500">Room</span>
        <input
          aria-label="Room"
          list="known-rooms"
          value={room}
          placeholder="Kitchen"
          onChange={e => setRoom(e.target.value)}
          onBlur={commit}
          className="min-h-11 w-full rounded-lg border border-slate-300 px-3"
        />
      </label>
      <label className="block">
        <span className="mb-1 block text-xs text-slate-500">Circuit name</span>
        <input
          aria-label="Circuit name"
          value={name}
          placeholder="Island pendants"
          onChange={e => setName(e.target.value)}
          onBlur={commit}
          className="min-h-11 w-full rounded-lg border border-slate-300 px-3"
        />
      </label>
    </div>
  )
}
