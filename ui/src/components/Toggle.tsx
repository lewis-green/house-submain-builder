interface Props {
  label: string
  hint?: string
  checked: boolean
  disabled?: boolean
  onChange: (checked: boolean) => void
}

/** A checkbox with room for a line explaining what it changes. The whole row is the target. */
export function Toggle({ label, hint, checked, disabled, onChange }: Props) {
  return (
    <label className="flex min-h-11 cursor-pointer items-center gap-2 rounded-lg active:bg-press">
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={e => onChange(e.target.checked)}
        className="checkbox"
      />
      {/* min-w-0 and wrap-anywhere: beside a 44px box on a 320px phone at 200% text,
          a word like "terminations" has to break rather than push the page sideways. */}
      <span className="min-w-0 wrap-anywhere">
        <span className="block font-medium">{label}</span>
        {hint && <span className="block text-sm text-ink-2">{hint}</span>}
      </span>
    </label>
  )
}
