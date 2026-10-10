import { act, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppShell } from '../src/components/AppShell'

/**
 * jsdom lays nothing out, so the bar's height and the viewport are played
 * back by hand: a ResizeObserver stub that remembers its callback, and a
 * getBoundingClientRect on the bar that answers with whatever the test says.
 */
let resize: (() => void) | null = null
class FakeResizeObserver {
  constructor(callback: () => void) { resize = callback }
  observe() {}
  disconnect() {}
}

// button → the flex row of actions → the bar → the block that is (or is not) sticky.
const barOf = (actions: HTMLElement) => actions.parentElement!.parentElement!
const blockOf = (actions: HTMLElement) => barOf(actions).parentElement!

function show(barHeight: number) {
  Object.defineProperty(window, 'innerHeight', { value: 600, configurable: true })
  render(
    <MemoryRouter>
      <AppShell title="Panel" actions={<button type="button">Issue PDF revision</button>}>
        <p>Drawing</p>
      </AppShell>
    </MemoryRouter>,
  )
  const actions = screen.getByRole('button', { name: /issue pdf/i })
  barOf(actions).getBoundingClientRect = () => ({ height: barHeight } as DOMRect)
  act(() => resize?.())
  return actions
}

describe('AppShell action bar', () => {
  beforeEach(() => { vi.stubGlobal('ResizeObserver', FakeResizeObserver) })
  afterEach(() => { vi.unstubAllGlobals(); resize = null })

  it('pins the bar to the bottom while it is a reasonable share of the screen', () => {
    const actions = show(120)

    expect(blockOf(actions).className).toMatch(/sticky bottom-0/)
    expect(screen.getByRole('status').parentElement!.className).toBe('')
  })

  it('lets the bar into the page, and keeps messages on screen, when it would take over a third of the viewport', () => {
    // 250 of 600: the two wrapped buttons and the note at 200% text.
    const actions = show(250)

    expect(blockOf(actions).className).not.toMatch(/sticky/)
    expect(screen.getByRole('status').parentElement!.className).toMatch(/fixed inset-x-0 bottom-0/)
  })
})
