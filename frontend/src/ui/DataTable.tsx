import { ArrowDown, ArrowUp, ChevronsUpDown } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';
import { Checkbox } from './Checkbox';
import { EmptyState } from './EmptyState';
import { Skeleton } from './Skeleton';

export interface Column<TRow> {
  /** Stable identity for React keys and sort wiring. */
  id: string;
  header: ReactNode;
  cell: (row: TRow) => ReactNode;
  /** Server sort key. Omit to make the column unsortable. */
  sortKey?: string;
  align?: 'left' | 'right' | 'center';
  /** Tailwind width utility, e.g. `w-40`. Applied to the `<col>`. */
  width?: string;
  /** Hidden below the given breakpoint to keep narrow screens readable. */
  hideBelow?: 'sm' | 'md' | 'lg' | 'xl' | '2xl';
}

const HIDE_BELOW: Record<NonNullable<Column<unknown>['hideBelow']>, string> = {
  sm: 'hidden sm:table-cell',
  md: 'hidden md:table-cell',
  lg: 'hidden lg:table-cell',
  xl: 'hidden xl:table-cell',
  '2xl': 'hidden 2xl:table-cell',
};

/**
 * The `<col>` of a hidden column has to go too. Left in place, it keeps its
 * width while its cells vanish, and every width after it lands one column to
 * the left — the tag column collapses and "Held by" takes the condition's size.
 */
const HIDE_COL_BELOW: Record<NonNullable<Column<unknown>['hideBelow']>, string> = {
  sm: 'hidden sm:table-column',
  md: 'hidden md:table-column',
  lg: 'hidden lg:table-column',
  xl: 'hidden xl:table-column',
  '2xl': 'hidden 2xl:table-column',
};

const ALIGN = {
  left: 'text-left',
  right: 'text-right',
  center: 'text-center',
} as const;

export interface DataTableProps<TRow> {
  columns: Column<TRow>[];
  rows: TRow[];
  rowKey: (row: TRow) => string;
  loading?: boolean;
  /** Number of skeleton rows while loading. Match the usual page size. */
  skeletonRows?: number;
  empty?: ReactNode;
  /** Makes the whole row activatable. Rows stay keyboard reachable. */
  onRowClick?: (row: TRow) => void;
  /** Current sort expression, e.g. `-updatedAtUtc`. */
  sort?: string;
  onSortChange?: (sort: string) => void;
  className?: string;
  /** Adds a subtle tint to a row, e.g. to mark retired assets. */
  rowClassName?: (row: TRow) => string | undefined;
  /**
   * Turns on row selection. Both must be supplied together.
   *
   * Selection is held by the *caller*, keyed by `rowKey`, so it survives paging
   * and refetches — a bulk action needs the rows a user ticked three pages ago,
   * not just the ones currently rendered.
   */
  selectedKeys?: ReadonlySet<string>;
  onSelectionChange?: (keys: Set<string>) => void;
  /**
   * `fixed` sizes columns from their `width` and gives the rest to unsized ones,
   * so long values truncate instead of widening the table past its card. Use
   * it for wide registers; leave the default for short tables.
   */
  layout?: 'auto' | 'fixed';
}

export function DataTable<TRow>({
  columns,
  rows,
  rowKey,
  loading = false,
  skeletonRows = 8,
  empty,
  onRowClick,
  sort,
  onSortChange,
  className,
  rowClassName,
  selectedKeys,
  onSelectionChange,
  layout = 'auto',
}: DataTableProps<TRow>) {
  const selectable = Boolean(selectedKeys && onSelectionChange);
  // Only the rows on screen can be toggled by the header box; rows selected on
  // other pages are left alone, which is what makes cross-page selection work.
  const pageKeys = rows.map(rowKey);
  const allOnPageSelected =
    pageKeys.length > 0 && pageKeys.every((key) => selectedKeys?.has(key));
  const someOnPageSelected = pageKeys.some((key) => selectedKeys?.has(key));

  function toggleRow(key: string) {
    if (!selectedKeys || !onSelectionChange) return;
    const next = new Set(selectedKeys);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    onSelectionChange(next);
  }

  function togglePage() {
    if (!selectedKeys || !onSelectionChange) return;
    const next = new Set(selectedKeys);
    if (allOnPageSelected) for (const key of pageKeys) next.delete(key);
    else for (const key of pageKeys) next.add(key);
    onSelectionChange(next);
  }
  const descending = sort?.startsWith('-') ?? false;
  const activeSortKey = descending ? sort?.slice(1) : sort;

  function toggleSort(key: string) {
    if (!onSortChange) return;
    // First click on a new column sorts ascending; clicking the active column flips it.
    onSortChange(activeSortKey === key && !descending ? `-${key}` : key);
  }

  if (!loading && rows.length === 0) {
    return <>{empty ?? <EmptyState title="Nothing to show" />}</>;
  }

  return (
    <div className={cn('w-full overflow-x-auto', className)}>
      <table
        className={cn('w-full border-collapse text-base', layout === 'fixed' && 'table-fixed')}
      >
        <colgroup>
          {selectable && <col className="w-10" />}
          {columns.map((column) => (
            <col
              key={column.id}
              className={cn(column.width, column.hideBelow && HIDE_COL_BELOW[column.hideBelow])}
            />
          ))}
        </colgroup>

        <thead className="sticky top-0 z-10">
          <tr>
            {selectable && (
              <th scope="col" className="bg-surface-sunken px-3 py-2.5">
                <Checkbox
                  checked={
                    allOnPageSelected
                      ? true
                      : someOnPageSelected
                        ? 'indeterminate'
                        : false
                  }
                  onCheckedChange={togglePage}
                  label={
                    <span className="sr-only">
                      {allOnPageSelected ? 'Clear this page' : 'Select this page'}
                    </span>
                  }
                />
              </th>
            )}
            {columns.map((column) => {
              const sortable = Boolean(column.sortKey && onSortChange);
              const isActive = column.sortKey != null && column.sortKey === activeSortKey;
              return (
                <th
                  key={column.id}
                  scope="col"
                  aria-sort={
                    isActive ? (descending ? 'descending' : 'ascending') : undefined
                  }
                  className={cn(
                    'border-b border-border bg-surface-sunken px-3 py-2',
                    'text-xs font-semibold uppercase tracking-wide text-text-muted',
                    ALIGN[column.align ?? 'left'],
                    column.hideBelow && HIDE_BELOW[column.hideBelow],
                  )}
                >
                  {sortable ? (
                    <button
                      type="button"
                      onClick={() => toggleSort(column.sortKey!)}
                      className={cn(
                        // Buttons reset text-transform, which left sortable
                        // headers in mixed case beside uppercase plain ones.
                        'inline-flex items-center gap-1 rounded-xs uppercase tracking-wide transition-colors hover:text-text',
                        isActive && 'text-text',
                        column.align === 'right' && 'flex-row-reverse',
                      )}
                    >
                      {column.header}
                      {isActive ? (
                        descending ? (
                          <ArrowDown className="size-3" />
                        ) : (
                          <ArrowUp className="size-3" />
                        )
                      ) : (
                        <ChevronsUpDown className="size-3 opacity-40" />
                      )}
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              );
            })}
          </tr>
        </thead>

        <tbody>
          {loading
            ? Array.from({ length: skeletonRows }, (_, index) => (
                <tr key={`skeleton-${index}`} className="border-b border-border">
                  {selectable && <td className="px-3 py-2.5" />}
                  {columns.map((column) => (
                    <td
                      key={column.id}
                      className={cn(
                        'px-3 py-2.5',
                        column.hideBelow && HIDE_BELOW[column.hideBelow],
                      )}
                    >
                      <Skeleton className="h-4 w-full max-w-40" />
                    </td>
                  ))}
                </tr>
              ))
            : rows.map((row) => (
                <tr
                  key={rowKey(row)}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  // A clickable row needs a keyboard equivalent. Every such table
                  // also puts a real link in its first cell, so this is an
                  // enhancement rather than the only route in.
                  tabIndex={onRowClick ? 0 : undefined}
                  onKeyDown={
                    onRowClick
                      ? (event) => {
                          if (event.key === 'Enter' && event.target === event.currentTarget) {
                            onRowClick(row);
                          }
                        }
                      : undefined
                  }
                  className={cn(
                    // Striped, so the eye can follow a long row across the
                    // table without a ruler.
                    'border-b border-border/70 last:border-b-0 even:bg-row-stripe',
                    'transition-colors duration-(--duration-instant) ease-out',
                    onRowClick && 'cursor-pointer hover:bg-surface-sunken',
                    rowClassName?.(row),
                  )}
                >
                  {selectable && (
                    <td
                      className="px-3 py-2.5 align-middle"
                      // The checkbox must not trigger the row's own click.
                      onClick={(event) => event.stopPropagation()}
                    >
                      <Checkbox
                        checked={selectedKeys?.has(rowKey(row)) ?? false}
                        onCheckedChange={() => toggleRow(rowKey(row))}
                        label={<span className="sr-only">Select this row</span>}
                      />
                    </td>
                  )}
                  {columns.map((column) => (
                    <td
                      key={column.id}
                      className={cn(
                        'px-3 py-2.5 align-middle',
                        ALIGN[column.align ?? 'left'],
                        column.hideBelow && HIDE_BELOW[column.hideBelow],
                      )}
                    >
                      {column.cell(row)}
                    </td>
                  ))}
                </tr>
              ))}
        </tbody>
      </table>
    </div>
  );
}
