import { describe, expect, it } from 'vitest'
import type { CircuitResponse } from '../src/api/types'
import { buildCircuits } from '../src/wizard/buildCircuits'
import { circuitsForEdit, countsFromCircuits, removedByEdit } from '../src/wizard/editCircuits'

// A panel as the engineer left it: some circuits renamed and given rooms since the build.
const existing: CircuitResponse[] = [
  { id: 'd1', type: 'DimmedLighting', name: 'Hall lights', room: 'Hall', sequence: 1, wattsPerMetre: null, lengthMetres: null },
  { id: 'd2', type: 'DimmedLighting', name: 'Lighting 2', room: null, sequence: 2, wattsPerMetre: null, lengthMetres: null },
  { id: 's1', type: 'Switched', name: 'Landing', room: 'Landing', sequence: 3, wattsPerMetre: null, lengthMetres: null },
  { id: 'b1', type: 'Cover', name: 'Blind 1', room: null, sequence: 4, wattsPerMetre: null, lengthMetres: null },
  { id: 't1', type: 'LedTape', name: 'Kitchen plinth', room: 'Kitchen', sequence: 5, wattsPerMetre: 9.6, lengthMetres: 4 },
  { id: 't2', type: 'RgbwTape', name: 'Snug cove', room: null, sequence: 6, wattsPerMetre: 19.2, lengthMetres: 3 },
]

// The API orders by sequence, but nothing here should depend on it.
const shuffled = [existing[4], existing[1], existing[5], existing[0], existing[3], existing[2]]

describe('countsFromCircuits', () => {
  it('counts each type and reads the tape runs back in sequence order, carrying their ids', () => {
    expect(countsFromCircuits(shuffled)).toEqual({
      dimmed: 2,
      switched: 1,
      blinds: 1,
      tape: [
        { id: 't1', name: 'Kitchen plinth', wattsPerMetre: 9.6, lengthMetres: 4, colour: false },
        { id: 't2', name: 'Snug cove', wattsPerMetre: 19.2, lengthMetres: 3, colour: true },
      ],
    })
  })

  it('gives a tape run with no stored dimensions the same defaults as a new run', () => {
    const counts = countsFromCircuits([
      { id: 't1', type: 'LedTape', name: 'Tape 1', room: null, sequence: 1, wattsPerMetre: null, lengthMetres: null },
    ])

    expect(counts.tape).toEqual([{ id: 't1', name: 'Tape 1', wattsPerMetre: 14.4, lengthMetres: 5, colour: false }])
  })

  it('reads an empty panel as all zeroes', () => {
    expect(countsFromCircuits([])).toEqual({ dimmed: 0, switched: 0, blinds: 0, tape: [] })
  })
})

describe('circuitsForEdit', () => {
  it('sends every circuit back unchanged, by id and with its room, when nothing was edited', () => {
    const circuits = circuitsForEdit(shuffled, countsFromCircuits(shuffled))

    expect(circuits).toEqual([
      { id: 'd1', type: 'DimmedLighting', name: 'Hall lights', room: 'Hall', sequence: 1 },
      { id: 'd2', type: 'DimmedLighting', name: 'Lighting 2', room: null, sequence: 2 },
      { id: 's1', type: 'Switched', name: 'Landing', room: 'Landing', sequence: 3 },
      { id: 'b1', type: 'Cover', name: 'Blind 1', room: null, sequence: 4 },
      { id: 't1', type: 'LedTape', name: 'Kitchen plinth', room: 'Kitchen', sequence: 5, wattsPerMetre: 9.6, lengthMetres: 4 },
      { id: 't2', type: 'RgbwTape', name: 'Snug cove', room: null, sequence: 6, wattsPerMetre: 19.2, lengthMetres: 3 },
    ])
  })

  it('adds a new circuit after the kept ones of its type, numbered by its place there, and resequences the rest', () => {
    const counts = { ...countsFromCircuits(existing), dimmed: 3 }

    const circuits = circuitsForEdit(existing, counts)

    expect(circuits.map(c => [c.id, c.type, c.name, c.sequence])).toEqual([
      ['d1', 'DimmedLighting', 'Hall lights', 1],
      ['d2', 'DimmedLighting', 'Lighting 2', 2],
      [undefined, 'DimmedLighting', 'Lighting 3', 3],
      ['s1', 'Switched', 'Landing', 4],
      ['b1', 'Cover', 'Blind 1', 5],
      ['t1', 'LedTape', 'Kitchen plinth', 6],
      ['t2', 'RgbwTape', 'Snug cove', 7],
    ])
    expect(circuits[2]).not.toHaveProperty('room')
  })

  it('skips a number a kept circuit already answers to, so no two circuits share a name', () => {
    const panel: CircuitResponse[] = [
      { id: 's1', type: 'Switched', name: 'Switched 3', room: null, sequence: 1, wattsPerMetre: null, lengthMetres: null },
      { id: 's2', type: 'Switched', name: 'Porch', room: null, sequence: 2, wattsPerMetre: null, lengthMetres: null },
    ]

    const circuits = circuitsForEdit(panel, { dimmed: 0, switched: 4, blinds: 0, tape: [] })

    expect(circuits.map(c => c.name)).toEqual(['Switched 3', 'Porch', 'Switched 4', 'Switched 5'])
  })

  it('drops the last circuits of a type when its count goes down', () => {
    const counts = { ...countsFromCircuits(existing), dimmed: 1, blinds: 0 }

    const circuits = circuitsForEdit(existing, counts)

    expect(circuits.map(c => c.id)).toEqual(['d1', 's1', 't1', 't2'])
    expect(circuits.map(c => c.sequence)).toEqual([1, 2, 3, 4])
  })

  it('keeps a tape run by id through a rename, a new size and a change of colour', () => {
    const counts = countsFromCircuits(existing)
    counts.tape = [
      { ...counts.tape[0], name: 'Island plinth', lengthMetres: 6, colour: true },
      counts.tape[1],
    ]

    const [plinth] = circuitsForEdit(existing, counts).filter(c => c.id === 't1')

    expect(plinth).toEqual({
      id: 't1', type: 'RgbwTape', name: 'Island plinth', room: 'Kitchen', sequence: 5, wattsPerMetre: 9.6, lengthMetres: 6,
    })
  })

  it('sends a new tape run without an id, and an unnamed one under its numbered name', () => {
    const counts = countsFromCircuits(existing)
    counts.tape = [counts.tape[1], { wattsPerMetre: 14.4, lengthMetres: 5, colour: false }]

    const tape = circuitsForEdit(existing, counts).filter(c => c.type === 'LedTape' || c.type === 'RgbwTape')

    expect(tape).toEqual([
      { id: 't2', type: 'RgbwTape', name: 'Snug cove', room: null, sequence: 5, wattsPerMetre: 19.2, lengthMetres: 3 },
      { type: 'LedTape', name: 'Tape 2', sequence: 6, wattsPerMetre: 14.4, lengthMetres: 5 },
    ])
  })

  it('builds exactly what the create form would for a panel with no circuits yet', () => {
    const counts = {
      dimmed: 2, switched: 1, blinds: 1,
      tape: [{ name: 'Plinth', wattsPerMetre: 9.6, lengthMetres: 4, colour: false }, { wattsPerMetre: 14.4, lengthMetres: 3, colour: true }],
    }

    expect(circuitsForEdit([], counts)).toEqual(buildCircuits(counts))
  })
})

describe('removedByEdit', () => {
  it('names nothing when every circuit is kept', () => {
    expect(removedByEdit(existing, countsFromCircuits(existing))).toEqual([])
  })

  it('lists the circuits a lower count drops, from the end of their type, and the tape runs taken away, in panel order', () => {
    const counts = { ...countsFromCircuits(shuffled), dimmed: 0, switched: 0 }
    counts.tape = [counts.tape[1]]

    expect(removedByEdit(shuffled, counts).map(c => c.name)).toEqual(['Hall lights', 'Lighting 2', 'Landing', 'Kitchen plinth'])
  })

  it('drops the last of a type, not the first', () => {
    const counts = { ...countsFromCircuits(existing), dimmed: 1 }

    expect(removedByEdit(existing, counts).map(c => c.id)).toEqual(['d2'])
  })
})
