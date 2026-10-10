import type { DiagnosticResponse, Severity } from '../api/types'

const rank: Record<Severity, number> = { Error: 0, Warning: 1, Info: 2 }

const styles: Record<Severity, string> = {
  Error: 'border-danger bg-danger-soft text-danger',
  Warning: 'border-warn bg-warn-soft text-warn',
  Info: 'border-rule bg-sheet text-ink',
}

/** Said in words as well as colour, so severity survives a glance in sunlight and a screen reader. */
const words: Record<Severity, string> = { Error: 'Error', Warning: 'Warning', Info: 'Note' }

export function Diagnostics({ diagnostics }: { diagnostics: DiagnosticResponse[] }) {
  if (diagnostics.length === 0) return null

  const sorted = [...diagnostics].sort((a, b) => rank[a.severity] - rank[b.severity])

  return (
    <ul className="space-y-2">
      {sorted.map((d, i) => (
        <li key={`${d.code}-${i}`} className={`rounded-lg border-l-4 border p-3 ${styles[d.severity]}`}>
          <p><span className="font-bold">{words[d.severity]}:</span> {d.message}</p>
          {d.suggestion && <p className="mt-1 text-sm">{d.suggestion}</p>}
        </li>
      ))}
    </ul>
  )
}
