import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { LocationResponse, ProjectResponse, RackResponse, SubmainResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button, ButtonLink } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'
import { LocationSheet } from '../locations/LocationSheet'
import { asLocations } from '../locations/options'

/** One automation panel as a row: its name and how far its design has got. */
function PanelRow({ submain: s, tagged }: { submain: SubmainResponse; tagged?: boolean }) {
  return (
    <li className="py-1">
      <Link to={`/panels/${s.id}`} className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press">
        {/* wrap-anywhere: an automation panel named by a long reference must break rather than spill off the page. */}
        <span className="min-w-0 flex-1 wrap-anywhere">
          {tagged && <Kind>Automation panel</Kind>}
          <span className="block text-lg font-semibold">{s.name}</span>
          <span className="block text-ink-2">
            {s.deviceCount === 0 ? 'Not designed yet' : `${s.deviceCount} devices · ${s.circuitCount} circuits`}
          </span>
        </span>
        <span aria-hidden="true" className="text-xl text-ink-2">›</span>
      </Link>
    </li>
  )
}

function RackRow({ rack: r, tagged }: { rack: RackResponse; tagged?: boolean }) {
  return (
    <li className="py-1">
      <Link to={`/racks/${r.id}`} className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press">
        <span className="min-w-0 flex-1 wrap-anywhere">
          {tagged && <Kind>Rack</Kind>}
          <span className="block text-lg font-semibold">{r.name}</span>
          <span className="block text-ink-2">{r.cabinet} · {r.usedU} of {r.heightU}U used</span>
          {r.fedFromSubmainName && <span className="block text-ink-2">Fed from {r.fedFromSubmainName}</span>}
        </span>
        <span aria-hidden="true" className="text-xl text-ink-2">›</span>
      </Link>
    </li>
  )
}

/** What a row is, said above its name, so a location listing panels and racks together reads at a glance. */
function Kind({ children }: { children: string }) {
  return <span className="block text-sm font-medium uppercase tracking-wide text-ink-2">{children}</span>
}

/**
 * A house is its automation panels and its network racks. Until it has
 * locations they are listed side by side, each added the same way; once it
 * has some, they are listed by where they are, in the order the locations
 * were added, with anything not yet placed last.
 */
export function ProjectPage() {
  const { projectId } = useParams()
  const [project, setProject] = useState<ProjectResponse | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[] | null>(null)
  const [racks, setRacks] = useState<RackResponse[] | null>(null)
  const [locations, setLocations] = useState<LocationResponse[] | null>(null)
  // The location being renamed or removed, or 'new' while one is being added.
  const [editing, setEditing] = useState<LocationResponse | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() =>
    Promise.all([
      api.get<ProjectResponse>(`/projects/${projectId}`),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
      api.get<RackResponse[]>(`/projects/${projectId}/racks`),
      api.get(`/projects/${projectId}/locations`),
    ])
      .then(([p, s, r, l]) => { setProject(p); setSubmains(s); setRacks(r); setLocations(asLocations(l)) })
      .catch((e: ApiError) => setError(e.message)), [projectId])

  useEffect(() => { void load() }, [load])

  const loaded = project !== null && submains !== null && racks !== null && locations !== null
  const unplacedPanels = submains?.filter(s => !locations?.some(l => l.id === s.locationId)) ?? []
  const unplacedRacks = racks?.filter(r => !locations?.some(l => l.id === r.locationId)) ?? []
  const addLocation = <Button variant="secondary" onClick={() => setEditing('new')}>Add location</Button>

  return (
    <AppShell
      title={project?.name ?? 'House'}
      subtitle={project?.address ?? undefined}
      back={{ to: '/', label: 'Houses' }}
      actions={loaded ? <ButtonLink to={`/projects/${projectId}/bom`} variant="ghost">Parts list</ButtonLink> : undefined}
    >
      {error && <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />}
      {!loaded && !error && <Skeleton label="Loading house" rows={3} rowClass="h-20" />}

      {loaded && locations.length > 0 && (
        <>
          <div className="mb-6 flex flex-wrap gap-2">
            <ButtonLink to={`/projects/${projectId}/panels/new`} variant="primary">Add automation panel</ButtonLink>
            <ButtonLink to={`/projects/${projectId}/racks/new`} variant="secondary">Add rack</ButtonLink>
            {addLocation}
          </div>

          {locations.map(location => {
            const here = submains.filter(s => s.locationId === location.id)
            const racked = racks.filter(r => r.locationId === location.id)
            return (
              <section key={location.id} aria-labelledby={`location-${location.id}`} className="mb-8">
                <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                  <h2 id={`location-${location.id}`} className="min-w-0 wrap-anywhere text-lg font-bold">{location.name}</h2>
                  <Button variant="ghost" aria-label={`Edit ${location.name}`} onClick={() => setEditing(location)}>
                    Edit
                  </Button>
                </div>
                {here.length + racked.length === 0 ? (
                  <p className="border-y border-rule py-3 text-ink-2">Nothing here yet</p>
                ) : (
                  <ul className="divide-y divide-rule border-y border-rule">
                    {here.map(s => <PanelRow key={s.id} submain={s} tagged />)}
                    {racked.map(r => <RackRow key={r.id} rack={r} tagged />)}
                  </ul>
                )}
              </section>
            )
          })}

          {unplacedPanels.length + unplacedRacks.length > 0 && (
            <section aria-labelledby="unplaced-heading" className="mb-8">
              <h2 id="unplaced-heading" className="mb-2 text-lg font-bold">Not placed</h2>
              <ul className="divide-y divide-rule border-y border-rule">
                {unplacedPanels.map(s => <PanelRow key={s.id} submain={s} tagged />)}
                {unplacedRacks.map(r => <RackRow key={r.id} rack={r} tagged />)}
              </ul>
            </section>
          )}
        </>
      )}

      {loaded && locations.length === 0 && (
        <>
          <section aria-labelledby="panels-heading">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 id="panels-heading" className="text-lg font-bold">Automation panels</h2>
              <ButtonLink to={`/projects/${projectId}/panels/new`} variant="primary">Add automation panel</ButtonLink>
            </div>
            {submains.length === 0 ? (
              <EmptyState
                title="No automation panels yet"
                hint="Add the first automation panel and its layout is designed from the circuits it feeds."
              />
            ) : (
              <ul className="divide-y divide-rule border-y border-rule">
                {/* py-1 on each row keeps 8px between one tappable row and the next. */}
                {submains.map(s => <PanelRow key={s.id} submain={s} />)}
              </ul>
            )}
          </section>

          <section aria-labelledby="racks-heading" className="mt-8">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 id="racks-heading" className="text-lg font-bold">Network racks</h2>
              <ButtonLink to={`/projects/${projectId}/racks/new`} variant="secondary">Add rack</ButtonLink>
            </div>
            {racks.length === 0 ? (
              <EmptyState
                title="No network racks yet"
                hint="A rack holds the switches and patch panels. Add one, fill it from the catalogue and label its ports."
              />
            ) : (
              <ul className="divide-y divide-rule border-y border-rule">
                {racks.map(r => <RackRow key={r.id} rack={r} />)}
              </ul>
            )}
          </section>

          <div className="mt-8 rounded-lg border border-rule bg-sheet p-4">
            <p className="text-ink-2">Say where the panels and racks are — a plant room, a garage — and the house lists them by place.</p>
            <div className="mt-3">{addLocation}</div>
          </div>
        </>
      )}

      {editing && projectId && (
        <LocationSheet
          projectId={projectId}
          location={editing === 'new' ? undefined : editing}
          onChanged={() => void load()}
          onClose={() => setEditing(null)}
        />
      )}
    </AppShell>
  )
}
