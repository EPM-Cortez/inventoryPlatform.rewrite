import { X } from 'lucide-react';
import type { ReactNode } from 'react';

import { IconButton } from './Button';

/**
 * Actions for the ticked rows of a table, floating at the bottom of the view.
 *
 * It appears below the table rather than above it on purpose: a bar that slid in
 * above the rows would push every row down on the first tick, and the next
 * click — aimed at the row underneath — would land on a different one.
 */
export function SelectionBar({
  count,
  noun,
  note,
  onClear,
  children,
}: {
  count: number;
  /** Plural form, e.g. "assets" — "1 asset selected" is derived. */
  noun: { one: string; other: string };
  /** A caveat under the count, e.g. rows selected on other pages. */
  note?: ReactNode;
  onClear: () => void;
  /** The actions; the primary one last. */
  children: ReactNode;
}) {
  if (count === 0) return null;

  return (
    <div className="pointer-events-none sticky bottom-3 z-20 flex justify-center px-3 pb-3">
      <div
        className="animate-view pointer-events-auto flex max-w-full flex-wrap items-center gap-3 rounded-lg border border-border-strong bg-surface-raised px-3 py-2 shadow-lg"
        role="region"
        aria-label="Selected rows"
        aria-live="polite"
      >
        <div className="min-w-0">
          <p className="text-sm font-medium text-text">
            {count} {count === 1 ? noun.one : noun.other} selected
          </p>
          {note && <p className="text-xs text-warning">{note}</p>}
        </div>
        <div className="flex items-center gap-2">
          {children}
          <IconButton label="Clear selection" size="sm" onClick={onClear}>
            <X />
          </IconButton>
        </div>
      </div>
    </div>
  );
}
