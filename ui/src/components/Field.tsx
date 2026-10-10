import { useId, type InputHTMLAttributes } from 'react'

interface Props extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> {
  label: string
  value: string
  onChange: (value: string) => void
  hint?: string
  errors?: string[]
  type?: 'text' | 'number'
  inputMode?: 'text' | 'numeric' | 'decimal'
}

/**
 * A labelled text box. Keyboard hints (autocapitalize, autocomplete,
 * enterkeyhint) pass straight through, because the right keyboard for a house
 * name is not the right one for a wattage.
 */
export function Field({ label, value, onChange, hint, errors, type = 'text', inputMode, className = '', ...rest }: Props) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const invalid = (errors?.length ?? 0) > 0

  return (
    <div className={className}>
      <label htmlFor={id} className="block font-medium">{label}</label>
      <input
        id={id}
        type={type}
        inputMode={inputMode}
        value={value}
        onChange={e => onChange(e.target.value)}
        aria-invalid={invalid}
        aria-describedby={[hint ? hintId : null, invalid ? errorId : null].filter(Boolean).join(' ') || undefined}
        className={`mt-1 min-h-11 w-full min-w-0 rounded-lg border bg-sheet px-3 ${invalid ? 'border-danger' : 'border-rule'}`}
        {...rest}
      />
      {hint && <p id={hintId} className="mt-1 text-sm text-ink-2">{hint}</p>}
      {invalid && (
        <ul id={errorId} className="mt-1 space-y-0.5">
          {errors!.map(e => <li key={e} className="text-sm font-medium text-danger">{e}</li>)}
        </ul>
      )}
    </div>
  )
}
