import { Tabs as RadixTabs } from 'radix-ui';
import type { ComponentPropsWithoutRef } from 'react';

import { cn } from '@/lib/cn';

export const Tabs = RadixTabs.Root;

export function TabsList({
  className,
  ...props
}: ComponentPropsWithoutRef<typeof RadixTabs.List>) {
  return (
    <RadixTabs.List
      className={cn(
        'flex items-center gap-1 overflow-x-auto border-b border-border px-1 no-scrollbar',
        className,
      )}
      {...props}
    />
  );
}

export function TabsTrigger({
  className,
  ...props
}: ComponentPropsWithoutRef<typeof RadixTabs.Trigger>) {
  return (
    <RadixTabs.Trigger
      className={cn(
        'relative whitespace-nowrap px-3 py-2 text-base font-medium text-text-muted',
        'transition-colors hover:text-text',
        // The underline is drawn inside the trigger so it sits exactly on the
        // list's bottom border regardless of scroll position.
        'after:absolute after:inset-x-2 after:-bottom-px after:h-0.5 after:rounded-full',
        'after:bg-transparent after:transition-colors',
        'data-[state=active]:text-text data-[state=active]:after:bg-accent',
        className,
      )}
      {...props}
    />
  );
}

export function TabsContent({
  className,
  ...props
}: ComponentPropsWithoutRef<typeof RadixTabs.Content>) {
  return <RadixTabs.Content className={cn('pt-4 outline-none', className)} {...props} />;
}

/** Count chip for a tab label, e.g. `Warranties (3)`. */
export function TabCount({ value }: { value: number | undefined }) {
  if (value === undefined) return null;
  return (
    <span className="ml-1.5 rounded-full bg-surface-sunken px-1.5 py-0.5 text-2xs tabular-nums text-text-subtle">
      {value}
    </span>
  );
}
