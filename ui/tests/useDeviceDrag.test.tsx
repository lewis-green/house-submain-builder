import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LONG_PRESS_MS, useDeviceDrag } from '../src/panel/useDeviceDrag'
import { SLOT_PX } from '../src/panel/geometry'
import type { LayoutResponse, PlacedDeviceResponse } from '../src/api/types'

const device = (id: string, rowIndex: number, startSlot: number, moduleWidth: number): PlacedDeviceResponse => ({
  id, deviceTypeId: 'x', category: 'Dimmer240', rowIndex, startSlot, moduleWidth,
  label: id, terminalRole: 'None', channels: [],
})

const layout: LayoutResponse = {
  rows: 4, slotsPerRow: 54,
  devices: [device('a', 1, 0, 3), device('b', 1, 3, 3)],
}

/** A pointer event whose target sits inside an element carrying data-device-id. */
function pointerOn(deviceId: string, clientX = 0, clientY = 0, pointerType = 'touch') {
  const holder = document.createElement('div')
  holder.setAttribute('data-device-id', deviceId)
  const child = document.createElement('rect')
  holder.appendChild(child)
  document.body.appendChild(holder)

  return {
    target: child,
    clientX,
    clientY,
    pointerId: 1,
    pointerType,
    button: 0,
    currentTarget: { setPointerCapture: vi.fn() },
  } as unknown as React.PointerEvent
}

const toPanelPoint = (x: number, y: number) => ({ x, y })

describe('useDeviceDrag', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => { vi.useRealTimers(); document.body.innerHTML = '' })

  it('does not start a drag on a plain tap', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))

    expect(result.current.dragging).toBeNull()
  })

  it('starts a drag after a long press', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })

    expect(result.current.dragging?.deviceId).toBe('a')
  })

  it('cancels the pending drag when the pointer pans away first', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 0)))
    act(() => result.current.bind.onPointerMove(pointerOn('a', 50, 0)))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })

    expect(result.current.dragging).toBeNull()
  })

  it('snaps to slot boundaries while dragging', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    // Centre of the device over slot 30, plus a pixel of slop.
    act(() => result.current.bind.onPointerMove(pointerOn('a', SLOT_PX * 30 + SLOT_PX * 1.5 + 1, 0)))

    expect(result.current.dragging?.startSlot).toBe(30)
  })

  it('marks an overlapping target invalid and refuses to commit it', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit, toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    // Drop 'a' on top of 'b' at slot 3, in row 1.
    act(() => result.current.bind.onPointerMove(pointerOn('a', SLOT_PX * 3 + SLOT_PX * 1.5, 90)))

    expect(result.current.dragging?.valid).toBe(false)

    act(() => result.current.bind.onPointerUp())

    expect(onCommit).not.toHaveBeenCalled()
    expect(result.current.dragging).toBeNull()
  })

  it('commits once on release over a free slot', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit, toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    act(() => result.current.bind.onPointerMove(pointerOn('a', SLOT_PX * 30 + SLOT_PX * 1.5, 90)))
    act(() => result.current.bind.onPointerUp())

    expect(onCommit).toHaveBeenCalledTimes(1)
    expect(onCommit).toHaveBeenCalledWith('a', 1, 30)
  })

  it('picks a device up with the mouse as soon as it moves, with no long press', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit, toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 90, 'mouse')))
    act(() => result.current.bind.onPointerMove(pointerOn('a', SLOT_PX * 30 + SLOT_PX * 1.5, 90, 'mouse')))

    expect(result.current.dragging?.deviceId).toBe('a')

    act(() => result.current.bind.onPointerUp())

    expect(onCommit).toHaveBeenCalledWith('a', 1, 30)
  })

  it('leaves a mouse wobble smaller than a drag as a click', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a', 0, 0, 'mouse')))
    act(() => result.current.bind.onPointerMove(pointerOn('a', 3, 0, 'mouse')))

    expect(result.current.dragging).toBeNull()
  })

  it('cancels touch scrolling only once the long press has armed', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))
    const wrapper = document.createElement('div')
    act(() => result.current.bind.ref(wrapper))

    const touchMove = () => {
      const e = new Event('touchmove', { cancelable: true })
      wrapper.dispatchEvent(e)
      return e.defaultPrevented
    }

    // Before anything is pressed a swipe is a scroll, and must stay one.
    expect(touchMove()).toBe(false)

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    expect(touchMove()).toBe(false)

    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    expect(touchMove()).toBe(true)

    // Dropping must not become a synthesised click that opens the sheet.
    const end = new Event('touchend', { cancelable: true })
    wrapper.dispatchEvent(end)
    expect(end.defaultPrevented).toBe(true)

    act(() => result.current.bind.onPointerUp())
    expect(touchMove()).toBe(false)
  })

  it('stops listening to an element it has let go of', () => {
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit: vi.fn(), toPanelPoint }))
    const wrapper = document.createElement('div')
    act(() => result.current.bind.ref(wrapper))
    act(() => result.current.bind.ref(null))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })

    // Armed, but the listeners were removed with the element: a touch on the
    // old node scrolls as normal.
    const e = new Event('touchmove', { cancelable: true })
    wrapper.dispatchEvent(e)
    expect(e.defaultPrevented).toBe(false)
  })

  it('does not lift a device that a reload replaced during the hold', () => {
    const onCommit = vi.fn()
    const { result, rerender } = renderHook(({ layout }) => useDeviceDrag({ layout, onCommit, toPanelPoint }), { initialProps: { layout } })
    const wrapper = document.createElement('div')
    act(() => result.current.bind.ref(wrapper))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    // A rebuild lands while the thumb is still down: every device has a new id.
    rerender({ layout: { ...layout, devices: [device('a2', 1, 0, 3), device('b2', 1, 3, 3)] } })
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })

    expect(result.current.dragging).toBeNull()
    // Not armed, so a swipe is still a scroll.
    const e = new Event('touchmove', { cancelable: true })
    wrapper.dispatchEvent(e)
    expect(e.defaultPrevented).toBe(false)

    act(() => result.current.bind.onPointerUp())
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('does not commit a device that a reload replaced during the drag', () => {
    const onCommit = vi.fn()
    const { result, rerender } = renderHook(({ layout }) => useDeviceDrag({ layout, onCommit, toPanelPoint }), { initialProps: { layout } })

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    act(() => result.current.bind.onPointerMove(pointerOn('a', SLOT_PX * 30 + SLOT_PX * 1.5, 90)))
    expect(result.current.dragging?.valid).toBe(true)

    rerender({ layout: { ...layout, devices: [device('a2', 1, 0, 3), device('b2', 1, 3, 3)] } })
    act(() => result.current.bind.onPointerUp())

    // A PATCH for 'a' could only come back 404; the drop is dropped instead.
    expect(onCommit).not.toHaveBeenCalled()
    expect(result.current.dragging).toBeNull()
  })

  it('abandons the drag on pointer cancel without committing', () => {
    const onCommit = vi.fn()
    const { result } = renderHook(() => useDeviceDrag({ layout, onCommit, toPanelPoint }))

    act(() => result.current.bind.onPointerDown(pointerOn('a')))
    act(() => { vi.advanceTimersByTime(LONG_PRESS_MS) })
    act(() => result.current.bind.onPointerCancel())

    expect(onCommit).not.toHaveBeenCalled()
    expect(result.current.dragging).toBeNull()
  })
})
