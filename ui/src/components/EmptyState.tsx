import type { ReactNode } from 'react'

interface Props {
  title: string
  /** One sentence on what to do about it. */
  hint?: string
  /** The thing to do about it, usually a ButtonLink or Button. */
  action?: ReactNode
}

/** An empty screen is an invitation to act, not a blank. */
export function EmptyState({ title, hint, action }: Props) {
  return (
    <div className="rounded-lg border border-dashed border-rule px-4 py-8 text-center">
      <svg aria-hidden="true" viewBox="0 0 48 24" className="mx-auto mb-3 h-6 w-12 stroke-ink-2" fill="none" strokeWidth={2}>
        <rect x="1" y="1" width="46" height="22" rx="2" />
        <path d="M1 12h46" strokeDasharray="3 3" />
      </svg>
      <p className="font-semibold">{title}</p>
      {hint && <p className="mt-1 text-ink-2">{hint}</p>}
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  )
}
