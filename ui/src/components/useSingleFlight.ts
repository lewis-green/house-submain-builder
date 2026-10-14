import { useCallback, useRef } from 'react'

/**
 * Runs one piece of async work at a time and drops any call made while it is
 * in flight. For the actions that make something irreversible — an automation panel, a
 * numbered revision, a house — a disabled button is not enough on its own: the
 * `disabled` comes from React state, which has not rendered yet when a second
 * activation lands in the same tick, so both went to the server. The guard
 * here is a ref, checked and set synchronously before the first await.
 */
export function useSingleFlight() {
  const inFlight = useRef(false)

  return useCallback(async (work: () => Promise<void>) => {
    if (inFlight.current) return
    inFlight.current = true
    try {
      await work()
    } finally {
      inFlight.current = false
    }
  }, [])
}
