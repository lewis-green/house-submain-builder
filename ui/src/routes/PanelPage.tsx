import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type {
  DesignResponse, DeviceTypeResponse, ExtraFixture, SubmainResponse, TerminalCircuit, TerminalsResponse,
} from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button, ButtonLink } from '../components/Button'
import { Diagnostics } from '../components/Diagnostics'
import { EmptyState } from '../components/EmptyState'
import { ErrorNote } from '../components/ErrorNote'
import { Skeleton } from '../components/Skeleton'
import { Toggle } from '../components/Toggle'
import { announce } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'
import { DeviceSheet } from '../panel/DeviceSheet'
import { IssueButton } from '../panel/IssueButton'
import { PanelOverview } from '../panel/PanelOverview'
import { PanelSvg } from '../panel/PanelSvg'
import { TerminalSheet } from '../panel/TerminalSheet'
import { DEVICE_INSET, ROW_PX, SLOT_PX, panelSize, rowToY } from '../panel/geometry'
import { useDeviceDrag } from '../panel/useDeviceDrag'
import { useScrollExtent } from '../panel/useScrollExtent'
import { SLOT_UNITS_PER_MODULE } from '../units'
import { FixturePicker } from '../wizard/FixturePicker'

const ZOOM_STEP = 1.25
const ZOOM_MIN = 0.3
const ZOOM_MAX = 3

/** The row-label gutter that stays put while the drawing scrolls under it. */
const GUTTER_PX = 44
/** Breathing space around the drawing inside the scroller. */
const PAD_PX = 8
/** The smallest target a thumb can rely on; below it a device is for looking at, not tapping. */
const TAPPABLE_PX = 44

/** The screen's nav links, alike so neither reads as the main one. */
const navLink = 'inline-flex min-h-11 min-w-0 items-center rounded-lg px-3 text-right font-medium text-ink-2 active:bg-press'

export function PanelPage() {
  const { submainId } = useParams()
  const [design, setDesign] = useState<DesignResponse | null>(null)
  const [submain, setSubmain] = useState<SubmainResponse | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  // The circuits in terminal order while the sheet is open; null when it is shut.
  const [terminals, setTerminals] = useState<TerminalCircuit[] | null>(null)
  const [zoom, setZoom] = useState(1)
  const [rooms, setRooms] = useState<string[]>([])
  const [reworking, setReworking] = useState(false)
  // null until the catalogue has arrived: the picker has nothing honest to offer before then.
  const [catalogue, setCatalogue] = useState<DeviceTypeResponse[] | null>(null)
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  // Counts loads; the PDF button re-asks the server whether its revision is still current on each.
  const [loads, setLoads] = useState(0)
  const svgWrapper = useRef<HTMLDivElement>(null)
  const scroller = useRef<HTMLDivElement>(null)
  const once = useSingleFlight()
  // Its own guard: a rebuild in flight must not swallow a tap on the terminals, nor the reverse.
  const openingTerminals = useSingleFlight()
  // Mirrors selectedId so an async open can see a device sheet opened while it waited.
  const selectedIdRef = useRef<string | null>(null)
  useEffect(() => { selectedIdRef.current = selectedId }, [selectedId])

  // The stored layout, not /design/preview: preview regenerates and returns
  // id: null on every device, so nothing would be draggable.
  const load = useCallback(() =>
    Promise.all([
      api.get<DesignResponse>(`/submains/${submainId}/layout`),
      // Also the submain itself: without it there is no project to go back to.
      api.get<SubmainResponse>(`/submains/${submainId}`),
    ])
      .then(([layout, s]) => {
        setDesign(layout)
        setSubmain(s)
        setLoads(n => n + 1)
        // Rooms already used in this house, offered before anyone types one.
        // A failure here costs a suggestion list, not the panel.
        api.get<string[]>(`/projects/${s.projectId}/rooms`).then(setRooms).catch(() => setRooms([]))
      })
      .catch((e: ApiError) => setLoadError(e.message)), [submainId])

  // On its own, with its own retry: the catalogue is what "Other devices" is
  // made of, and an empty picker with no word of why reads as an empty catalogue.
  const loadCatalogue = useCallback(() =>
    api.get<DeviceTypeResponse[]>('/catalogue/device-types')
      .then(list => { setCatalogue(list); setCatalogueError(null) })
      .catch((e: ApiError) => setCatalogueError(e.message)), [])

  useEffect(() => { void load() }, [load])
  useEffect(() => { void loadCatalogue() }, [loadCatalogue])

  const toPanelPoint = useCallback((clientX: number, clientY: number) => {
    const box = svgWrapper.current?.getBoundingClientRect()
    if (!box) return { x: clientX, y: clientY }
    return { x: (clientX - box.left) / zoom, y: (clientY - box.top) / zoom }
  }, [zoom])

  const commitMove = useCallback(async (deviceId: string, rowIndex: number, startSlot: number) => {
    if (!design) return
    const previous = design

    // Optimistic: rewrite the one device's position, never a re-layout.
    setDesign({
      ...design,
      layout: {
        ...design.layout,
        devices: design.layout.devices.map(d =>
          d.id === deviceId ? { ...d, rowIndex, startSlot } : d),
      },
    })

    try {
      const result = await api.patch<{ layoutVersion: number }>(`/devices/${deviceId}/position`, {
        rowIndex, startSlot, basedOnLayoutVersion: previous.layoutVersion,
      })
      setDesign(d => (d ? { ...d, layoutVersion: result.layoutVersion } : d))
      announce(`Moved to row ${rowIndex + 1}, module ${startSlot / SLOT_UNITS_PER_MODULE + 1}`, 'ok')
    } catch (e) {
      const error = e as ApiError
      setDesign(previous)
      if (error.conflict) {
        announce('This panel changed elsewhere — reloading')
        await load()
      } else {
        announce(`Not moved: ${error.message}`, 'error')
      }
    }
  }, [design, load])

  // Stable, so the sheet is not handed a new function on every re-render.
  const closeSheet = useCallback(() => setSelectedId(null), [])

  const { dragging, bind } = useDeviceDrag({
    layout: design?.layout ?? { rows: 0, slotsPerRow: 0, devices: [] },
    onCommit: (id, row, slot) => { void commitMove(id, row, slot) },
    toPanelPoint,
  })

  // Name and room are one edit. Sending only the name would clear the room,
  // which is exactly what this used to do.
  async function saveCircuit(circuitId: string, name: string, room: string | null) {
    try {
      await api.patch(`/circuits/${circuitId}`, { name, room })
      await load()
      announce(`Saved ${name}`, 'ok')
    } catch (e) {
      announce(`Not saved: ${(e as ApiError).message}`, 'error')
      // Rethrown so the sheet knows the pair is still unsaved and will try again.
      throw e
    }
  }

  /**
   * All of these change how the panel is laid out, so the panel has to be built
   * again — there is no way to apply them to the devices already placed. The
   * save and the rebuild are one request and one transaction on the server, so
   * a refused or failed rebuild leaves the option exactly as it was: the toggle
   * never shows a setting the drawing does not. One rebuild at a time: a second
   * change landing in the same tick, before the controls have disabled, is dropped.
   */
  const setInstallOption = (
    option: { hasIsolator?: boolean; terminalsAtBottom?: boolean; extraFixtures?: ExtraFixture[] },
  ) => once(async () => {
    if (!submain) return
    setReworking(true)
    try {
      setSubmain(await api.patch<SubmainResponse>(`/submains/${submain.id}`, {
        name: submain.name,
        enclosureTypeId: submain.enclosureTypeId,
        ...option,
        rebuild: true,
      }))
      await load()
      announce('Panel rebuilt', 'ok')
    } catch (e) {
      announce(`Panel not rebuilt: ${(e as ApiError).message}`, 'error')
      // Usually nothing changed; if the reply was lost rather than the work refused, this shows what did.
      await load()
    } finally {
      setReworking(false)
    }
  })

  async function freeChannel(deviceId: string, channelIndex: number) {
    if (!design) return
    try {
      await api.patch(`/devices/${deviceId}/channels/${channelIndex}`, {
        circuitId: null, isSpare: true, basedOnLayoutVersion: design.layoutVersion,
      })
      await load()
      announce(`Channel ${channelIndex + 1} freed`, 'ok')
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        announce('This panel changed elsewhere — reloading')
        await load()
      } else {
        announce(`Channel not freed: ${error.message}`, 'error')
      }
    }
  }

  // One load at a time (a double tap would fetch twice), and not over a device
  // sheet that is already open.
  const openTerminals = () => openingTerminals(async () => {
    if (selectedIdRef.current) return
    try {
      const response = await api.get<TerminalsResponse>(`/submains/${submainId}/terminals`)
      if (selectedIdRef.current) return
      setTerminals(response.circuits)
    } catch (e) {
      announce(`Terminals not loaded: ${(e as ApiError).message}`, 'error')
    }
  })

  /**
   * One request: the order and counts are saved and the panel rebuilt together,
   * so a refused rebuild (no room for the extra terminals) saves nothing.
   * Rethrows a refusal so the sheet keeps the edit.
   */
  async function saveTerminals(circuits: { circuitId: string; terminalCount: number }[]) {
    if (!design) throw new Error('The panel has not loaded yet')
    try {
      await api.put(`/submains/${submainId}/terminals`, { layoutVersion: design.layoutVersion, circuits })
      setTerminals(null)
      await load()
      announce('Terminal order saved, panel rebuilt', 'ok')
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        announce('This panel changed elsewhere — reloading')
        setTerminals(null)
        await load()
        return
      }
      announce(`Terminal order not saved: ${error.message}`, 'error')
      throw e
    }
  }

  const closeTerminals = useCallback(() => setTerminals(null), [])

  /**
   * The whole drawing across the scroller, for an overview before zooming back
   * in. Not held to ZOOM_MIN: a 24-module panel on a 393px phone needs 0.23×,
   * and a "fit" that leaves five modules off screen is not one. The hint under
   * the buttons says the devices are too small to tap at this size.
   */
  function fitWidth() {
    if (!design || !scroller.current) return
    const available = scroller.current.clientWidth - GUTTER_PX - PAD_PX * 2
    if (available <= 0) return
    // Rounded down so the drawing never comes out a fraction of a pixel too wide to fit.
    setZoom(Math.min(ZOOM_MAX, Math.floor((available / panelSize(design.layout).width) * 1000) / 1000))
    scroller.current.scrollLeft = 0
  }

  const selected = design?.layout.devices.find(d => d.id === selectedId) ?? null
  const empty = design !== null && design.layout.devices.length === 0
  const modules = design ? design.layout.slotsPerRow / SLOT_UNITS_PER_MODULE : 0

  const extent = useScrollExtent(scroller, design, zoom)
  const moreLeft = extent !== null && extent.left > 1
  const moreRight = extent !== null && extent.left + extent.width < extent.total - 1

  // Honest about what Fit width gives on a phone: the narrowest device on
  // screen, as drawn, against what a thumb can hit.
  const narrowestPx = design && !empty
    ? Math.min(...design.layout.devices.filter(d => d.category !== 'Terminal240').map(d => d.moduleWidth)) * SLOT_PX * zoom - DEVICE_INSET * 2
    : Infinity
  const tooSmallToTap = narrowestPx < TAPPABLE_PX

  return (
    <AppShell
      title={submain?.name ?? 'Panel'}
      subtitle={design && !empty ? `${design.layout.rows} rows of ${modules} modules` : undefined}
      back={{ to: submain ? `/projects/${submain.projectId}` : '/', label: 'House' }}
      // The parts list is a place to go, not something done to the panel, so it
      // sits with the title like Catalogue on the houses screen — and the bar
      // below keeps to the PDF pair, one row even on a 320px phone. Edit goes
      // there too: it leaves this screen for the form that built the panel.
      nav={design && !empty && submainId ? (
        <>
          <Link to={`/submains/${submainId}/bom`} className={navLink}>Bill of materials</Link>
          <Link to={`/submains/${submainId}/edit`} className={navLink}>Edit</Link>
        </>
      ) : undefined}
      actions={design && !empty && submainId
        ? <IssueButton submainId={submainId} layoutVersion={design.layoutVersion} syncKey={loads} />
        : undefined}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}

      {!design && !loadError && <Skeleton label="Loading panel" rows={3} rowClass="h-24" />}

      {design && empty && (
        <EmptyState
          title="This submain has no panel yet"
          hint="Enter its circuits in the wizard and the panel is laid out for you."
          action={submain && (
            <ButtonLink to={`/projects/${submain.projectId}/submains/new`} variant="primary">Design the panel</ButtonLink>
          )}
        />
      )}

      {design && !empty && (
        <>
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <div className="flex flex-wrap items-center gap-2">
              {/* Never zooms in: after a Fit below ZOOM_MIN, "−" holds rather than jumps up. */}
              <Button variant="secondary" aria-label="Zoom out" onClick={() => setZoom(z => Math.max(Math.min(ZOOM_MIN, z), z / ZOOM_STEP))}>−</Button>
              <Button variant="secondary" aria-label="Zoom in" onClick={() => setZoom(z => Math.min(ZOOM_MAX, z * ZOOM_STEP))}>+</Button>
              <Button variant="secondary" onClick={fitWidth}>Fit width</Button>
              <Button variant="ghost" onClick={() => setZoom(1)}>1×</Button>
            </div>
            <p className="text-sm text-ink-2">
              {tooSmallToTap
                ? 'At this size the devices are too small to tap. Zoom in, or tap 1×, to name or move one.'
                : 'Tap a device to name its circuits, or the terminals to change their order. Drag a device to move it — on a touchscreen, hold it first.'}
            </p>
          </div>

          <div className="relative">
            <div
              ref={scroller}
              className="overflow-auto rounded-lg border border-rule bg-sheet"
              style={{ overscrollBehaviorX: 'contain' }}
            >
              <div className="flex w-fit">
                {/* Row labels in a column that sticks to the left edge, so "Row 2"
                    is on screen whichever modules are. */}
                {/* Opaque: a half-device sliding under a translucent label read as
                    a glitch. The drawing's own left padding keeps its first module clear. */}
                <div
                  aria-hidden="true"
                  className="relative sticky left-0 z-10 shrink-0 border-r border-rule bg-sheet"
                  style={{ width: GUTTER_PX }}
                >
                  {Array.from({ length: design.layout.rows }, (_, row) => (
                    <div
                      key={row}
                      className="absolute left-0 flex w-full items-center pl-1.5 text-[12px] font-medium text-ink-2"
                      style={{ top: PAD_PX + rowToY(row) * zoom, height: ROW_PX * zoom }}
                    >
                      Row {row + 1}
                    </div>
                  ))}
                </div>
                <div style={{ padding: PAD_PX }}>
                  <div
                    ref={el => { svgWrapper.current = el; bind.ref(el) }}
                    onPointerDown={bind.onPointerDown}
                    onPointerMove={bind.onPointerMove}
                    onPointerUp={bind.onPointerUp}
                    onPointerCancel={bind.onPointerCancel}
                    // No callout or selection on a long press: the press is how a
                    // device is picked up, and the magnifier would steal it.
                    style={{ width: 'fit-content', WebkitTouchCallout: 'none', WebkitUserSelect: 'none', userSelect: 'none' } as React.CSSProperties}
                  >
                    <PanelSvg
                      layout={design.layout}
                      selectedId={selectedId}
                      onSelect={setSelectedId}
                      dragging={dragging}
                      zoom={zoom}
                      onOpenTerminals={() => void openTerminals()}
                    />
                  </div>
                </div>
              </div>
            </div>
            {/* A shadow on whichever edge has more drawing behind it: iOS draws no scrollbar. */}
            {moreLeft && (
              <div aria-hidden="true" className="pointer-events-none absolute inset-y-0 w-6 bg-linear-to-r from-ink/20 to-transparent" style={{ left: GUTTER_PX }} />
            )}
            {moreRight && (
              <div aria-hidden="true" className="pointer-events-none absolute inset-y-0 right-0 w-8 rounded-r-lg bg-linear-to-l from-ink/20 to-transparent" />
            )}
          </div>

          <PanelOverview
            layout={design.layout}
            zoom={zoom}
            originPx={GUTTER_PX + PAD_PX}
            gutterPx={GUTTER_PX}
            extent={extent}
            onPan={left => { if (scroller.current) scroller.current.scrollLeft = left }}
          />

          {/* Where the dragged device would land, for those who cannot see the ghost. */}
          <p aria-live="polite" className="sr-only">
            {dragging && `Row ${dragging.rowIndex + 1}, module ${dragging.startSlot / SLOT_UNITS_PER_MODULE + 1}: ${dragging.valid ? 'free' : 'no room'}`}
          </p>

          {design.diagnostics.length > 0 && (
            <div className="mt-4">
              <Diagnostics diagnostics={design.diagnostics} />
            </div>
          )}

          <details className="group mt-6 rounded-lg border border-rule bg-sheet">
            <summary className="flex min-h-11 cursor-pointer items-center justify-between rounded-lg px-4 font-semibold active:bg-press">
              <span>Install options {reworking && <span className="font-normal text-ink-2">· rebuilding…</span>}</span>
              <span aria-hidden="true" className="text-ink-2 transition-transform group-open:rotate-180">▾</span>
            </summary>
            <div className="space-y-4 border-t border-rule p-4">
              <p className="text-sm text-ink-2">Changing any of these rebuilds the panel. Dragged positions are kept where they still fit.</p>
              <Toggle
                label="Cables enter at the bottom"
                hint="Puts the terminations on the bottom rail instead of the top."
                checked={submain?.terminalsAtBottom ?? false}
                disabled={reworking || !submain}
                onChange={terminalsAtBottom => void setInstallOption({ terminalsAtBottom })}
              />
              <Toggle
                label="Fit a main isolator"
                hint="Turn this off for a submain already isolated upstream."
                checked={submain?.hasIsolator ?? true}
                disabled={reworking || !submain}
                onChange={hasIsolator => void setInstallOption({ hasIsolator })}
              />
              <div>
                <h2 className="mb-2 font-semibold">Other devices</h2>
                {catalogueError && (
                  <ErrorNote
                    message={`The device catalogue could not be loaded, so nothing can be added here yet. ${catalogueError}`}
                    onRetry={() => { setCatalogueError(null); void loadCatalogue() }}
                  />
                )}
                {catalogue && (
                  <FixturePicker
                    catalogue={catalogue}
                    fixtures={submain?.extraFixtures ?? []}
                    disabled={reworking || !submain}
                    onChange={extraFixtures => void setInstallOption({ extraFixtures })}
                  />
                )}
              </div>
            </div>
          </details>
        </>
      )}

      {selected && selected.id && (
        <DeviceSheet
          device={selected}
          onSaveCircuit={saveCircuit}
          rooms={rooms}
          onFreeChannel={index => freeChannel(selected.id!, index)}
          onClose={closeSheet}
        />
      )}

      {terminals && (
        <TerminalSheet circuits={terminals} onSave={saveTerminals} onClose={closeTerminals} />
      )}
    </AppShell>
  )
}
