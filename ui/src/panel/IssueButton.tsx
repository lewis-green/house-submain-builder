import { useEffect, useState } from 'react'
import { ApiError, api } from '../api/client'
import type { RevisionView } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { buttonClass } from '../components/buttonClass'
import { announce } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'

interface Props {
  submainId: string
  /** The layout as drawn right now; a change here means the list is asked again. */
  layoutVersion: number
  /**
   * Bumped by the panel after every save that could change what the PDF
   * prints — a circuit name, a room, a freed channel, a rebuild. Positions bump
   * layoutVersion; these do not, so without it a renamed circuit would leave
   * the old PDF offered as current.
   */
  syncKey?: number
}

/**
 * Issues a revision, then offers its PDF as a plain link. The PDF always
 * renders from what was issued. It is a link and not a window.open after the
 * request: iOS Safari only honours window.open inside the tap itself, and
 * blocks it silently once a network round-trip has passed — which issued the
 * revision and showed nothing. A link the user taps cannot be blocked, and in
 * a Home Screen app it hands the PDF to Safari, which can save and share it.
 *
 * Whether the latest PDF is still current is the server's verdict (`current`
 * on each revision): it compares what the revision embeds with what issuing
 * now would embed, so the UI never has to guess which edits a drawing or
 * schedule would show. The server also numbers the revisions, so "revision 3"
 * means the same thing here, in the status line and on the paper.
 */
export function IssueButton({ submainId, layoutVersion, syncKey = 0 }: Props) {
  const [busy, setBusy] = useState(false)
  const once = useSingleFlight()
  // null until the server has answered once: not knowing what is issued is
  // not the same as nothing being issued.
  const [revisions, setRevisions] = useState<RevisionView[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let cancelled = false
    api.get<RevisionView[]>(`/submains/${submainId}/revisions`)
      .then(list => {
        if (cancelled) return
        setRevisions(Array.isArray(list) ? list : [])
        setListError(null)
      })
      // The last good list stays on offer; what is lost is knowing whether it is still current.
      .catch((e: ApiError) => { if (!cancelled) setListError(e.message) })
    return () => { cancelled = true }
  }, [submainId, layoutVersion, syncKey, attempt])

  const latest = (revisions ?? []).reduce<RevisionView | null>(
    (best, r) => (!best || r.number > best.number ? r : best), null)
  const stale = latest !== null && !latest.current

  // A revision is numbered and kept, so two taps in one tick must issue one: `once` drops the second.
  const issue = () => once(async () => {
    setBusy(true)
    try {
      const revision = await api.post<RevisionView>(`/submains/${submainId}/revisions`)
      // Issued from the submain as it stands, so current — whatever the list said before.
      setRevisions(list => [...(list ?? []).map(r => ({ ...r, current: false })), { ...revision, current: true }])
      setListError(null)
      announce(`Revision ${revision.number} issued. Open the PDF below.`, 'ok')
    } catch (e) {
      announce((e as ApiError).message, 'error')
    } finally {
      setBusy(false)
    }
  })

  const retry = () => { setListError(null); setAttempt(n => n + 1) }

  // Nothing is known about what was issued: offering a first "Issue PDF
  // revision" here would read as "none yet", and a tap would duplicate one.
  if (listError && revisions === null) {
    return <ErrorNote compact message={`Could not check the issued PDFs: ${listError}`} onRetry={retry} />
  }

  if (!latest) {
    return (
      <Button onClick={issue} disabled={busy}>
        {busy ? 'Issuing…' : 'Issue PDF revision'}
      </Button>
    )
  }

  return (
    <>
      {/* One row, however narrow the phone: the pair share it and wrap their own words. */}
      <div className="grid w-full grid-cols-2 gap-3">
        <a
          href={`/api/revisions/${latest.id}/export/pdf`}
          target="_blank"
          rel="noopener"
          className={buttonClass(stale ? 'secondary' : 'primary', 'text-center')}
        >
          Open PDF (revision {latest.number})
        </a>
        <Button variant={stale ? 'primary' : 'secondary'} onClick={issue} disabled={busy} className="text-center">
          {busy ? 'Issuing…' : 'Issue new revision'}
        </Button>
      </div>
      {listError
        ? <ErrorNote compact message={`Could not check whether that PDF is still current: ${listError}`} onRetry={retry} />
        : stale && <p className="w-full text-sm text-ink-2">The panel has changed since that PDF was issued.</p>}
    </>
  )
}
