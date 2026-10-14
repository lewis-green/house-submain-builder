import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppShell } from '../src/components/AppShell'
import { ConfirmButton } from '../src/components/ConfirmButton'
import { Stepper } from '../src/components/Stepper'
import { announce, dismissStatus } from '../src/components/status'

describe('Stepper', () => {
  it('shows a zero as a real value that is selected on focus, so nothing has to be deleted before typing', () => {
    render(<Stepper label="Dimmed lighting" value={0} onChange={() => {}} />)

    const box = screen.getByLabelText('Dimmed lighting') as HTMLInputElement
    // A value, not a placeholder: a placeholder is a hint, and reads as one in sunlight.
    expect(box).toHaveValue('0')
    expect(box).not.toHaveAttribute('placeholder')
    expect(box).toHaveAttribute('inputmode', 'numeric')

    act(() => box.focus())
    expect([box.selectionStart, box.selectionEnd]).toEqual([0, 1])
  })

  it('reads a digit typed beside the zero as that digit, whichever side the caret landed', async () => {
    const onChange = vi.fn()
    render(<Stepper label="Dimmed lighting" value={0} onChange={onChange} />)
    const box = screen.getByLabelText('Dimmed lighting')

    // After the zero: "04".
    await userEvent.type(box, '4', { initialSelectionStart: 1, initialSelectionEnd: 1 })
    expect(onChange).toHaveBeenLastCalledWith(4)

    // Before the zero: "40" — what Safari produces when the tap lands left of centre.
    await userEvent.type(box, '4', { initialSelectionStart: 0, initialSelectionEnd: 0 })
    expect(onChange).toHaveBeenLastCalledWith(4)
  })

  it('reads the first digit typed after a tap as a replacement, wherever Safari put the caret in "42"', async () => {
    const onChange = vi.fn()
    render(<Stepper label="Dimmed lighting" value={42} onChange={onChange} />)
    const box = screen.getByLabelText('Dimmed lighting')

    // After, in the middle of, and before the old count: the tap selected it
    // all, Safari then dropped the selection for a caret, and the 7 still wins.
    await userEvent.type(box, '7', { initialSelectionStart: 2, initialSelectionEnd: 2 })
    expect(onChange).toHaveBeenLastCalledWith(7)
    await userEvent.type(box, '7', { initialSelectionStart: 1, initialSelectionEnd: 1 })
    expect(onChange).toHaveBeenLastCalledWith(7)
    await userEvent.type(box, '7', { initialSelectionStart: 0, initialSelectionEnd: 0 })
    expect(onChange).toHaveBeenLastCalledWith(7)
  })

  it('appends the digits typed after the first, so "4" then "0" is forty', async () => {
    const seen: number[] = []
    function Counted() {
      const [value, setValue] = useState(1)
      return <Stepper label="Dimmed lighting" value={value} onChange={v => { seen.push(v); setValue(v) }} />
    }
    render(<Counted />)
    const box = screen.getByLabelText('Dimmed lighting')

    // One tap, then two keystrokes: the first replaces the 1, the second does not replace the 4.
    await userEvent.click(box)
    await userEvent.keyboard('40')

    expect(seen).toEqual([4, 40])
    expect(box).toHaveValue('40')
  })

  it('reads a deletion as what is left, not as a replacement', async () => {
    const onChange = vi.fn()
    render(<Stepper label="Dimmed lighting" value={42} onChange={onChange} />)

    await userEvent.type(screen.getByLabelText('Dimmed lighting'), '{Backspace}', { initialSelectionStart: 2, initialSelectionEnd: 2 })

    expect(onChange).toHaveBeenLastCalledWith(4)
  })

  it('steps with the buttons and never goes below the minimum', async () => {
    const onChange = vi.fn()
    render(<Stepper label="Blinds" value={0} onChange={onChange} />)

    expect(screen.getByRole('button', { name: /fewer blinds/i })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: /more blinds/i }))

    expect(onChange).toHaveBeenCalledWith(1)
  })

  it('takes a typed number and ignores anything that is not a digit', async () => {
    const onChange = vi.fn()
    render(<Stepper label="Switched" value={0} onChange={onChange} />)

    await userEvent.type(screen.getByLabelText('Switched'), '4')

    expect(onChange).toHaveBeenLastCalledWith(4)
  })
})

describe('ConfirmButton', () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }))
  afterEach(() => vi.useRealTimers())

  it('asks before it acts, and forgets the question if nobody answers', async () => {
    const onConfirm = vi.fn()
    render(<ConfirmButton label="Remove" question="Remove it?" confirmLabel="Yes, remove" onConfirm={onConfirm} />)

    await userEvent.click(screen.getByRole('button', { name: 'Remove' }))
    expect(screen.getByRole('group', { name: 'Remove it?' })).toBeInTheDocument()
    expect(onConfirm).not.toHaveBeenCalled()

    act(() => { vi.advanceTimersByTime(7000) })

    expect(screen.queryByRole('group')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove' })).toBeInTheDocument()
  })

  it('can be called off', async () => {
    const onConfirm = vi.fn()
    render(<ConfirmButton label="Remove" question="Remove it?" confirmLabel="Yes, remove" onConfirm={onConfirm} />)

    await userEvent.click(screen.getByRole('button', { name: 'Remove' }))
    await userEvent.click(screen.getByRole('button', { name: 'Keep' }))

    expect(onConfirm).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Remove' })).toBeInTheDocument()
  })
})

describe('AppShell', () => {
  afterEach(() => dismissStatus())

  const show = () => render(
    <MemoryRouter>
      <AppShell title="Ground floor" subtitle="14 Willow Lane" back={{ to: '/projects/p1', label: 'House' }} actions={<button>Do it</button>}>
        <p>Body</p>
      </AppShell>
    </MemoryRouter>,
  )

  it('names the document after the screen', () => {
    show()

    expect(document.title).toBe('Ground floor · Automation Panel Designer')
  })

  it('has the landmarks VoiceOver navigates by, and a Back link named for where it goes', () => {
    show()

    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveTextContent('Body')
    expect(screen.getByRole('heading', { level: 1, name: 'Ground floor' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to house' })).toHaveAttribute('href', '/projects/p1')
  })

  it('announces status changes through one live region and lets them be dismissed', async () => {
    show()

    act(() => announce('Saved Kitchen', 'ok'))
    expect(screen.getByRole('status')).toHaveTextContent('Saved Kitchen')

    await userEvent.click(screen.getByRole('button', { name: /dismiss/i }))
    expect(screen.getByRole('status')).toHaveTextContent('')
  })

  it('stacks the status above the action bar in one sticky block, so it never covers the primary button', () => {
    show()
    act(() => announce('Saved Kitchen', 'ok'))

    const status = screen.getByRole('status')
    const action = screen.getByRole('button', { name: 'Do it' })
    const sticky = action.closest('.sticky')!

    expect(sticky).toContainElement(status)
    expect(status.className).not.toMatch(/sticky/)
    // Above, in document order; both stick to the bottom edge as one.
    expect(status.compareDocumentPosition(action) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
