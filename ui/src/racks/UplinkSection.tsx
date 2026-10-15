import { useState } from 'react'
import { ApiError } from '../api/client'
import { MAX_UPLINK_TEXT, type HouseSwitch, type RackItemResponse, type SaveUplinkRequest } from '../api/types'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Field } from '../components/Field'
import { SelectField } from '../components/SelectField'
import { allPorts, defaultUplinkPort, fromName, uplinkText, wouldLoop } from './uplinks'

export interface UplinkProps {
  /** Every switch in the house, this one included, or null until they have loaded. */
  switches: HouseSwitch[] | null
  /** Why the switches did not load, if they did not. */
  switchesError?: string | null
  /** Fetches the switches again after they failed to load. */
  onRetrySwitches?: () => void
  /** Rejects with the server's refusal, which the form shows by the field it is about. */
  onSave: (request: SaveUplinkRequest) => Promise<void>
  onRemove: () => Promise<void>
}

type Mode = 'none' | 'switch' | 'text'

/** The request fields the form has a place for; anything else is shown under the whole form. */
const FIELDS = ['port', 'toItemId', 'toPort', 'toText']

/**
 * A switch's one uplink: the port it leaves by, and where it goes — another
 * switch's port anywhere in the house, or something said in words. The
 * server checks every choice again; the form only keeps the obvious wrong
 * ones out of reach, and says why beside each port it will not offer.
 */
export function UplinkSection({ item, rackId, switches, switchesError, onRetrySwitches, onSave, onRemove }: UplinkProps & {
  item: RackItemResponse
  /** The rack the sheet is in, so a switch from another is named with its rack. */
  rackId: string
}) {
  const uplink = item.uplink ?? null
  const [port, setPort] = useState(String(uplink?.port ?? defaultUplinkPort(item)))
  const [mode, setMode] = useState<Mode>(uplink ? (uplink.toText ? 'text' : 'switch') : 'none')
  const [toItemId, setToItemId] = useState(uplink?.toItemId ?? '')
  const [toPort, setToPort] = useState(uplink?.toPort ? String(uplink.toPort) : '')
  const [toText, setToText] = useState(uplink?.toText ?? '')
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [outside, setOutside] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const arriving = new Map((item.downlinks ?? []).map(d => [d.port, d]))
  const ownPorts = allPorts(item.portGroups).map(({ number, group }) => {
    const taker = arriving.get(number)
    return {
      value: String(number),
      label: `${number} · ${group.name}${taker ? ` — taken by ${fromName(taker, rackId)}` : ''}`,
      disabled: taker !== undefined,
    }
  })

  const others = (switches ?? []).filter(s => s.id !== item.id)
  const target = others.find(s => s.id === toItemId)
  const switchOptions: { value: string; label: string; disabled?: boolean; group?: string }[] = [
    { value: '', label: 'Choose a switch' },
    ...others.map(s => {
      const loop = wouldLoop(switches ?? [], item.id, s.id)
      return {
        value: s.id,
        label: `${s.label} · ${s.model}${loop ? ' — would make a loop' : ''}`,
        disabled: loop,
        group: `${s.locationName ?? 'Not placed'} · ${s.rackName}`,
      }
    }),
  ]
  // The switch it goes to stays on offer when the list has not loaded or has
  // lost it, so the picker never shows "Choose a switch" over a recorded link.
  const kept = uplink?.toItemId && !target && toItemId === uplink.toItemId
  if (kept) switchOptions.push({ value: uplink.toItemId!, label: `${uplink.toLabel} · ${uplink.toRackName}` })

  const targetPorts = target
    ? allPorts(target.portGroups).map(({ number, group }) => {
      const taker = target.downlinks.find(d => d.port === number && d.fromItemId !== item.id)
      const reason = number === target.uplinkPort ? 'its own uplink' : taker ? `taken by ${fromName(taker, rackId)}` : null
      return { value: String(number), label: `${number} · ${group.name}${reason ? ` — ${reason}` : ''}`, disabled: reason !== null }
    })
    : kept && uplink.toPort ? [{ value: String(uplink.toPort), label: String(uplink.toPort) }] : []

  const complete = mode === 'switch' ? Boolean(port && toItemId && toPort) : mode === 'text' ? toText.trim() !== '' : false

  const switchHint = switchesError
    ? undefined
    : switches === null ? 'Loading the house\'s switches…'
      : others.length === 0 ? 'There is no other switch in this house yet. Add one to a rack, or say where it goes in words.'
        : undefined

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setErrors({})
    setOutside([])
    try {
      await action()
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) {
        setErrors(e.fieldErrors)
        setOutside(e.errorsOutside(FIELDS))
      } else {
        setOutside([(e as Error).message])
      }
    } finally {
      setBusy(false)
    }
  }

  const save = () => run(() => onSave(mode === 'switch'
    ? { port: Number(port), toItemId, toPort: Number(toPort) }
    : { port: Number(port), toText: toText.trim() }))

  // The sheet keys this section on the recorded uplink, so a save or a
  // removal that lands starts it again from what the server now holds.
  const remove = () => run(onRemove)

  return (
    <section aria-labelledby={`uplink-${item.id}`} className="space-y-4">
      <div>
        <h3 id={`uplink-${item.id}`} className="font-semibold">Uplink</h3>
        <p className="text-sm text-ink-2 wrap-anywhere">
          {uplink ? `Port ${uplink.port}: ${uplinkText(uplink)}.` : 'No uplink recorded. A switch has one, to another switch or to the router.'}
        </p>
      </div>
      <SelectField label="Uplink port" value={port} onChange={setPort} options={ownPorts} errors={errors.port} />
      <SelectField
        label="Goes to"
        value={mode}
        onChange={value => setMode(value as Mode)}
        options={[
          { value: 'none', label: 'Not recorded' },
          { value: 'switch', label: 'Another switch in this house' },
          { value: 'text', label: 'Something else' },
        ]}
      />
      {mode === 'switch' && (
        <>
          <SelectField
            label="Switch"
            value={toItemId}
            onChange={value => { setToItemId(value); setToPort('') }}
            options={switchOptions}
            hint={switchHint}
            errors={errors.toItemId}
          />
          {switchesError && (
            <ErrorNote compact message={`The house's switches did not load: ${switchesError}`} onRetry={onRetrySwitches} />
          )}
          <SelectField
            label="Its port"
            value={toPort}
            onChange={setToPort}
            disabled={targetPorts.length === 0}
            options={[{ value: '', label: toItemId ? 'Choose a port' : 'Choose a switch first' }, ...targetPorts]}
            errors={errors.toPort}
          />
        </>
      )}
      {mode === 'text' && (
        <Field
          label="What it plugs into"
          value={toText}
          onChange={setToText}
          placeholder="UDM Pro, ISP fibre"
          maxLength={MAX_UPLINK_TEXT}
          autoCapitalize="words"
          autoComplete="off"
          errors={errors.toText}
        />
      )}
      {outside.length > 0 && (
        <ul className="space-y-0.5">
          {outside.map(message => <li key={message} className="text-sm font-medium text-danger">{message}</li>)}
        </ul>
      )}
      {mode === 'none' && uplink && <p className="text-sm text-ink-2">Remove uplink clears it, at both ends.</p>}
      {(mode !== 'none' || uplink) && (
        <div className="flex flex-wrap gap-2">
          {mode !== 'none' && <Button onClick={() => void save()} disabled={busy || !complete}>{busy ? 'Saving…' : 'Save uplink'}</Button>}
          {/* With "Not recorded" chosen, removing is what saving that choice means, so it is the primary action. */}
          {uplink && (
            <Button variant={mode === 'none' ? 'primary' : 'secondary'} onClick={() => void remove()} disabled={busy}>Remove uplink</Button>
          )}
        </div>
      )}
    </section>
  )
}
