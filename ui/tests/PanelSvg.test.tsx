import { createEvent, fireEvent, render, screen } from '@testing-library/react'
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

  it('draws a run of terminals as one block with each terminal labelled', () => {
    const { container } = render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    // Without a handler the run is still a picture: nothing to open.
    expect(screen.getByRole('img', { name: /L1.*L3/ })).toBeInTheDocument()
    const labels = [...container.querySelectorAll('text')].map(t => t.textContent)
    expect(labels).toEqual(expect.arrayContaining(['L1', 'L2', 'L3']))
  })

  it('opens the terminal order from the run when it can', async () => {
    const onOpenTerminals = vi.fn()
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} onOpenTerminals={onOpenTerminals} />)

    await userEvent.click(screen.getByRole('button', { name: /terminals L1–L3/i }))

    expect(onOpenTerminals).toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Terminals L1–L3, change order' })).toBeInTheDocument()
  })

  it('opens the terminal order from the keyboard, once per press, without scrolling', async () => {
    const onOpenTerminals = vi.fn()
    render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} onOpenTerminals={onOpenTerminals} />)
    screen.getByRole('button', { name: /terminals L1–L3/i }).focus()

    await userEvent.keyboard('{Enter}')
    expect(onOpenTerminals).toHaveBeenCalledTimes(1)
    await userEvent.keyboard(' ')
    expect(onOpenTerminals).toHaveBeenCalledTimes(2)

    const space = createEvent.keyDown(screen.getByRole('button', { name: /terminals L1–L3/i }), { key: ' ' })
    fireEvent(screen.getByRole('button', { name: /terminals L1–L3/i }), space)
    expect(space.defaultPrevented).toBe(true)
    fireEvent.keyDown(screen.getByRole('button', { name: /terminals L1–L3/i }), { key: 'Enter', repeat: true })
    expect(onOpenTerminals).toHaveBeenCalledTimes(3)
  })

  it('selects a device with Space, without scrolling', () => {
    const onSelect = vi.fn()
    render(<PanelSvg layout={layout} selectedId={null} onSelect={onSelect} />)
    const button = screen.getByRole('button', { name: /dimmer 1/i })
    const space = createEvent.keyDown(button, { key: ' ' })
    fireEvent(button, space)

    expect(onSelect).toHaveBeenCalledTimes(1)
    expect(space.defaultPrevented).toBe(true)
    fireEvent.keyDown(button, { key: 'Enter', repeat: true })
    expect(onSelect).toHaveBeenCalledTimes(1)
  })

  it('keeps the first label of a run inside the inset block', () => {
    const { container } = render(<PanelSvg layout={layout} selectedId={null} onSelect={() => {}} />)

    const rect = container.querySelector('svg > g[role="img"] > rect')!
    const first = container.querySelector('svg > g[role="img"] text')!
    const left = Number(rect.getAttribute('x'))
    // Slot 0 is 18px wide; the inset trims its outer edge, so the label sits
    // in the middle of what is left, not at 9.
    expect(Number(first.getAttribute('x'))).toBeCloseTo((left + 18) / 2)
  })

  it('centres a lone terminal label in the visible block, not the full slot', () => {
    const lone: LayoutResponse = { rows: 1, slotsPerRow: 54, devices: [terminal(1, 0)] }
    const { container } = render(<PanelSvg layout={lone} selectedId={null} onSelect={() => {}} />)

    const rect = container.querySelector('svg > g[role="img"] > rect')!
    const text = container.querySelector('svg > g[role="img"] text')!
    const centre = Number(rect.getAttribute('x')) + Number(rect.getAttribute('width')) / 2
    expect(Number(text.getAttribute('x'))).toBeCloseTo(centre)
    expect(container.querySelector('svg > g[role="img"] > g')).toHaveAttribute('aria-hidden', 'true')
  })

  it('marks a circuit bridged across several terminals with a bar', () => {
    const bridged: LayoutResponse = {
      rows: 1, slotsPerRow: 54,
      devices: [
        { ...terminal(1, 0), label: 'C1', circuitId: 'a' },
        { ...terminal(2, 1), label: 'C2.1', circuitId: 'b' },
        { ...terminal(3, 2), label: 'C2.2', circuitId: 'b' },
      ],
    }
    const { container } = render(<PanelSvg layout={bridged} selectedId={null} onSelect={() => {}} />)

    expect(container.querySelector('[data-bridge="C2.1–C2.2"]')).not.toBeNull()
    expect(container.querySelectorAll('[data-bridge]')).toHaveLength(1)
  })

  it('never draws a bridge bar across another circuit\'s terminal', () => {
    const interleaved: LayoutResponse = {
      rows: 1, slotsPerRow: 54,
      devices: [
        { ...terminal(1, 0), label: 'C2.1', circuitId: 'b' },
        { ...terminal(2, 1), label: 'C3', circuitId: 'c' },
        { ...terminal(3, 2), label: 'C2.2', circuitId: 'b' },
      ],
    }
    const { container } = render(<PanelSvg layout={interleaved} selectedId={null} onSelect={() => {}} />)

    expect(container.querySelectorAll('[data-bridge]')).toHaveLength(0)
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
