import { Slot } from 'radix-ui';
import type { ButtonHTMLAttributes, ReactNode } from 'react';

import { cn } from '@/lib/cn';
import { Spinner } from './Spinner';

export type ButtonVariant =
  | 'primary'
  | 'secondary'
  | 'ghost'
  | 'danger'
  | 'danger-subtle'
  | 'link';
export type ButtonSize = 'xs' | 'sm' | 'md' | 'lg';

const VARIANTS: Record<ButtonVariant, string> = {
  primary:
    'bg-accent text-accent-fg shadow-sm hover:bg-accent-hover active:bg-accent-active border border-transparent',
  secondary:
    'bg-surface text-text border border-border-strong shadow-sm hover:bg-surface-hover active:bg-surface-sunken',
  ghost: 'bg-transparent text-text-muted border border-transparent hover:bg-surface-hover hover:text-text',
  danger:
    'bg-danger text-white shadow-sm hover:bg-danger-hover border border-transparent dark:text-[#2b0d0a]',
  'danger-subtle':
    'bg-danger-soft text-danger border border-danger-border hover:brightness-95 dark:hover:brightness-125',
  link: 'bg-transparent text-accent underline-offset-4 hover:underline border border-transparent p-0 h-auto shadow-none',
};

const SIZES: Record<ButtonSize, string> = {
  xs: 'h-7 px-2 gap-1.5 text-xs rounded-sm',
  sm: 'h-8 px-2.5 gap-1.5 text-sm rounded-md',
  md: 'h-9 px-3.5 gap-2 text-base rounded-md',
  lg: 'h-11 px-5 gap-2 text-lg rounded-lg',
};

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Shows a spinner and blocks interaction. Keeps the label so width is stable. */
  loading?: boolean;
  /**
   * Renders the single child element instead of a `<button>`, e.g. a router
   * `<Link>`, keeping the button styling.
   *
   * The child must be exactly one element, and it owns its own content — so
   * `iconLeft`, `iconRight` and `loading` are ignored. Put icons inside the
   * child instead: `<Button asChild><Link>Open <ArrowRight /></Link></Button>`.
   */
  asChild?: boolean;
  iconLeft?: ReactNode;
  iconRight?: ReactNode;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  loading = false,
  asChild = false,
  iconLeft,
  iconRight,
  className,
  children,
  disabled,
  type,
  ...props
}: ButtonProps) {
  const classes = cn(
    'inline-flex shrink-0 select-none items-center justify-center whitespace-nowrap font-medium',
    'transition-[color,background-color,border-color,box-shadow,transform]',
    'duration-(--duration-instant) ease-out',
    // A press that moves is the cheapest possible confirmation that the click
    // registered — it lands before any network request can answer. Scaled rather
    // than nudged so it cannot shift anything around it.
    'active:scale-[0.97] active:duration-[50ms]',
    'disabled:pointer-events-none disabled:opacity-50 disabled:active:scale-100',
    '[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg]:size-4',
    VARIANTS[variant],
    variant === 'link' ? '' : SIZES[size],
    className,
  );

  // Slot requires a single element child. Wrapping `children` between the two
  // icon slots would hand it an array of three — `[undefined, child, undefined]` —
  // and `Children.only` throws, taking the whole page down with it.
  if (asChild) {
    return (
      <Slot.Root className={classes} aria-busy={loading || undefined} {...props}>
        {children}
      </Slot.Root>
    );
  }

  return (
    <button
      type={type ?? 'button'}
      className={classes}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading ? <Spinner className="size-4" /> : iconLeft}
      {children}
      {iconRight}
    </button>
  );
}

/** Square button for a lone icon. `label` is required — it becomes the a11y name. */
export interface IconButtonProps extends Omit<ButtonProps, 'iconLeft' | 'iconRight'> {
  label: string;
}

const ICON_SIZES: Record<ButtonSize, string> = {
  xs: 'size-7 rounded-sm',
  sm: 'size-8 rounded-md',
  md: 'size-9 rounded-md',
  lg: 'size-11 rounded-lg',
};

export function IconButton({
  label,
  size = 'md',
  variant = 'ghost',
  className,
  children,
  ...props
}: IconButtonProps) {
  return (
    <Button
      variant={variant}
      size={size}
      aria-label={label}
      title={label}
      className={cn('p-0', ICON_SIZES[size], className)}
      {...props}
    >
      {children}
    </Button>
  );
}
