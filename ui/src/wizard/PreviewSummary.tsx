import type { DesignResponse, DeviceCategory } from '../api/types'
import { Diagnostics } from '../components/Diagnostics'
import { formatModules } from '../units'

const kinds: { category: DeviceCategory; one: string; many: string }[] = [
  { category: 'Dimmer240', one: 'dimmer', many: 'dimmers' },
  { category: 'Dimmer0_10V', one: 'tape dimmer', many: 'tape dimmers' },
  { category: 'Relay', one: 'relay', many: 'relays' },
  { category: 'Psu24V', one: 'PSU', many: 'PSUs' },
  { category: 'Terminal240', one: 'terminal', many: 'terminals' },
]

export function PreviewSummary({ design }: { design: DesignResponse }) {
  const counts = kinds
    .map(k => ({ ...k, count: design.layout.devices.filter(d => d.category === k.category).length }))
    .filter(k => k.count > 0)

  const hasError = design.diagnostics.some(d => d.severity === 'Error')

  return (
    <div className="space-y-3">
      <ul className="flex flex-wrap gap-2">
        {counts.map(k => (
          <li key={k.category} className="rounded-full bg-slate-100 px-3 py-1 text-sm">
            {k.count} {k.count === 1 ? k.one : k.many}
          </li>
        ))}
      </ul>

      <p className="text-sm text-slate-600">
        Uses {formatModules(design.summary.slotsUsed)} of {formatModules(design.summary.totalSlots)}
        {' '}across {design.summary.rowsUsed} {design.summary.rowsUsed === 1 ? 'row' : 'rows'}.
      </p>

      {!hasError && design.layout.devices.length > 0 && (
        <p className="text-sm font-medium text-green-700">Fits in this enclosure.</p>
      )}

      <Diagnostics diagnostics={design.diagnostics} />
    </div>
  )
}
