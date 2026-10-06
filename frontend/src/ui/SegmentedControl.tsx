import { RadioGroup } from 'radix-ui';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

export interface SegmentedOption<T extends string> {
  value: T;
  label: ReactNode;
}

export interface SegmentedControlProps<T extends string> {
  /** Accessible name for the group. Shown above it unless `hideLabel` is set. */
  label: string;
  hideLabel?: boolean;
  value: T;
  onChange: (value: T) => void;
  options: readonly SegmentedOption<T>[];
  size?: 'sm' | 'md';
  disabled?: boolean;
  className?: string;
}

/**
 * A short, fixed set of mutually exclusive choices shown all at once — quicker
 * than opening a select when there are only a handful. Radio semantics, so arrow
 * keys move between segments and the group is one tab stop.
 */
export function SegmentedControl<T extends string>({
  label,
  hideLabel,
  value,
  onChange,
  options,
  size = 'md',
  disabled,
  className,
}: SegmentedControlProps<T>) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      {!hideLabel && (
        <span className="text-sm font-medium text-text" aria-hidden="true">
          {label}
        </span>
      )}
      <RadioGroup.Root
        aria-label={label}
        value={value}
        onValueChange={(next) => onChange(next as T)}
        orientation="horizontal"
        disabled={disabled}
        className="inline-flex w-fit max-w-full gap-0.5 overflow-x-auto rounded-md border border-border bg-surface-sunken p-0.5 no-scrollbar"
      >
        {options.map((option) => (
          <RadioGroup.Item
            key={option.value}
            value={option.value}
            className={cn(
              'inline-flex items-center gap-1.5 whitespace-nowrap rounded-sm font-medium text-text-muted',
              'transition-[color,background-color,box-shadow] duration-(--duration-fast) ease-out',
              'hover:text-text disabled:pointer-events-none disabled:opacity-50',
              'data-[state=checked]:bg-surface data-[state=checked]:text-text data-[state=checked]:shadow-sm',
              '[&_svg]:size-3.5 [&_svg]:shrink-0',
              size === 'sm' ? 'h-7 px-2.5 text-sm' : 'h-8 px-3 text-base',
            )}
          >
            {option.label}
          </RadioGroup.Item>
        ))}
      </RadioGroup.Root>
    </div>
  );
}
