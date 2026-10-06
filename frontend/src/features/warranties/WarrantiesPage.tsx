import { ShieldCheck } from 'lucide-react';
import { useMemo } from 'react';
import { Link } from 'react-router';

import { useExpiringWarranties } from '@/api/hooks';
import type { Warranty } from '@/api/types';
import { formatDate, formatDaysRemaining } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { NativeSelect } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { WarrantyStatePill } from '@/ui/StatusPill';

const DEFAULT_PAGE_SIZE = 50;

/**
 * Windows offered for `GET /warranties/expiring?withinDays=`. The API defaults to
 * the server's configured warning threshold when the parameter is omitted.
 */
const WINDOWS = [
  { value: '', label: 'Default warning window' },
  { value: '30', label: 'Next 30 days' },
  { value: '60', label: 'Next 60 days' },
  { value: '90', label: 'Next 90 days' },
  { value: '180', label: 'Next 6 months' },
  { value: '365', label: 'Next 12 months' },
];

export default function WarrantiesPage() {
  const url = useUrlState();

  const withinDaysRaw = url.get('withinDays');
  const withinDays = withinDaysRaw ? Number(withinDaysRaw) : undefined;
  const page = url.getNumber('page', 1);
  const pageSize = url.getNumber('pageSize', DEFAULT_PAGE_SIZE);

  const warranties = useExpiringWarranties(withinDays, page, pageSize);

  const columns = useMemo<Column<Warranty>[]>(
    () => [
      {
        id: 'provider',
        header: 'Provider',
        cell: (warranty) => (
          <div className="flex min-w-0 items-center gap-2">
            <div className="min-w-0">
              <Link
                to={`/assets/${warranty.assetId}?tab=warranties`}
                className="truncate rounded-xs font-medium text-accent hover:underline"
              >
                {warranty.provider}
              </Link>
              <p className="truncate text-xs text-text-subtle">{warranty.coverageType}</p>
            </div>
            {warranty.isPrimary && <Badge tone="accent">Primary</Badge>}
          </div>
        ),
      },
      {
        id: 'state',
        header: 'State',
        width: 'w-36',
        cell: (warranty) => <WarrantyStatePill state={warranty.state} />,
      },
      {
        id: 'ends',
        header: 'Ends',
        width: 'w-32',
        cell: (warranty) => (
          <span className="whitespace-nowrap text-text">{formatDate(warranty.endDate)}</span>
        ),
      },
      {
        id: 'remaining',
        header: 'Remaining',
        width: 'w-44',
        cell: (warranty) => (
          <span className="text-text-muted">
            {formatDaysRemaining(warranty.daysRemaining) ?? '—'}
          </span>
        ),
      },
      {
        id: 'reference',
        header: 'Reference',
        hideBelow: 'lg',
        cell: (warranty) =>
          warranty.reference ? (
            <span className="font-mono text-xs text-text-muted">{warranty.reference}</span>
          ) : (
            <span className="text-text-subtle">—</span>
          ),
      },
      {
        id: 'asset',
        header: <span className="sr-only">Asset</span>,
        align: 'right',
        width: 'w-28',
        cell: (warranty) => (
          <Link
            to={`/assets/${warranty.assetId}`}
            className="rounded-xs text-sm font-medium text-text-muted hover:text-accent hover:underline"
          >
            View asset
          </Link>
        ),
      },
    ],
    [],
  );

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Warranty coverage"
        description="Coverage that has expired or is due to expire, so replacements and renewals can be planned."
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <NativeSelect
            value={withinDaysRaw ?? ''}
            onChange={(event) => url.set({ withinDays: event.target.value })}
            aria-label="Expiry window"
            className="w-auto min-w-52"
          >
            {WINDOWS.map((window) => (
              <option key={window.value} value={window.value}>
                {window.label}
              </option>
            ))}
          </NativeSelect>

          <p className="text-sm text-text-muted">
            Includes coverage that has already expired.
          </p>
        </div>

        {warranties.isError ? (
          <ErrorState error={warranties.error} onRetry={() => void warranties.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={warranties.data?.items ?? []}
              rowKey={(warranty) => warranty.id}
              loading={warranties.isLoading}
              empty={
                <EmptyState
                  icon={<ShieldCheck />}
                  title="Nothing expiring in this window"
                  description={
                    withinDays
                      ? `No recorded coverage ends within ${withinDays} days. Try a longer window.`
                      : 'No recorded coverage falls inside the warning window.'
                  }
                />
              }
            />

            {(warranties.data?.totalCount ?? 0) > 0 && (
              <Pagination
                page={warranties.data?.page ?? 1}
                pageSize={warranties.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={warranties.data?.totalCount ?? 0}
                onPageChange={(next) => url.set({ page: next })}
                onPageSizeChange={(next) => url.set({ pageSize: next, page: 1 })}
                itemLabel="warranties"
              />
            )}
          </>
        )}
      </Card>

      <p className="text-sm text-text-subtle">
        Looking for assets rather than policies? Filter the register by warranty state on
        the{' '}
        <Link
          to={`/assets?warrantyState=expiringSoon`}
          className="rounded-xs text-accent hover:underline"
        >
          assets page
        </Link>
        .
      </p>
    </div>
  );
}
