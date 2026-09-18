import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse, SubmainResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Spinner } from '../components/Spinner'

export function ProjectPage() {
  const { projectId } = useParams()
  const navigate = useNavigate()
  const [project, setProject] = useState<ProjectResponse | null>(null)
  const [submains, setSubmains] = useState<SubmainResponse[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = () =>
    Promise.all([
      api.get<ProjectResponse>(`/projects/${projectId}`),
      api.get<SubmainResponse[]>(`/projects/${projectId}/submains`),
    ])
      .then(([p, s]) => { setProject(p); setSubmains(s) })
      .catch((e: ApiError) => setError(e.message))

  useEffect(() => { void load() }, [projectId])

  if (error) return <ErrorNote message={error} onRetry={() => { setError(null); void load() }} />
  if (!project || !submains) return <Spinner label="Loading house" />

  return (
    <div className="space-y-6 p-4">
      <div>
        <Link to="/" className="text-sm text-slate-500">← Houses</Link>
        <h1 className="mt-1 text-xl font-semibold">{project.name}</h1>
        {project.address && <p className="text-sm text-slate-500">{project.address}</p>}
      </div>

      {submains.length === 0 ? (
        <p className="text-slate-500">No submains yet. Add the first one to design its panel.</p>
      ) : (
        <ul className="space-y-2">
          {submains.map(s => (
            <li key={s.id}>
              <Link
                to={`/submains/${s.id}/panel`}
                className="block rounded-lg border border-slate-200 p-4 active:bg-slate-50"
              >
                <span className="font-medium">{s.name}</span>
                <span className="block text-sm text-slate-500">
                  {s.deviceCount === 0
                    ? 'Not designed yet'
                    : `${s.deviceCount} devices · ${s.circuitCount} circuits`}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      <Button onClick={() => navigate(`/projects/${projectId}/submains/new`)}>Add submain</Button>
    </div>
  )
}
