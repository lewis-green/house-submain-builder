import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import { Link, type LinkProps } from 'react-router'
import { buttonClass, type Variant } from './buttonClass'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  children: ReactNode
  ref?: Ref<HTMLButtonElement>
}

export function Button({ variant = 'primary', className = '', children, type = 'button', ...rest }: Props) {
  return (
    <button type={type} className={buttonClass(variant, className)} {...rest}>
      {children}
    </button>
  )
}

interface LinkButtonProps extends LinkProps {
  variant?: Variant
  children: ReactNode
}

/** A route link drawn and sized like a button, for navigation that is an action. */
export function ButtonLink({ variant = 'secondary', className = '', children, ...rest }: LinkButtonProps) {
  return (
    <Link className={buttonClass(variant, className)} {...rest}>
      {children}
    </Link>
  )
}
