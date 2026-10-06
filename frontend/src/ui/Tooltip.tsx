import { Tooltip as RadixTooltip } from 'radix-ui';
import type { ReactNode } from 'react';

export function TooltipProvider({ children }: { children: ReactNode }) {
  return (
    <RadixTooltip.Provider delayDuration={350} skipDelayDuration={200}>
      {children}
    </RadixTooltip.Provider>
  );
}

export interface TooltipProps {
  content: ReactNode;
  side?: 'top' | 'right' | 'bottom' | 'left';
  children: ReactNode;
}

/**
 * Supplementary hints only. A tooltip is invisible to touch users and to anyone
 * scanning quickly, so it must never be the only place information appears.
 */
export function Tooltip({ content, side = 'top', children }: TooltipProps) {
  if (!content) return <>{children}</>;

  return (
    <RadixTooltip.Root>
      <RadixTooltip.Trigger asChild>{children}</RadixTooltip.Trigger>
      <RadixTooltip.Portal>
        <RadixTooltip.Content
          side={side}
          sideOffset={6}
          collisionPadding={8}
          className={
            'z-50 max-w-64 rounded-md border border-border bg-surface-raised px-2.5 py-1.5 text-xs text-text shadow-lg ' +
            'origin-(--radix-tooltip-content-transform-origin) ' +
            'data-[state=delayed-open]:animate-[popover-in_var(--duration-fast)_var(--ease-enter)] ' +
            'data-[state=instant-open]:animate-[popover-in_var(--duration-instant)_var(--ease-enter)] ' +
            'data-[state=closed]:animate-[popover-out_var(--duration-instant)_var(--ease-exit)]'
          }
        >
          {content}
          <RadixTooltip.Arrow className="fill-[var(--surface-raised)]" />
        </RadixTooltip.Content>
      </RadixTooltip.Portal>
    </RadixTooltip.Root>
  );
}
