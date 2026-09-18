import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { BomView } from '../api/types'
import { ErrorNote } from '../components/ErrorNote'
import { Spinner } from '../components/Spinner'

export function BomPage() {
  const { projectId, submainId } = useParams()
  const scope = submainId ? `/submains/${submainId}` : `/projects/${projectId}`

  const [bom, setBom] = useState<BomView | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = () =>
    api.get<BomView>(`${scope}/bom`)
      .then(setBom)
      .catch((e: ApiError) =>
        setError(e.status === 404
          ? 'Nothing has been issued yet. Issue a panel to get a bill of materials.'
          : e.message))

  useEffect(() => { void load() }, [scope])

  if (error) return <ErrorNote message={error} />
  if (!bom) return <Spinner label="Loading bill of materials" />

  return (
    <div className="space-y-4 p-4">
      <Link
        to={submainId ? `/submains/${submainId}/panel` : `/projects/${projectId}`}
        className="text-sm text-slate-500"
      >
        ← Back
      </Link>
      <h1 className="text-xl font-semibold">Bill of materials</h1>

      {bom.lines.length === 0 && (
        <p className="text-slate-500">Nothing issued yet, so there is nothing to order.</p>
      )}

      <ul className="space-y-2">
        {bom.lines.map(line => (
          <li key={line.catalogueId} className="rounded-lg border border-slate-200 p-3">
            <div className="flex items-baseline justify-between gap-3">
              <span className="font-medium">{line.description}</span>
              <span className="shrink-0 text-sm tabular-nums">× {line.quantity}</span>
            </div>
            <div className="flex items-baseline justify-between gap-3 text-sm text-slate-500">
              <span>{line.partNumber}</span>
              {!line.panelMounted && <span className="shrink-0">external</span>}
            </div>
          </li>
        ))}
      </ul>

      <a
        href={`/api${scope}/bom.csv`}
        className="inline-flex min-h-11 items-center rounded-lg border border-slate-300 px-4 text-sm font-medium"
      >
        Download CSV
      </a>
    </div>
  )
}
