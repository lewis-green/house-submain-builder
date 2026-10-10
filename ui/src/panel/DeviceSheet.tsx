import { useRef, useState } from 'react'
import type { PlacedDeviceResponse } from '../api/types'
import { ConfirmButton } from '../components/ConfirmButton'
import { Sheet } from '../components/Sheet'
import { deviceStyle } from './deviceStyle'
import { SLOT_UNITS_PER_MODULE, formatModuleCount, formatModules } from '../units'

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
  // The label as the server gives it — "Cover 1", the same words as the PDF.
  // The kind underneath ("Blinds") says what it is.
  const title = device.label

  // "+24V · 1.3 modules wide · row 1" said "+24V" twice; the kind is only worth
  // a mention when it is not already the title.
  const subtitle = [
    style.kind !== title ? style.kind : null,
    `${formatModules(device.moduleWidth)} wide`,
    `row ${device.rowIndex + 1}`,
  ].filter(Boolean).join(' · ')

  return (
    <Sheet title={title} subtitle={subtitle} onClose={onClose}>
      <datalist id="known-rooms">
        {rooms.map(room => <option key={room} value={room} />)}
      </datalist>

      {device.channels.length === 0 ? (
        // Nothing to name, so say what the thing is and where it sits — the
        // questions someone taps a block to answer.
        <div className="space-y-3">
          <p>{style.what}</p>
          <p className="text-ink-2">
            Row {device.rowIndex + 1}, from module {formatModuleCount(device.startSlot + SLOT_UNITS_PER_MODULE)}.
            {' '}It has no channels, so there is nothing to name here.
          </p>
          <p className="text-sm text-ink-2">To move it, drag it on the drawing — on a touchscreen, hold it first.</p>
        </div>
      ) : (
        <>
          <p className="mb-3 text-sm text-ink-2">
            Name each channel's circuit and the room it feeds. Changes save when you leave a box or press Return.
          </p>

          <ul className="space-y-3">
            {device.channels.map(channel => (
              <li key={channel.channelIndex} className="rounded-lg border border-rule p-3">
                <h3 className="mb-2 text-sm font-semibold text-ink-2">
                  Channel {channel.channelIndex + 1}
                </h3>
                {channel.circuitId ? (
                  <div className="space-y-3">
                    <CircuitFields
                      // Remounts when the channel is pointed at a different circuit,
                      // so the inputs never show the previous circuit's values.
                      key={channel.circuitId}
                      initialName={channel.circuitName ?? ''}
                      initialRoom={channel.circuitRoom ?? ''}
                      onCommit={(name, room) => onSaveCircuit(channel.circuitId!, name, room)}
                    />
                    <ConfirmButton
                      label="Free this channel"
                      question={`Free channel ${channel.channelIndex + 1}? Its circuit will be left unwired.`}
                      confirmLabel="Free it"
                      onConfirm={() => onFreeChannel(channel.channelIndex)}
                    />
                  </div>
                ) : (
                  <p className="text-ink-2">Spare</p>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </Sheet>
  )
}

type Pair = { name: string; room: string }

const samePair = (a: Pair, b: Pair | null) => b !== null && a.name === b.name && a.room === b.room

/**
 * Room and circuit name are one edit, committed together on blur or Return:
 * the API replaces both fields, so sending a name alone would wipe the room.
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
  // catch up once the reload lands — and recorded only once the save has
  // succeeded, so a failed save leaves the pair unsaved and the next Return or
  // blur with the same words tries again instead of waiting for a letter to change.
  const saved = useRef({ name: initialName, room: initialRoom })
  // The pair whose save is still in flight: Return commits and then blurs the
  // box, and that blur must not send the same pair a second time.
  const pending = useRef<Pair | null>(null)

  async function save(edit: Pair) {
    pending.current = edit
    try {
      await onCommit(edit.name.trim(), edit.room.trim() || null)
      saved.current = edit
    } catch {
      // The page has already said why. Staying unsaved is what makes the retry work.
    } finally {
      if (pending.current === edit) pending.current = null
    }
  }

  function commit() {
    // A blank name is not an edit — the API rejects it, and clearing the field
    // to retype should not fire a failing request on the way past.
    if (!name.trim()) return
    const edit = { name, room }
    if (samePair(edit, saved.current) || samePair(edit, pending.current)) return

    void save(edit)
  }

  const input = 'min-h-11 w-full rounded-lg border border-rule bg-sheet px-3'

  return (
    <form
      className="space-y-2"
      onSubmit={e => {
        e.preventDefault()
        commit()
        ;(document.activeElement as HTMLElement | null)?.blur?.()
      }}
    >
      <label className="block">
        <span className="mb-1 block text-sm font-medium">Room</span>
        <input
          aria-label="Room"
          list="known-rooms"
          value={room}
          placeholder="Kitchen"
          autoCapitalize="words"
          autoComplete="off"
          enterKeyHint="next"
          onChange={e => setRoom(e.target.value)}
          onBlur={commit}
          className={input}
        />
      </label>
      <label className="block">
        <span className="mb-1 block text-sm font-medium">Circuit name</span>
        <input
          aria-label="Circuit name"
          value={name}
          placeholder="Island pendants"
          autoCapitalize="sentences"
          autoComplete="off"
          enterKeyHint="done"
          onChange={e => setName(e.target.value)}
          onBlur={commit}
          className={input}
        />
      </label>
      {/* Return submits a form only when it has a submit button; this one is for the keyboard, not the thumb. */}
      <button type="submit" className="sr-only" tabIndex={-1} aria-hidden="true">Save</button>
    </form>
  )
}
