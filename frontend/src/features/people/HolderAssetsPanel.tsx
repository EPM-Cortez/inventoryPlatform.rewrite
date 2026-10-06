import { PackageOpen } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import type { PagedResponse, HolderAsset } from '@/api/types';
import { formatDate, formatDateTime, formatNumber } from '@/lib/format';
import { Card, CardHeader } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Pagination } from '@/ui/Pagination';
import { SegmentedControl } from '@/ui/SegmentedControl';
import { Skeleton } from '@/ui/Skeleton';
import { StatusDot } from '@/ui/StatusDot';
import { AssetConditionDot, AssetStatusDot } from '@/ui/StatusPill';
import { SummaryFact } from '@/ui/SummaryPanel';

export const HOLDER_ASSETS_PAGE_SIZE = 25;

const SCOPE_OPTIONS = [
  { value: 'current', label: 'Holding now' },
  { value: 'history', label: 'All history' },
] as const;

export interface HolderAssetsPanelProps {
  /** Query result from `usePersonAssets` or `useDepartmentAssets`. */
  query: {
    data: PagedResponse<HolderAsset> | undefined;
    isLoading: boolean;
    isError: boolean;
    error: unknown;
    refetch: () => unknown;
  };
  includeHistory: boolean;
  onIncludeHistoryChange: (value: boolean) => void;
  page: number;
  onPageChange: (page: number) => void;
  holderLabel: string;
}

/**
 * The held-assets table, shared by people and departments — the API returns the
 * same `HolderAsset` shape for both.
 */
export function HolderAssetsPanel({
  query,
  includeHistory,
  onIncludeHistoryChange,
  page,
  onPageChange,
  holderLabel,
}: HolderAssetsPanelProps) {
  const columns: Column<HolderAsset>[] = [
    {
      id: 'assetTag',
      header: 'Asset',
      cell: (row) => (
        <div className="min-w-0">
          <Link
            to={`/assets/${row.assetId}`}
            className="block truncate rounded-xs font-medium tabular-nums text-text hover:text-accent hover:underline"
          >
            {row.assetTag}
          </Link>
          <p className="truncate text-xs text-text-muted">
            {row.location?.name ?? <span className="text-text-subtle">No location</span>}
          </p>
        </div>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      width: 'w-32',
      cell: (row) => <AssetStatusDot status={row.status} />,
    },
    {
      id: 'condition',
      header: 'Condition',
      hideBelow: 'md',
      width: 'w-32',
      cell: (row) => <AssetConditionDot condition={row.condition} />,
    },
    {
      id: 'since',
      header: 'Issued',
      hideBelow: 'sm',
      width: 'w-44',
      cell: (row) => (
        <span className="text-text-muted">{formatDateTime(row.assignment.assignedAtUtc)}</span>
      ),
    },
    includeHistory
      ? {
          id: 'returned',
          header: 'Returned',
          width: 'w-44',
          cell: (row) =>
            row.assignment.returnedAtUtc ? (
              <span className="text-text-muted">
                {formatDateTime(row.assignment.returnedAtUtc)}
              </span>
            ) : (
              <StatusDot tone="accent">Still held</StatusDot>
            ),
        }
      : {
          id: 'due',
          header: 'Due back',
          width: 'w-48',
          cell: (row) =>
            row.assignment.expectedReturnOn ? (
              row.assignment.isOverdue ? (
                <StatusDot tone="danger">
                  Overdue · {formatDate(row.assignment.expectedReturnOn)}
                </StatusDot>
              ) : (
                <span className="text-text-muted">
                  {formatDate(row.assignment.expectedReturnOn)}
                </span>
              )
            ) : (
              <span className="text-text-subtle">Open-ended</span>
            ),
        },
  ];

  const total = query.data?.totalCount;

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="Equipment"
        description={
          total === undefined
            ? undefined
            : includeHistory
              ? `${total} ${total === 1 ? 'item' : 'items'} ever issued`
              : `${total} ${total === 1 ? 'item' : 'items'} held right now`
        }
        actions={
          <SegmentedControl
            label="Which equipment to show"
            hideLabel
            size="sm"
            value={includeHistory ? 'history' : 'current'}
            onChange={(scope) => onIncludeHistoryChange(scope === 'history')}
            options={SCOPE_OPTIONS}
          />
        }
      />

      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => query.refetch()} />
      ) : (
        <>
          <DataTable
            columns={columns}
            rows={query.data?.items ?? []}
            rowKey={(row) => row.assignment.id}
            loading={query.isLoading}
            skeletonRows={4}
            empty={
              <EmptyState
                icon={<PackageOpen />}
                title={includeHistory ? 'Nothing ever held' : 'Nothing held right now'}
                description={
                  includeHistory
                    ? `${holderLabel} has never been issued an asset.`
                    : `${holderLabel} does not currently hold any assets. Switch to “All history” to see past custody.`
                }
              />
            }
          />

          {(total ?? 0) > HOLDER_ASSETS_PAGE_SIZE && (
            <Pagination
              page={page}
              pageSize={query.data?.pageSize ?? HOLDER_ASSETS_PAGE_SIZE}
              totalCount={total ?? 0}
              onPageChange={onPageChange}
              itemLabel="assets"
            />
          )}
        </>
      )}
    </Card>
  );
}

/**
 * "Holding now" in a person's or department's summary row, from
 * `usePersonHeldCount` or `useDepartmentHeldCount`.
 */
export function HeldNowFact({
  query,
}: {
  query: { data: number | undefined; isLoading: boolean; isError: boolean };
}) {
  return (
    <SummaryFact label="Holding now" empty={query.isError ? 'Unavailable' : 'Nothing'}>
      {query.isLoading ? (
        <Skeleton className="mt-0.5 h-5 w-20" />
      ) : query.data ? (
        <>
          <span className="font-medium tabular-nums">{formatNumber(query.data)}</span>{' '}
          <span className="text-text-muted">{query.data === 1 ? 'item' : 'items'}</span>
        </>
      ) : null}
    </SummaryFact>
  );
}

/** Small hook so both detail pages share the same paging + toggle behaviour. */
export function useHolderAssetsState() {
  const [includeHistory, setIncludeHistory] = useState(false);
  const [page, setPage] = useState(1);

  return {
    includeHistory,
    page,
    setPage,
    setIncludeHistory: (value: boolean) => {
      setIncludeHistory(value);
      // The result set changes size, so any page beyond the first is meaningless.
      setPage(1);
    },
  };
}
