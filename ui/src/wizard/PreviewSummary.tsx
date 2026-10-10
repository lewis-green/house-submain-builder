import type { DesignResponse, DeviceCategory } from '../api/types'
import { Diagnostics } from '../components/Diagnostics'
import { formatModuleCount } from '../units'

const kinds: { category: DeviceCategory; one: string; many: string }[] = [
  { category: 'Dimmer240', one: 'dimmer', many: 'dimmers' },
  { category: 'Dimmer0_10V', one: 'tape dimmer', many: 'tape dimmers' },
  { category: 'Relay', one: 'relay', many: 'relays' },
  { category: 'Cover', one: 'blinds controller', many: 'blinds controllers' },
  { category: 'LedController', one: 'LED controller', many: 'LED controllers' },
  { category: 'EnergyMeter', one: 'energy meter', many: 'energy meters' },
  { category: 'Network', one: 'network device', many: 'network devices' },
  { category: 'Isolator', one: 'isolator', many: 'isolators' },
  { category: 'Dc24VPositive', one: '+24V block', many: '+24V blocks' },
  { category: 'Dc24VNegative', one: '-24V block', many: '-24V blocks' },
  { category: 'Terminal240', one: 'terminal', many: 'terminals' },
]

interface Props {
  design: DesignResponse
  /** False when only fixtures are asked for: the panel fits, but Generate needs a circuit. */
  hasCircuits?: boolean
}

export function PreviewSummary({ design, hasCircuits = true }: Props) {
  const counts = kinds
    .map(k => ({ ...k, count: design.layout.devices.filter(d => d.category === k.category).length }))
    .filter(k => k.count > 0)

  const hasError = design.diagnostics.some(d => d.severity === 'Error')
  const { slotsUsed, totalSlots, rowsUsed } = design.summary

  return (
    <div className="space-y-3">
      <ul className="flex flex-wrap gap-2">
        {counts.map(k => (
          <li key={k.category} className="rounded-md border border-rule px-2.5 py-1 font-medium">
            {k.count} {k.count === 1 ? k.one : k.many}
          </li>
        ))}
      </ul>

      <p className="text-ink-2">
        Uses {formatModuleCount(slotsUsed)} of {formatModuleCount(totalSlots)} modules
        {' '}across {rowsUsed} {rowsUsed === 1 ? 'row' : 'rows'}.
      </p>

      {!hasError && design.layout.devices.length > 0 && (
        <p className="flex items-center gap-2 font-semibold text-ok">
          <span aria-hidden="true">✓</span>
          {/* The same sentence the Generate button gives, so the preview and the button agree. */}
          {hasCircuits ? 'Fits in this enclosure.' : 'Fits in this enclosure. Add at least one circuit to generate a panel.'}
        </p>
      )}

      <Diagnostics diagnostics={design.diagnostics} />
    </div>
  )
}
