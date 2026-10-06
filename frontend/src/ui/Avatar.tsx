import type { ReactNode } from 'react';

import { initials } from '@/lib/format';
import { cn } from '@/lib/cn';

/*
 * Soft tones rotate by name so a long list of people is easier to scan. They are
 * decoration only — the name always sits beside the avatar, so nothing depends
 * on telling the colours apart.
 */
const TONES = [
  'bg-accent-soft text-accent-soft-fg',
  'bg-info-soft text-info',
  'bg-success-soft text-success',
  'bg-warning-soft text-warning',
  'bg-neutral-soft text-text-muted',
];

const SIZES = {
  sm: 'size-7 text-2xs',
  md: 'size-9 text-xs',
  lg: 'size-14 text-lg',
  /** Record headers, where the avatar stands in for a picture of the thing. */
  xl: 'size-20 text-2xl',
} as const;

function toneFor(name: string): string {
  let hash = 0;
  for (let index = 0; index < name.length; index += 1) {
    hash = (hash * 31 + name.charCodeAt(index)) | 0;
  }
  return TONES[Math.abs(hash) % TONES.length] ?? 'bg-accent-soft text-accent-soft-fg';
}

export function Avatar({
  name,
  size = 'md',
  className,
  children,
}: {
  name: string;
  size?: keyof typeof SIZES;
  className?: string;
  /** Replaces the initials, e.g. an icon for something that is not a person. */
  children?: ReactNode;
}) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'flex shrink-0 select-none items-center justify-center rounded-full font-semibold',
        '[&_svg]:size-[45%]',
        SIZES[size],
        toneFor(name),
        className,
      )}
    >
      {children ?? initials(name)}
    </span>
  );
}
