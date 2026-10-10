import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PanelOverview } from '../src/panel/PanelOverview'
import type { LayoutResponse } from '../src/api/types'

const layout: LayoutResponse = {
  rows: 3, slotsPerRow: 72,
  devices: [{
    id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
    moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None', channels: [],
  }],
}

// A 24-module panel is 1296px at 1×; the drawing starts 52px into the scroller
// (a 44px gutter and 8px of padding) and a 375px phone shows 359px of it.
const common = { layout, zoom: 1, originPx: 52, gutterPx: 44 }

describe('PanelOverview', () => {
  it('is tall enough to be a thumb target, since tapping it pans the drawing', () => {
    const { container } = render(<PanelOverview {...common} extent={{ left: 0, width: 359, total: 1400 }} onPan={() => {}} />)

    // h-11 is 44px: the strip is interactive, so it is held to the same rule as a button.
    expect(container.querySelector('svg')!.getAttribute('class')).toMatch(/\bh-11\b/)
  })

  it('says which modules are on screen, counting from the edge of the gutter', () => {
    render(<PanelOverview {...common} extent={{ left: 0, width: 359, total: 1400 }} onPan={() => {}} />)

    // 307px of drawing past the gutter is modules 1 to 6 (54px each).
    expect(screen.getByText(/showing modules 1–6 of 24/i)).toBeInTheDocument()
  })

  it('moves on as the drawing is scrolled', () => {
    render(<PanelOverview {...common} extent={{ left: 540, width: 359, total: 1400 }} onPan={() => {}} />)

    expect(screen.getByText(/showing modules 10–16 of 24/i)).toBeInTheDocument()
  })

  it('says so when the whole panel is on screen', () => {
    render(<PanelOverview {...common} zoom={0.25} extent={{ left: 0, width: 400, total: 400 }} onPan={() => {}} />)

    expect(screen.getByText(/all 24 modules shown/i)).toBeInTheDocument()
  })

  it('pans the drawing to wherever the strip is tapped', () => {
    const onPan = vi.fn()
    const { container } = render(<PanelOverview {...common} extent={{ left: 0, width: 359, total: 1400 }} onPan={onPan} />)
    const strip = container.querySelector('svg')!
    strip.getBoundingClientRect = () => ({ left: 0, width: 359, top: 0, height: 40, right: 359, bottom: 40, x: 0, y: 0, toJSON: () => {} })

    // The middle of the strip is the middle of the panel: module 12, 648px in.
    fireEvent.pointerDown(strip, { clientX: 179.5, pointerId: 1 })

    // Scrolled so that point sits in the middle of the 315px that show past the gutter.
    expect(onPan).toHaveBeenCalledWith(648 + 52 - 44 - 315 / 2)
  })
})
