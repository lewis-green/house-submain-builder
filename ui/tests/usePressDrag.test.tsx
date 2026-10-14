import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LONG_PRESS_MS, usePressDrag } from '../src/drag/usePressDrag'

/** A pointer event whose target sits inside an element carrying data-item-id. */
function pointerOn(id: string, clientY: number, pointerType = 'touch') {
  const holder = document.createElement('div')
  holder.setAttribute('data-item-id', id)
  const child = document.createElement('span')
  holder.appendChild(child)
  document.body.appendChild(holder)
  return {
    target: child, clientX: 0, clientY, pointerId: 1, pointerType, button: 0,
    currentTarget: { setPointerCapture: vi.fn() },
  } as unknown as React.PointerEvent
}

/** A vertical list of slots 10px tall; slot 3 is taken by someone else. */
const options = (onCommit = vi.fn()) => ({
  idAttribute: 'data-item-id',
  start: (id: string) => (id === 'a' ? 0 : null),
  locate: (_id: string, point: { x: number; y: number }) => {
    const slot = Math.round(point.y / 10)
    return { target: slot, valid: slot !== 3 }
  },
  exists: (id: string) => id === 'a',
  onCommit,
  toPoint: (x: number, y: number) => ({ x, y }),
})

describe('usePressDrag', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => { vi.useRealTimers(); document.body.innerHTML = '' })

  it('lifts an item with a finger only after the long press, then follows it', () => {
    const { result } = renderHook(() => usePressDrag(options()))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0)))
    expect(result.current.dragging).toBeNull()
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    act(() => result.current.bind.onPointerMove(pointerOn('a', 52)))

    expect(result.current.dragging).toEqual({ id: 'a', target: 5, valid: true })
  })

  it('lifts an item with a mouse as soon as it moves, and commits on release', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => usePressDrag(options(onCommit)))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 'mouse')))
    act(() => result.current.bind.onPointerMove(pointerOn('a', 70, 'mouse')))
    act(() => result.current.bind.onPointerUp())

    expect(onCommit).toHaveBeenCalledWith('a', 7)
  })

  it('never commits a drop the caller says is not allowed', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => usePressDrag(options(onCommit)))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 'mouse')))
    act(() => result.current.bind.onPointerMove(pointerOn('a', 30, 'mouse')))
    expect(result.current.dragging?.valid).toBe(false)
    act(() => result.current.bind.onPointerUp())

    expect(onCommit).not.toHaveBeenCalled()
  })

  it('does not lift an item that has gone by the time the long press fires', () => {
    const { result } = renderHook(() => usePressDrag(options()))

    act(() => result.current.bind.onPointerDown(pointerOn('gone', 0)))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })

    expect(result.current.dragging).toBeNull()
  })
})
