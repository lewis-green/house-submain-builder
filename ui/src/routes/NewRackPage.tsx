import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { LocationResponse, RackResponse, RackTypeResponse, SubmainResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { SelectField } from '../components/SelectField'
import { Skeleton } from '../components/Skeleton'
import { announce, dismissStatus } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'
import { asLocations, locationOptions } from '../locations'

/**
 * Three answers and the rack exists: what it is called, which cabinet, and
 * which automation panel feeds it. What goes in it is added on the rack's own screen,
 * where it can be seen going in.
 */
export function NewRackPage() {
  const { projectId } = useParams()
  const navigate = useNavigate()
  const once = useSingleFlight()

  const [cabinets, setCabinets] = useState<RackTypeResponse[] | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [rackTypeId, setRackTypeId] = useState('')
  const [fedFrom, setFedFrom] = useState('')
  const [locations, setLocations] = useState<LocationResponse[]>([])
  const [locationId, setLocationId] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  const load = useCallback(() =>
    Promise.all([
      api.get<RackTypeResponse[]>('/catalogue/rack-types'),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
      api.get(`/projects/${projectId}/locations`),
    ])
      .then(([c, s, l]) => {
        setCabinets(c)
        setSubmains(s)
        setLocations(asLocations(l))
        setLoadError(null)
        // A 12U wall cabinet is the common case in a house; anything else is one tap away.
        setRackTypeId(id => id || (c.find(x => x.heightU === 12) ?? c[0])?.id || '')
      })
      .catch((e: ApiError) => setLoadError(e.message)), [projectId])

  useEffect(() => { void load() }, [load])

  // A rack is something that then exists in the house: two taps in one tick must make one.
  const save = () => once(async () => {
    setSaving(true)
    setFieldErrors({})
    dismissStatus()
    try {
      const rack = await api.post<RackResponse>(`/projects/${projectId}/racks`, {
        name: name.trim(), rackTypeId, fedFromSubmainId: fedFrom || null, locationId: locationId || null,
      })
      announce(`${rack.name} added. Add what goes in it.`, 'ok')
      navigate(`/racks/${rack.id}`)
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) announce(error.message, 'error')
    } finally {
      setSaving(false)
    }
  })

  return (
    <AppShell
      title="New rack"
      back={{ to: `/projects/${projectId}`, label: 'House' }}
      actions={cabinets ? (
        <Button type="submit" form="new-rack" disabled={saving}>{saving ? 'Adding…' : 'Add rack'}</Button>
      ) : undefined}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}
      {!cabinets && !loadError && <Skeleton label="Loading cabinets" rows={3} rowClass="h-14" />}

      {cabinets && (
        <form id="new-rack" className="space-y-5" noValidate onSubmit={e => { e.preventDefault(); void save() }}>
          <p className="text-ink-2">A rack holds the switches and patch panels for part of the house. You'll add what goes in it next.</p>
          <Field
            label="Rack name"
            value={name}
            onChange={setName}
            errors={fieldErrors.name}
            placeholder="Comms cupboard"
            autoCapitalize="words"
            autoComplete="off"
            enterKeyHint="done"
          />
          <SelectField
            label="Cabinet"
            value={rackTypeId}
            onChange={setRackTypeId}
            errors={fieldErrors.rackTypeId}
            options={cabinets.map(c => ({ value: c.id, label: `${c.heightU}U · ${c.description}` }))}
          />
          <SelectField
            label="Fed from"
            hint="The automation panel this rack's supply comes from. It changes nothing on that panel."
            value={fedFrom}
            onChange={setFedFrom}
            errors={fieldErrors.fedFromSubmainId}
            options={[{ value: '', label: 'Not fed from an automation panel' }, ...submains.map(s => ({ value: s.id, label: s.name }))]}
          />
          <SelectField
            label="Location"
            hint="Where in the house this rack is. Add locations on the house page."
            value={locationId}
            onChange={setLocationId}
            errors={fieldErrors.locationId}
            options={locationOptions(locations, null)}
          />
        </form>
      )}
    </AppShell>
  )
}
