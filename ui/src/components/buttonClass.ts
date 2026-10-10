export type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'

const styles: Record<Variant, string> = {
  primary: 'bg-accent text-accent-ink active:brightness-90 disabled:bg-rule disabled:text-ink-2',
  secondary: 'border border-rule bg-sheet text-ink active:bg-press disabled:text-ink-2',
  ghost: 'text-ink active:bg-press disabled:text-ink-2',
  danger: 'border border-danger text-danger active:bg-danger-soft disabled:text-ink-2',
}

/**
 * min-h-11 and min-w-11 are 44px: the smallest target that is reliably
 * tappable, including for a one-glyph label like "+". Shared by Button,
 * ButtonLink and the plain <a> tags that download a file, so a link that acts
 * like a button is the same size as one.
 */
export const buttonClass = (variant: Variant, className = '') =>
  `inline-flex min-h-11 min-w-11 items-center justify-center gap-2 rounded-lg px-4 font-semibold ${styles[variant]} ${className}`
