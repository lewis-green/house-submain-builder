import { useId, useRef } from 'react'

interface Props {
  label: string
  value: number
  min?: number
  max?: number
  hint?: string
  disabled?: boolean
  onChange: (value: number) => void
}

/**
 * What was typed into `old` to make `raw`, wherever the caret was: the part of
 * `raw` left once a prefix and a suffix of `old` are taken off its ends. When
 * `raw` is not `old` with something inserted — a deletion, a selection
 * replaced by the browser already — it is taken as it is.
 */
function typedInto(raw: string, old: string) {
  if (raw.length <= old.length) return raw
  for (let k = 0; k <= old.length; k++) {
    if (raw.startsWith(old.slice(0, k)) && raw.endsWith(old.slice(k))) {
      return raw.slice(k, raw.length - old.length + k)
    }
  }
  return raw
}

/**
 * A count with −/+ buttons a gloved thumb can hit, and a box in the middle for
 * typing a bigger number. Below `min` is never sent. A zero is a real value,
 * drawn in full ink — a placeholder would be a hint, and a hint grey reads as
 * an empty box in sunlight. Tapping the box means "type a new count": the
 * first digit typed after a tap replaces what was there, so nobody has to
 * delete a "0", or a "42", first.
 */
export function Stepper({ label, value, min = 0, max = 99, hint, disabled, onChange }: Props) {
  const id = useId()
  const hintId = `${id}-hint`

  // True from a tap until the first keystroke. The text is also selected on
  // focus and click so the replacement is visible, but Safari places the caret
  // where the finger landed *after* both have fired, which undoes the
  // selection — so the first keystroke cannot rely on it and is read here
  // as a replacement whatever the caret did.
  const replacing = useRef(false)

  const clamp = (n: number) => Math.min(max, Math.max(min, n))

  const arm = (e: React.SyntheticEvent<HTMLInputElement>) => {
    replacing.current = true
    const box = e.currentTarget
    box.setSelectionRange(0, box.value.length)
  }

  /**
   * Digits only, read as a number. When the first keystroke after a tap
   * arrives with the old count still in it — "427", "472" or "742" for a 7
   * typed into "42" — the old count is a stand-in, not digits, and is dropped.
   */
  const parse = (raw: string) => {
    const digits = raw.replace(/\D/g, '')
    const typed = replacing.current ? typedInto(digits, String(value)) : digits
    replacing.current = false
    return clamp(Number(typed) || min)
  }

  return (
    <div>
      <label htmlFor={id} className="block font-medium">{label}</label>
      <div className="mt-1 flex items-stretch gap-2">
        <button
          type="button"
          aria-label={`Fewer ${label.toLowerCase()}`}
          disabled={disabled || value <= min}
          onClick={() => onChange(clamp(value - 1))}
          className="min-h-11 min-w-12 rounded-lg border border-rule bg-sheet text-xl font-semibold active:bg-press disabled:text-ink-2"
        >
          −
        </button>
        <input
          id={id}
          type="text"
          inputMode="numeric"
          pattern="[0-9]*"
          enterKeyHint="done"
          autoComplete="off"
          disabled={disabled}
          aria-describedby={hint ? hintId : undefined}
          value={String(value)}
          onFocus={arm}
          onClick={arm}
          onChange={e => onChange(parse(e.target.value))}
          className="min-h-11 w-full min-w-0 rounded-lg border border-rule bg-sheet text-center font-semibold"
        />
        <button
          type="button"
          aria-label={`More ${label.toLowerCase()}`}
          disabled={disabled || value >= max}
          onClick={() => onChange(clamp(value + 1))}
          className="min-h-11 min-w-12 rounded-lg border border-rule bg-sheet text-xl font-semibold active:bg-press disabled:text-ink-2"
        >
          +
        </button>
      </div>
      {hint && <p id={hintId} className="mt-1 text-sm text-ink-2">{hint}</p>}
    </div>
  )
}
