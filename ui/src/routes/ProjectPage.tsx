import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse, SubmainResponse } from '../api/types'
import { AppShell } from '../components/AppShell'
import { ButtonLink } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'

export function ProjectPage() {
  const { projectId } = useParams()
  const [project, setProject] = useState<ProjectResponse | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() =>
    Promise.all([
      api.get<ProjectResponse>(`/projects/${projectId}`),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
    ])
      .then(([p, s]) => { setProject(p); setSubmains(s) })
      .catch((e: ApiError) => setError(e.message)), [projectId])

  useEffect(() => { void load() }, [load])

  const loaded = project !== null && submains !== null

  return (
    <AppShell
      title={project?.name ?? 'House'}
      subtitle={project?.address ?? undefined}
      back={{ to: '/', label: 'Houses' }}
      actions={loaded ? (
        <>
          <ButtonLink to={`/projects/${projectId}/submains/new`} variant="primary">Add automation panel</ButtonLink>
          <ButtonLink to={`/projects/${projectId}/bom`} variant="ghost">Bill of materials</ButtonLink>
        </>
      ) : undefined}
    >
      {error && <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />}
      {!loaded && !error && <Skeleton label="Loading house" rows={2} rowClass="h-20" />}

      {submains && (submains.length === 0 ? (
        <EmptyState
          title="No automation panels yet"
          hint="Add the first automation panel and its layout is designed from the circuits it feeds."
        />
      ) : (
        <ul className="divide-y divide-rule border-y border-rule">
          {/* py-1 on each row keeps 8px between one tappable row and the next. */}
          {submains.map(s => (
            <li key={s.id} className="py-1">
              <Link
                to={`/submains/${s.id}/panel`}
                className="flex min-h-14 items-center gap-3 rounded-lg py-2 active:bg-press"
              >
                {/* wrap-anywhere: an automation panel named by a long reference must break rather than spill off the page. */}
                <span className="min-w-0 flex-1 wrap-anywhere">
                  <span className="block text-lg font-semibold">{s.name}</span>
                  <span className="block text-ink-2">
                    {s.deviceCount === 0
                      ? 'Not designed yet'
                      : `${s.deviceCount} devices · ${s.circuitCount} circuits`}
                  </span>
                </span>
                <span aria-hidden="true" className="text-xl text-ink-2">›</span>
              </Link>
            </li>
          ))}
        </ul>
      ))}
    </AppShell>
  )
}
