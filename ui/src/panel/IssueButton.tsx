import { useState } from 'react'
import { ApiError, api } from '../api/client'
import type { RevisionView } from '../api/types'
import { Button } from '../components/Button'

interface Props {
  submainId: string
  onIssued?: () => void
}

/** Issues a revision, then opens its PDF. The PDF always renders from what was issued. */
export function IssueButton({ submainId, onIssued }: Props) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function issue() {
    setBusy(true)
    setError(null)
    try {
      const revision = await api.post<RevisionView>(`/submains/${submainId}/revisions`)
      onIssued?.()
      window.open(`/api/revisions/${revision.id}/export/pdf`, '_blank', 'noopener')
    } catch (e) {
      setError((e as ApiError).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Button onClick={issue} disabled={busy}>
        {busy ? 'Issuing…' : 'Issue & download PDF'}
      </Button>
      {error && <p role="alert" className="mt-2 text-sm text-red-600">{error}</p>}
    </div>
  )
}
