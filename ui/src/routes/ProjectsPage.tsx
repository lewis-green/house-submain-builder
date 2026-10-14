import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Skeleton } from '../components/Skeleton'
import { announce, dismissStatus } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'
import { APP_NAME } from '../useTitle'

export function ProjectsPage() {
  const [projects, setProjects] = useState<ProjectResponse[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  // What the server refused that neither field can show, said by the button.
  const [addError, setAddError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const once = useSingleFlight()

  const load = () =>
    api.get<ProjectResponse[]>('/projects')
      .then(setProjects)
      .catch((e: ApiError) => setLoadError(e.message))

  useEffect(() => { void load() }, [])

  // Two activations in one tick would add two houses; `once` drops the second.
  const add = () => once(async () => {
    setSaving(true)
    setFieldErrors({})
    setAddError(null)
    // The last attempt's reason must not sit beside this one's.
    dismissStatus()
    try {
      await api.post<ProjectResponse>('/projects', { name, address: address || null, notes: null })
      announce(`Added ${name.trim()}`, 'ok')
      setName('')
      setAddress('')
      await load()
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      // The typed name and address stay put: a failure is a reason to retry,
      // not to start again. Only the name shows its own errors; whatever else
      // was refused is said at the form's level so it is never swallowed. Said
      // once: the note is an alert, so VoiceOver hears it without a status
      // toast repeating the sentence above it; the status region speaks only
      // when the server gave no field to pin the refusal to.
      const elsewhere = error.errorsOutside(['name'])
      if (elsewhere.length > 0) setAddError(elsewhere.join(' '))
      else if (!error.fieldErrors.name) announce(`Not added: ${error.message}`, 'error')
    } finally {
      setSaving(false)
    }
  })

  return (
    <AppShell
      title="Houses"
      subtitle={APP_NAME}
      nav={(
        <Link to="/catalogue" className="inline-flex min-h-11 min-w-0 items-center rounded-lg px-3 text-right font-medium text-ink-2 active:bg-press">
          Catalogue
        </Link>
      )}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}
      {!projects && !loadError && <Skeleton label="Loading houses" rows={3} rowClass="h-20" />}

      {projects && (projects.length === 0 ? (
        <EmptyState
          title="No houses yet"
          hint="Add one below. Each house holds the automation panels you design."
        />
      ) : (
        <ul className="divide-y divide-rule border-y border-rule">
          {/* py-1 on each row keeps 8px between one tappable row and the next. */}
          {projects.map(p => (
            <li key={p.id} className="py-1">
              {/* The name column claims two fifths of the row before the count has to make
                  way (flex-1's zero basis would let the count squeeze it to a letter a line);
                  when the count cannot fit beside that — 200% text on a 320px phone — the row
                  wraps and the count drops under the name. wrap-anywhere: a reference with no
                  spaces breaks rather than spilling off the page. */}
              <Link
                to={`/projects/${p.id}`}
                className="flex min-h-14 flex-wrap items-center gap-3 rounded-lg py-2 active:bg-press"
              >
                <span className="min-w-0 grow basis-2/5 wrap-anywhere">
                  <span className="block text-lg font-semibold">{p.name}</span>
                  {p.address && <span className="block text-ink-2">{p.address}</span>}
                </span>
                <span className="ml-auto flex shrink-0 items-center gap-3 text-ink-2">
                  <span className="text-right">
                    {p.submainCount} {p.submainCount === 1 ? 'automation panel' : 'automation panels'}
                  </span>
                  <span aria-hidden="true" className="text-xl">›</span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      ))}

      {projects && (
        <form
          className="mt-8 space-y-3 rounded-lg border border-rule bg-sheet p-4"
          onSubmit={e => { e.preventDefault(); void add() }}
        >
          <h2 className="font-semibold">Add a house</h2>
          <Field
            label="House name"
            value={name}
            onChange={setName}
            errors={fieldErrors.name}
            placeholder="14 Willow Lane"
            autoCapitalize="words"
            autoComplete="off"
            enterKeyHint="next"
          />
          <Field
            label="Address"
            value={address}
            onChange={setAddress}
            placeholder="Optional"
            autoCapitalize="words"
            autoComplete="street-address"
            enterKeyHint="go"
          />
          {addError && <ErrorNote compact message={`Not added: ${addError}`} />}
          <Button type="submit" disabled={saving}>{saving ? 'Adding…' : 'Add house'}</Button>
        </form>
      )}
    </AppShell>
  )
}
