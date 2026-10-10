import { useEffect, useState } from 'react'

/**
 * Whether an element takes more than `share` of the viewport's height, kept
 * up to date as it and the viewport change size. The element arrives as state
 * rather than a ref so the measuring starts the moment it mounts, which may be
 * well after the hook does — the action bar only appears once a panel has loaded.
 *
 * jsdom has no ResizeObserver; there the answer is simply no, and the layout
 * is the one every existing test expects.
 */
export function useTooTall(element: HTMLElement | null, share: number) {
  const [tooTall, setTooTall] = useState(false)

  useEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') return

    const check = () => setTooTall(element.getBoundingClientRect().height > window.innerHeight * share)
    // An observer reports once on observe(), so the first measurement needs no call of its own.
    const observer = new ResizeObserver(check)
    observer.observe(element)
    window.addEventListener('resize', check)

    return () => {
      observer.disconnect()
      window.removeEventListener('resize', check)
    }
  }, [element, share])

  return tooTall
}
