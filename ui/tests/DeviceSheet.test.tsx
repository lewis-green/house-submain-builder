import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DeviceSheet } from '../src/panel/DeviceSheet'
import type { PlacedDeviceResponse } from '../src/api/types'

const device: PlacedDeviceResponse = {
  id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
  moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
  channels: [
    { channelIndex: 0, circuitId: 'c1', circuitName: 'Ceiling spots', circuitRoom: 'Kitchen', isSpare: false },
    { channelIndex: 1, circuitId: null, circuitName: null, circuitRoom: null, isSpare: true },
  ],
}

const noop = () => {}
const rooms = ['Boot room', 'Kitchen', 'Snug']

function show(props: Partial<React.ComponentProps<typeof DeviceSheet>> = {}) {
  return render(
    <DeviceSheet
      device={device}
      rooms={rooms}
      onSaveCircuit={noop}
      onFreeChannel={noop}
      onClose={noop}
      {...props}
    />,
  )
}

describe('DeviceSheet', () => {
  it('shows the device, its width in modules and each channel', () => {
    show()

    expect(screen.getByRole('heading', { name: 'Dimmer 1' })).toBeInTheDocument()
    expect(screen.getByText(/1T/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('Ceiling spots')).toBeInTheDocument()
    expect(screen.getByText(/spare/i)).toBeInTheDocument()
  })

  it('shows the room the channel feeds next to its circuit name', () => {
    show()

    expect(screen.getByLabelText('Room')).toHaveValue('Kitchen')
    expect(screen.getByLabelText('Circuit name')).toHaveValue('Ceiling spots')
  })

  it('offers the rooms already used in the house', () => {
    const { container } = show()

    const options = [...container.querySelectorAll('datalist option')].map(o => o.getAttribute('value'))
    expect(options).toEqual(rooms)
    expect(screen.getByLabelText('Room')).toHaveAttribute('list', 'known-rooms')
  })

  it('saves the room and the name together, so neither wipes the other', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    const name = screen.getByLabelText('Circuit name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Island pendants')
    await userEvent.tab()

    expect(onSaveCircuit).toHaveBeenCalledWith('c1', 'Island pendants', 'Kitchen')
  })

  it('saves the name alongside a changed room', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    const room = screen.getByLabelText('Room')
    await userEvent.clear(room)
    await userEvent.type(room, 'Snug')
    await userEvent.tab()

    expect(onSaveCircuit).toHaveBeenCalledWith('c1', 'Ceiling spots', 'Snug')
  })

  it('sends no room rather than an empty one when the field is cleared', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    await userEvent.clear(screen.getByLabelText('Room'))
    await userEvent.tab()

    expect(onSaveCircuit).toHaveBeenCalledWith('c1', 'Ceiling spots', null)
  })

  it('saves once when tabbing across both fields after a single change', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    const room = screen.getByLabelText('Room')
    await userEvent.clear(room)
    await userEvent.type(room, 'Snug')
    await userEvent.tab()
    await userEvent.tab()

    expect(onSaveCircuit).toHaveBeenCalledTimes(1)
  })

  it('does not call the API when nothing changed', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    await userEvent.click(screen.getByLabelText('Circuit name'))
    await userEvent.tab()

    expect(onSaveCircuit).not.toHaveBeenCalled()
  })

  it('does not save a circuit with its name cleared', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    await userEvent.clear(screen.getByLabelText('Circuit name'))
    await userEvent.tab()

    expect(onSaveCircuit).not.toHaveBeenCalled()
  })

  it('offers no name or room boxes for a spare channel', () => {
    show()

    // One assigned channel and one spare: exactly one pair of fields.
    expect(screen.getAllByLabelText('Room')).toHaveLength(1)
    expect(screen.getAllByLabelText('Circuit name')).toHaveLength(1)
  })

  it('frees a channel on request', async () => {
    const onFreeChannel = vi.fn()
    show({ onFreeChannel })

    await userEvent.click(screen.getByRole('button', { name: /free this channel/i }))

    expect(onFreeChannel).toHaveBeenCalledWith(0)
  })

  it('closes on escape', async () => {
    const onClose = vi.fn()
    show({ onClose })

    await userEvent.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalled()
  })
})
