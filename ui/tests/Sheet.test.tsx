import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Sheet } from '../src/components/Sheet'

const tree = (onClose: () => void) => (
  <div>
    <button>Behind</button>
    <Sheet title="Dimmer 1" subtitle="Dimmer · 1 module wide" onClose={onClose}>
      <input aria-label="First" />
      <input aria-label="Last" />
    </Sheet>
  </div>
)

const show = (onClose = vi.fn()) => {
  const view = render(tree(onClose))
  return { ...view, onClose }
}

describe('Sheet', () => {
  it('pads its header and body by the safe-area side insets, for landscape under the Dynamic Island', () => {
    render(<Sheet title="Dimmer 1" onClose={() => {}}><input aria-label="Room" /></Sheet>)

    const dialog = screen.getByRole('dialog')
    const header = dialog.firstElementChild!
    const body = screen.getByLabelText('Room').parentElement!
    for (const el of [header, body]) {
      expect(el.className).toMatch(/pl-\[max\(1rem,env\(safe-area-inset-left\)\)\]/)
      expect(el.className).toMatch(/pr-\[max\(1rem,env\(safe-area-inset-right\)\)\]/)
    }
  })

  it('pins the page behind it while open and lets it go on close', () => {
    const { unmount } = show()

    // position:fixed, not overflow:hidden alone: iOS ignores the latter for touch.
    expect(document.body.style.position).toBe('fixed')
    expect(document.body.style.overflow).toBe('hidden')

    unmount()

    expect(document.body.style.position).toBe('')
    expect(document.body.style.overflow).toBe('')
  })

  it('is a labelled modal dialog with a close button', async () => {
    const { onClose } = show()

    expect(screen.getByRole('dialog', { name: 'Dimmer 1' })).toHaveAttribute('aria-modal', 'true')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(onClose).toHaveBeenCalled()
  })

  it('keeps Tab inside the dialog in both directions', async () => {
    show()

    screen.getByLabelText('Last').focus()
    await userEvent.tab()
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: 'Behind' }))
    expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true)

    screen.getByRole('button', { name: 'Close' }).focus()
    await userEvent.tab({ shift: true })
    expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true)
  })

  it('closes on the backdrop and on Escape', async () => {
    const { onClose } = show()

    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole('dialog').previousElementSibling as HTMLElement)
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  it('gives focus back to whatever opened it', () => {
    const opener = document.createElement('button')
    document.body.appendChild(opener)
    opener.focus()

    const { unmount } = show()
    expect(document.activeElement).not.toBe(opener)

    unmount()
    expect(document.activeElement).toBe(opener)
    opener.remove()
  })

  it('leaves focus in the field being typed in when the parent re-renders with a new onClose', async () => {
    const opener = document.createElement('button')
    document.body.appendChild(opener)
    opener.focus()

    // The parent re-renders whenever a save lands, with a fresh arrow function
    // for onClose each time — which is exactly what used to dismiss the keyboard.
    const { rerender } = show()
    const last = screen.getByLabelText('Last')
    await userEvent.type(last, 'Isl')
    expect(document.activeElement).toBe(last)

    rerender(tree(vi.fn()))
    await userEvent.type(last, 'and')

    expect(document.activeElement).toBe(last)
    expect(last).toHaveValue('Island')
    // And the latest onClose is still the one Escape calls.
    const latest = vi.fn()
    rerender(tree(latest))
    await userEvent.keyboard('{Escape}')
    expect(latest).toHaveBeenCalledTimes(1)
    opener.remove()
  })

  it('blurs the field being typed in before closing on Escape, so its pending edit commits', async () => {
    // The order matters: the sheet unmounts on close, and WebKit fires no blur
    // for an element already out of the document, so a commit-on-blur never ran.
    const order: string[] = []
    const onClose = vi.fn(() => order.push('close'))
    render(
      <Sheet title="Dimmer 1" onClose={onClose}>
        <input aria-label="Room" onBlur={() => order.push('blur')} />
      </Sheet>,
    )

    await userEvent.type(screen.getByLabelText('Room'), 'Kitchen')
    await userEvent.keyboard('{Escape}')

    expect(order).toEqual(['blur', 'close'])
  })

  it('closes on a pull down of the handle, wherever the finger ends up', () => {
    const { onClose } = show()
    const header = screen.getByRole('heading', { name: 'Dimmer 1' }).closest('div[style]')!

    fireEvent.pointerDown(header, { pointerId: 1, clientY: 20 })
    // The release lands on the header too, because the pointer was captured
    // at pointerdown; without capture a 60px pull ends over the sheet body.
    fireEvent.pointerUp(header, { pointerId: 1, clientY: 100 })

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('leaves the pointer with its Close button, so a mouse click on it still lands there', () => {
    // jsdom has no pointer capture; a browser does, and a capture taken here
    // retargets the click to the header so the button never hears it.
    const capture = vi.fn()
    HTMLElement.prototype.setPointerCapture = capture
    try {
      show()

      fireEvent.pointerDown(screen.getByRole('button', { name: 'Close' }), { pointerId: 1, clientY: 20 })
      expect(capture).not.toHaveBeenCalled()

      // The pull from the handle still takes the pointer.
      fireEvent.pointerDown(screen.getByRole('heading', { name: 'Dimmer 1' }), { pointerId: 2, clientY: 20 })
      expect(capture).toHaveBeenCalledWith(2)
    } finally {
      delete (HTMLElement.prototype as Partial<HTMLElement>).setPointerCapture
    }
  })

  it('does not close on a short pull of the handle', () => {
    const { onClose } = show()
    const header = screen.getByRole('heading', { name: 'Dimmer 1' }).closest('div[style]')!

    fireEvent.pointerDown(header, { pointerId: 1, clientY: 20 })
    fireEvent.pointerUp(header, { pointerId: 1, clientY: 50 })

    expect(onClose).not.toHaveBeenCalled()
  })
})
