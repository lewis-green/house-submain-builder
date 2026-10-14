import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse, RackResponse, SubmainResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { ButtonLink } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'

/** A house is its submains and its network racks, listed side by side, each added the same way. */
export function ProjectPage() {
  const { projectId } = useParams()
  const [project, setProject] = useState<ProjectResponse | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[] | null>(null)
  const [racks, setRacks] = useState<RackResponse[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() =>
    Promise.all([
      api.get<ProjectResponse>(`/projects/${projectId}`),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
      api.get<RackResponse[]>(`/projects/${projectId}/racks`),
    ])
      .then(([p, s, r]) => { setProject(p); setSubmains(s); setRacks(r) })
      .catch((e: ApiError) => setError(e.message)), [projectId])

  useEffect(() => { void load() }, [load])

  const loaded = project !== null && submains !== null && racks !== null

  return (
    <AppShell
      title={project?.name ?? 'House'}
      subtitle={project?.address ?? undefined}
      back={{ to: '/', label: 'Houses' }}
      actions={loaded ? <ButtonLink to={`/projects/${projectId}/bom`} variant="ghost">Bill of materials</ButtonLink> : undefined}
    >
      {error && <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />}
      {!loaded && !error && <Skeleton label="Loading house" rows={3} rowClass="h-20" />}

      {loaded && (
        <>
          <section aria-labelledby="submains-heading">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 id="submains-heading" className="text-lg font-bold">Submains</h2>
              <ButtonLink to={`/projects/${projectId}/submains/new`} variant="primary">Add submain</ButtonLink>
            </div>
            {submains.length === 0 ? (
              <EmptyState
                title="No submains yet"
                hint="A submain is one panel. Add the first one and its panel is designed from the circuits it feeds."
              />
            ) : (
              <ul className="divide-y divide-rule border-y border-rule">
                {/* py-1 on each row keeps 8px between one tappable row and the next. */}
                {submains.map(s => (
                  <li key={s.id} className="py-1">
                    <Link to={`/submains/${s.id}/panel`} className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press">
                      {/* wrap-anywhere: a submain named by a long reference must break rather than spill off the page. */}
                      <span className="min-w-0 flex-1 wrap-anywhere">
                        <span className="block text-lg font-semibold">{s.name}</span>
                        <span className="block text-ink-2">
                          {s.deviceCount === 0 ? 'Not designed yet' : `${s.deviceCount} devices · ${s.circuitCount} circuits`}
                        </span>
                      </span>
                      <span aria-hidden="true" className="text-xl text-ink-2">›</span>
                    </Link>
                  </li>
                ))}
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
                {racks.map(r => (
                  <li key={r.id} className="py-1">
                    <Link to={`/racks/${r.id}`} className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press">
                      <span className="min-w-0 flex-1 wrap-anywhere">
                        <span className="block text-lg font-semibold">{r.name}</span>
                        <span className="block text-ink-2">{r.cabinet} · {r.usedU} of {r.heightU}U used</span>
                        {r.fedFromSubmainName && <span className="block text-ink-2">Fed from {r.fedFromSubmainName}</span>}
                      </span>
                      <span aria-hidden="true" className="text-xl text-ink-2">›</span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </AppShell>
  )
}
