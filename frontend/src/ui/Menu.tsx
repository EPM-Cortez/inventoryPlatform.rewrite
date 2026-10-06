import { DropdownMenu } from 'radix-ui';
import type { ComponentPropsWithoutRef, ReactNode } from 'react';

import { cn } from '@/lib/cn';

const CONTENT =
  'z-50 min-w-44 overflow-hidden rounded-lg border border-border bg-surface-raised p-1 ' +
  'shadow-lg origin-(--radix-dropdown-menu-content-transform-origin) ' +
  'data-[state=open]:animate-[popover-in_var(--duration-fast)_var(--ease-enter)] ' +
  'data-[state=closed]:animate-[popover-out_var(--duration-instant)_var(--ease-exit)]';

const ITEM =
  'flex cursor-pointer select-none items-center gap-2 rounded-sm px-2 py-1.5 text-base ' +
  'text-text outline-none transition-colors duration-(--duration-instant) ' +
  'data-[highlighted]:bg-surface-hover data-[disabled]:pointer-events-none data-[disabled]:opacity-50 ' +
  '[&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-text-subtle';

export const Menu = DropdownMenu.Root;
export const MenuTrigger = DropdownMenu.Trigger;

export function MenuContent({
  className,
  align = 'end',
  sideOffset = 6,
  children,
  ...props
}: ComponentPropsWithoutRef<typeof DropdownMenu.Content>) {
  return (
    <DropdownMenu.Portal>
      <DropdownMenu.Content
        align={align}
        sideOffset={sideOffset}
        className={cn(CONTENT, className)}
        {...props}
      >
        {children}
      </DropdownMenu.Content>
    </DropdownMenu.Portal>
  );
}

export function MenuItem({
  className,
  destructive,
  ...props
}: ComponentPropsWithoutRef<typeof DropdownMenu.Item> & { destructive?: boolean }) {
  return (
    <DropdownMenu.Item
      className={cn(
        ITEM,
        destructive &&
          'text-danger data-[highlighted]:bg-danger-soft [&_svg]:text-danger',
        className,
      )}
      {...props}
    />
  );
}

export function MenuSeparator() {
  return <DropdownMenu.Separator className="my-1 h-px bg-border" />;
}

export function MenuLabel({ children }: { children: ReactNode }) {
  return (
    <DropdownMenu.Label className="px-2 py-1.5 text-xs font-medium uppercase tracking-wide text-text-subtle">
      {children}
    </DropdownMenu.Label>
  );
}
