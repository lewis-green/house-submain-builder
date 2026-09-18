import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DeviceSheet } from '../src/panel/DeviceSheet'
import type { PlacedDeviceResponse } from '../src/api/types'

const device: PlacedDeviceResponse = {
  id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
  moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
  channels: [
    { channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen ceiling', isSpare: false },
    { channelIndex: 1, circuitId: null, circuitName: null, isSpare: true },
  ],
}

const noop = () => {}

describe('DeviceSheet', () => {
  it('shows the device, its width in modules and each channel', () => {
    render(<DeviceSheet device={device} onRenameCircuit={noop} onFreeChannel={noop} onClose={noop} />)

    expect(screen.getByRole('heading', { name: 'Dimmer 1' })).toBeInTheDocument()
    expect(screen.getByText(/1T/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('Kitchen ceiling')).toBeInTheDocument()
    expect(screen.getByText(/spare/i)).toBeInTheDocument()
  })

  it('renames a circuit through its channel', async () => {
    const onRenameCircuit = vi.fn()
    render(<DeviceSheet device={device} onRenameCircuit={onRenameCircuit} onFreeChannel={noop} onClose={noop} />)

    const input = screen.getByDisplayValue('Kitchen ceiling')
    await userEvent.clear(input)
    await userEvent.type(input, 'Kitchen island')
    await userEvent.tab()

    expect(onRenameCircuit).toHaveBeenCalledWith('c1', 'Kitchen island')
  })

  it('does not call the API when the name comes back unchanged', async () => {
    const onRenameCircuit = vi.fn()
    render(<DeviceSheet device={device} onRenameCircuit={onRenameCircuit} onFreeChannel={noop} onClose={noop} />)

    await userEvent.click(screen.getByDisplayValue('Kitchen ceiling'))
    await userEvent.tab()

    expect(onRenameCircuit).not.toHaveBeenCalled()
  })

  it('offers no rename box for a spare channel', () => {
    render(<DeviceSheet device={device} onRenameCircuit={noop} onFreeChannel={noop} onClose={noop} />)

    expect(screen.getAllByRole('textbox')).toHaveLength(1)
  })

  it('frees a channel on request', async () => {
    const onFreeChannel = vi.fn()
    render(<DeviceSheet device={device} onRenameCircuit={noop} onFreeChannel={onFreeChannel} onClose={noop} />)

    await userEvent.click(screen.getByRole('button', { name: /free this channel/i }))

    expect(onFreeChannel).toHaveBeenCalledWith(0)
  })

  it('closes on escape', async () => {
    const onClose = vi.fn()
    render(<DeviceSheet device={device} onRenameCircuit={noop} onFreeChannel={noop} onClose={onClose} />)

    await userEvent.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalled()
  })
})
