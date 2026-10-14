import { useCallback, useRef, useState } from 'react'
import type { RackItemResponse, RackLayoutResponse } from '../api/types'
import { ConfirmButton } from '../components/ConfirmButton'
import { SelectField } from '../components/SelectField'
import { Sheet } from '../components/Sheet'
import { frontPanel, poeCapable, poeTicked, portRows } from './ports'
import { freeTopUs } from './rackTargets'

interface Props {
  item: RackItemResponse
  layout: RackLayoutResponse
  /** Saves one port. Rejects when the server refuses it, having said why. */
  onSavePort: (number: number, label: string | null, poe: boolean) => Promise<void>
  onMove: (topU: number) => void
  onRemove: () => void
  onClose: () => void
}

/**
 * Where a rack item's ports are labelled. The rows do the work: one per port,
 * and Return saves and moves on to the next, so a 48-port switch is labelled
 * in one run with the keyboard up. The front panel above is the picture of
 * where each port is, and a way to jump to one.
 */
export function ItemSheet({ item, layout, onSavePort, onMove, onRemove, onClose }: Props) {
  const inputs = useRef(new Map<number, HTMLInputElement>())
  const rows = portRows(item)
  const capable = poeCapable(item)
  const labelled = item.ports.filter(p => p.label).length
  const positions = freeTopUs(layout, item)

  const register = useCallback((number: number, element: HTMLInputElement | null) => {
    if (element) inputs.current.set(number, element)
    else inputs.current.delete(number)
  }, [])

  const focusPort = (number: number) => inputs.current.get(number)?.focus()

  const focusAfter = (number: number) => {
    const next = rows.find(r => r.number > number)
    if (next) focusPort(next.number)
    else (document.activeElement as HTMLElement | null)?.blur()
  }

  const subtitle = [
    `U${item.topU}`,
    `${item.heightU}U`,
    capable > 0 ? `PoE ticked on ${poeTicked(item)} of ${capable} ports` : null,
    item.poeBudgetW ? `${item.poeBudgetW} W PoE budget` : null,
  ].filter(Boolean).join(' · ')

  const span = (top: number) => item.heightU === 1 ? `U${top}` : `U${top - item.heightU + 1}–U${top}`

  return (
    <Sheet title={`${item.label} · ${item.model}`} subtitle={subtitle} onClose={onClose}>
      {rows.length > 0 && <FrontPanel item={item} onPick={focusPort} />}

      {item.portGroups.map(group => (
        <section key={group.name} className="mt-4">
          <h3 className="font-semibold">
            {group.name} · ports {group.from}–{group.to}{group.poeTick ? '' : ' (no PoE)'}
          </h3>
          {!group.poeTick && <p className="text-sm text-ink-2">{group.name} ports carry data only, so there is no PoE tick.</p>}
          <ul>
            {rows.filter(r => r.group === group).map(row => (
              <PortRow
                key={row.number}
                number={row.number}
                poeOffered={group.poeTick}
                initialLabel={row.label}
                initialPoe={row.poe}
                register={register}
                onSave={(label, poe) => onSavePort(row.number, label, poe)}
                onNext={() => focusAfter(row.number)}
              />
            ))}
          </ul>
        </section>
      ))}

      <div className="mt-6 space-y-4 border-t border-rule pt-4">
        <SelectField
          label="Move to"
          value=""
          onChange={value => { if (value) onMove(Number(value)) }}
          options={[
            { value: '', label: `Stays in ${span(item.topU)}` },
            ...positions.map(u => ({ value: String(u), label: span(u) })),
          ]}
          hint={positions.length === 0 ? 'There is no free space to move it to.' : undefined}
        />
        <ConfirmButton
          label="Remove from rack"
          question={`Remove ${item.label}${labelled > 0 ? ` and its ${labelled} port ${labelled === 1 ? 'label' : 'labels'}` : ''}?`}
          confirmLabel="Yes, remove it"
          onConfirm={onRemove}
        />
      </div>
    </Sheet>
  )
}

/**
 * A picture, not a set of controls: the cells are smaller than a thumb, so
 * they are hidden from assistive tech and a tap only jumps to the port's row,
 * which is where the real controls are.
 */
function FrontPanel({ item, onPick }: { item: RackItemResponse; onPick: (number: number) => void }) {
  const { top, bottom, uplinks } = frontPanel(item)
  const stored = new Map(item.ports.map(p => [p.number, p]))

  const cell = (number: number) => {
    const port = stored.get(number)
    return (
      <span
        key={number}
        data-port={number}
        className={`relative flex h-6 min-w-6 items-center justify-center rounded-sm border text-[10px] font-semibold ${
          port?.label ? 'border-ink bg-ink text-paper' : 'border-ink-2 bg-sheet text-ink-2'}`}
      >
        {number}
        {port?.poe && <span className="absolute -top-1 -right-1 size-2 rounded-full bg-accent" />}
      </span>
    )
  }

  return (
    <div
      aria-hidden="true"
      data-testid="front-panel"
      className="flex gap-3 overflow-x-auto rounded-lg border border-rule bg-rail p-2"
      onClick={e => {
        const port = (e.target as HTMLElement).closest('[data-port]')?.getAttribute('data-port')
        if (port) onPick(Number(port))
      }}
    >
      <div className="space-y-1">
        <div className="flex gap-1">{top.map(cell)}</div>
        {bottom.length > 0 && <div className="flex gap-1">{bottom.map(cell)}</div>}
      </div>
      {uplinks.length > 0 && <div className="grid grid-flow-col grid-rows-2 gap-1">{uplinks.map(cell)}</div>}
    </div>
  )
}

function PortRow({ number, poeOffered, initialLabel, initialPoe, register, onSave, onNext }: {
  number: number
  poeOffered: boolean
  initialLabel: string
  initialPoe: boolean
  register: (number: number, element: HTMLInputElement | null) => void
  onSave: (label: string | null, poe: boolean) => Promise<void>
  onNext: () => void
}) {
  const [label, setLabel] = useState(initialLabel)
  const [poe, setPoe] = useState(initialPoe)

  // What the server has, recorded only once a save succeeds: a refused save
  // must leave the same text sendable again.
  const saved = useRef({ label: initialLabel.trim(), poe: initialPoe })
  // The save on its way, if any. The next one waits for it and compares
  // against what it stored, so a PoE tick straight after typing — which blurs
  // the box and saves the label first — sends the pair once more, with both.
  const inFlight = useRef<Promise<boolean> | null>(null)
  // Return saves and moves on; the blur that the move causes must not send
  // the pair a second time.
  const skipBlur = useRef(false)

  async function save(nextLabel: string, nextPoe: boolean): Promise<boolean> {
    if (inFlight.current) await inFlight.current

    const trimmed = nextLabel.trim()
    if (trimmed === saved.current.label && nextPoe === saved.current.poe) return true

    const attempt = onSave(trimmed || null, nextPoe).then(
      () => { saved.current = { label: trimmed, poe: nextPoe }; return true },
      () => false,
    )
    inFlight.current = attempt
    try {
      return await attempt
    } finally {
      if (inFlight.current === attempt) inFlight.current = null
    }
  }

  return (
    <li className="flex items-center gap-3 py-1">
      <label className="flex min-w-0 flex-1 items-center gap-3">
        <span className="w-8 shrink-0 text-right font-semibold tabular-nums">{number}</span>
        <input
          ref={element => register(number, element)}
          aria-label={`Port ${number} label`}
          value={label}
          maxLength={60}
          enterKeyHint="next"
          autoCapitalize="words"
          autoComplete="off"
          onChange={e => setLabel(e.target.value)}
          onBlur={() => {
            if (skipBlur.current) {
              skipBlur.current = false
              return
            }
            void save(label, poe)
          }}
          onKeyDown={e => {
            if (e.key !== 'Enter') return
            e.preventDefault()
            skipBlur.current = true
            void save(label, poe)
            onNext()
          }}
          className="min-h-11 w-full min-w-0 rounded-lg border border-rule bg-sheet px-3"
        />
      </label>
      {poeOffered && (
        <label className="flex min-h-11 shrink-0 items-center gap-2">
          <input
            type="checkbox"
            className="checkbox"
            aria-label={`Port ${number} PoE`}
            checked={poe}
            onChange={async e => {
              const next = e.target.checked
              setPoe(next)
              if (!(await save(label, next))) setPoe(!next)
            }}
          />
          <span aria-hidden="true">PoE</span>
        </label>
      )}
    </li>
  )
}
