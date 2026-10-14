import type { CircuitInput, CircuitType } from '../api/types'

/**
 * A tape run. Colour runs go on an RGBWW controller instead of a 0-10V dimmer.
 * A blank name falls back to "Tape 1" and so on, as the other circuits are named.
 * The id is the circuit's, when the run was read back from a built panel to edit.
 */
export interface TapeInput { id?: string; name?: string; wattsPerMetre: number; lengthMetres: number; colour: boolean }

/** What Add tape circuit starts a run at: a common white tape, a typical cabinet run. */
export const NEW_TAPE_RUN: TapeInput = { wattsPerMetre: 14.4, lengthMetres: 5, colour: false }

export interface CountsInput {
  dimmed: number
  switched: number
  blinds: number
  tape: TapeInput[]
}

/**
 * The circuits the form counts rather than lists, in the order they are
 * sequenced, and what one is called until the engineer renames it. Shared with
 * the edit, so a circuit added to a built panel is named as a new one would be.
 */
export const COUNTED_TYPES = [
  { count: 'dimmed', type: 'DimmedLighting', prefix: 'Lighting' },
  { count: 'switched', type: 'Switched', prefix: 'Switched' },
  { count: 'blinds', type: 'Cover', prefix: 'Blind' },
] as const satisfies readonly { count: keyof CountsInput; type: CircuitType; prefix: string }[]

export const tapeType = (colour: boolean): CircuitType => colour ? 'RgbwTape' : 'LedTape'

/** The name a run goes by: what the engineer typed, or its place among the runs. */
export const tapeName = (t: TapeInput, index: number) =>
  t.name?.trim() || `${t.colour ? 'Colour tape' : 'Tape'} ${index + 1}`

export function buildCircuits(counts: CountsInput): CircuitInput[] {
  const circuits: CircuitInput[] = []
  let sequence = 1

  for (const { count, type, prefix } of COUNTED_TYPES) {
    for (let n = 1; n <= counts[count]; n++) {
      circuits.push({ type, name: `${prefix} ${n}`, sequence: sequence++ })
    }
  }
  counts.tape.forEach((t, i) => {
    circuits.push({
      type: tapeType(t.colour),
      name: tapeName(t, i),
      sequence: sequence++,
      wattsPerMetre: t.wattsPerMetre,
      lengthMetres: t.lengthMetres,
    })
  })

  return circuits
}
