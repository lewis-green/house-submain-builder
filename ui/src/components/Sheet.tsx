import { useEffect, useRef, useState, type ReactNode } from 'react'

interface Props {
  title: string
  /** A line under the title: what kind of device, how wide, which row. */
  subtitle?: string
  onClose: () => void
  children: ReactNode
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/** A downward drag on the handle past this closes the sheet. */
const DISMISS_DRAG_PX = 60

/**
 * Keeps the page behind it still: iOS ignores overflow:hidden on body for
 * touch scrolling, so the body is pinned with position:fixed at its current
 * scroll offset and put back on close.
 */
function useBodyScrollLock() {
  useEffect(() => {
    const { style } = document.body
    const scrollY = window.scrollY
    const previous = { position: style.position, top: style.top, width: style.width, overflow: style.overflow }

    style.position = 'fixed'
    style.top = `-${scrollY}px`
    style.width = '100%'
    style.overflow = 'hidden'

    return () => {
      Object.assign(style, previous)
      window.scrollTo(0, scrollY)
    }
  }, [])
}

/**
 * Fixed elements are laid out against the layout viewport, which does not
 * shrink when the keyboard comes up — so without this the lower half of the
 * sheet, and the field being typed into, sits behind the keyboard.
 */
function useKeyboardSafeBox() {
  const [box, setBox] = useState<{ top: number; height: number } | null>(null)

  useEffect(() => {
    const vv = window.visualViewport
    if (!vv) return

    const update = () => {
      const keyboardUp = vv.height < window.innerHeight - 100
      setBox(keyboardUp ? { top: vv.offsetTop, height: vv.height } : null)
    }

    vv.addEventListener('resize', update)
    vv.addEventListener('scroll', update)
    return () => {
      vv.removeEventListener('resize', update)
      vv.removeEventListener('scroll', update)
    }
  }, [])

  return box
}

export function Sheet({ title, subtitle, onClose, children }: Props) {
  const panel = useRef<HTMLDivElement>(null)
  const restoreTo = useRef<Element | null>(null)
  const dragStartY = useRef<number | null>(null)
  const keyboardBox = useKeyboardSafeBox()

  // The parent hands over a new onClose on every render — and it re-renders
  // whenever a save lands while the sheet is open. Reading the latest one from
  // a ref lets the effects below run once per open instead of once per render,
  // which is what used to pull focus out of the field being typed in (and the
  // keyboard down with it) every time a circuit was saved.
  const onCloseRef = useRef(onClose)
  useEffect(() => { onCloseRef.current = onClose }, [onClose])

  // Every way out goes through here. The field being typed in is blurred first
  // so its pending edit commits: the sheet unmounts on close, and WebKit fires
  // no blur for an element that has already left the document — which is how a
  // room typed and then dismissed with Escape used to vanish without a word. A
  // tap on Close or the backdrop blurs the field on its own; Escape does not.
  const close = () => {
    (document.activeElement as HTMLElement | null)?.blur?.()
    onCloseRef.current()
  }

  useBodyScrollLock()

  // Focus moves into the dialog once, when it opens, and goes back to whatever
  // opened it when it closes. Nothing in between may touch it.
  useEffect(() => {
    restoreTo.current = document.activeElement
    panel.current?.focus()

    return () => { (restoreTo.current as HTMLElement | null)?.focus?.() }
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        close()
        return
      }

      // Tab wraps inside the dialog: the page behind is covered, so landing on
      // it would leave a keyboard or VoiceOver user somewhere they cannot see.
      if (e.key === 'Tab' && panel.current) {
        const focusable = [...panel.current.querySelectorAll<HTMLElement>(FOCUSABLE)]
        if (focusable.length === 0) return
        const first = focusable[0]
        const last = focusable[focusable.length - 1]
        const active = document.activeElement

        if (e.shiftKey && (active === first || active === panel.current)) {
          e.preventDefault()
          last.focus()
        } else if (!e.shiftKey && active === last) {
          e.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener('keydown', onKey)

    return () => document.removeEventListener('keydown', onKey)
  }, [])

  const body = useRef<HTMLDivElement>(null)

  // Whatever is focused scrolls to the middle of the sheet, where the
  // keyboard cannot reach it. Only the sheet body is scrolled: scrollIntoView
  // would scroll every scrollable ancestor too, and an overflow:hidden box is
  // one of those — it dragged the title and Close button up under the
  // dialog's own clip, out of reach.
  const onFocus = (e: React.FocusEvent) => {
    const scroller = body.current
    if (!(e.target instanceof HTMLInputElement) || !scroller) return
    const field = e.target.getBoundingClientRect()
    const box = scroller.getBoundingClientRect()
    scroller.scrollTop += (field.top + field.height / 2) - (box.top + box.height / 2)
  }

  return (
    <>
      <div
        className="fixed inset-0 z-30 bg-ink/40"
        style={{ touchAction: 'none' }}
        onClick={close}
        aria-hidden="true"
      />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onFocus={onFocus}
        className="fixed inset-x-0 bottom-0 z-40 flex max-h-[85dvh] flex-col overflow-hidden rounded-t-2xl bg-sheet shadow-2xl"
        style={keyboardBox
          ? { top: keyboardBox.top, height: keyboardBox.height, maxHeight: 'none', bottom: 'auto', borderRadius: 0 }
          : undefined}
      >
        <div
          // Side padding never less than the safe area: in landscape the sheet
          // spans the Dynamic Island's band, and a field 16px from the edge
          // would start under it.
          className="shrink-0 border-b border-rule pt-2 pb-3 pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))]"
          style={{ touchAction: 'none' }}
          // Captured, so the release is seen here even when the finger has
          // left the header: without capture a 60px pull ends over the body.
          // WebKit throws for a pointer it does not consider active (a
          // synthesised event, say); the pull still works without capture then.
          onPointerDown={e => {
            // A press on Close is a click, not a pull. Capturing it would send
            // the click to this header instead of the button — which a finger
            // never shows, because Safari aims a tap's click at what was tapped,
            // but a mouse or trackpad does: the × did nothing at all.
            if ((e.target as Element).closest('button')) return
            dragStartY.current = e.clientY
            try { e.currentTarget.setPointerCapture(e.pointerId) } catch { /* not an active pointer */ }
          }}
          onPointerUp={e => {
            if (dragStartY.current !== null && e.clientY - dragStartY.current > DISMISS_DRAG_PX) close()
            dragStartY.current = null
          }}
          onPointerCancel={() => { dragStartY.current = null }}
        >
          <div className="mx-auto mb-2 h-1.5 w-12 rounded-full bg-rule" />
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              {/* Wraps rather than truncates: "Dimmer 1" cut to "Dimmer…" loses the one
                  word that says which dimmer is being named. */}
              <h2 className="wrap-anywhere text-lg font-bold">{title}</h2>
              {subtitle && <p className="text-sm text-ink-2">{subtitle}</p>}
            </div>
            {/* Sized in px: a thumb needs 44px whatever the text size, and a Close
                button that doubled with 200% text was what squeezed the title out. */}
            <button
              type="button"
              onClick={close}
              aria-label="Close"
              className="-mr-2 -mt-1 flex h-[44px] w-[44px] shrink-0 items-center justify-center rounded-lg text-2xl leading-none active:bg-press"
            >
              ×
            </button>
          </div>
        </div>
        <div
          ref={body}
          className="min-h-0 flex-1 overflow-y-auto pt-4 pb-[max(1rem,env(safe-area-inset-bottom))] pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))]"
          style={{ overscrollBehavior: 'contain', WebkitOverflowScrolling: 'touch' } as React.CSSProperties}
        >
          {children}
        </div>
      </div>
    </>
  )
}
