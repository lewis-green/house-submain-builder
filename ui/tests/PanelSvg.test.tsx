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

    // A picture, not a button: terminals have nothing to name or move, so a
    // tap on them would only open an empty sheet.
    expect(screen.getByRole('img', { name: /L1.*L3/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /L1/ })).not.toBeInTheDocument()
  })

  it('says in words, not only colour, when a dragged device has no room', () => {
    const { container } = render(
      <PanelSvg
        layout={layout}
        selectedId={null}
        onSelect={() => {}}
        dragging={{ deviceId: 'd1', rowIndex: 1, startSlot: 3, valid: false }}
      />,
    )

    expect(container.textContent).toMatch(/no room/i)
  })

  it('draws every label at a size that can be read at default zoom', () => {
    const { container } = render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    const sizes = [...container.querySelectorAll('text')]
      .map(t => Number(/text-\[(\d+)px\]/.exec(t.getAttribute('class') ?? '')?.[1]))
    expect(sizes.length).toBeGreaterThan(0)
    expect(Math.min(...sizes)).toBeGreaterThanOrEqual(12)
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

  it('keeps the server\'s label for a blinds controller and says what it is, as the PDF does', () => {
    // "Cover 1" is the label everywhere — screen, PDF, stored positions — and
    // "Blinds" is its kind, so a person who has never met a Shelly Cover still knows.
    const cover: PlacedDeviceResponse = { ...dimmer, id: 'cv1', category: 'Cover', label: 'Cover 1', startSlot: 6, channels: [] }
    const { container } = render(<PanelSvg layout={{ ...layout, devices: [cover] }} selectedId={null} onSelect={() => {}} />)

    expect(screen.getByRole('button', { name: /^Cover 1, Blinds/ })).toBeInTheDocument()
    expect(container.textContent).toContain('Cover 1')
    expect(container.textContent).toContain('Blinds')
    expect(container.textContent).not.toContain('Blinds 1')
  })

  it('draws the dragged device last, so an invalid ghost is never hidden under the device in its way', () => {
    const dimmer2: PlacedDeviceResponse = { ...dimmer, id: 'd2', label: 'Dimmer 2', startSlot: 3, channels: [] }
    const { container } = render(
      <PanelSvg
        layout={{ ...layout, devices: [terminal(1, 0), dimmer, dimmer2] }}
        selectedId={null}
        onSelect={() => {}}
        // Dimmer 1 dropped exactly onto Dimmer 2, which is drawn after it in list order.
        dragging={{ deviceId: 'd1', rowIndex: 1, startSlot: 3, valid: false }}
      />,
    )

    const svg = container.querySelector('svg')!
    const last = svg.lastElementChild!
    expect(last.getAttribute('data-device-id')).toBe('d1')
    expect(last.textContent).toMatch(/no room/i)
    expect(last.querySelector('rect[stroke-dasharray]')).not.toBeNull()
  })

  it('renders an empty layout without crashing', () => {
    render(<PanelSvg layout={{ rows: 2, slotsPerRow: 36, devices: [] }} selectedId={null} onSelect={() => {}} />)

    expect(screen.queryAllByRole('button')).toHaveLength(0)
  })
})
