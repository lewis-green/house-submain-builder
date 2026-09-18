import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ApiError, api } from '../api/client'
import type { DeviceCategory, EnclosureType } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Spinner } from '../components/Spinner'
import { SLOT_UNITS_PER_MODULE, formatModules } from '../units'

interface DeviceTypeView {
  id: string
  manufacturer: string
  model: string
  partNumber: string
  category: DeviceCategory
  moduleWidth: number
  channelCount: number
  maxLoadPerChannelW: number | null
  maxTotalLoadW: number | null
  cost: number
  active: boolean
}

const categories: DeviceCategory[] =
  ['Terminal240', 'Dimmer240', 'Dimmer0_10V', 'Relay', 'Psu24V', 'Accessory']

/**
 * Widths are entered and shown in DIN modules and converted on save. An admin
 * should never have to think in thirds. Terminal blocks are the exception: a
 * third of a module is exactly what one is, so they are entered as blocks per
 * module instead.
 */
export function CataloguePage() {
  const [devices, setDevices] = useState<DeviceTypeView[] | null>(null)
  const [enclosures, setEnclosures] = useState<EnclosureType[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<DeviceTypeView | null>(null)

  const load = () =>
    Promise.all([
      api.get<DeviceTypeView[]>('/catalogue/device-types'),
      api.get<EnclosureType[]>('/catalogue/enclosures'),
    ])
      .then(([d, e]) => { setDevices(d); setEnclosures(e) })
      .catch((err: ApiError) => setError(err.message))

  useEffect(() => { void load() }, [])

  if (error) return <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />
  if (!devices || !enclosures) return <Spinner label="Loading catalogue" />

  return (
    <div className="space-y-6 p-4">
      <div>
        <Link to="/" className="text-sm text-slate-500">← Houses</Link>
        <h1 className="mt-1 text-xl font-semibold">Catalogue</h1>
      </div>

      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-slate-700">Devices</h2>
        <ul className="space-y-2">
          {devices.map(device => (
            <li key={device.id} className="rounded-lg border border-slate-200 p-3">
              <div className="flex items-baseline justify-between gap-3">
                <span className="font-medium">{device.manufacturer} {device.model}</span>
                <span className="shrink-0 text-sm text-slate-500">
                  {device.category === 'Terminal240'
                    ? `${SLOT_UNITS_PER_MODULE / device.moduleWidth} per module`
                    : formatModules(device.moduleWidth)}
                </span>
              </div>
              <div className="flex items-baseline justify-between gap-3 text-sm text-slate-500">
                <span>{device.partNumber}</span>
                <span>
                  {device.cost > 0
                    ? new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(device.cost)
                    : 'No price'}
                </span>
              </div>
              {!device.active && <p className="text-sm text-amber-700">Inactive</p>}
              <Button variant="ghost" className="mt-1" onClick={() => setEditing(device)}>Edit</Button>
            </li>
          ))}
        </ul>
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-slate-700">Enclosures</h2>
        <ul className="space-y-2">
          {enclosures.map(enclosure => (
            <li key={enclosure.id} className="rounded-lg border border-slate-200 p-3">
              <span className="font-medium">{enclosure.description}</span>
              <span className="block text-sm text-slate-500">
                {enclosure.rows} rows of {enclosure.slotsPerRow / SLOT_UNITS_PER_MODULE} modules
              </span>
            </li>
          ))}
        </ul>
      </section>

      {editing && (
        <DeviceTypeForm
          device={editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); void load() }}
        />
      )}
    </div>
  )
}

export function DeviceTypeForm({
  device,
  onClose,
  onSaved,
}: {
  device: DeviceTypeView
  onClose: () => void
  onSaved: () => void
}) {
  const isTerminal = device.category === 'Terminal240'

  const [modules, setModules] = useState(
    isTerminal
      ? String(SLOT_UNITS_PER_MODULE / device.moduleWidth)
      : String(device.moduleWidth / SLOT_UNITS_PER_MODULE))
  const [channels, setChannels] = useState(String(device.channelCount))
  const [cost, setCost] = useState(String(device.cost))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  async function save() {
    setSaving(true)
    setFieldErrors({})

    const entered = Number(modules)
    const moduleWidth = isTerminal
      ? Math.round(SLOT_UNITS_PER_MODULE / (entered || 1))
      : Math.round((entered || 0) * SLOT_UNITS_PER_MODULE)

    try {
      await api.patch(`/catalogue/device-types/${device.id}`, {
        manufacturer: device.manufacturer,
        model: device.model,
        partNumber: device.partNumber,
        category: device.category,
        moduleWidth,
        channelCount: Number(channels) || 0,
        maxLoadPerChannelW: device.maxLoadPerChannelW,
        maxTotalLoadW: device.maxTotalLoadW,
        cost: Number(cost) || 0,
        active: device.active,
      })
      onSaved()
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) {
        setFieldErrors({ form: [error.message] })
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-3 rounded-lg border border-slate-300 p-4">
      <h3 className="font-medium">{device.manufacturer} {device.model}</h3>

      <Field
        label={isTerminal ? 'Blocks per DIN module' : 'Width in DIN modules'}
        value={modules}
        onChange={setModules}
        type="number"
        inputMode="decimal"
        hint={isTerminal ? 'Three 2003-7646 fit in one module.' : 'A Shelly Pro Dimmer is 1.'}
        errors={fieldErrors.moduleWidth}
      />

      {!isTerminal && (
        <Field
          label="Channels"
          value={channels}
          onChange={setChannels}
          type="number"
          inputMode="numeric"
          errors={fieldErrors.channelCount}
        />
      )}

      <Field label="Cost (£)" value={cost} onChange={setCost} type="number" inputMode="decimal" errors={fieldErrors.cost} />

      {fieldErrors.form && <p role="alert" className="text-sm text-red-600">{fieldErrors.form[0]}</p>}

      <div className="flex gap-2">
        <Button onClick={save} disabled={saving}>Save</Button>
        <Button variant="secondary" onClick={onClose}>Cancel</Button>
      </div>
    </div>
  )
}
