interface Props {
  label: string
  hint?: string
  checked: boolean
  disabled?: boolean
  onChange: (checked: boolean) => void
}

/** A checkbox with room for a line explaining what it changes. */
export function Toggle({ label, hint, checked, disabled, onChange }: Props) {
  return (
    <label className="flex min-h-11 items-start gap-3">
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={e => onChange(e.target.checked)}
        className="mt-1 size-5 shrink-0 rounded border-slate-300"
      />
      <span>
        <span className="block text-sm font-medium text-slate-700">{label}</span>
        {hint && <span className="block text-xs text-slate-500">{hint}</span>}
      </span>
    </label>
  )
}
