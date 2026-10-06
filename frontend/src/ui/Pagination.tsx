import { ChevronLeft, ChevronRight } from 'lucide-react';

import { formatNumber } from '@/lib/format';
import { Button } from './Button';
import { NativeSelect } from './Field';

const PAGE_SIZES = [25, 50, 100, 200] as const;

export interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
  /** Plural noun for the summary line, e.g. `assets`. */
  itemLabel?: string;
}

export function Pagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
  onPageSizeChange,
  itemLabel = 'records',
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-3 py-2.5">
      <p className="text-sm text-text-muted" aria-live="polite">
        {totalCount === 0 ? (
          `No ${itemLabel}`
        ) : (
          <>
            <span className="font-medium text-text">
              {formatNumber(first)}–{formatNumber(last)}
            </span>{' '}
            of {formatNumber(totalCount)} {itemLabel}
          </>
        )}
      </p>

      <div className="flex items-center gap-3">
        {onPageSizeChange && (
          <label className="flex items-center gap-2 text-sm text-text-muted">
            <span className="hidden sm:inline">Per page</span>
            <NativeSelect
              value={pageSize}
              onChange={(event) => onPageSizeChange(Number(event.target.value))}
              className="h-8 w-20 text-sm"
              aria-label="Records per page"
            >
              {PAGE_SIZES.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </NativeSelect>
          </label>
        )}

        <div className="flex items-center gap-1">
          <Button
            size="sm"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1}
            iconLeft={<ChevronLeft />}
          >
            <span className="hidden sm:inline">Previous</span>
          </Button>
          <span className="px-2 text-sm tabular-nums text-text-muted">
            {formatNumber(page)} / {formatNumber(totalPages)}
          </span>
          <Button
            size="sm"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages}
            iconRight={<ChevronRight />}
          >
            <span className="hidden sm:inline">Next</span>
          </Button>
        </div>
      </div>
    </div>
  );
}
