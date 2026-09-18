import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ApiError, api } from '../api/client'
import type { ProjectResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Spinner } from '../components/Spinner'

export function ProjectsPage() {
  const [projects, setProjects] = useState<ProjectResponse[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)

  const load = () =>
    api.get<ProjectResponse[]>('/projects')
      .then(setProjects)
      .catch((e: ApiError) => setLoadError(e.message))

  useEffect(() => { void load() }, [])

  async function add() {
    setSaving(true)
    setFieldErrors({})
    try {
      await api.post<ProjectResponse>('/projects', { name, address: address || null, notes: null })
      setName('')
      setAddress('')
      await load()
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      if (Object.keys(error.fieldErrors).length === 0) setLoadError(error.message)
    } finally {
      setSaving(false)
    }
  }

  if (loadError) return <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />
  if (!projects) return <Spinner label="Loading houses" />

  return (
    <div className="space-y-6 p-4">
      <h1 className="text-xl font-semibold">Houses</h1>

      {projects.length === 0 ? (
        <p className="text-slate-500">No houses yet. Add one below to get started.</p>
      ) : (
        <ul className="space-y-2">
          {projects.map(p => (
            <li key={p.id}>
              <Link to={`/projects/${p.id}`} className="block rounded-lg border border-slate-200 p-4 active:bg-slate-50">
                <span className="font-medium">{p.name}</span>
                {p.address && <span className="block text-sm text-slate-500">{p.address}</span>}
                <span className="block text-sm text-slate-500">
                  {p.submainCount} {p.submainCount === 1 ? 'submain' : 'submains'}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      <div className="space-y-3 rounded-lg border border-slate-200 p-4">
        <h2 className="text-sm font-semibold text-slate-700">Add a house</h2>
        <Field label="House name" value={name} onChange={setName} errors={fieldErrors.name} />
        <Field label="Address" value={address} onChange={setAddress} />
        <Button onClick={add} disabled={saving}>Add house</Button>
      </div>
    </div>
  )
}
