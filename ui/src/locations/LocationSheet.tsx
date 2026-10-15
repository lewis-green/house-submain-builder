import { useState } from 'react'
import { ApiError, api } from '../api/client'
import type { LocationResponse } from '../api/types'
import { Button } from '../components/Button'
import { ConfirmButton } from '../components/ConfirmButton'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Sheet } from '../components/Sheet'
import { announce, dismissStatus } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'

interface Props {
  projectId: string
  /** The location to rename or remove; without one, the sheet adds a new one. */
  location?: LocationResponse
  /** Called once the house has changed, so the page can load it again. */
  onChanged: () => void
  onClose: () => void
}

/**
 * Adds a location, or renames or removes one. Removing asks first and says
 * plainly that what is in the location stays in the house: nothing on a
 * panel or a rack is lost, it only stops being placed.
 */
export function LocationSheet({ projectId, location, onChanged, onClose }: Props) {
  const once = useSingleFlight()
  const [name, setName] = useState(location?.name ?? '')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  // What the server refused that the name field cannot show, said by the button.
  const [refusal, setRefusal] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const run = (work: () => Promise<string>, failed: string) => once(async () => {
    setBusy(true)
    setFieldErrors({})
    setRefusal(null)
    dismissStatus()
    try {
      announce(await work(), 'ok')
      onChanged()
      onClose()
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      // Said once, as the house form says it: by the name field when the
      // refusal is about the name, by the note for anything else the server
      // named, and by the status line only when it named nothing.
      const elsewhere = error.errorsOutside(['name'])
      if (elsewhere.length > 0) setRefusal(`${failed}: ${elsewhere.join(' ')}`)
      else if (!error.fieldErrors.name) announce(`${failed}: ${error.message}`, 'error')
    } finally {
      setBusy(false)
    }
  })

  // The rename sends the notes back as they are: the endpoint replaces them
  // with whatever it is sent, and this sheet does not edit them.
  const save = () => run(async () => {
    if (location) {
      const saved = await api.patch<LocationResponse>(`/locations/${location.id}`, { name: name.trim(), notes: location.notes })
      return `Renamed to ${saved.name}`
    }
    const added = await api.post<LocationResponse>(`/projects/${projectId}/locations`, { name: name.trim() })
    return `Added ${added.name}`
  }, location ? 'Not renamed' : 'Not added')

  const remove = () => run(async () => {
    await api.del(`/locations/${location!.id}`)
    return `Removed ${location!.name}`
  }, 'Not removed')

  const title = location ? location.name : 'Add location'

  return (
    <Sheet title={title} subtitle={location ? 'Rename or remove this location' : 'A plant room, a garage, a comms cupboard'} onClose={onClose}>
      <form className="space-y-4" noValidate onSubmit={e => { e.preventDefault(); void save() }}>
        <Field
          label="Location name"
          value={name}
          onChange={setName}
          errors={fieldErrors.name}
          placeholder="Plant room"
          autoCapitalize="words"
          autoComplete="off"
          enterKeyHint="done"
        />
        {refusal && <ErrorNote compact message={refusal} />}
        <Button type="submit" disabled={busy}>
          {location ? (busy ? 'Saving…' : 'Save') : (busy ? 'Adding…' : 'Add location')}
        </Button>
      </form>

      {location && (
        <div className="mt-6 border-t border-rule pt-4">
          <ConfirmButton
            label="Remove location"
            question={`Remove ${location.name}? Its automation panels and racks stay in the house, not placed.`}
            confirmLabel="Yes, remove it"
            disabled={busy}
            onConfirm={() => void remove()}
          />
        </div>
      )}
    </Sheet>
  )
}
