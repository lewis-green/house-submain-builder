import { useEffect, useRef, useState } from 'react'
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

/** The server's wording for each circuit type. */
const TYPE_NAMES: Record<string, string> = {
  DimmedLighting: 'Dimmed lighting',
  Switched: 'Switched',
  LedTape: 'LED tape',
  Cover: 'Blinds',
  RgbwTape: 'Colour LED tape',
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
  const [refusal, setRefusal] = useState<string | null>(null)
  const once = useSingleFlight()
  // Which row's move buttons, by circuit: so focus can follow a row that has hit an end.
  const moveButtons = useRef(new Map<string, { up: HTMLButtonElement | null; down: HTMLButtonElement | null }>())
  const focusAfter = useRef<{ id: string; button: 'up' | 'down' } | null>(null)
  const changed = !sameAs(rows, circuits)

  // A move button that has just become disabled drops focus to the page. When
  // the row has reached an end, keep the person on that row instead.
  useEffect(() => {
    const target = focusAfter.current
    focusAfter.current = null
    if (target) moveButtons.current.get(target.id)?.[target.button]?.focus()
  }, [rows])

  const move = (index: number, by: -1 | 1) => {
    const arrival = index + by
    if (arrival === 0) focusAfter.current = { id: rows[index].circuitId, button: 'down' }
    else if (arrival === rows.length - 1) focusAfter.current = { id: rows[index].circuitId, button: 'up' }
    setRefusal(null)
    setRows(current => {
      const next = [...current]
      ;[next[index], next[arrival]] = [next[arrival], next[index]]
      return next
    })
  }

  const setCount = (index: number, terminalCount: number) => {
    setRefusal(null)
    setRows(current => current.map((c, i) => (i === index ? { ...c, terminalCount } : c)))
  }

  const setMoveButton = (id: string, which: 'up' | 'down', el: HTMLButtonElement | null) => {
    const entry = moveButtons.current.get(id) ?? { up: null, down: null }
    entry[which] = el
    moveButtons.current.set(id, entry)
  }

  const save = () => once(async () => {
    setSaving(true)
    setRefusal(null)
    try {
      await onSave(rows.map(({ circuitId, terminalCount }) => ({ circuitId, terminalCount })))
    } catch (e) {
      // The page announces it too, but that is behind this sheet: say why here,
      // and keep the edit so it can be tried again.
      setRefusal((e as Error).message)
    } finally {
      setSaving(false)
    }
  })

  return (
    <Sheet title="Terminal order" subtitle="C1 is the first terminal on the rail. Saving rebuilds the panel." onClose={() => { if (!saving) onClose() }}>
      <ol className="space-y-2">
        {rows.map((circuit, index) => (
          <li key={circuit.circuitId} className="rounded-lg border border-rule p-3">
            <div className="flex items-center gap-2">
              <span data-testid="terminal" className="min-w-16 shrink-0 font-mono font-semibold">
                {terminalLabel(index + 1, circuit.terminalCount)}
              </span>
              <span className="min-w-0 flex-1 break-words">
                <strong className="font-medium">{circuit.name}</strong>
                {circuit.room && <span className="text-ink-2"> · {circuit.room}</span>}
                <span className="text-ink-2"> · {TYPE_NAMES[circuit.type] ?? circuit.type}</span>
              </span>
              <Button ref={el => setMoveButton(circuit.circuitId, 'up', el)} variant="secondary" aria-label={`Move ${circuit.name} up`} disabled={index === 0 || saving} onClick={() => move(index, -1)}>↑</Button>
              <Button ref={el => setMoveButton(circuit.circuitId, 'down', el)} variant="secondary" aria-label={`Move ${circuit.name} down`} disabled={index === rows.length - 1 || saving} onClick={() => move(index, 1)}>↓</Button>
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
      {refusal && (
        <p role="alert" className="mt-4 rounded-lg border border-l-4 border-danger bg-danger-soft p-3 font-medium text-danger">{refusal}</p>
      )}
      <div className="mt-4 flex justify-end gap-2">
        <Button variant="ghost" disabled={saving} onClick={onClose}>Cancel</Button>
        <Button variant="primary" disabled={!changed || saving} onClick={() => void save()}>
          {saving ? 'Saving…' : 'Save order'}
        </Button>
      </div>
    </Sheet>
  )
}
