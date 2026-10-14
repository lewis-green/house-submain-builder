import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { ItemSheet } from '../src/racks/ItemSheet'

const sw: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 't48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, poeBudgetW: 720,
  portGroups: [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }],
  ports: [{ number: 3, label: 'Office', poe: false }],
}
const layout: RackLayoutResponse = {
  rackId: 'r1', heightU: 12, freeU: 10, layoutVersion: 3,
  items: [sw, { ...sw, id: 'i2', label: 'Patch panel 1', topU: 11, ports: [] }],
}

function show(onSavePort = vi.fn(async () => {}), extra: Partial<Parameters<typeof ItemSheet>[0]> = {}) {
  const onMove = vi.fn()
  const onRemove = vi.fn()
  render(<ItemSheet item={sw} layout={layout} onSavePort={onSavePort} onMove={onMove} onRemove={onRemove} onClose={() => {}} {...extra} />)
  return { onSavePort, onMove, onRemove }
}

describe('ItemSheet', () => {
  it('groups the rows by port group, and an SFP+ row offers no PoE tick and says why', () => {
    show()

    expect(screen.getByRole('heading', { name: /1G · ports 1–4/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /SFP\+ · ports 5–6 \(no PoE\)/ })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Port 1 PoE' })).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Port 5 PoE' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Port 3 label')).toHaveValue('Office')
  })

  it('Return saves the label and moves straight on to the next port', async () => {
    const { onSavePort } = show()

    await userEvent.type(screen.getByLabelText('Port 1 label'), 'Kitchen AP{Enter}')

    expect(onSavePort).toHaveBeenCalledWith(1, 'Kitchen AP', false)
    expect(screen.getByLabelText('Port 2 label')).toHaveFocus()
    expect(onSavePort).toHaveBeenCalledTimes(1)
  })

  it('saves on leaving a field, once, and not again for the same text', async () => {
    const { onSavePort } = show()

    await userEvent.type(screen.getByLabelText('Port 2 label'), 'Lounge TV')
    await userEvent.tab()
    await userEvent.click(screen.getByLabelText('Port 2 label'))
    await userEvent.tab()

    expect(onSavePort).toHaveBeenCalledTimes(1)
    expect(onSavePort).toHaveBeenCalledWith(2, 'Lounge TV', false)
  })

  it('a PoE tick saves straight away, with whatever label has been typed', async () => {
    const { onSavePort } = show()

    await userEvent.type(screen.getByLabelText('Port 4 label'), 'Garden cam')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Port 4 PoE' }))

    expect(onSavePort).toHaveBeenLastCalledWith(4, 'Garden cam', true)
    expect(screen.getByRole('checkbox', { name: 'Port 4 PoE' })).toBeChecked()
  })

  it('keeps the typed label when the server refuses it, and Return tries the same text again', async () => {
    const onSavePort = vi.fn()
      .mockRejectedValueOnce(new Error('refused'))
      .mockResolvedValueOnce(undefined)
    show(onSavePort)

    const box = screen.getByLabelText('Port 1 label')
    await userEvent.type(box, 'Kitchen AP')
    await userEvent.tab()
    expect(box).toHaveValue('Kitchen AP')

    await userEvent.click(box)
    await userEvent.keyboard('{Enter}')

    expect(onSavePort).toHaveBeenCalledTimes(2)
    expect(onSavePort).toHaveBeenLastCalledWith(1, 'Kitchen AP', false)
  })

  it('unticks the box again when a PoE save is refused', async () => {
    show(vi.fn().mockRejectedValue(new Error('refused')))

    await userEvent.click(screen.getByRole('checkbox', { name: 'Port 2 PoE' }))

    expect(screen.getByRole('checkbox', { name: 'Port 2 PoE' })).not.toBeChecked()
  })

  it('tapping a port on the front panel jumps to its row', async () => {
    show()

    await userEvent.click(within(screen.getByTestId('front-panel')).getByText('4', { selector: '[data-port]' }))

    expect(screen.getByLabelText('Port 4 label')).toHaveFocus()
  })

  it('offers only free U to move to, and moves on a pick', async () => {
    const { onMove } = show()

    const select = screen.getByLabelText('Move to')
    const options = within(select).getAllByRole('option').map(o => o.textContent)
    expect(options[0]).toBe('Stays in U12')
    expect(options).not.toContain('U11')
    expect(options).toContain('U10')

    await userEvent.selectOptions(select, '10')
    expect(onMove).toHaveBeenCalledWith(10)
  })

  it('asks before removing, naming the labels that go with it', async () => {
    const { onRemove } = show()

    await userEvent.click(screen.getByRole('button', { name: 'Remove from rack' }))
    expect(screen.getByText('Remove Switch 1 and its 1 port label?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Yes, remove it' }))

    expect(onRemove).toHaveBeenCalled()
  })
})
