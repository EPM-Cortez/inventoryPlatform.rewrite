import { Building2, DoorOpen, Warehouse } from 'lucide-react';

import { cn } from '@/lib/cn';

/**
 * A building for anything that holds other places, a door for a room, a
 * warehouse for a store — the location counterpart of `DeviceGlyph`, so the
 * locations table opens each row the same way the asset register does.
 */
export function LocationGlyph({
  type,
  hasChildren,
  size = 'md',
  className,
}: {
  type: string | null;
  hasChildren: boolean;
  size?: 'md' | 'xl';
  className?: string;
}) {
  const kind = (type ?? '').toLowerCase();
  const Icon = /store|stock|cupboard|warehouse/.test(kind)
    ? Warehouse
    : hasChildren || /building|block|site|wing|floor/.test(kind)
      ? Building2
      : DoorOpen;

  return (
    <span
      aria-hidden="true"
      className={cn(
        'flex shrink-0 items-center justify-center bg-surface-sunken text-text-muted',
        size === 'xl' ? 'size-20 rounded-xl [&_svg]:size-9' : 'size-9 rounded-md [&_svg]:size-[1.125rem]',
        className,
      )}
    >
      <Icon />
    </span>
  );
}
