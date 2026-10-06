import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';
import type { BadgeTone } from './Badge';

const DOTS: Record<BadgeTone, string> = {
  neutral: 'bg-text-subtle',
  accent: 'bg-accent',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
  info: 'bg-info',
};

/**
 * A coloured dot and a plain word — the quiet form of a status, for dense rows
 * and headers where a pill per cell turns the page into confetti. The word
 * always carries the meaning; the dot only helps the eye find it.
 */
export function StatusDot({
  tone,
  children,
  muted,
  className,
}: {
  tone: BadgeTone;
  children: ReactNode;
  /** Secondary facts (condition beside status) read in the muted text colour. */
  muted?: boolean;
  className?: string;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap text-sm',
        muted ? 'text-text-muted' : 'text-text',
        className,
      )}
    >
      <span className={cn('size-2 shrink-0 rounded-full', DOTS[tone])} aria-hidden="true" />
      {children}
    </span>
  );
}
