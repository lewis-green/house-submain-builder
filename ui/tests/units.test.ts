import { describe, expect, it } from 'vitest'
import { SLOT_UNITS_PER_MODULE, formatModuleCount, formatModules, toModules } from '../src/units'

describe('slot units', () => {
  it('counts three slot units to a DIN module', () => {
    expect(SLOT_UNITS_PER_MODULE).toBe(3)
    expect(toModules(3)).toBe(1)
    expect(toModules(9)).toBe(3)
  })

  it('formats a whole module without a decimal point', () => {
    expect(formatModuleCount(3)).toBe('1')
    expect(formatModuleCount(9)).toBe('3')
  })

  it('formats a part module to one decimal place', () => {
    expect(formatModuleCount(1)).toBe('0.3')
    expect(formatModuleCount(4)).toBe('1.3')
  })

  it('spells the unit out, because "T" means nothing to a newcomer', () => {
    expect(formatModules(3)).toBe('1 module')
    expect(formatModules(4)).toBe('1.3 modules')
    expect(formatModules(9)).toBe('3 modules')
  })
})
