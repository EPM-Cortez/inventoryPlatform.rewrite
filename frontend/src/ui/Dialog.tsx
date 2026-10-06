import { X } from 'lucide-react';
import { Dialog as RadixDialog } from 'radix-ui';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';
import { IconButton } from './Button';
import { useDeferredClose } from './useDeferredClose';

/** Must stay in step with the exit animations below. */
const EXIT_MS = 130;

const OVERLAY =
  'fixed inset-0 z-50 bg-[#0d1117]/45 backdrop-blur-[2px] ' +
  'data-[state=open]:animate-[overlay-in_var(--duration-fast)_ease-out] ' +
  'data-[state=closed]:animate-[overlay-out_var(--duration-fast)_var(--ease-exit)]';

export type DialogSize = 'sm' | 'md' | 'lg' | 'xl';

const SIZES: Record<DialogSize, string> = {
  sm: 'max-w-md',
  md: 'max-w-lg',
  lg: 'max-w-2xl',
  xl: 'max-w-4xl',
};

export interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  size?: DialogSize;
  /** Sticky action row. Put the primary action last, matching platform order. */
  footer?: ReactNode;
  children: ReactNode;
  /** Blocks dismissal by overlay click and Escape — use only while saving. */
  dismissible?: boolean;
}

export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  size = 'md',
  footer,
  children,
  dismissible = true,
}: DialogProps) {
  const { closing, requestClose } = useDeferredClose(onOpenChange, EXIT_MS);

  return (
    <RadixDialog.Root
      open={open && !closing}
      onOpenChange={(next) => (next ? onOpenChange(true) : requestClose())}
    >
      <RadixDialog.Portal>
        <RadixDialog.Overlay className={OVERLAY} />
        <RadixDialog.Content
          className={cn(
            'fixed left-1/2 top-1/2 z-50 flex max-h-[min(90dvh,44rem)] w-[calc(100vw-2rem)]',
            // Centred with the `translate` property rather than a transform
            // utility: the enter/exit keyframes animate `transform`, and a
            // running animation replaces that property outright — which would
            // drop the centring for the length of the animation and snap it
            // back at the end.
            '[translate:-50%_-50%] flex-col overflow-hidden',
            'rounded-xl border border-border bg-surface-raised shadow-overlay',
            'data-[state=open]:animate-[dialog-in_var(--duration-base)_var(--ease-enter)]',
            'data-[state=closed]:animate-[dialog-out_var(--duration-fast)_var(--ease-exit)]',
            SIZES[size],
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
                // Radix warns without a description; keep it available to
                // assistive tech without taking layout space.
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

export const DialogClose = RadixDialog.Close;
