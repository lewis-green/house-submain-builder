import { useEffect, useState } from 'react'
import { Button } from './Button'

interface Props {
  /** The action as first shown, e.g. "Remove". */
  label: string
  /** The question asked before it happens, e.g. "Remove the meter?". */
  question: string
  /** The confirming button, e.g. "Yes, remove it". */
  confirmLabel: string
  disabled?: boolean
  onConfirm: () => void
  className?: string
}

/** How long the question stays open before a stray tap is forgotten. */
const FORGET_AFTER_MS = 6000

/**
 * Two taps for anything that throws work away: the first asks, the second
 * does it. A brush of the thumb against "Remove" then costs nothing, and the
 * question closes itself so the screen does not stay armed.
 */
export function ConfirmButton({ label, question, confirmLabel, disabled, onConfirm, className = '' }: Props) {
  const [asking, setAsking] = useState(false)

  useEffect(() => {
    if (!asking) return
    const timer = setTimeout(() => setAsking(false), FORGET_AFTER_MS)
    return () => clearTimeout(timer)
  }, [asking])

  if (!asking) {
    return (
      <Button variant="danger" disabled={disabled} onClick={() => setAsking(true)} className={className}>
        {label}
      </Button>
    )
  }

  return (
    <div role="group" aria-label={question} className={`flex flex-wrap items-center gap-2 ${className}`}>
      <span className="font-medium">{question}</span>
      <Button variant="danger" onClick={() => { setAsking(false); onConfirm() }}>{confirmLabel}</Button>
      <Button variant="secondary" onClick={() => setAsking(false)}>Keep</Button>
    </div>
  )
}
