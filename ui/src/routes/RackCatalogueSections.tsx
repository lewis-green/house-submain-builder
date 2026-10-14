import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { RackDeviceTypeResponse, RackTypeResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Toggle } from '../components/Toggle'
import { announce } from '../components/status'

/**
 * Rack cabinets and rack items, so an ASSUMED part number can be confirmed
 * without a redeploy. Ports are not edited here: a slip in a port group would
 * renumber every port on a printed schedule, so that stays with whoever
 * maintains the seed.
 */
export function RackCatalogueSections() {
  const [cabinets, setCabinets] = useState<RackTypeResponse[] | null>(null)
  const [items, setItems] = useState<RackDeviceTypeResponse[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<string | null>(null)

  const load = useCallback(() =>
    Promise.all([
      api.get<RackTypeResponse[]>('/catalogue/rack-types'),
      api.get<RackDeviceTypeResponse[]>('/catalogue/rack-device-types'),
    ])
      .then(([c, i]) => { setCabinets(c); setItems(i); setError(null) })
      .catch((e: ApiError) => setError(e.message)), [])

  useEffect(() => { void load() }, [load])

  const done = () => { setEditing(null); void load() }

  if (error) {
    return <div className="mt-8"><ErrorNote message={`Could not load the rack catalogue: ${error}`} onRetry={() => void load()} /></div>
  }
  if (!cabinets || !items) return null

  return (
    <>
      <section aria-labelledby="rack-cabinets-heading" className="mt-8">
        <h2 id="rack-cabinets-heading" className="mb-2 font-semibold">Rack cabinets</h2>
        <ul className="divide-y divide-rule border-y border-rule">
          {cabinets.map(c => (
            <li key={c.id} className="py-3">
              {editing === c.id
                ? <CabinetForm cabinet={c} onDone={done} onCancel={() => setEditing(null)} />
                : <Row title={c.description} detail={`${c.heightU}U · ${c.partNumber}`} onEdit={() => setEditing(c.id)} />}
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="rack-items-heading" className="mt-8">
        <h2 id="rack-items-heading" className="mb-2 font-semibold">Rack items</h2>
        <ul className="divide-y divide-rule border-y border-rule">
          {items.map(item => (
            <li key={item.id} className="py-3">
              {editing === item.id
                ? <ItemForm item={item} onDone={done} onCancel={() => setEditing(null)} />
                : (
                  <Row
                    title={item.description}
                    detail={`${item.kind === 'Accessory' ? 'Accessory' : `${item.heightU}U · ${item.portCount} ports`} · ${item.partNumber}${item.active ? '' : ' · not offered'}`}
                    onEdit={() => setEditing(item.id)}
                  />
                )}
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}

function Row({ title, detail, onEdit }: { title: string; detail: string; onEdit: () => void }) {
  return (
    <div className="flex items-center gap-3">
      <div className="min-w-0 flex-1 wrap-anywhere">
        <p className="font-semibold">{title}</p>
        <p className="text-ink-2">{detail}</p>
      </div>
      <Button variant="ghost" aria-label={`Edit ${title}`} onClick={onEdit}>Edit</Button>
    </div>
  )
}

function CabinetForm({ cabinet, onDone, onCancel }: { cabinet: RackTypeResponse; onDone: () => void; onCancel: () => void }) {
  const [manufacturer, setManufacturer] = useState(cabinet.manufacturer)
  const [model, setModel] = useState(cabinet.model)
  const [partNumber, setPartNumber] = useState(cabinet.partNumber)
  const [height, setHeight] = useState(String(cabinet.heightU))
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  async function save() {
    setErrors({})
    try {
      await api.patch(`/catalogue/rack-types/${cabinet.id}`, {
        manufacturer, model, partNumber, heightU: Number(height) || 0, mounting: cabinet.mounting,
      })
      announce(`${manufacturer} ${model} saved`, 'ok')
      onDone()
    } catch (e) {
      const error = e as ApiError
      setErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) announce(error.message, 'error')
    }
  }

  return (
    <form className="space-y-3" onSubmit={e => { e.preventDefault(); void save() }} noValidate>
      <Field label="Manufacturer" value={manufacturer} onChange={setManufacturer} errors={errors.manufacturer} />
      <Field label="Model" value={model} onChange={setModel} errors={errors.model} />
      <Field label="Part number" value={partNumber} onChange={setPartNumber} errors={errors.partNumber} autoCapitalize="characters" />
      <Field label="Height (U)" value={height} onChange={setHeight} inputMode="numeric" errors={errors.heightU} />
      <div className="flex gap-2">
        <Button type="submit">Save</Button>
        <Button variant="secondary" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}

function ItemForm({ item, onDone, onCancel }: { item: RackDeviceTypeResponse; onDone: () => void; onCancel: () => void }) {
  const [manufacturer, setManufacturer] = useState(item.manufacturer)
  const [model, setModel] = useState(item.model)
  const [partNumber, setPartNumber] = useState(item.partNumber)
  const [active, setActive] = useState(item.active)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  async function save() {
    setErrors({})
    try {
      // Everything this form does not edit goes back as it came, so a part
      // number fix can never disturb the ports.
      await api.patch(`/catalogue/rack-device-types/${item.id}`, {
        manufacturer, model, partNumber, kind: item.kind, heightU: item.heightU, portGroups: item.portGroups,
        poeBudgetW: item.poeBudgetW, requiredAccessoryId: item.requiredAccessoryId, active,
      })
      announce(`${manufacturer} ${model} saved`, 'ok')
      onDone()
    } catch (e) {
      const error = e as ApiError
      setErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) announce(error.message, 'error')
    }
  }

  return (
    <form className="space-y-3" onSubmit={e => { e.preventDefault(); void save() }} noValidate>
      <Field label="Manufacturer" value={manufacturer} onChange={setManufacturer} errors={errors.manufacturer} />
      <Field label="Model" value={model} onChange={setModel} errors={errors.model} />
      <Field label="Part number" value={partNumber} onChange={setPartNumber} errors={errors.partNumber} autoCapitalize="characters" />
      {item.kind !== 'Accessory' && (
        <Toggle
          label="Offered in Add to rack"
          hint="Turning this off keeps it in racks that already have it."
          checked={active}
          onChange={setActive}
        />
      )}
      <div className="flex gap-2">
        <Button type="submit">Save</Button>
        <Button variant="secondary" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}
