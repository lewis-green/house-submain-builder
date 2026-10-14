import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { ApiError, api } from '../api/client'
import type {
  DesignResponse, DeviceTypeResponse, EnclosureType, ExtraFixture, SubmainResponse,
} from '../api/types'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { ConfirmButton } from '../components/ConfirmButton'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { Skeleton } from '../components/Skeleton'
import { Stepper } from '../components/Stepper'
import { Toggle } from '../components/Toggle'
import { announce, dismissStatus } from '../components/status'
import { useSingleFlight } from '../components/useSingleFlight'
import { FixturePicker } from '../wizard/FixturePicker'
import { PreviewSummary } from '../wizard/PreviewSummary'
import { buildCircuits, type TapeInput } from '../wizard/buildCircuits'
import { formatModules } from '../units'

const PREVIEW_DEBOUNCE_MS = 400

/**
 * Size first, maker last: a select cannot truncate with an ellipsis in WebKit,
 * so on a narrow phone whatever comes last is cut mid-word — and the maker is
 * the part every option shares. A model that already names its size ("3 row x
 * 24 way") is left out rather than said twice.
 */
const describeEnclosure = (e: EnclosureType) => {
  const size = `${e.rows} rows × ${formatModules(e.slotsPerRow)}`
  const maker = /\brows?\b/i.test(e.model) ? e.manufacturer : `${e.manufacturer} ${e.model}`
  return `${size} · ${maker}`
}

/**
 * One scrolling page rather than paged steps: on a phone, moving back and forth
 * between steps costs more than scrolling does. It reads in the order an
 * engineer thinks — what the box is, how it is installed, what it feeds — with
 * the verdict and the Generate button pinned to the bottom.
 */
export function NewSubmainPage() {
  const { projectId } = useParams()
  const navigate = useNavigate()

  const [enclosures, setEnclosures] = useState<EnclosureType[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [enclosureId, setEnclosureId] = useState('')
  const [dimmed, setDimmed] = useState(0)
  const [switched, setSwitched] = useState(0)
  const [blinds, setBlinds] = useState(0)
  const [tape, setTape] = useState<TapeInput[]>([])
  const [fixtures, setFixtures] = useState<ExtraFixture[]>([])
  const [catalogue, setCatalogue] = useState<DeviceTypeResponse[]>([])
  const [hasIsolator, setHasIsolator] = useState(true)
  const [terminalsAtBottom, setTerminalsAtBottom] = useState(false)

  const [design, setDesign] = useState<DesignResponse | null>(null)
  const [previewing, setPreviewing] = useState(false)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  // What the server refused that no field above can show, said by Generate —
  // and only while the inputs are the ones it was refused for.
  const [generateError, setGenerateError] = useState<{ message: string; inputs: unknown } | null>(null)
  const [generating, setGenerating] = useState(false)
  const once = useSingleFlight()

  // A callback, not an inline effect, so the error state can offer Try again.
  const load = useCallback(() =>
    Promise.all([
      api.get<EnclosureType[]>('/catalogue/enclosures'),
      api.get<DeviceTypeResponse[]>('/catalogue/device-types'),
    ])
      .then(([list, devices]) => {
        setEnclosures(list)
        setCatalogue(devices)
        // The biggest box first: a panel that fits is the common case, and a
        // smaller enclosure is a choice someone makes on purpose.
        const largest = [...list].sort((a, b) => b.totalSlots - a.totalSlots)[0]
        if (largest) setEnclosureId(largest.id)
      })
      .catch((e: ApiError) => setLoadError(e.message)), [])

  useEffect(() => { void load() }, [load])

  // One object for both preview and generate, rebuilt only when an input
  // changes, so the preview effect fires on real edits and not on every render.
  const request = useMemo(() => ({
    enclosureTypeId: enclosureId,
    hasIsolator,
    terminalsAtBottom,
    circuits: buildCircuits({ dimmed, switched, blinds, tape }),
    extraFixtures: fixtures,
  }), [enclosureId, hasIsolator, terminalsAtBottom, dimmed, switched, blinds, tape, fixtures])

  const circuits = request.circuits
  const nothingToDesign = circuits.length === 0 && fixtures.length === 0

  // Which request the shown preview answers. Generate is only offered while
  // that is the current one: a verdict on the previous counts is no verdict.
  const [previewedFor, setPreviewedFor] = useState<typeof request | null>(null)
  const latestRequest = useRef(request)
  useEffect(() => { latestRequest.current = request }, [request])

  const preview = useCallback(async () => {
    const mine = request
    setPreviewing(true)
    try {
      const result = await api.post<DesignResponse>(`/projects/${projectId}/design/preview`, mine)
      // A slow reply to an earlier edit must not overwrite a newer verdict.
      if (latestRequest.current !== mine) return
      setDesign(result)
      setPreviewedFor(mine)
      setPreviewError(null)
    } catch (e) {
      if (latestRequest.current !== mine) return
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      setPreviewError(error.message)
    } finally {
      if (latestRequest.current === mine) setPreviewing(false)
    }
  }, [projectId, request])

  // Preview is stateless on the server: nothing is created in the house until
  // Generate, so backing out of here leaves no empty submain behind.
  useEffect(() => {
    if (!enclosureId || nothingToDesign) return

    const timer = setTimeout(() => { void preview() }, PREVIEW_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [enclosureId, nothingToDesign, preview])

  const shownDesign = nothingToDesign ? null : design
  const fresh = previewedFor === request

  // One call saves the submain and lays out its panel in one transaction, so a
  // failure anywhere creates nothing: the house never gains an empty submain,
  // and tapping Generate again cannot make a second one. Two taps in one tick
  // cannot either: `once` drops the second before it reaches the server.
  const generate = () => once(async () => {
    setGenerating(true)
    setFieldErrors({})
    setGenerateError(null)
    // The last attempt's reason must not sit beside this one's.
    dismissStatus()
    try {
      const created = await api.post<SubmainResponse>(`/projects/${projectId}/submains`, {
        name: name.trim() || 'New submain',
        ...request,
        generate: true,
      })
      announce('Panel generated', 'ok')
      navigate(`/submains/${created.id}/panel`)
    } catch (e) {
      const error = e as ApiError
      setFieldErrors(error.fieldErrors)
      // The name is the only field here that shows its own errors; anything
      // else the server refused is said at the form's level, or it is said
      // nowhere. Said once: the note is an alert, so VoiceOver hears it without
      // a status toast repeating the sentence above it; the status region speaks
      // only when the server gave no field to pin the refusal to.
      const elsewhere = error.errorsOutside(['name'])
      if (elsewhere.length > 0) setGenerateError({ message: elsewhere.join(' '), inputs: request })
      else if (!error.fieldErrors.name) announce(`Not generated: ${error.message}`, 'error')
      // A 422 carries the diagnostics that explain why: keep the engineer here
      // with the inputs they can change, rather than throwing them to an error page.
      if (error.status === 422) await preview()
    } finally {
      setGenerating(false)
    }
  })

  const blocked = shownDesign?.diagnostics.some(d => d.severity === 'Error') ?? false
  // The one reason Generate is not available right now, said under the button.
  const whyNot = circuits.length === 0
    ? 'Add at least one circuit to generate a panel.'
    : previewError
      ? 'The design could not be checked. Try the preview again above.'
      : !fresh
        ? 'Checking the design…'
        : blocked
          ? 'Fix the error in the preview to generate.'
          : null

  return (
    <AppShell
      title="New submain"
      back={{ to: `/projects/${projectId}`, label: 'House' }}
      actions={(
        <div className="w-full">
          {generateError?.inputs === request && (
            <div className="mb-3"><ErrorNote compact message={`Not generated: ${generateError.message}`} /></div>
          )}
          <Button type="submit" form="new-submain" disabled={generating || whyNot !== null} className="w-full">
            {generating ? 'Generating…' : 'Generate panel'}
          </Button>
          {whyNot && <p className="mt-2 text-center text-sm text-ink-2">{whyNot}</p>}
        </div>
      )}
    >
      {loadError && <ErrorNote message={loadError} onRetry={() => { setLoadError(null); void load() }} />}
      {!enclosures && !loadError && <Skeleton label="Loading catalogue" rows={4} rowClass="h-14" />}

      {enclosures && (
        <form
          id="new-submain"
          className="space-y-6"
          onSubmit={e => { e.preventDefault(); void generate() }}
        >
          <p className="text-ink-2">
            A submain is one panel: the enclosure on a wall and everything in it.
          </p>

          <Field
            label="Submain name"
            value={name}
            onChange={setName}
            errors={fieldErrors.name}
            placeholder="Ground floor"
            autoCapitalize="words"
            autoComplete="off"
            enterKeyHint="next"
          />

          <div>
            <label htmlFor="enclosure" className="block font-medium">Enclosure</label>
            <select
              id="enclosure"
              value={enclosureId}
              onChange={e => setEnclosureId(e.target.value)}
              className="select mt-1 w-full min-w-0 rounded-lg border border-rule bg-sheet pl-3"
            >
              {enclosures.map(e => (
                <option key={e.id} value={e.id}>{describeEnclosure(e)}</option>
              ))}
            </select>
          </div>

          <fieldset className="space-y-3 rounded-lg border border-rule bg-sheet p-4">
            <legend className="px-1 font-semibold">The install</legend>
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
          </fieldset>

          <fieldset className="space-y-3">
            <legend className="mb-1 font-semibold">Circuits</legend>
            <p className="text-sm text-ink-2">How many of each this panel feeds. The right Shelly devices follow from the counts.</p>
            <Stepper label="Dimmed lighting" value={dimmed} onChange={setDimmed} />
            <Stepper label="Switched" value={switched} onChange={setSwitched} hint="Lights and loads that are only on or off." />
            <Stepper label="Blinds" value={blinds} onChange={setBlinds} />
          </fieldset>

          <fieldset className="space-y-3">
            <legend className="mb-1 font-semibold">LED tape</legend>
            {tape.map((t, i) => (
              <div key={i} className="grid grid-cols-2 gap-3 rounded-lg border border-rule bg-sheet p-3">
                <Field
                  label="Tape name"
                  value={t.name ?? ''}
                  onChange={name => setTape(tape.map((x, j) => j === i ? { ...x, name } : x))}
                  placeholder="Kitchen plinth"
                  hint={`Left blank, it is called ${t.colour ? 'Colour tape' : 'Tape'} ${i + 1}.`}
                  autoCapitalize="sentences"
                  enterKeyHint="next"
                  className="col-span-2"
                />
                <Field
                  label="Watts per metre"
                  value={String(t.wattsPerMetre)}
                  onChange={v => setTape(tape.map((x, j) => j === i ? { ...x, wattsPerMetre: Number(v) || 0 } : x))}
                  type="number"
                  inputMode="decimal"
                  enterKeyHint="next"
                />
                <Field
                  label="Length (m)"
                  value={String(t.lengthMetres)}
                  onChange={v => setTape(tape.map((x, j) => j === i ? { ...x, lengthMetres: Number(v) || 0 } : x))}
                  type="number"
                  inputMode="decimal"
                  enterKeyHint="done"
                />
                <div className="col-span-2">
                  <Toggle
                    label="Colour (RGBW)"
                    hint="Runs off a colour controller instead of a 0-10V dimmer."
                    checked={t.colour}
                    onChange={colour => setTape(tape.map((x, j) => j === i ? { ...x, colour } : x))}
                  />
                </div>
                <ConfirmButton
                  label="Remove this run"
                  question="Remove this tape run?"
                  confirmLabel="Remove"
                  onConfirm={() => setTape(tape.filter((_, j) => j !== i))}
                  className="col-span-2"
                />
              </div>
            ))}
            <Button
              variant="secondary"
              onClick={() => setTape([...tape, { wattsPerMetre: 14.4, lengthMetres: 5, colour: false }])}
            >
              Add tape circuit
            </Button>
          </fieldset>

          <fieldset className="space-y-3">
            <legend className="mb-1 font-semibold">Other devices</legend>
            <p className="text-sm text-ink-2">
              Gear no circuit asks for: an energy meter, a LAN switch, a relay for later.
            </p>
            <FixturePicker catalogue={catalogue} fixtures={fixtures} onChange={setFixtures} />
          </fieldset>

          <section aria-labelledby="preview-heading" className="min-h-28 rounded-lg border border-rule bg-sheet p-4">
            <h2 id="preview-heading" className="mb-3 font-semibold">
              What this panel needs {previewing && <span className="font-normal text-ink-2">· updating…</span>}
            </h2>
            {previewError && (
              <div className="mb-3">
                <ErrorNote message={previewError} onRetry={() => void preview()} />
              </div>
            )}
            {shownDesign
              ? <div className={previewing || previewError ? 'opacity-60' : undefined}><PreviewSummary design={shownDesign} hasCircuits={circuits.length > 0} /></div>
              : !previewError && <p className="text-ink-2">Add circuits above and the devices, the space they take and whether they fit appear here.</p>}
          </section>
        </form>
      )}
    </AppShell>
  )
}
