import { describe, expect, it } from 'vitest'
import { buildCircuits } from '../src/wizard/buildCircuits'

describe('buildCircuits', () => {
  it('numbers each type from one and sequences them across the whole set', () => {
    const circuits = buildCircuits({ dimmed: 2, switched: 1, tape: [{ wattsPerMetre: 14.4, lengthMetres: 5 }] })

    expect(circuits.map(c => [c.type, c.name, c.sequence])).toEqual([
      ['DimmedLighting', 'Lighting 1', 1],
      ['DimmedLighting', 'Lighting 2', 2],
      ['Switched', 'Switched 1', 3],
      ['LedTape', 'Tape 1', 4],
    ])
  })

  it('carries tape dimensions onto the tape circuits only', () => {
    const circuits = buildCircuits({ dimmed: 1, switched: 0, tape: [{ wattsPerMetre: 9.6, lengthMetres: 4 }] })

    expect(circuits[0].wattsPerMetre).toBeUndefined()
    expect(circuits[1].wattsPerMetre).toBe(9.6)
    expect(circuits[1].lengthMetres).toBe(4)
  })

  it('returns nothing for an empty submain', () => {
    expect(buildCircuits({ dimmed: 0, switched: 0, tape: [] })).toEqual([])
  })
})
