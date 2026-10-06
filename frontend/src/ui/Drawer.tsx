import { X } from 'lucide-react';
import { Dialog as RadixDialog } from 'radix-ui';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';
import { useDeferredClose } from './useDeferredClose';

/** Must stay in step with the exit animations below. */
const EXIT_MS = 180;
import { IconButton } from './Button';

export type DrawerWidth = 'sm' | 'md' | 'lg';

const WIDTHS: Record<DrawerWidth, string> = {
  sm: 'max-w-md',
  md: 'max-w-xl',
  lg: 'max-w-3xl',
};

export interface DrawerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  width?: DrawerWidth;
  footer?: ReactNode;
  children: ReactNode;
  dismissible?: boolean;
}

/**
 * Right-hand side panel, for long forms where the list behind still matters —
 * editing a full asset record, or a bulk change across a queue of assets.
 *
 * Short record forms (people, departments, accounts, locations) use `Dialog`
 * instead: a handful of fields reads better centred than stretched down a panel.
 */
export function Drawer({
  open,
  onOpenChange,
  title,
  description,
  width = 'md',
  footer,
  children,
  dismissible = true,
}: DrawerProps) {
  const { closing, requestClose } = useDeferredClose(onOpenChange, EXIT_MS);

  return (
    <RadixDialog.Root
      open={open && !closing}
      onOpenChange={(next) => (next ? onOpenChange(true) : requestClose())}
    >
      <RadixDialog.Portal>
        <RadixDialog.Overlay className={cn(
            'fixed inset-0 z-50 bg-[#0d1117]/40',
            'data-[state=open]:animate-[overlay-in_var(--duration-fast)_ease-out]',
            'data-[state=closed]:animate-[overlay-out_var(--duration-fast)_var(--ease-exit)]',
          )} />
        <RadixDialog.Content
          className={cn(
            'fixed inset-y-0 right-0 z-50 flex w-full flex-col',
            'border-l border-border bg-surface-raised shadow-overlay',
            'data-[state=open]:animate-[drawer-in_var(--duration-slow)_var(--ease-enter)]',
            'data-[state=closed]:animate-[drawer-out_var(--duration-base)_var(--ease-exit)]',
            WIDTHS[width],
          )}
          onEscapeKeyDown={(event) => {
            if (!dismissible) event.preventDefault();
          }}
          onPointerDownOutside={(event) => {
            if (!dismissible) event.preventDefault();
          }}
        >
          <div className="flex items-start justify-between gap-4 border-b border-border px-5 py-4">
            <div className="min-w-0">
              <RadixDialog.Title className="text-xl font-semibold tracking-[-0.01em] text-text">
                {title}
              </RadixDialog.Title>
              {description ? (
                <RadixDialog.Description className="mt-1 text-sm text-text-muted">
                  {description}
                </RadixDialog.Description>
              ) : (
                <RadixDialog.Description className="sr-only">{title}</RadixDialog.Description>
              )}
            </div>
            {dismissible && (
              <RadixDialog.Close asChild>
                <IconButton label="Close" size="sm" className="-mr-1 -mt-1">
                  <X />
                </IconButton>
              </RadixDialog.Close>
            )}
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>

          {footer && (
            <div className="flex items-center justify-end gap-2 border-t border-border bg-surface-sunken/60 px-5 py-3.5">
              {footer}
            </div>
          )}
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}
