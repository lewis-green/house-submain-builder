import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type { DesignResponse, SubmainResponse } from '../api/types'
import { Button } from '../components/Button'
import { Diagnostics } from '../components/Diagnostics'
import { ErrorNote } from '../components/ErrorNote'
import { Spinner } from '../components/Spinner'
import { DeviceSheet } from '../panel/DeviceSheet'
import { IssueButton } from '../panel/IssueButton'
import { PanelSvg } from '../panel/PanelSvg'
import { useDeviceDrag } from '../panel/useDeviceDrag'

export function PanelPage() {
  const { submainId } = useParams()
  const [design, setDesign] = useState<DesignResponse | null>(null)
  const [submain, setSubmain] = useState<SubmainResponse | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [zoom, setZoom] = useState(1)
  const svgWrapper = useRef<HTMLDivElement>(null)

  // The stored layout, not /design/preview: preview regenerates and returns
  // id: null on every device, so nothing would be draggable.
  const load = useCallback(() =>
    Promise.all([
      api.get<DesignResponse>(`/submains/${submainId}/layout`),
      // Also the submain itself: without it there is no project to go back to.
      api.get<SubmainResponse>(`/submains/${submainId}`),
    ])
      .then(([layout, s]) => { setDesign(layout); setSubmain(s) })
      .catch((e: ApiError) => setLoadError(e.message)), [submainId])

  useEffect(() => { void load() }, [load])

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
      setNotice(null)
    } catch (e) {
      const error = e as ApiError
      setDesign(previous)
      if (error.conflict) {
        setNotice('This panel changed elsewhere — reloading')
        await load()
      } else {
        setNotice(error.message)
      }
    }
  }, [design, load])

  const { dragging, bind } = useDeviceDrag({
    layout: design?.layout ?? { rows: 0, slotsPerRow: 0, devices: [] },
    onCommit: (id, row, slot) => { void commitMove(id, row, slot) },
    toPanelPoint,
  })

  async function renameCircuit(circuitId: string, name: string) {
    try {
      await api.patch(`/circuits/${circuitId}`, { name, room: null })
      await load()
    } catch (e) {
      setNotice((e as ApiError).message)
    }
  }

  async function freeChannel(deviceId: string, channelIndex: number) {
    if (!design) return
    try {
      await api.patch(`/devices/${deviceId}/channels/${channelIndex}`, {
        circuitId: null, isSpare: true, basedOnLayoutVersion: design.layoutVersion,
      })
      await load()
    } catch (e) {
      const error = e as ApiError
      if (error.conflict) {
        setNotice('This panel changed elsewhere — reloading')
        await load()
      } else {
        setNotice(error.message)
      }
    }
  }

  if (loadError) return <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />
  if (!design) return <Spinner label="Loading panel" />

  const selected = design.layout.devices.find(d => d.id === selectedId) ?? null
  const empty = design.layout.devices.length === 0

  return (
    <div className="p-4 pb-24">
      <Link
        to={submain ? `/projects/${submain.projectId}` : '/'}
        className="text-sm text-slate-500"
      >
        ← Back
      </Link>
      <h1 className="mt-1 mb-3 text-xl font-semibold">{submain?.name ?? 'Panel'}</h1>

      {notice && (
        <p role="status" className="mb-3 rounded-lg bg-slate-100 p-3 text-sm text-slate-700">{notice}</p>
      )}

      <div className="mb-3">
        <Diagnostics diagnostics={design.diagnostics} />
      </div>

      {empty ? (
        <p className="text-slate-500">This submain has no panel yet. Generate one from the wizard.</p>
      ) : (
        <>
          <div className="mb-2 flex items-center gap-2">
            <Button variant="secondary" onClick={() => setZoom(z => Math.max(0.4, z - 0.2))}>−</Button>
            <Button variant="secondary" onClick={() => setZoom(z => Math.min(3, z + 0.2))}>+</Button>
            <Button variant="ghost" onClick={() => setZoom(1)}>Fit</Button>
            <span className="text-sm text-slate-500">Long-press a device to move it</span>
          </div>

          <div className="overflow-auto rounded-lg border border-slate-200 bg-white p-2">
            <div
              ref={svgWrapper}
              {...bind}
              style={{ ...bind.style, transform: `scale(${zoom})`, transformOrigin: 'top left', width: 'fit-content' }}
            >
              <PanelSvg
                layout={design.layout}
                selectedId={selectedId}
                onSelect={setSelectedId}
                dragging={dragging}
              />
            </div>
          </div>
        </>
      )}

      {!empty && submainId && (
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <IssueButton submainId={submainId} />
          <Link to={`/submains/${submainId}/bom`} className="text-sm underline">Bill of materials</Link>
        </div>
      )}

      {selected && selected.id && (
        <DeviceSheet
          device={selected}
          onRenameCircuit={renameCircuit}
          onFreeChannel={index => freeChannel(selected.id!, index)}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  )
}
