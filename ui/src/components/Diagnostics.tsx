import type { DiagnosticResponse, Severity } from '../api/types'

const rank: Record<Severity, number> = { Error: 0, Warning: 1, Info: 2 }

const styles: Record<Severity, string> = {
  Error: 'border-red-200 bg-red-50 text-red-800',
  Warning: 'border-amber-200 bg-amber-50 text-amber-900',
  Info: 'border-slate-200 bg-slate-50 text-slate-700',
}

export function Diagnostics({ diagnostics }: { diagnostics: DiagnosticResponse[] }) {
  if (diagnostics.length === 0) return null

  const sorted = [...diagnostics].sort((a, b) => rank[a.severity] - rank[b.severity])

  return (
    <ul className="space-y-2">
      {sorted.map((d, i) => (
        <li key={`${d.code}-${i}`} className={`rounded-lg border p-3 text-sm ${styles[d.severity]}`}>
          <p>{d.message}</p>
          {d.suggestion && <p className="mt-1 opacity-80">{d.suggestion}</p>}
        </li>
      ))}
    </ul>
  )
}
