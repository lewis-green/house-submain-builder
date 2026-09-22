import { useState } from 'react'
import type { DeviceCategory, DeviceTypeResponse, ExtraFixture } from '../api/types'
import { Button } from '../components/Button'

interface Props {
  catalogue: DeviceTypeResponse[]
  fixtures: ExtraFixture[]
  disabled?: boolean
  onChange: (fixtures: ExtraFixture[]) => void
}

/** Ordered so the gear people actually add by hand comes first. */
const order: DeviceCategory[] = [
  'EnergyMeter', 'Network', 'Relay', 'Dimmer240', 'Dimmer0_10V', 'Cover', 'LedController', 'Isolator',
]

/**
 * Devices the circuits do not imply. A meter or a LAN switch feeds nothing, so
 * no count of lights or blinds will ever ask for one.
 */
export function FixturePicker({ catalogue, fixtures, disabled, onChange }: Props) {
  const [chosen, setChosen] = useState('')

  // Only things that take rail space: a driver or a jumper bar is not a fixture
  // anyone places.
  const placeable = catalogue.filter(d => d.active && d.moduleWidth > 0)
  const groups = order
    .map(category => ({ category, devices: placeable.filter(d => d.category === category) }))
    .filter(g => g.devices.length > 0)

  const nameOf = (id: string) => {
    const device = catalogue.find(d => d.id === id)
    return device ? `${device.manufacturer} ${device.model}` : 'Unknown device'
  }

  function add() {
    if (!chosen) return
    const already = fixtures.find(f => f.deviceTypeId === chosen)
    onChange(already
      ? fixtures.map(f => f.deviceTypeId === chosen ? { ...f, quantity: f.quantity + 1 } : f)
      : [...fixtures, { deviceTypeId: chosen, quantity: 1 }])
    setChosen('')
  }

  return (
    <div className="space-y-3">
      <ul className="space-y-2">
        {fixtures.map(fixture => (
          <li key={fixture.deviceTypeId} className="flex items-center gap-3">
            <span className="flex-1 text-sm">{nameOf(fixture.deviceTypeId)}</span>
            <input
              type="number"
              min={1}
              inputMode="numeric"
              aria-label={`Quantity of ${nameOf(fixture.deviceTypeId)}`}
              value={fixture.quantity}
              disabled={disabled}
              onChange={e => onChange(fixtures.map(f => f.deviceTypeId === fixture.deviceTypeId
                ? { ...f, quantity: Math.max(1, Number(e.target.value) || 1) }
                : f))}
              className="min-h-11 w-20 rounded-lg border border-slate-300 px-3"
            />
            <Button
              variant="ghost"
              disabled={disabled}
              onClick={() => onChange(fixtures.filter(f => f.deviceTypeId !== fixture.deviceTypeId))}
            >
              Remove
            </Button>
          </li>
        ))}
      </ul>

      <div className="flex items-center gap-3">
        <select
          aria-label="Device to add"
          value={chosen}
          disabled={disabled}
          onChange={e => setChosen(e.target.value)}
          className="min-h-11 flex-1 rounded-lg border border-slate-300 px-3"
        >
          <option value="">Choose a device…</option>
          {groups.map(group => (
            <optgroup key={group.category} label={group.category}>
              {group.devices.map(device => (
                <option key={device.id} value={device.id}>
                  {device.manufacturer} {device.model}
                </option>
              ))}
            </optgroup>
          ))}
        </select>
        <Button variant="secondary" disabled={disabled || !chosen} onClick={add}>Add</Button>
      </div>
    </div>
  )
}
