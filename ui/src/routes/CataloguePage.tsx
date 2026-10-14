import { useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { DeviceTypeResponse, EnclosureType } from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Skeleton } from '../components/Skeleton'
import { announce } from '../components/status'
import { SLOT_UNITS_PER_MODULE, formatModules } from '../units'
import { RackCatalogueSections } from './RackCatalogueSections'

type DeviceTypeView = DeviceTypeResponse

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

  const loaded = devices !== null && enclosures !== null

  return (
    <AppShell
      title="Catalogue"
      subtitle="The parts a panel can be built from"
      back={{ to: '/', label: 'Houses' }}
    >
      {error && <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />}
      {!loaded && !error && <Skeleton label="Loading catalogue" rows={5} rowClass="h-20" />}

      {loaded && (
        <>
          <section aria-labelledby="devices-heading">
            <h2 id="devices-heading" className="mb-2 font-semibold">Devices</h2>
            <ul className="divide-y divide-rule border-y border-rule">
              {devices.map(device => (
                <li key={device.id} className="py-3">
                  {editing?.id === device.id ? (
                    <DeviceTypeForm
                      device={device}
                      onClose={() => setEditing(null)}
                      onSaved={() => { setEditing(null); announce(`Saved ${device.manufacturer} ${device.model}`, 'ok'); void load() }}
                    />
                  ) : (
                    <div className="flex items-center gap-3">
                      <div className="min-w-0 flex-1">
                        <p className="font-semibold">{device.manufacturer} {device.model}</p>
                        <p className="text-ink-2">
                          {device.partNumber}
                          {' · '}
                          {device.category === 'Terminal240'
                            ? `${SLOT_UNITS_PER_MODULE / device.moduleWidth} per module`
                            : device.moduleWidth === 0
                              ? 'takes no rail space'
                              : `${formatModules(device.moduleWidth)} wide`}
                        </p>
                        {!device.active && <p className="text-sm font-medium text-warn">Inactive</p>}
                      </div>
                      <Button variant="secondary" onClick={() => setEditing(device)} aria-label={`Edit ${device.manufacturer} ${device.model}`}>
                        Edit
                      </Button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </section>

          <section aria-labelledby="enclosures-heading" className="mt-8">
            <h2 id="enclosures-heading" className="mb-2 font-semibold">Enclosures</h2>
            <ul className="divide-y divide-rule border-y border-rule">
              {enclosures.map(enclosure => (
                <li key={enclosure.id} className="py-3">
                  <p className="font-semibold">{enclosure.description}</p>
                  <p className="text-ink-2">
                    {enclosure.rows} rows of {enclosure.slotsPerRow / SLOT_UNITS_PER_MODULE} modules
                  </p>
                </li>
              ))}
            </ul>
          </section>

          <RackCatalogueSections />
        </>
      )}
    </AppShell>
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
    <form
      className="space-y-3 rounded-lg border border-rule bg-sheet p-4"
      onSubmit={e => { e.preventDefault(); void save() }}
    >
      <h3 className="font-semibold">{device.manufacturer} {device.model}</h3>

      <Field
        label={isTerminal ? 'Blocks per DIN module' : 'Width in DIN modules'}
        value={modules}
        onChange={setModules}
        type="number"
        inputMode="decimal"
        enterKeyHint={isTerminal ? 'done' : 'next'}
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
          enterKeyHint="done"
          errors={fieldErrors.channelCount}
        />
      )}

      {fieldErrors.form && <p role="alert" className="font-medium text-danger">{fieldErrors.form[0]}</p>}

      <div className="flex gap-2">
        <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
        <Button variant="secondary" onClick={onClose}>Cancel</Button>
      </div>
    </form>
  )
}
