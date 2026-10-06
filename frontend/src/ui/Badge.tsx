import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

const TONES: Record<BadgeTone, string> = {
  neutral: 'bg-neutral-soft text-neutral border-neutral-border',
  accent: 'bg-accent-soft text-accent-soft-fg border-accent-soft',
  success: 'bg-success-soft text-success border-success-border',
  warning: 'bg-warning-soft text-warning border-warning-border',
  danger: 'bg-danger-soft text-danger border-danger-border',
  info: 'bg-info-soft text-info border-info-border',
};

export interface BadgeProps {
  tone?: BadgeTone;
  /** Adds a filled dot before the label — helps distinguish tones without relying on colour alone. */
  dot?: boolean;
  className?: string;
  children: ReactNode;
}

export function Badge({ tone = 'neutral', dot = false, className, children }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2 py-0.5',
        'text-xs font-medium',
        TONES[tone],
        className,
      )}
    >
      {dot && (
        <span className="size-1.5 rounded-full bg-current opacity-80" aria-hidden="true" />
      )}
      {children}
    </span>
  );
}

/** Small monospace chip for identifiers: asset tags, serials, codes. */
export function CodeChip({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-sm bg-surface-sunken px-1.5 py-0.5',
        'font-mono text-xs tracking-tight text-text-muted',
        className,
      )}
    >
      {children}
    </span>
  );
}
