import { render, screen, waitFor } from '@testing-library/react'
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
    expect(screen.getByText(/1 module wide/)).toBeInTheDocument()
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

    // Asked first: freeing a channel unwires a circuit, and a thumb slips.
    await userEvent.click(screen.getByRole('button', { name: /free this channel/i }))
    expect(onFreeChannel).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: /^free it$/i }))

    expect(onFreeChannel).toHaveBeenCalledWith(0)
  })

  it('saves on Return as well as on leaving the box', async () => {
    const onSaveCircuit = vi.fn()
    show({ onSaveCircuit })

    const name = screen.getByLabelText('Circuit name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Island pendants{Enter}')

    expect(onSaveCircuit).toHaveBeenCalledTimes(1)
    expect(onSaveCircuit).toHaveBeenCalledWith('c1', 'Island pendants', 'Kitchen')
  })

  it('says what a device without channels is and where it sits, rather than showing an empty list', () => {
    show({ device: { ...device, id: 'iso', category: 'Isolator', label: 'Isolator', rowIndex: 0, startSlot: 63, moduleWidth: 6, channels: [] } })

    expect(screen.getByRole('heading', { name: 'Isolator' })).toBeInTheDocument()
    expect(screen.getByText(/two-pole main switch/i)).toBeInTheDocument()
    expect(screen.getByText(/row 1, from module 22/i)).toBeInTheDocument()
    expect(screen.getByText(/drag it on the drawing — on a touchscreen, hold it first/i)).toBeInTheDocument()
    expect(screen.queryByText(/no channels to name/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
    // The subtitle does not repeat the title.
    expect(screen.getByText('2 modules wide · row 1')).toBeInTheDocument()
  })

  it('has a close button big enough for a thumb', async () => {
    const onClose = vi.fn()
    show({ onClose })

    await userEvent.click(screen.getByRole('button', { name: /close/i }))

    expect(onClose).toHaveBeenCalled()
  })

  it('closes on escape', async () => {
    const onClose = vi.fn()
    show({ onClose })

    await userEvent.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalled()
  })

  it('saves a room typed and then dismissed with Escape, as the sheet promises for leaving a box', async () => {
    const onSaveCircuit = vi.fn()
    const onClose = vi.fn()
    show({ onSaveCircuit, onClose })

    const room = screen.getByLabelText('Room')
    await userEvent.clear(room)
    await userEvent.type(room, 'Kitchen 219')
    await userEvent.keyboard('{Escape}')

    expect(onSaveCircuit).toHaveBeenCalledWith('c1', 'Ceiling spots', 'Kitchen 219')
    expect(onClose).toHaveBeenCalled()
  })

  it('tries again on Return after a failed save, without the text having to change', async () => {
    const onSaveCircuit = vi.fn()
      .mockRejectedValueOnce(new Error('Store down'))
      .mockResolvedValue(undefined)
    show({ onSaveCircuit })

    const name = screen.getByLabelText('Circuit name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Retry me{Enter}')
    await waitFor(() => expect(onSaveCircuit).toHaveBeenCalledTimes(1))

    // Return blurs the box, so back into it and Return once more with the same words.
    await userEvent.click(name)
    await userEvent.keyboard('{Enter}')
    await waitFor(() => expect(onSaveCircuit).toHaveBeenCalledTimes(2))
    expect(onSaveCircuit).toHaveBeenLastCalledWith('c1', 'Retry me', 'Kitchen')

    // Once it has succeeded, the same words are saved and are not sent a third time.
    await userEvent.click(name)
    await userEvent.keyboard('{Enter}')
    await userEvent.tab()
    expect(onSaveCircuit).toHaveBeenCalledTimes(2)
  })
})
