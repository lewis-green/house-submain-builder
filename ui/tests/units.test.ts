import { describe, expect, it } from 'vitest'
import { SLOT_UNITS_PER_MODULE, formatModules, toModules } from '../src/units'

describe('slot units', () => {
  it('counts three slot units to a DIN module', () => {
    expect(SLOT_UNITS_PER_MODULE).toBe(3)
    expect(toModules(3)).toBe(1)
    expect(toModules(9)).toBe(3)
  })

  it('formats a whole module without a decimal point', () => {
    expect(formatModules(3)).toBe('1T')
    expect(formatModules(9)).toBe('3T')
  })

  it('formats a part module to one decimal place', () => {
    expect(formatModules(1)).toBe('0.3T')
    expect(formatModules(4)).toBe('1.3T')
  })
})
