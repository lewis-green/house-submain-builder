import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type {
  DesignResponse, DeviceTypeResponse, EnclosureType, ExtraFixture, SubmainResponse,
} from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Toggle } from '../components/Toggle'
import { Spinner } from '../components/Spinner'
import { FixturePicker } from '../wizard/FixturePicker'
import { PreviewSummary } from '../wizard/PreviewSummary'
import { buildCircuits, type TapeInput } from '../wizard/buildCircuits'

const PREVIEW_DEBOUNCE_MS = 400

/**
 * One scrolling page rather than paged steps: on a phone, moving back and forth
 * between steps costs more than scrolling does.
 */
export function NewSubmainPage() {
  const { projectId } = useParams()
  const navigate = useNavigate()

  const [enclosures, setEnclosures] = useState<EnclosureType[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [enclosureId, setEnclosureId] = useState('')
  const [dimmed, setDimmed] = useState('0')
  const [switched, setSwitched] = useState('0')
  const [blinds, setBlinds] = useState('0')
  const [tape, setTape] = useState<TapeInput[]>([])
  const [fixtures, setFixtures] = useState<ExtraFixture[]>([])
  const [catalogue, setCatalogue] = useState<DeviceTypeResponse[]>([])
  const [hasIsolator, setHasIsolator] = useState(true)
  const [terminalsAtBottom, setTerminalsAtBottom] = useState(false)

  const [submainId, setSubmainId] = useState<string | null>(null)
  const [design, setDesign] = useState<DesignResponse | null>(null)
  const [previewing, setPreviewing] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [generating, setGenerating] = useState(false)

  const debounce = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  useEffect(() => {
    Promise.all([
      api.get<EnclosureType[]>('/catalogue/enclosures'),
      api.get<DeviceTypeResponse[]>('/catalogue/device-types'),
    ])
      .then(([list, devices]) => {
        setEnclosures(list)
        setCatalogue(devices)
        if (list.length > 0) setEnclosureId(list[0].id)
      })
      .catch((e: ApiError) => setLoadError(e.message))
  }, [])

  const counts = {
    dimmed: Number(dimmed) || 0,
    switched: Number(switched) || 0,
    blinds: Number(blinds) || 0,
    tape,
  }
  const circuits = buildCircuits(counts)

  // Preview persists nothing, so an abandoned wizard leaves an empty submain
  // and no devices.
  useEffect(() => {
    if (!enclosureId || (circuits.length === 0 && fixtures.length === 0)) {
      setDesign(null)
      return
    }

    clearTimeout(debounce.current)
    debounce.current = setTimeout(() => { void preview() }, PREVIEW_DEBOUNCE_MS)
    return () => clearTimeout(debounce.current)
  }, [
    enclosureId, dimmed, switched, blinds, hasIsolator, terminalsAtBottom,
    JSON.stringify(tape), JSON.stringify(fixtures),
  ])

  async function ensureSubmain(): Promise<string> {
    if (submainId) return submainId

    const created = await api.post<SubmainResponse>(`/projects/${projectId}/submains`, {
      name: name.trim() || 'New submain',
      enclosureTypeId: enclosureId,
      hasIsolator,
      terminalsAtBottom,
      circuits: [],
    })
    setSubmainId(created.id)
    return created.id
  }

  async function preview() {
    setPreviewing(true)
    try {
      const id = await ensureSubmain()
      setDesign(await api.post<DesignResponse>(`/submains/${id}/design/preview`, {
        enclosureTypeId: enclosureId,
        hasIsolator,
        terminalsAtBottom,
        circuits,
        extraFixtures: fixtures,
      }))
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
    } finally {
      setPreviewing(false)
    }
  }

  async function generate() {
    setGenerating(true)
    setFieldErrors({})
    try {
      const id = await ensureSubmain()
      await api.patch<SubmainResponse>(`/submains/${id}`, {
        name: name.trim() || 'New submain',
        enclosureTypeId: enclosureId,
        hasIsolator,
        terminalsAtBottom,
        circuits,
        extraFixtures: fixtures,
      })
      await api.post<DesignResponse>(`/submains/${id}/design/generate`, { circuits })
      navigate(`/submains/${id}/panel`)
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      // A 422 carries the diagnostics that explain why: keep the engineer here
      // with the inputs they can change, rather than throwing them to an error page.
      if (error.status === 422) await preview()
    } finally {
      setGenerating(false)
    }
  }

  if (loadError) return <ErrorNote message={loadError} />
  if (!enclosures) return <Spinner label="Loading catalogue" />

  const blocked = design?.diagnostics.some(d => d.severity === 'Error') ?? false

  return (
    <div className="space-y-6 p-4 pb-24">
      <div>
        <Link to={`/projects/${projectId}`} className="text-sm text-slate-500">← Back</Link>
        <h1 className="mt-1 text-xl font-semibold">New submain</h1>
      </div>

      <Field label="Submain name" value={name} onChange={setName} errors={fieldErrors.name} />

      <div>
        <label htmlFor="enclosure" className="block text-sm font-medium text-slate-700">Enclosure</label>
        <select
          id="enclosure"
          value={enclosureId}
          onChange={e => setEnclosureId(e.target.value)}
          className="mt-1 min-h-11 w-full rounded-lg border border-slate-300 px-3"
        >
          {enclosures.map(e => (
            <option key={e.id} value={e.id}>
              {e.description} — {e.rows} rows
            </option>
          ))}
        </select>
      </div>

      <div className="space-y-3 rounded-lg border border-slate-200 p-4">
        <h2 className="text-sm font-semibold text-slate-700">The install</h2>
        <Toggle
          label="Cables enter at the bottom"
          hint="Puts the terminations on the bottom rail instead of the top."
          checked={terminalsAtBottom}
          onChange={setTerminalsAtBottom}
        />
        <Toggle
          label="Fit a main isolator"
          hint="Turn this off for a submain already isolated upstream."
          checked={hasIsolator}
          onChange={setHasIsolator}
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <Field label="Dimmed lighting" value={dimmed} onChange={setDimmed} type="number" inputMode="numeric" />
        <Field label="Switched" value={switched} onChange={setSwitched} type="number" inputMode="numeric" />
        <Field label="Blinds" value={blinds} onChange={setBlinds} type="number" inputMode="numeric" />
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-slate-700">LED tape</h2>
        {tape.map((t, i) => (
          <div key={i} className="grid grid-cols-2 gap-3 rounded-lg border border-slate-200 p-3">
            <Field
              label="Watts per metre"
              value={String(t.wattsPerMetre)}
              onChange={v => setTape(tape.map((x, j) => j === i ? { ...x, wattsPerMetre: Number(v) || 0 } : x))}
              type="number"
              inputMode="decimal"
            />
            <Field
              label="Length (m)"
              value={String(t.lengthMetres)}
              onChange={v => setTape(tape.map((x, j) => j === i ? { ...x, lengthMetres: Number(v) || 0 } : x))}
              type="number"
              inputMode="decimal"
            />
            <label className="col-span-2 flex min-h-11 items-center gap-3">
              <input
                type="checkbox"
                checked={t.colour}
                onChange={e => setTape(tape.map((x, j) => j === i ? { ...x, colour: e.target.checked } : x))}
                className="size-5 rounded border-slate-300"
              />
              <span className="text-sm text-slate-700">
                Colour (RGBW) — runs off a controller instead of a 0-10V dimmer
              </span>
            </label>
            <Button variant="ghost" onClick={() => setTape(tape.filter((_, j) => j !== i))}>Remove</Button>
          </div>
        ))}
        <Button
          variant="secondary"
          onClick={() => setTape([...tape, { wattsPerMetre: 14.4, lengthMetres: 5, colour: false }])}
        >
          Add tape circuit
        </Button>
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-slate-700">Other devices</h2>
        <p className="text-xs text-slate-500">
          Gear no circuit asks for: an energy meter, a LAN switch, a relay for later.
        </p>
        <FixturePicker catalogue={catalogue} fixtures={fixtures} onChange={setFixtures} />
      </div>

      <div className="rounded-lg border border-slate-200 p-4">
        <h2 className="mb-3 text-sm font-semibold text-slate-700">
          Preview {previewing && <span className="font-normal text-slate-400">updating…</span>}
        </h2>
        {design
          ? <div className={previewing ? 'opacity-50' : undefined}><PreviewSummary design={design} /></div>
          : <p className="text-sm text-slate-500">Enter some circuits to see what this panel needs.</p>}
      </div>

      <Button onClick={generate} disabled={generating || blocked || circuits.length === 0}>
        Generate panel
      </Button>
    </div>
  )
}
