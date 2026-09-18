import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'secondary' | 'ghost'

const styles: Record<Variant, string> = {
  primary: 'bg-slate-900 text-white active:bg-slate-700 disabled:bg-slate-300',
  secondary: 'border border-slate-300 bg-white text-slate-900 active:bg-slate-50 disabled:text-slate-400',
  ghost: 'text-slate-600 active:bg-slate-100 disabled:text-slate-300',
}

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  children: ReactNode
}

/** min-h-11 is 44px: the smallest target that is reliably tappable. */
export function Button({ variant = 'primary', className = '', children, ...rest }: Props) {
  return (
    <button
      type="button"
      className={`min-h-11 rounded-lg px-4 text-sm font-medium ${styles[variant]} ${className}`}
      {...rest}
    >
      {children}
    </button>
  )
}
