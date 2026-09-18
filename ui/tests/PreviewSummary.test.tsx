import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { PreviewSummary } from '../src/wizard/PreviewSummary'
import type { DesignResponse } from '../src/api/types'

const design = (
  summary: Partial<DesignResponse['summary']> = {},
  diagnostics: DesignResponse['diagnostics'] = [],
): DesignResponse => ({
  submainId: 's1',
  layoutVersion: 0,
  layout: {
    rows: 4,
    slotsPerRow: 54,
    devices: [
      { id: null, deviceTypeId: 'd', category: 'Dimmer240', rowIndex: 1, startSlot: 0, moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None', channels: [] },
      { id: null, deviceTypeId: 'r', category: 'Relay', rowIndex: 2, startSlot: 0, moduleWidth: 9, label: 'Relay 1', terminalRole: 'None', channels: [] },
    ],
  },
  diagnostics,
  bom: [],
  summary: { rowsUsed: 3, slotsUsed: 36, totalSlots: 216, deviceCount: 2, spareChannels: 1, ...summary },
})

describe('PreviewSummary', () => {
  it('counts devices by kind in modules, not slot units', () => {
    render(<PreviewSummary design={design()} />)

    expect(screen.getByText(/1 dimmer/i)).toBeInTheDocument()
    expect(screen.getByText(/1 relay/i)).toBeInTheDocument()
    expect(screen.getByText(/12T of 72T/i)).toBeInTheDocument()
  })

  it('shows an error diagnostic with its suggestion', () => {
    render(<PreviewSummary design={design({}, [
      { severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'Needs 5 rows', suggestion: 'Use the 6x24' },
    ])} />)

    expect(screen.getByText(/needs 5 rows/i)).toBeInTheDocument()
    expect(screen.getByText(/use the 6x24/i)).toBeInTheDocument()
  })

  it('does not claim a design fits when there is an error', () => {
    render(<PreviewSummary design={design({}, [
      { severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'Needs 5 rows', suggestion: null },
    ])} />)

    expect(screen.queryByText(/fits/i)).not.toBeInTheDocument()
  })

  it('says a design fits when there is nothing to report', () => {
    render(<PreviewSummary design={design()} />)

    expect(screen.getByText(/fits/i)).toBeInTheDocument()
  })

  it('puts errors above warnings', () => {
    render(<PreviewSummary design={design({}, [
      { severity: 'Warning', code: 'TAPE_LOAD_MISSING', message: 'No tape load', suggestion: null },
      { severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'Needs 5 rows', suggestion: null },
    ])} />)

    const items = screen.getAllByRole('listitem').map(li => li.textContent)
    expect(items.findIndex(t => t?.includes('Needs 5 rows')))
      .toBeLessThan(items.findIndex(t => t?.includes('No tape load')))
  })
})
