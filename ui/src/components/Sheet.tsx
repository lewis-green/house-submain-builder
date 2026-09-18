import { useEffect, useRef, type ReactNode } from 'react'

interface Props {
  title: string
  onClose: () => void
  children: ReactNode
}

export function Sheet({ title, onClose, children }: Props) {
  const panel = useRef<HTMLDivElement>(null)
  const restoreTo = useRef<Element | null>(null)

  useEffect(() => {
    restoreTo.current = document.activeElement
    panel.current?.focus()

    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)

    return () => {
      document.removeEventListener('keydown', onKey)
      ;(restoreTo.current as HTMLElement | null)?.focus?.()
    }
  }, [onClose])

  return (
    <>
      <div className="fixed inset-0 bg-black/30" onClick={onClose} aria-hidden="true" />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className="fixed inset-x-0 bottom-0 max-h-[75vh] overflow-y-auto rounded-t-2xl bg-white pb-[env(safe-area-inset-bottom)] shadow-2xl"
      >
        <div className="sticky top-0 bg-white px-4 pt-3">
          <div className="mx-auto h-1 w-10 rounded-full bg-slate-300" />
        </div>
        <div className="p-4">{children}</div>
      </div>
    </>
  )
}
