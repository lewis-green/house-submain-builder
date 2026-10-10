import { dismissStatus, useStatus, type Tone } from './status'

const tones: Record<Tone, string> = {
  info: 'border-rule bg-sheet text-ink',
  ok: 'border-ok bg-ok-soft text-ok',
  error: 'border-danger bg-danger-soft text-danger',
}

const marks: Record<Tone, string> = { info: 'i', ok: '✓', error: '!' }

/**
 * Always in the DOM, so VoiceOver announces every change — a region that
 * appears with its first message is often missed. The shell places it in the
 * sticky block above the action bar, where a thumb can dismiss it without it
 * ever covering the primary button.
 */
export function StatusRegion() {
  const status = useStatus()

  return (
    <div role="status" aria-live="polite">
      {status && (
        <div
          key={status.id}
          className={`mb-3 flex items-center gap-3 rounded-lg border px-3 py-2 shadow-lg ${tones[status.tone]}`}
        >
          <span
            aria-hidden="true"
            className="flex size-6 shrink-0 items-center justify-center rounded-full border border-current text-sm font-bold"
          >
            {marks[status.tone]}
          </span>
          <span className="flex-1 font-medium">{status.message}</span>
          <button
            type="button"
            onClick={dismissStatus}
            aria-label="Dismiss message"
            className="-mr-2 flex size-11 shrink-0 items-center justify-center rounded-lg text-xl active:bg-press"
          >
            ×
          </button>
        </div>
      )}
    </div>
  )
}
