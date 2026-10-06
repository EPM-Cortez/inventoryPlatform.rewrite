import type { ReactNode } from 'react';

import { Button } from './Button';

/**
 * The strip across the top of every list: search with the result count beside
 * it, then a row of filter buttons. One component so every register — assets,
 * people, departments, leavers, accounts — reads the same way.
 */
export function ListToolbar({
  search,
  count,
  filters,
  onClearFilters,
  actions,
}: {
  /** Usually a `SearchInput`; it fills the available width. */
  search: ReactNode;
  /** e.g. "175 people". Omitted while the first page loads. */
  count?: ReactNode;
  /** `FilterButton`s. The row is left out entirely when there are none. */
  filters?: ReactNode;
  /** Shown only when something is filtered. */
  onClearFilters?: () => void;
  /** Extra controls at the right of the filter row, e.g. expand/collapse all. */
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-2.5 border-b border-border p-3">
      <div className="flex items-center gap-3">
        <div className="min-w-0 flex-1">{search}</div>
        {count !== undefined && (
          <p
            className="hidden shrink-0 text-sm tabular-nums text-text-subtle sm:block"
            aria-live="polite"
          >
            {count}
          </p>
        )}
      </div>

      {filters && (
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filters">
          {filters}
          {onClearFilters && (
            <Button size="sm" variant="ghost" onClick={onClearFilters} className="text-text-muted">
              Clear all
            </Button>
          )}
          {actions && <div className="ml-auto flex items-center gap-1">{actions}</div>}
        </div>
      )}
    </div>
  );
}
