import { useState } from 'react'
import type { PlacedDeviceResponse } from '../api/types'
import { Button } from '../components/Button'
import { Sheet } from '../components/Sheet'
import { deviceStyle } from './deviceStyle'
import { formatModules } from '../units'

interface Props {
  device: PlacedDeviceResponse
  onRenameCircuit: (circuitId: string, name: string) => void | Promise<void>
  onFreeChannel: (channelIndex: number) => void | Promise<void>
  onClose: () => void
}

export function DeviceSheet({ device, onRenameCircuit, onFreeChannel, onClose }: Props) {
  const style = deviceStyle(device.category)

  return (
    <Sheet title={device.label} onClose={onClose}>
      <div className="mb-4">
        <h2 className="text-lg font-semibold">{device.label}</h2>
        <p className="text-sm text-slate-500">
          {style.kind} · {formatModules(device.moduleWidth)} · row {device.rowIndex + 1}
        </p>
      </div>

      <ul className="space-y-3">
        {device.channels.map(channel => (
          <li key={channel.channelIndex} className="rounded-lg border border-slate-200 p-3">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">
              Channel {channel.channelIndex + 1}
            </p>
            {channel.circuitId ? (
              <div className="space-y-2">
                <CircuitName
                  initial={channel.circuitName ?? ''}
                  onCommit={name => onRenameCircuit(channel.circuitId!, name)}
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

/** Commits on blur, and only when the value actually changed. */
function CircuitName({ initial, onCommit }: { initial: string; onCommit: (name: string) => void | Promise<void> }) {
  const [value, setValue] = useState(initial)

  return (
    <input
      aria-label="Circuit name"
      value={value}
      onChange={e => setValue(e.target.value)}
      onBlur={() => { if (value !== initial && value.trim()) void onCommit(value.trim()) }}
      className="min-h-11 w-full rounded-lg border border-slate-300 px-3"
    />
  )
}
