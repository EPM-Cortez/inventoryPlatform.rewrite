import { ArrowRight, CircleCheck, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import type {
  DashboardExceptionAsset,
  DashboardExceptionCode,
  DashboardExceptionCount,
} from '@/api/dashboardTypes';
import { useDashboardException } from '@/api/hooks';
import { formatNumber, formatRelative } from '@/lib/format';
import { cn } from '@/lib/cn';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { Drawer } from '@/ui/Drawer';
import { ErrorState } from '@/ui/EmptyState';
import { Pagination } from '@/ui/Pagination';
import { AssetConditionPill, AssetStatusPill } from '@/ui/StatusPill';

const PAGE_SIZE = 25;

/**
 * Things on the register that look wrong.
 *
 * Counts come from the server across the whole estate, so they do not change
 * with whatever filter the assets page happens to have. Each one opens the
 * assets behind it — a count nobody can act on is just an accusation.
 */
export function ExceptionsCard({
  exceptions,
  loading,
}: {
  exceptions: DashboardExceptionCount[];
  loading: boolean;
}) {
  const [open, setOpen] = useState<DashboardExceptionCount | null>(null);

  const withIssues = exceptions.filter((exception) => exception.count > 0);
  const clear = exceptions.filter((exception) => exception.count === 0);

  return (
    <>
      <Card>
        <CardHeader
          title="Needs attention"
          description="Counted across every asset, not just the ones on screen."
          actions={
            !loading &&
            withIssues.length === 0 && (
              <span className="flex items-center gap-1.5 text-sm text-success">
                <CircleCheck className="size-4" aria-hidden />
                All clear
              </span>
            )
          }
        />
        <CardBody className="p-0">
          {loading ? (
            <ul className="divide-y divide-border">
              {Array.from({ length: 4 }, (_, index) => (
                <li key={index} className="h-12 animate-pulse bg-surface-sunken/40" />
              ))}
            </ul>
          ) : (
            <ul className="divide-y divide-border">
              {withIssues.map((exception) => (
                <li key={exception.code}>
                  <button
                    type="button"
                    onClick={() => setOpen(exception)}
                    className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left transition-colors hover:bg-surface-hover"
                  >
                    <span className="flex min-w-0 items-center gap-2.5">
                      <TriangleAlert
                        className="size-4 shrink-0 text-warning"
                        aria-hidden
                      />
                      <span className="truncate text-base text-text">
                        {exception.label}
                      </span>
                    </span>
                    <span className="flex shrink-0 items-center gap-2">
                      <span className="text-lg font-semibold tabular-nums text-text">
                        {formatNumber(exception.count)}
                      </span>
                      <ArrowRight className="size-3.5 text-text-subtle" aria-hidden />
                    </span>
                  </button>
                </li>
              ))}

              {/* Zeroes are worth showing: "no lost assets" is information, and
                  hiding it makes the list look like it forgot to check. */}
              {clear.length > 0 && (
                <li className="flex flex-wrap gap-x-4 gap-y-1 px-4 py-3">
                  {clear.map((exception) => (
                    <span
                      key={exception.code}
                      className="flex items-center gap-1.5 text-xs text-text-subtle"
                    >
                      <CircleCheck className="size-3 text-success" aria-hidden />
                      {exception.label}
                    </span>
                  ))}
                </li>
              )}
            </ul>
          )}
        </CardBody>
      </Card>

      {open && (
        <ExceptionDrawer exception={open} onClose={() => setOpen(null)} />
      )}
    </>
  );
}

function ExceptionDrawer({
  exception,
  onClose,
}: {
  exception: DashboardExceptionCount;
  onClose: () => void;
}) {
  const [page, setPage] = useState(1);
  const assets = useDashboardException(
    exception.code as DashboardExceptionCode,
    page,
    PAGE_SIZE,
  );

  const rows = assets.data?.items ?? [];
  const total = assets.data?.totalCount ?? 0;

  const columns: Column<DashboardExceptionAsset>[] = [
    {
      id: 'asset',
      header: 'Asset',
      cell: (asset) => (
        <div className="min-w-0">
          <Link
            to={`/assets/${asset.assetId}`}
            className="truncate rounded-xs font-mono text-sm font-medium text-accent hover:underline"
          >
            {asset.assetTag}
          </Link>
          <p className="truncate text-2xs text-text-subtle">
            {asset.deviceName ?? asset.category.name}
          </p>
        </div>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      width: 'w-32',
      cell: (asset) => <AssetStatusPill status={asset.status} />,
    },
    {
      id: 'condition',
      header: 'Condition',
      width: 'w-32',
      hideBelow: 'md',
      cell: (asset) => <AssetConditionPill condition={asset.condition} />,
    },
    {
      id: 'location',
      header: 'Location',
      hideBelow: 'md',
      cell: (asset) =>
        asset.location ? (
          <span className="truncate text-text-muted">{asset.location.name}</span>
        ) : (
          <span className="text-text-subtle">Not recorded</span>
        ),
    },
    {
      id: 'updated',
      header: 'Updated',
      align: 'right',
      width: 'w-32',
      hideBelow: 'lg',
      cell: (asset) => (
        <span className="text-xs text-text-subtle">
          {formatRelative(asset.updatedAtUtc)}
        </span>
      ),
    },
  ];

  return (
    <Drawer
      open
      onOpenChange={(next) => !next && onClose()}
      title={exception.label}
      description={`${formatNumber(exception.count)} ${
        exception.count === 1 ? 'asset' : 'assets'
      } on the register`}
      width="lg"
      footer={
        <Button variant="primary" onClick={onClose}>
          Close
        </Button>
      }
    >
      {assets.isError ? (
        <ErrorState error={assets.error} onRetry={() => void assets.refetch()} />
      ) : (
        <div className={cn('-mx-5 -mb-4')}>
          <DataTable
            columns={columns}
            rows={rows}
            rowKey={(asset) => asset.assetId}
            loading={assets.isLoading}
            skeletonRows={6}
          />
          {total > PAGE_SIZE && (
            <Pagination
              page={assets.data?.page ?? 1}
              pageSize={PAGE_SIZE}
              totalCount={total}
              onPageChange={setPage}
              itemLabel="assets"
            />
          )}
        </div>
      )}
    </Drawer>
  );
}
