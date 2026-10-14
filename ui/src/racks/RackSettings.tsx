import { useState } from 'react'
import { ApiError, api } from '../api/client'
import type { RackResponse, RackTypeResponse, SubmainResponse } from '../api/types'
import { Button } from '../components/Button'
import { Field } from '../components/Field'
import { SelectField } from '../components/SelectField'
import { announce } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'

interface Props {
  rack: RackResponse
  cabinets: RackTypeResponse[]
  submains: SubmainResponse[]
  onSaved: (rack: RackResponse) => void
  /** The rack changed under this form: reload it, as the rest of the screen does. */
  onConflict: () => void
}

/**
 * Name, cabinet and feed, folded away below the drawing as the panel's
 * install options are: set once, and rarely changed after. A shorter cabinet
 * is refused by the server with the U to clear, shown under the cabinet.
 */
export function RackSettings({ rack, cabinets, submains, onSaved, onConflict }: Props) {
  const once = useSingleFlight()
  const [name, setName] = useState(rack.name)
  const [rackTypeId, setRackTypeId] = useState(rack.rackTypeId)
  const [fedFrom, setFedFrom] = useState(rack.fedFromSubmainId ?? '')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  const changed = name.trim() !== rack.name || rackTypeId !== rack.rackTypeId || (fedFrom || null) !== rack.fedFromSubmainId

  const save = () => once(async () => {
    setSaving(true)
    setFieldErrors({})
    try {
      const saved = await api.patch<RackResponse>(`/racks/${rack.id}`, {
        name: name.trim(), rackTypeId, fedFromSubmainId: fedFrom || null, notes: rack.notes,
      })
      onSaved(saved)
      announce('Rack settings saved', 'ok')
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        announce('This rack changed elsewhere — reloading')
        onConflict()
        return
      }
      setFieldErrors(error.fieldErrors)
      announce(Object.keys(error.fieldErrors).length > 0 ? 'Rack settings not saved — see the form' : error.message, 'error')
    } finally {
      setSaving(false)
    }
  })

  return (
    <details className="group rounded-lg border border-rule bg-sheet">
      <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between rounded-lg px-4 font-semibold active:bg-press">
        Rack settings
        <span aria-hidden="true" className="text-ink-2 transition-transform group-open:rotate-180 motion-reduce:transition-none">▾</span>
      </summary>
      <form className="space-y-4 px-4 pb-4" noValidate onSubmit={e => { e.preventDefault(); void save() }}>
        <Field label="Rack name" value={name} onChange={setName} errors={fieldErrors.name} autoCapitalize="words" autoComplete="off" />
        <SelectField
          label="Cabinet"
          value={rackTypeId}
          onChange={setRackTypeId}
          errors={fieldErrors.rackTypeId}
          options={cabinets.length > 0
            ? cabinets.map(c => ({ value: c.id, label: `${c.heightU}U · ${c.description}` }))
            : [{ value: rack.rackTypeId, label: rack.cabinet }]}
        />
        <SelectField
          label="Fed from"
          hint="The submain this rack's supply comes from. It changes nothing on that panel."
          value={fedFrom}
          onChange={setFedFrom}
          errors={fieldErrors.fedFromSubmainId}
          options={[{ value: '', label: 'Not fed from a submain' }, ...submains.map(s => ({ value: s.id, label: s.name }))]}
        />
        <Button type="submit" disabled={saving || !changed}>{saving ? 'Saving…' : 'Save settings'}</Button>
      </form>
    </details>
  )
}
