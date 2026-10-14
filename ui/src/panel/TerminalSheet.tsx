import { useState } from 'react'
import { MAX_TERMINALS_PER_CIRCUIT, type TerminalCircuit } from '../api/types'
import { Button } from '../components/Button'
import { Sheet } from '../components/Sheet'
import { Stepper } from '../components/Stepper'
import { useSingleFlight } from '../components/useSingleFlight'
import { terminalLabel } from './terminalLabels'

interface Props {
  circuits: TerminalCircuit[]
  /** Rejects when the save was refused; the sheet stays open with the edit intact. */
  onSave: (circuits: { circuitId: string; terminalCount: number }[]) => Promise<void>
  onClose: () => void
}

const sameAs = (a: TerminalCircuit[], b: TerminalCircuit[]) =>
  a.length === b.length && a.every((c, i) => c.circuitId === b[i].circuitId && c.terminalCount === b[i].terminalCount)

/**
 * The order circuits land along the terminal rail, and how many terminals each
 * lands on. Up and down buttons rather than drag: a terminal is a third of a
 * module, too small to pick up under a thumb, and a list reorders the same way
 * on every screen. Numbers follow place, so C1 is always the first terminal.
 */
export function TerminalSheet({ circuits, onSave, onClose }: Props) {
  const [rows, setRows] = useState(circuits)
  const [saving, setSaving] = useState(false)
  const once = useSingleFlight()
  const changed = !sameAs(rows, circuits)

  const move = (index: number, by: -1 | 1) => setRows(current => {
    const next = [...current]
    ;[next[index], next[index + by]] = [next[index + by], next[index]]
    return next
  })

  const setCount = (index: number, terminalCount: number) =>
    setRows(current => current.map((c, i) => (i === index ? { ...c, terminalCount } : c)))

  const save = () => once(async () => {
    setSaving(true)
    try {
      await onSave(rows.map(({ circuitId, terminalCount }) => ({ circuitId, terminalCount })))
    } catch {
      // The page has said why; keep the edit so it can be tried again.
    } finally {
      setSaving(false)
    }
  })

  return (
    <Sheet title="Terminal order" subtitle="C1 is the first terminal on the rail. Saving rebuilds the panel." onClose={onClose}>
      <ol className="space-y-2">
        {rows.map((circuit, index) => (
          <li key={circuit.circuitId} className="rounded-lg border border-rule p-3">
            <div className="flex items-center gap-2">
              <span data-testid="terminal" className="min-w-16 shrink-0 font-mono font-semibold">
                {terminalLabel(index + 1, circuit.terminalCount)}
              </span>
              <span className="min-w-0 flex-1 truncate">
                <strong className="font-medium">{circuit.name}</strong>
                {circuit.room && <span className="text-ink-2"> · {circuit.room}</span>}
              </span>
              <Button variant="secondary" aria-label={`Move ${circuit.name} up`} disabled={index === 0 || saving} onClick={() => move(index, -1)}>↑</Button>
              <Button variant="secondary" aria-label={`Move ${circuit.name} down`} disabled={index === rows.length - 1 || saving} onClick={() => move(index, 1)}>↓</Button>
            </div>
            <div className="mt-2">
              <Stepper
                label={`Cables for ${circuit.name}`}
                hint="Above one, the terminals are bridged on the line."
                value={circuit.terminalCount}
                min={1}
                max={MAX_TERMINALS_PER_CIRCUIT}
                disabled={saving}
                onChange={value => setCount(index, value)}
              />
            </div>
          </li>
        ))}
      </ol>
      <div className="mt-4 flex justify-end gap-2">
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="primary" disabled={!changed || saving} onClick={() => void save()}>
          {saving ? 'Saving…' : 'Save order'}
        </Button>
      </div>
    </Sheet>
  )
}
