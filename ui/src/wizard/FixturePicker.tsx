import { useState } from 'react'
import type { DeviceCategory, DeviceTypeResponse, ExtraFixture } from '../api/types'
import { Button } from '../components/Button'
import { ConfirmButton } from '../components/ConfirmButton'
import { Stepper } from '../components/Stepper'

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

/** What the option groups say; the category names are code, not English. */
const groupLabels: Partial<Record<DeviceCategory, string>> = {
  EnergyMeter: 'Energy meters', Network: 'Network', Relay: 'Relays', Dimmer240: 'Dimmers',
  Dimmer0_10V: 'Tape dimmers', Cover: 'Blinds', LedController: 'LED controllers', Isolator: 'Isolators',
}

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
      <ul className="space-y-3">
        {fixtures.map(fixture => (
          <li key={fixture.deviceTypeId} className="rounded-lg border border-rule bg-sheet p-3">
            <Stepper
              label={`Quantity of ${nameOf(fixture.deviceTypeId)}`}
              value={fixture.quantity}
              min={1}
              disabled={disabled}
              onChange={quantity => onChange(fixtures.map(f => f.deviceTypeId === fixture.deviceTypeId
                ? { ...f, quantity }
                : f))}
            />
            <ConfirmButton
              label="Remove"
              question={`Remove the ${nameOf(fixture.deviceTypeId)}?`}
              confirmLabel="Remove it"
              disabled={disabled}
              onConfirm={() => onChange(fixtures.filter(f => f.deviceTypeId !== fixture.deviceTypeId))}
              className="mt-2"
            />
          </li>
        ))}
      </ul>

      {/* Stacked, not side by side: a select is as wide as its longest option
          and cannot shrink, which is what used to push the page sideways. */}
      <div className="space-y-2">
        <select
          aria-label="Device to add"
          value={chosen}
          disabled={disabled}
          onChange={e => setChosen(e.target.value)}
          className="select w-full min-w-0 rounded-lg border border-rule bg-sheet pl-3"
        >
          <option value="">Choose a device…</option>
          {groups.map(group => (
            <optgroup key={group.category} label={groupLabels[group.category] ?? group.category}>
              {group.devices.map(device => (
                <option key={device.id} value={device.id}>
                  {device.manufacturer} {device.model}
                </option>
              ))}
            </optgroup>
          ))}
        </select>
        <Button variant="secondary" disabled={disabled || !chosen} onClick={add} className="w-full">
          Add
        </Button>
      </div>
    </div>
  )
}
