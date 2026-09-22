import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { PanelSvg } from '../src/panel/PanelSvg'
import type { LayoutResponse, PlacedDeviceResponse } from '../src/api/types'

const terminal = (n: number, slot: number): PlacedDeviceResponse => ({
  id: `t${n}`, deviceTypeId: 'tb', category: 'Terminal240', rowIndex: 0, startSlot: slot,
  moduleWidth: 1, label: `L${n}`, terminalRole: 'Line', channels: [],
})

const dimmer: PlacedDeviceResponse = {
  id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
  moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
  channels: [
    { channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen ceiling', circuitRoom: null, isSpare: false },
    { channelIndex: 1, circuitId: null, circuitName: null, circuitRoom: null, isSpare: true },
  ],
}

const layout: LayoutResponse = {
  rows: 4, slotsPerRow: 54,
  devices: [terminal(1, 0), terminal(2, 1), terminal(3, 2), dimmer],
}

describe('PanelSvg', () => {
  it('draws a device with an accessible name naming its circuits', () => {
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /dimmer 1.*kitchen ceiling/i })).toBeInTheDocument()
  })

  it('groups a run of terminals into one labelled block', () => {
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /L1.*L3/ })).toBeInTheDocument()
  })

  it('reports which device was tapped', async () => {
    const onSelect = vi.fn()
    render(<PanelSvg layout={layout} selectedId={null} onSelect={onSelect} />)

    await userEvent.click(screen.getByRole('button', { name: /dimmer 1/i }))

    expect(onSelect).toHaveBeenCalledWith('d1')
  })

  it('marks the selected device for assistive tech, not just visually', () => {
    render(<PanelSvg layout={layout} selectedId="d1" onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /dimmer 1/i })).toHaveAttribute('aria-pressed', 'true')
  })

  it('mentions spare channels so an engineer knows what is free', () => {
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /1 spare channel/i })).toBeInTheDocument()
  })

  it('renders an empty layout without crashing', () => {
    render(<PanelSvg layout={{ rows: 2, slotsPerRow: 36, devices: [] }} selectedId={null} onSelect={() => {}} />)

    expect(screen.queryAllByRole('button')).toHaveLength(0)
  })
})
