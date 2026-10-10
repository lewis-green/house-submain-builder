import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { useTitle } from '../useTitle'
import { StatusRegion } from './StatusRegion'
import { useTooTall } from './useTooTall'

interface Props {
  /** Shown in the header and, through useTitle, in the Safari tab and history. */
  title: string
  /** A line under the title: the house a panel belongs to, an address. */
  subtitle?: string
  back?: { to: string; label: string }
  /** Secondary navigation for this screen, rendered as a nav landmark. */
  nav?: ReactNode
  /**
   * The screen's primary action, pinned above the home indicator so a thumb
   * reaches it without scrolling past the content.
   */
  actions?: ReactNode
  children: ReactNode
}

/**
 * The action bar stays pinned only while it leaves this much of the screen to
 * the page above it. At 200% text two wrapped buttons and a note can be most
 * of a phone's height, and a bar that size is better scrolled to than stuck
 * over the drawing.
 */
const BAR_SHARE_LIMIT = 0.35

/**
 * Every screen is a drawing sheet: a title block across the top with the way
 * back in it, the content below, and the one thing to do next pinned to the
 * bottom. Keeping the shell mounted through loading and errors means Back
 * never disappears while a request is in flight.
 */
export function AppShell({ title, subtitle, back, nav, actions, children }: Props) {
  useTitle(title)
  const [bar, setBar] = useState<HTMLDivElement | null>(null)
  const unpinned = useTooTall(bar, BAR_SHARE_LIMIT)

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-2xl flex-col pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))]">
      {/* Standalone on an iPhone draws under the status bar: this keeps that strip opaque as the page scrolls. */}
      <div aria-hidden="true" className="fixed inset-x-0 top-0 z-20 h-[env(safe-area-inset-top)] bg-paper" />
      <header className="pt-[env(safe-area-inset-top)]">
        {/* flex-wrap: at 200% text on a 320px phone, Back and "Bill of materials" do
            not fit one row, and the nav takes a row of its own rather than being
            squeezed until a word spills off the page. */}
        <div className="flex min-h-11 flex-wrap items-center justify-between gap-2">
          {back ? (
            <Link
              to={back.to}
              aria-label={`Back to ${back.label.toLowerCase()}`}
              className="-ml-2 inline-flex min-h-11 shrink-0 items-center gap-1 rounded-lg px-2 font-medium text-ink-2 active:bg-press"
            >
              <span aria-hidden="true">‹</span> {back.label}
            </Link>
          ) : <span />}
          {/* ml-auto keeps the nav on the right once it has wrapped onto its own row;
              min-w-0 lets its link wrap its words should even that row be too narrow. */}
          {nav && <nav aria-label="More" className="ml-auto flex min-w-0 items-center gap-1">{nav}</nav>}
        </div>
        <div className="border-b-2 border-ink pb-2">
          {/* wrap-anywhere: a house named by a long reference must not push the page sideways. */}
          <h1 className="wrap-anywhere text-2xl font-bold leading-tight tracking-tight">{title}</h1>
          {subtitle && <p className="mt-0.5 text-ink-2">{subtitle}</p>}
        </div>
      </header>

      <main className="flex-1 py-4">{children}</main>

      {/* One sticky block, so a status message stacks above the action bar
          rather than sticking to the same edge and sitting on top of it. */}
      <div className={unpinned ? undefined : `sticky bottom-0 z-10 ${actions ? '' : 'pb-[env(safe-area-inset-bottom)]'}`}>
        {/* With the bar let into the page, messages still need somewhere on screen to land. */}
        <div className={unpinned ? 'fixed inset-x-0 bottom-0 z-10 mx-auto max-w-2xl pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))] pb-[env(safe-area-inset-bottom)]' : undefined}>
          <StatusRegion />
        </div>
        {actions && (
          <div ref={setBar} className="border-t border-rule bg-paper pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur-sm">
            <div className="flex flex-wrap items-center gap-3">{actions}</div>
          </div>
        )}
      </div>
    </div>
  )
}
