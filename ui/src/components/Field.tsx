import { useId } from 'react'

interface Props {
  label: string
  value: string
  onChange: (value: string) => void
  hint?: string
  errors?: string[]
  type?: 'text' | 'number'
  inputMode?: 'text' | 'numeric' | 'decimal'
}

export function Field({ label, value, onChange, hint, errors, type = 'text', inputMode }: Props) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const invalid = (errors?.length ?? 0) > 0

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-slate-700">{label}</label>
      <input
        id={id}
        type={type}
        inputMode={inputMode}
        value={value}
        onChange={e => onChange(e.target.value)}
        aria-invalid={invalid}
        aria-describedby={[hint ? hintId : null, invalid ? errorId : null].filter(Boolean).join(' ') || undefined}
        className={`mt-1 min-h-11 w-full rounded-lg border px-3 ${invalid ? 'border-red-500' : 'border-slate-300'}`}
      />
      {hint && <p id={hintId} className="mt-1 text-xs text-slate-500">{hint}</p>}
      {invalid && (
        <ul id={errorId} className="mt-1 space-y-0.5">
          {errors!.map(e => <li key={e} className="text-xs text-red-600">{e}</li>)}
        </ul>
      )}
    </div>
  )
}
