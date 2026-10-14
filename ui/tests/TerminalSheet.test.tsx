import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TerminalSheet } from '../src/panel/TerminalSheet'
import type { TerminalCircuit } from '../src/api/types'

const circuits: TerminalCircuit[] = [
  { circuitId: 'a', name: 'Kitchen', room: 'Kitchen', type: 'DimmedLighting', terminalCount: 1 },
  { circuitId: 'b', name: 'Hall', room: null, type: 'DimmedLighting', terminalCount: 1 },
  { circuitId: 'c', name: 'Porch', room: null, type: 'Switched', terminalCount: 1 },
]

function show(onSave = vi.fn().mockResolvedValue(undefined)) {
  render(<TerminalSheet circuits={circuits} onSave={onSave} onClose={() => {}} />)
  return onSave
}

const rows = () => screen.getAllByRole('listitem')
const names = () => rows().map(r => within(r).getByText(/Kitchen|Hall|Porch/, { selector: 'strong' }).textContent)

describe('TerminalSheet', () => {
  it('lists each circuit with the terminal it lands on', () => {
    show()
    expect(rows().map(r => within(r).getByTestId('terminal').textContent)).toEqual(['C1', 'C2', 'C3'])
    expect(names()).toEqual(['Kitchen', 'Hall', 'Porch'])
  })

  it('moves a circuit up and renumbers as it goes', async () => {
    show()
    await userEvent.click(screen.getByRole('button', { name: 'Move Porch up' }))
    expect(names()).toEqual(['Kitchen', 'Porch', 'Hall'])
    expect(within(rows()[1]).getByTestId('terminal').textContent).toBe('C2')
  })

  it('cannot move the first circuit up or the last one down', () => {
    show()
    expect(screen.getByRole('button', { name: 'Move Kitchen up' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Move Porch down' })).toBeDisabled()
  })

  it('shows the range when a circuit lands on several terminals', async () => {
    show()
    await userEvent.click(within(rows()[1]).getByRole('button', { name: /more cables/i }))
    await userEvent.click(within(rows()[1]).getByRole('button', { name: /more cables/i }))
    expect(within(rows()[1]).getByTestId('terminal').textContent).toBe('C2.1–C2.3')
  })

  it('saves nothing until something has changed', () => {
    show()
    expect(screen.getByRole('button', { name: 'Save order' })).toBeDisabled()
  })

  it('saves the circuits in their new order with their counts', async () => {
    const onSave = show()
    await userEvent.click(screen.getByRole('button', { name: 'Move Hall down' }))
    await userEvent.click(within(rows()[0]).getByRole('button', { name: /more cables/i }))
    await userEvent.click(screen.getByRole('button', { name: 'Save order' }))
    expect(onSave).toHaveBeenCalledWith([
      { circuitId: 'a', terminalCount: 2 },
      { circuitId: 'c', terminalCount: 1 },
      { circuitId: 'b', terminalCount: 1 },
    ])
  })

  it('keeps the edit when the save is refused', async () => {
    const onSave = vi.fn().mockRejectedValue(new Error('no room'))
    show(onSave)
    await userEvent.click(screen.getByRole('button', { name: 'Move Porch up' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save order' }))
    expect(names()).toEqual(['Kitchen', 'Porch', 'Hall'])
    expect(screen.getByRole('button', { name: 'Save order' })).toBeEnabled()
  })
})
