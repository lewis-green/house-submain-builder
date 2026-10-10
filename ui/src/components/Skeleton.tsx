interface Props {
  /** What is being fetched, for assistive tech; the bars say it visually. */
  label: string
  rows?: number
  /** Tailwind height class per row; list rows and a drawing differ. */
  rowClass?: string
}

/**
 * Placeholder rows the size of the content they stand in for, so the screen
 * does not jump when the data lands. Nothing spins: a pulse is enough, and it
 * is switched off for people who asked for less motion.
 */
export function Skeleton({ label, rows = 3, rowClass = 'h-16' }: Props) {
  return (
    <div role="status" aria-busy="true" aria-label={label} className="space-y-2">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className={`${rowClass} animate-pulse rounded-lg bg-rule-2 motion-reduce:animate-none`} />
      ))}
    </div>
  )
}
