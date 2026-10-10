import { Button } from './Button'

interface Props {
  message: string
  onRetry?: () => void
  /**
   * One row, for a place that cannot afford a block: the action bar, a
   * corner of a form. The message is said as it is, with no "Something went
   * wrong" in front, because the caller has already said what it was doing.
   */
  compact?: boolean
}

/** What went wrong and what to do about it, inside the page rather than instead of it. */
export function ErrorNote({ message, onRetry, compact }: Props) {
  if (compact) {
    return (
      <div role="alert" className="flex w-full items-center gap-3 rounded-lg border border-l-4 border-danger bg-danger-soft py-1 pr-1 pl-3 text-danger">
        <p className="min-w-0 flex-1 text-sm font-medium">{message}</p>
        {onRetry && <Button variant="secondary" className="shrink-0" onClick={onRetry}>Try again</Button>}
      </div>
    )
  }

  return (
    <div role="alert" className="rounded-lg border border-l-4 border-danger bg-danger-soft p-4 text-danger">
      <p className="font-medium"><span className="font-bold">Something went wrong:</span> {message}</p>
      {onRetry && <Button variant="secondary" className="mt-3" onClick={onRetry}>Try again</Button>}
    </div>
  )
}
