import { describe, expect, it } from 'vitest'
import { buildCircuits } from '../src/wizard/buildCircuits'

describe('buildCircuits', () => {
  it('numbers each type from one and sequences them across the whole set', () => {
    const circuits = buildCircuits({ dimmed: 2, switched: 1, blinds: 0, tape: [{ wattsPerMetre: 14.4, lengthMetres: 5, colour: false }] })

    expect(circuits.map(c => [c.type, c.name, c.sequence])).toEqual([
      ['DimmedLighting', 'Lighting 1', 1],
      ['DimmedLighting', 'Lighting 2', 2],
      ['Switched', 'Switched 1', 3],
      ['LedTape', 'Tape 1', 4],
    ])
  })

  it('carries tape dimensions onto the tape circuits only', () => {
    const circuits = buildCircuits({ dimmed: 1, switched: 0, blinds: 0, tape: [{ wattsPerMetre: 9.6, lengthMetres: 4, colour: false }] })

    expect(circuits[0].wattsPerMetre).toBeUndefined()
    expect(circuits[1].wattsPerMetre).toBe(9.6)
    expect(circuits[1].lengthMetres).toBe(4)
  })

  it('sequences blinds after the switched circuits', () => {
    const circuits = buildCircuits({ dimmed: 0, switched: 1, blinds: 2, tape: [] })

    expect(circuits.map(c => [c.type, c.name, c.sequence])).toEqual([
      ['Switched', 'Switched 1', 1],
      ['Cover', 'Blind 1', 2],
      ['Cover', 'Blind 2', 3],
    ])
  })

  it('sends a colour run to a controller rather than a 0-10V dimmer', () => {
    const circuits = buildCircuits({
      dimmed: 0, switched: 0, blinds: 0,
      tape: [
        { wattsPerMetre: 14.4, lengthMetres: 5, colour: false },
        { wattsPerMetre: 14.4, lengthMetres: 3, colour: true },
      ],
    })

    expect(circuits.map(c => [c.type, c.name])).toEqual([
      ['LedTape', 'Tape 1'],
      ['RgbwTape', 'Colour tape 2'],
    ])
  })

  it('carries the run dimensions onto a colour run too, so the driver is sized', () => {
    const circuits = buildCircuits({
      dimmed: 0, switched: 0, blinds: 0,
      tape: [{ wattsPerMetre: 19.2, lengthMetres: 6, colour: true }],
    })

    expect(circuits[0].wattsPerMetre).toBe(19.2)
    expect(circuits[0].lengthMetres).toBe(6)
  })

  it('returns nothing for an empty submain', () => {
    expect(buildCircuits({ dimmed: 0, switched: 0, blinds: 0, tape: [] })).toEqual([])
  })
})
