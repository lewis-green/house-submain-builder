import type { CircuitInput, CircuitResponse, CircuitType } from '../api/types'
import { COUNTED_TYPES, NEW_TAPE_RUN, tapeName, tapeType, type CountsInput } from './buildCircuits'

/**
 * Editing a built panel through the same counts that built it. The form only
 * knows "two dimmed, one switched"; the panel knows which two, what the engineer
 * has called them since, the rooms they feed and where they land on the rail.
 * These map one onto the other so the counts can change without losing that.
 *
 * The rule is the simplest one an engineer can predict: the circuits of a type
 * are kept in panel order, a higher count adds new ones after them, and a lower
 * count drops the last ones. Tape runs are listed one by one, so they are kept
 * by id instead.
 */

const isTape = (type: CircuitType) => type === 'LedTape' || type === 'RgbwTape'

const inSequence = (circuits: readonly CircuitResponse[]) => [...circuits].sort((a, b) => a.sequence - b.sequence)

const ofType = (circuits: readonly CircuitResponse[], type: CircuitType) =>
  inSequence(circuits).filter(c => c.type === type)

/** The counts and tape runs a built panel would show on the form. */
export function countsFromCircuits(existing: readonly CircuitResponse[]): CountsInput {
  const count = (type: CircuitType) => existing.filter(c => c.type === type).length

  return {
    dimmed: count('DimmedLighting'),
    switched: count('Switched'),
    blinds: count('Cover'),
    tape: inSequence(existing).filter(c => isTape(c.type)).map(c => ({
      id: c.id,
      name: c.name,
      // Only a run saved before tape carried its size can lack one: start it
      // where a new run starts rather than at zero watts.
      wattsPerMetre: c.wattsPerMetre ?? NEW_TAPE_RUN.wattsPerMetre,
      lengthMetres: c.lengthMetres ?? NEW_TAPE_RUN.lengthMetres,
      colour: c.type === 'RgbwTape',
    })),
  }
}

/**
 * The whole circuit list to send with the edit. A kept circuit goes back with
 * its id, which keeps its terminal place, and with its room, which the server
 * would otherwise clear. A new one goes without an id and is named as the
 * create form names it — "Lighting 3" for the third dimmed circuit — passing
 * over any number a kept circuit already answers to.
 */
export function circuitsForEdit(existing: readonly CircuitResponse[], counts: CountsInput): CircuitInput[] {
  const circuits: CircuitInput[] = []
  let sequence = 1

  for (const { count, type, prefix } of COUNTED_TYPES) {
    const kept = ofType(existing, type).slice(0, counts[count])
    for (const c of kept) {
      circuits.push({ id: c.id, type, name: c.name, room: c.room, sequence: sequence++ })
    }

    const taken = new Set(kept.map(c => c.name))
    let n = kept.length + 1
    for (let added = kept.length; added < counts[count]; added++) {
      while (taken.has(`${prefix} ${n}`)) n++
      circuits.push({ type, name: `${prefix} ${n++}`, sequence: sequence++ })
    }
  }

  const byId = new Map(existing.map(c => [c.id, c]))
  counts.tape.forEach((t, i) => {
    const stored = t.id === undefined ? undefined : byId.get(t.id)
    circuits.push({
      ...(stored && { id: stored.id }),
      type: tapeType(t.colour),
      name: tapeName(t, i),
      ...(stored && { room: stored.room }),
      sequence: sequence++,
      wattsPerMetre: t.wattsPerMetre,
      lengthMetres: t.lengthMetres,
    })
  })

  return circuits
}

/** The circuits saving these counts would delete, in panel order, to warn before it happens. */
export function removedByEdit(existing: readonly CircuitResponse[], counts: CountsInput): CircuitResponse[] {
  const dropped = new Set<string>()

  for (const { count, type } of COUNTED_TYPES) {
    for (const c of ofType(existing, type).slice(counts[count])) dropped.add(c.id)
  }

  const runs = new Set(counts.tape.map(t => t.id))
  for (const c of existing) {
    if (isTape(c.type) && !runs.has(c.id)) dropped.add(c.id)
  }

  return inSequence(existing).filter(c => dropped.has(c.id))
}

/** How many removed circuits the warning names before it starts counting. */
const NAMED_IN_WARNING = 5

/**
 * The removed circuits as the warning says them: a handful by name, the rest
 * counted, so dropping a dozen cannot fill the action bar. "and 1 more" would
 * take as much room as the name it hides, so a sixth is simply named.
 */
export function namesRemoved(removed: readonly CircuitResponse[]): string {
  const names = removed.map(c => c.name)
  if (names.length <= NAMED_IN_WARNING + 1) return names.join(', ')
  return `${names.slice(0, NAMED_IN_WARNING).join(', ')} and ${names.length - NAMED_IN_WARNING} more`
}
