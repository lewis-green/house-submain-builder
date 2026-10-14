import { useId } from 'react'

interface Option {
  value: string
  label: string
}

interface Props {
  label: string
  value: string
  options: Option[]
  onChange: (value: string) => void
  hint?: string
  errors?: string[]
  disabled?: boolean
}

/** A labelled select with the app's own chevron, and room for a hint and errors under it, as Field has. */
export function SelectField({ label, value, options, onChange, hint, errors, disabled }: Props) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const invalid = (errors?.length ?? 0) > 0

  return (
    <div>
      <label htmlFor={id} className="block font-medium">{label}</label>
      <select
        id={id}
        value={value}
        disabled={disabled}
        onChange={e => onChange(e.target.value)}
        aria-invalid={invalid}
        aria-describedby={[hint ? hintId : null, invalid ? errorId : null].filter(Boolean).join(' ') || undefined}
        className={`select mt-1 w-full min-w-0 rounded-lg border bg-sheet pl-3 ${invalid ? 'border-danger' : 'border-rule'}`}
      >
        {options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
      {hint && <p id={hintId} className="mt-1 text-sm text-ink-2">{hint}</p>}
      {invalid && (
        <ul id={errorId} className="mt-1 space-y-0.5">
          {errors!.map(e => <li key={e} className="text-sm font-medium text-danger">{e}</li>)}
        </ul>
      )}
    </div>
  )
}
