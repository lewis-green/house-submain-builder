import { useEffect, useState, type RefObject } from 'react'

/** Where the scroller is along its content, in CSS pixels. */
export interface ScrollExtent {
  left: number
  width: number
  total: number
}

/**
 * Tracks how far a horizontal scroller has been panned and how much of its
 * content is in view, so the page can say and show that there is more. iOS
 * draws no scrollbar, so without this a 24-module panel looks like the six
 * modules that happen to be on screen.
 */
export function useScrollExtent(ref: RefObject<HTMLElement | null>, content: unknown, scale: number): ScrollExtent | null {
  const [extent, setExtent] = useState<ScrollExtent | null>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return

    // No width means nothing has been laid out yet — unknown, not "all of it".
    const measure = () => setExtent(el.clientWidth === 0
      ? null
      : { left: el.scrollLeft, width: el.clientWidth, total: el.scrollWidth })
    measure()

    el.addEventListener('scroll', measure, { passive: true })
    // Rotating the phone, or the keyboard coming up, changes how much fits.
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(el)

    return () => {
      el.removeEventListener('scroll', measure)
      observer?.disconnect()
    }
    // Re-measured when the drawing itself changes size — a zoom or a rebuild.
  }, [ref, content, scale])

  return extent
}
