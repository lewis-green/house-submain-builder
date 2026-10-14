import type { CircuitInput } from '../api/types'

/**
 * A tape run. Colour runs go on an RGBWW controller instead of a 0-10V dimmer.
 * A blank name falls back to "Tape 1" and so on, as the other circuits are named.
 */
export interface TapeInput { name?: string; wattsPerMetre: number; lengthMetres: number; colour: boolean }

export interface CountsInput {
  dimmed: number
  switched: number
  blinds: number
  tape: TapeInput[]
}

export function buildCircuits({ dimmed, switched, blinds, tape }: CountsInput): CircuitInput[] {
  const circuits: CircuitInput[] = []
  let sequence = 1

  for (let n = 1; n <= dimmed; n++) {
    circuits.push({ type: 'DimmedLighting', name: `Lighting ${n}`, sequence: sequence++ })
  }
  for (let n = 1; n <= switched; n++) {
    circuits.push({ type: 'Switched', name: `Switched ${n}`, sequence: sequence++ })
  }
  for (let n = 1; n <= blinds; n++) {
    circuits.push({ type: 'Cover', name: `Blind ${n}`, sequence: sequence++ })
  }
  tape.forEach((t, i) => {
    circuits.push({
      type: t.colour ? 'RgbwTape' : 'LedTape',
      name: t.name?.trim() || `${t.colour ? 'Colour tape' : 'Tape'} ${i + 1}`,
      sequence: sequence++,
      wattsPerMetre: t.wattsPerMetre,
      lengthMetres: t.lengthMetres,
    })
  })

  return circuits
}
