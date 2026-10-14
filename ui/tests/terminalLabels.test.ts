import { describe, expect, it } from 'vitest'
import { terminalLabel } from '../src/panel/terminalLabels'

describe('terminalLabel', () => {
  it('is the bare number for a circuit on one terminal', () => {
    expect(terminalLabel(3, 1)).toBe('C3')
  })

  it('is the range for a bridged circuit', () => {
    expect(terminalLabel(2, 6)).toBe('C2.1–C2.6')
  })
})
