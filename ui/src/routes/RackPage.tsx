import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type {
  LocationResponse, RackDeviceTypeResponse, RackLayoutResponse, RackPortResponse, RackResponse, RackTypeResponse,
  SubmainResponse,
} from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'
import { announce } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'
import { IssueButton } from '../panel/IssueButton'
import { AddItemSheet } from '../racks/AddItemSheet'
import { ItemSheet } from '../racks/ItemSheet'
import { RackDrawing } from '../racks/RackDrawing'
import { RackSettings } from '../racks/RackSettings'
import { drawingSize } from '../racks/rackGeometry'
import { useRackDrag } from '../racks/useRackDrag'
import { asLocations, locationName } from '../locations'

const EMPTY_LAYOUT: RackLayoutResponse = { rackId: '', heightU: 0, freeU: 0, layoutVersion: 0, items: [] }

/**
 * A rack: the drawing first, the actions within thumb reach, the settings
 * folded away below. Like the panel screen, it draws what the server decided
 * and sends every change back for the server to decide again.
 */
export function RackPage() {
  const { rackId } = useParams()
  const [rack, setRack] = useState<RackResponse | null>(null)
  const [layout, setLayout] = useState<RackLayoutResponse | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [cabinets, setCabinets] = useState<RackTypeResponse[]>([])
  const [submains, setSubmains] = useState<SubmainResponse[]>([])
  const [locations, setLocations] = useState<LocationResponse[]>([])
  const [catalogue, setCatalogue] = useState<RackDeviceTypeResponse[] | null>(null)
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  // Bumped by every port save and settings save, so the PDF's currency is
  // asked again: a label, a rename, a cabinet or a feed change moves nothing,
  // so the layout version alone would never notice them.
  const [syncKey, setSyncKey] = useState(0)
  const drawing = useRef<HTMLDivElement | null>(null)
  const once = useSingleFlight()

  const load = useCallback(() =>
    Promise.all([
      api.get<RackResponse>(`/racks/${rackId}`),
      api.get<RackLayoutResponse>(`/racks/${rackId}/layout`),
    ])
      .then(([r, l]) => {
        setRack(r)
        setLayout(l)
        setLoadError(null)
        // The settings need the cabinets and the house's submains. A failure
        // costs the settings form its choices, not the rack.
        api.get<RackTypeResponse[]>('/catalogue/rack-types').then(setCabinets).catch(() => setCabinets([]))
        api.get<SubmainResponse[]>(`/projects/${r.projectId}/submains`).then(setSubmains).catch(() => setSubmains([]))
        api.get(`/projects/${r.projectId}/locations`).then(l => setLocations(asLocations(l))).catch(() => setLocations([]))
      })
      .catch((e: ApiError) => setLoadError(e.message)), [rackId])

  // On its own, with its own retry: an empty "Add to rack" with no word of why reads as an empty catalogue.
  const loadCatalogue = useCallback(() =>
    api.get<RackDeviceTypeResponse[]>('/catalogue/rack-device-types')
      .then(list => { setCatalogue(list); setCatalogueError(null) })
      .catch((e: ApiError) => setCatalogueError(e.message)), [])

  useEffect(() => { void load() }, [load])
  useEffect(() => { void loadCatalogue() }, [loadCatalogue])

  /** The drawing is scaled to the screen's width; this undoes that scale. */
  const toPoint = useCallback((clientX: number, clientY: number) => {
    const box = drawing.current?.querySelector('svg')?.getBoundingClientRect()
    if (!box || !layout || box.width === 0) return { x: clientX, y: clientY }
    const scale = drawingSize(layout.heightU).width / box.width
    return { x: (clientX - box.left) * scale, y: (clientY - box.top) * scale }
  }, [layout])

  const commitMove = useCallback(async (itemId: string, topU: number) => {
    if (!layout) return
    const previous = layout

    // Optimistic: the one item moves; nothing is re-laid out here.
    setLayout({
      ...layout,
      items: layout.items.map(i => (i.id === itemId ? { ...i, topU } : i)).sort((a, b) => b.topU - a.topU),
    })

    try {
      const result = await api.patch<{ layoutVersion: number }>(`/rack-items/${itemId}/position`, {
        topU, basedOnLayoutVersion: previous.layoutVersion,
      })
      setLayout(l => (l ? { ...l, layoutVersion: result.layoutVersion } : l))
      announce(`Moved to U${topU}`, 'ok')
    } catch (e) {
      const error = e as ApiError
      setLayout(previous)
      if (error.conflict) {
        announce('This rack changed elsewhere — reloading')
        await load()
      } else {
        announce(`Not moved: ${error.message}`, 'error')
      }
    }
  }, [layout, load])

  const { dragging, bind } = useRackDrag({
    layout: layout ?? EMPTY_LAYOUT,
    onCommit: (id, topU) => { void commitMove(id, topU) },
    toPoint,
  })

  // Stable, so neither sheet is handed a new function on every re-render.
  const closeItem = useCallback(() => setSelectedId(null), [])
  const closeAdd = useCallback(() => setAdding(false), [])

  async function addItem(rackDeviceTypeId: string): Promise<string | null> {
    try {
      const next = await api.post<RackLayoutResponse>(`/racks/${rackId}/items`, { rackDeviceTypeId })
      const added = next.items.find(i => !layout?.items.some(old => old.id === i.id))
      setLayout(next)
      setAdding(false)
      announce(added ? `${added.label} added in U${added.topU}` : 'Added to the rack', 'ok')
      return null
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        await load()
        return 'This rack changed elsewhere. It has been reloaded — try again.'
      }
      return error.message
    }
  }

  async function savePort(itemId: string, number: number, label: string | null, poe: boolean) {
    try {
      const saved = await api.put<RackPortResponse>(`/rack-items/${itemId}/ports/${number}`, { label, poe })
      setLayout(l => l && {
        ...l,
        items: l.items.map(i => i.id !== itemId ? i : {
          ...i,
          ports: [...i.ports.filter(p => p.number !== number), ...(saved.label || saved.poe ? [saved] : [])]
            .sort((a, b) => a.number - b.number),
        }),
      })
      setSyncKey(n => n + 1)
    } catch (e) {
      const error = e as ApiError
      announce(`Port ${number} not saved: ${error.fieldErrors?.label?.[0] ?? error.fieldErrors?.poe?.[0] ?? error.message}`, 'error')
      throw e
    }
  }

  // A second "Yes, remove it" in the same tick would carry a stale version and
  // come back as a conflict; `once` drops it before it is sent.
  const removeItem = (itemId: string) => once(async () => {
    if (!layout) return
    try {
      const next = await api.del<RackLayoutResponse>(`/rack-items/${itemId}?basedOnLayoutVersion=${layout.layoutVersion}`)
      setSelectedId(null)
      setLayout(next)
      announce('Removed from the rack', 'ok')
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        announce('This rack changed elsewhere — reloading')
        await load()
      } else {
        announce(`Not removed: ${error.message}`, 'error')
      }
    }
  })

  const selected = layout?.items.find(i => i.id === selectedId) ?? null
  const subtitle = rack
    ? `${rack.cabinet} · ${rack.fedFromSubmainName ? `fed from ${rack.fedFromSubmainName}` : 'not fed from an automation panel'}`
    : undefined
  const place = rack && locationName(locations, rack.locationId)

  return (
    <AppShell
      title={rack?.name ?? 'Rack'}
      subtitle={place ? `${place} · ${subtitle}` : subtitle}
      back={{ to: rack ? `/projects/${rack.projectId}` : '/', label: 'House' }}
      nav={rack ? (
        <Link to={`/racks/${rack.id}/bom`} className="inline-flex min-h-11 items-center rounded-lg px-2 font-medium text-ink-2 active:bg-press">
          Parts list
        </Link>
      ) : undefined}
      actions={layout && rack ? (
        <>
          <Button variant="secondary" onClick={() => setAdding(true)}>Add to rack</Button>
          {/* An empty rack is refused by the server; offering the button would only invite the refusal. */}
          {layout.items.length > 0 && <IssueButton kind="rack" ownerId={rack.id} layoutVersion={layout.layoutVersion} syncKey={syncKey} />}
        </>
      ) : undefined}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}
      {!layout && !loadError && <Skeleton label="Loading rack" rows={1} rowClass="h-96" />}

      {layout && rack && (
        <>
          {layout.items.length === 0 ? (
            <div className="mb-3">
              <EmptyState
                title="Nothing in this rack yet"
                hint="Add a switch or a patch panel and it goes in the top free U."
                action={<Button onClick={() => setAdding(true)}>Add to rack</Button>}
              />
            </div>
          ) : (
            <p className="mb-2 text-sm text-ink-2">Tap an item to label its ports. Drag it to move it — on a touchscreen, hold it first.</p>
          )}

          <div
            ref={el => { drawing.current = el; bind.ref(el) }}
            onPointerDown={bind.onPointerDown}
            onPointerMove={bind.onPointerMove}
            onPointerUp={bind.onPointerUp}
            onPointerCancel={bind.onPointerCancel}
            // No callout or selection on a long press: the press is how an item
            // is picked up, and the magnifier would steal it.
            style={{ WebkitTouchCallout: 'none', WebkitUserSelect: 'none', userSelect: 'none' } as React.CSSProperties}
            className="rounded-lg border border-rule bg-sheet p-2"
          >
            <RackDrawing layout={layout} selectedId={selectedId} onSelect={setSelectedId} dragging={dragging} />
          </div>
          <p className="mt-2 text-sm text-ink-2">{layout.freeU}U free of {layout.heightU}U. A filled port is labelled; a dot means PoE.</p>
          {/* Where the dragged item would land, for those who cannot see the ghost.
              A live region and not role="status": that is the shell's status line, and two would be read as one. */}
          <p aria-live="polite" className="sr-only">
            {dragging && `U${dragging.topU}: ${dragging.valid ? 'free' : 'no room'}`}
          </p>

          <div className="mt-6">
            <RackSettings
              key={`${rack.name}|${rack.rackTypeId}|${rack.fedFromSubmainId}`}
              rack={rack}
              cabinets={cabinets}
              submains={submains}
              locations={locations}
              onSaved={saved => { setRack(saved); setSyncKey(n => n + 1); void load() }}
              onConflict={() => void load()}
            />
          </div>
        </>
      )}

      {adding && layout && (
        <AddItemSheet
          catalogue={catalogue}
          catalogueError={catalogueError}
          onRetryCatalogue={() => void loadCatalogue()}
          freeU={layout.freeU}
          onAdd={addItem}
          onClose={closeAdd}
        />
      )}

      {selected && layout && (
        <ItemSheet
          key={selected.id}
          item={selected}
          layout={layout}
          onSavePort={(number, label, poe) => savePort(selected.id, number, label, poe)}
          onMove={topU => { void commitMove(selected.id, topU) }}
          onRemove={() => void removeItem(selected.id)}
          onClose={closeItem}
        />
      )}
    </AppShell>
  )
}
