import { useSyncExternalStore } from 'react'

export type Tone = 'info' | 'ok' | 'error'

export interface Status {
  id: number
  message: string
  tone: Tone
}

/** Errors stay until the next announcement; the rest clear themselves. */
const CLEAR_AFTER_MS = 4000

let current: Status | null = null
let next = 0
let timer: ReturnType<typeof setTimeout> | undefined
const listeners = new Set<() => void>()

const notify = () => listeners.forEach(fn => fn())

/**
 * One place every screen reports to, outside React so a save that finishes
 * after its page has unmounted can still say so. The shell renders the region;
 * see StatusRegion.
 */
export function announce(message: string, tone: Tone = 'info') {
  clearTimeout(timer)
  current = { id: next++, message, tone }
  notify()

  if (tone !== 'error') {
    timer = setTimeout(() => { current = null; notify() }, CLEAR_AFTER_MS)
  }
}

export function dismissStatus() {
  clearTimeout(timer)
  current = null
  notify()
}

const subscribe = (fn: () => void) => {
  listeners.add(fn)
  return () => { listeners.delete(fn) }
}

export const useStatus = () => useSyncExternalStore(subscribe, () => current, () => current)
