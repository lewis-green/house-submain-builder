import { useState } from 'react'
import type { RackDeviceTypeResponse } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Sheet } from '../components/Sheet'
import { Skeleton } from '../components/Skeleton'
import { useSingleFlight } from '../components/useSingleFlight'

interface Props {
  catalogue: RackDeviceTypeResponse[] | null
  catalogueError: string | null
  onRetryCatalogue: () => void
  freeU: number
  /** Adds the item: resolves to null when it went in, or to the reason it did not. */
  onAdd: (rackDeviceTypeId: string) => Promise<string | null>
  onClose: () => void
}

function summary(type: RackDeviceTypeResponse) {
  const poe = type.portGroups.filter(g => g.poeTick).reduce((n, g) => n + g.to - g.from + 1, 0)
  return `${type.heightU}U · ${type.portCount} ports${type.kind === 'Switch' && poe ? ` (${poe} PoE)` : ''}`
}

/** What can go in a rack, one tap each. The server decides where it goes: the top free U. */
export function AddItemSheet({ catalogue, catalogueError, onRetryCatalogue, freeU, onAdd, onClose }: Props) {
  const once = useSingleFlight()
  const [busy, setBusy] = useState<string | null>(null)
  const [refusal, setRefusal] = useState<string | null>(null)

  // Accessories are bought with the item that needs them, never placed; an
  // item retired from the catalogue stays where it is but is not offered.
  const placeable = (catalogue ?? []).filter(t => t.active && t.kind !== 'Accessory')

  // Two taps in one tick must add one item, not two.
  const add = (id: string) => once(async () => {
    setBusy(id)
    setRefusal(null)
    try {
      setRefusal(await onAdd(id))
    } finally {
      setBusy(null)
    }
  })

  return (
    <Sheet title="Add to rack" subtitle={`${freeU}U free · it goes in the top free U`} onClose={onClose}>
      {catalogueError && <ErrorNote compact message={`Could not load the rack catalogue: ${catalogueError}`} onRetry={onRetryCatalogue} />}
      {!catalogue && !catalogueError && <Skeleton label="Loading the rack catalogue" rows={3} rowClass="h-14" />}
      {refusal && <div className="mb-3"><ErrorNote compact message={refusal} /></div>}

      <ul className="divide-y divide-rule">
        {placeable.map(type => (
          <li key={type.id} className="py-1">
            <Button
              variant="ghost"
              className="w-full justify-between text-left"
              disabled={busy !== null}
              onClick={() => void add(type.id)}
            >
              {/* flex-1, not justify-between: the button's own justify-center outranks a class added after it. */}
              <span className="min-w-0 flex-1 wrap-anywhere">
                <span className="block font-semibold">{type.model}</span>
                <span className="block text-sm font-normal text-ink-2">{summary(type)}</span>
              </span>
              <span aria-hidden="true" className="text-xl">{busy === type.id ? '…' : '+'}</span>
            </Button>
          </li>
        ))}
      </ul>
    </Sheet>
  )
}
