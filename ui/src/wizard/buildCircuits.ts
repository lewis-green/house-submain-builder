import type { CircuitInput } from '../api/types'

export interface TapeInput { wattsPerMetre: number; lengthMetres: number }
export interface CountsInput { dimmed: number; switched: number; tape: TapeInput[] }

export function buildCircuits({ dimmed, switched, tape }: CountsInput): CircuitInput[] {
  const circuits: CircuitInput[] = []
  let sequence = 1

  for (let n = 1; n <= dimmed; n++) {
    circuits.push({ type: 'DimmedLighting', name: `Lighting ${n}`, sequence: sequence++ })
  }
  for (let n = 1; n <= switched; n++) {
    circuits.push({ type: 'Switched', name: `Switched ${n}`, sequence: sequence++ })
  }
  tape.forEach((t, i) => {
    circuits.push({
      type: 'LedTape',
      name: `Tape ${i + 1}`,
      sequence: sequence++,
      wattsPerMetre: t.wattsPerMetre,
      lengthMetres: t.lengthMetres,
    })
  })

  return circuits
}
