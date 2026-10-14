import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { BomView } from '../api/types'
import { AppShell } from '../components/AppShell'
import { ButtonLink } from '../components/Button'
import { buttonClass } from '../components/buttonClass'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'

export function BomPage() {
  const { projectId, submainId, rackId } = useParams()
  const scope = rackId ? `/racks/${rackId}` : submainId ? `/submains/${submainId}` : `/projects/${projectId}`
  const backTo = rackId ? `/racks/${rackId}` : submainId ? `/submains/${submainId}/panel` : `/projects/${projectId}`
  const owner = rackId ? 'rack' : submainId ? 'panel' : null

  const [bom, setBom] = useState<BomView | null>(null)
  const [notIssued, setNotIssued] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() =>
    api.get<BomView>(`${scope}/bom`)
      .then(setBom)
      // A 404 is the ordinary state of a panel nobody has issued yet, not a fault.
      .catch((e: ApiError) => e.status === 404 ? setNotIssued(true) : setError(e.message)), [scope])

  useEffect(() => { void load() }, [load])

  return (
    <AppShell
      title="Bill of materials"
      subtitle={owner === 'rack' ? 'For this rack' : owner === 'panel' ? 'For this submain' : 'For the whole house, from each issued PDF'}
      back={{ to: backTo, label: owner === 'rack' ? 'Rack' : owner === 'panel' ? 'Panel' : 'House' }}
      actions={bom && bom.lines.length > 0 ? (
        <a href={`/api${scope}/bom.csv`} className={buttonClass('primary')}>Download CSV</a>
      ) : undefined}
    >
      {error && <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />}
      {!bom && !notIssued && !error && <Skeleton label="Loading bill of materials" rows={4} rowClass="h-16" />}

      {notIssued && (
        <EmptyState
          title="Nothing has been issued yet"
          hint="A bill of materials comes from an issued PDF revision, so it lists what was actually drawn."
          action={<ButtonLink to={backTo} variant="primary">{owner ? `Go to the ${owner}` : 'Back to the house'}</ButtonLink>}
        />
      )}

      {bom && bom.lines.length === 0 && (
        <EmptyState title="Nothing to order" hint={owner ? `The issued ${owner} has no parts in it.` : 'The issued drawings have no parts in them.'} />
      )}

      {bom && bom.lines.length > 0 && (
        <>
          <p className="mb-3 text-sm text-ink-2">Part numbers marked assumed need checking against a datasheet before ordering.</p>
          <ul className="divide-y divide-rule border-y border-rule">
            {bom.lines.map(line => (
              <li key={line.catalogueId} className="flex items-start gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <p className="font-semibold">{line.description}</p>
                  <p className="text-ink-2">
                    {line.partNumber}
                    {!line.panelMounted && <span className="ml-2 rounded border border-rule px-1.5 text-sm">external</span>}
                  </p>
                </div>
                <span className="shrink-0 text-lg font-semibold">× {line.quantity}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </AppShell>
  )
}
