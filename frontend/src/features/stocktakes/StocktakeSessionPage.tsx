import { CheckCircle2, XCircle } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';

import {
  useCompleteStocktake,
  useLocations,
  useStocktake,
  useStocktakeItems,
} from '@/api/hooks';
import type { StocktakeItem, StocktakeItemFilter } from '@/api/stocktakeTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { ASSET_CONDITION_LABELS, formatDateTime, formatNumber, formatRelative } from '@/lib/format';
import { asEnum, useUrlState } from '@/lib/useUrlState';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { LoadingBlock } from '@/ui/Spinner';
import { useToast } from '@/ui/Toast';

import { CancelStocktakeDialog } from './CancelStocktakeDialog';
import { ScanPanel } from './ScanPanel';
import {
  STOCKTAKE_FILTER_TABS,
  STOCKTAKE_STATUS_LABELS,
  itemDiscrepancies,
} from './stocktakeLabels';

const PAGE_SIZE = 50;
const FILTER_VALUES = STOCKTAKE_FILTER_TABS.map((tab) => tab.value);

export default function StocktakeSessionPage() {
  const { sessionId } = useParams<{ sessionId: string }>();
  const url = useUrlState();
  const { can } = useSession();
  const toast = useToast();

  const [completing, setCompleting] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  const session = useStocktake(sessionId);
  const locations = useLocations();
  const complete = useCompleteStocktake(sessionId ?? '');

  const filter = (asEnum(url.get('filter'), FILTER_VALUES) ??
    'missing') as StocktakeItemFilter;
  const page = url.getNumber('page', 1);
  const items = useStocktakeItems(sessionId, filter, page, PAGE_SIZE);

  if (session.isLoading) return <LoadingBlock label="Loading stocktake…" />;
  if (session.isError) {
    return <ErrorState error={session.error} onRetry={() => void session.refetch()} />;
  }
  if (!session.data) return null;

  const data = session.data;
  const open = data.status === 'inProgress';
  const canManage = can(Permission.StocktakesManage);
  const { progress } = data;

  const columns: Column<StocktakeItem>[] = [
    {
      id: 'asset',
      header: 'Asset',
      cell: (item) => (
        <div className="min-w-0">
          <Link
            to={`/assets/${item.assetId}`}
            onClick={(event) => event.stopPropagation()}
            className="truncate rounded-xs font-mono text-sm font-medium text-accent hover:underline"
          >
            {item.assetTag}
          </Link>
          <p className="truncate text-2xs text-text-subtle">
            {item.deviceName ?? item.category.name}
          </p>
        </div>
      ),
    },
    {
      id: 'expected',
      header: 'On the register',
      cell: (item) => (
        <div className="min-w-0 text-sm">
          <p className="truncate text-text-muted">
            {item.expectedLocation?.name ?? 'No location'}
          </p>
          <p className="truncate text-2xs text-text-subtle">
            {ASSET_CONDITION_LABELS[item.expectedCondition]}
          </p>
        </div>
      ),
    },
    {
      id: 'observed',
      header: 'Found',
      cell: (item) =>
        item.isSeen ? (
          <div className="min-w-0 text-sm">
            <p
              className={cn(
                'truncate',
                item.isWrongLocation ? 'font-medium text-warning' : 'text-text-muted',
              )}
            >
              {item.observedLocation?.name ?? '—'}
            </p>
            <p
              className={cn(
                'truncate text-2xs',
                item.isConditionChanged ? 'font-medium text-warning' : 'text-text-subtle',
              )}
            >
              {item.observedCondition
                ? ASSET_CONDITION_LABELS[item.observedCondition]
                : 'Condition not recorded'}
            </p>
          </div>
        ) : (
          <span className="text-sm text-text-subtle">Not seen</span>
        ),
    },
    {
      id: 'flags',
      header: 'Difference',
      width: 'w-52',
      cell: (item) => {
        const flags = itemDiscrepancies(item);
        return flags.length === 0 ? (
          <Badge tone="success" dot>
            Matches
          </Badge>
        ) : (
          <div className="flex flex-wrap gap-1">
            {flags.map((flag) => (
              <Badge key={flag} tone="warning">
                {flag}
              </Badge>
            ))}
          </div>
        );
      },
    },
    {
      id: 'when',
      header: 'Recorded',
      align: 'right',
      width: 'w-40',
      hideBelow: 'lg',
      cell: (item) =>
        item.observedAtUtc ? (
          <div className="min-w-0">
            <p className="truncate text-xs text-text-muted">
              {formatRelative(item.observedAtUtc)}
            </p>
            <p className="truncate text-2xs text-text-subtle">
              {item.observedByDisplayName}
            </p>
          </div>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        breadcrumbs={[{ label: 'Stocktakes', to: '/stocktakes' }, { label: data.name }]}
        title={data.name}
        description={
          data.scope === 'all'
            ? 'Everything on the register'
            : `${data.scope === 'location' ? 'Location' : 'Category'} · ${
                data.scopeLocation?.name ?? data.scopeCategory?.name ?? '—'
              }`
        }
        meta={
          <>
            <Badge
              tone={open ? 'accent' : data.status === 'completed' ? 'success' : 'neutral'}
              dot
            >
              {STOCKTAKE_STATUS_LABELS[data.status]}
            </Badge>
            <span className="text-xs text-text-subtle">
              Started {formatRelative(data.startedAtUtc)} by {data.startedByDisplayName}
            </span>
          </>
        }
        actions={
          open && canManage ? (
            <>
              <Button
                variant="danger-subtle"
                iconLeft={<XCircle />}
                onClick={() => setCancelling(true)}
              >
                Cancel
              </Button>
              <Button
                variant="primary"
                iconLeft={<CheckCircle2 />}
                onClick={() => setCompleting(true)}
              >
                Finish
              </Button>
            </>
          ) : undefined
        }
      />

      {data.status === 'cancelled' && (
        <Alert tone="info" title="This stocktake was cancelled">
          {data.cancellationReason} — {data.cancelledByDisplayName},{' '}
          {formatDateTime(data.cancelledAtUtc)}. Everything recorded before it was
          cancelled is kept.
        </Alert>
      )}

      {data.status === 'completed' && (
        <Alert tone="success" title="Finished">
          Completed by {data.completedByDisplayName},{' '}
          {formatDateTime(data.completedAtUtc)}. The differences below are a record of
          what was found — the register was not changed by them.
        </Alert>
      )}

      <section
        aria-label="Progress"
        className="grid gap-3 sm:grid-cols-3 lg:grid-cols-6"
      >
        <ProgressTile label="Expected" value={progress.expected} />
        <ProgressTile label="Seen" value={progress.seen} tone="success" />
        <ProgressTile label="Not found" value={progress.missing} tone="danger" />
        <ProgressTile label="Unexpected" value={progress.unexpected} tone="warning" />
        <ProgressTile label="Wrong place" value={progress.wrongLocation} tone="warning" />
        <ProgressTile
          label="Condition differs"
          value={progress.conditionChanged}
          tone="warning"
        />
      </section>

      {open && canManage && (
        <ScanPanel
          session={data}
          locations={locations.data?.items ?? []}
          locationsLoading={locations.isLoading}
        />
      )}

      <Card className="overflow-hidden">
        <div
          role="tablist"
          aria-label="Item filter"
          className="flex flex-wrap gap-1 border-b border-border p-2"
        >
          {STOCKTAKE_FILTER_TABS.map((tab) => {
            const count = tab.count ? progress[tab.count] : null;
            const active = filter === tab.value;
            return (
              <button
                key={tab.value}
                role="tab"
                aria-selected={active}
                title={tab.hint}
                onClick={() => url.set({ filter: tab.value, page: undefined })}
                className={cn(
                  'flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium',
                  'transition-colors duration-(--duration-instant)',
                  active
                    ? 'bg-accent-soft text-accent-soft-fg'
                    : 'text-text-muted hover:bg-surface-hover hover:text-text',
                )}
              >
                {tab.label}
                {count !== null && (
                  <span
                    className={cn(
                      'rounded-full px-1.5 text-2xs tabular-nums',
                      active ? 'bg-accent/20' : 'bg-surface-sunken text-text-subtle',
                    )}
                  >
                    {formatNumber(count)}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {items.isError ? (
          <ErrorState error={items.error} onRetry={() => void items.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={items.data?.items ?? []}
              rowKey={(item) => item.assetId}
              loading={items.isLoading}
              skeletonRows={8}
              empty={
                <EmptyState
                  icon={<CheckCircle2 />}
                  title={emptyTitle(filter)}
                  description="Nothing in this view."
                />
              }
            />
            {(items.data?.totalCount ?? 0) > PAGE_SIZE && (
              <Pagination
                page={items.data?.page ?? 1}
                pageSize={PAGE_SIZE}
                totalCount={items.data?.totalCount ?? 0}
                onPageChange={(next) => url.set({ page: next })}
                itemLabel="assets"
              />
            )}
          </>
        )}
      </Card>

      {completing && (
        <ConfirmDialog
          open
          onOpenChange={(next) => !next && setCompleting(false)}
          title="Finish this stocktake?"
          description={
            <>
              {progress.missing > 0 ? (
                <>
                  <strong>{formatNumber(progress.missing)}</strong>{' '}
                  {progress.missing === 1 ? 'asset was' : 'assets were'} never found.
                  Finishing records that as the outcome — it does not mark them lost, and
                  it does not change any asset.
                </>
              ) : (
                'Everything expected was found. Finishing closes the session; no asset is changed by it.'
              )}
              <p className="mt-2">No more scans can be recorded afterwards.</p>
            </>
          }
          confirmLabel="Finish stocktake"
          loading={complete.isPending}
          onConfirm={() =>
            complete.mutate(
              { version: data.version },
              {
                onSuccess: () => {
                  toast.success('Stocktake finished', data.name);
                  setCompleting(false);
                },
                onError: (error) => {
                  toast.fromError(error, 'Could not finish it');
                  setCompleting(false);
                },
              },
            )
          }
        />
      )}

      {cancelling && (
        <CancelStocktakeDialog session={data} onClose={() => setCancelling(false)} />
      )}
    </div>
  );
}

function ProgressTile({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone?: 'success' | 'warning' | 'danger';
}) {
  // The numeral stays in the normal text colour; only the label carries tone, and
  // only when there is something in that state worth looking at.
  const toned = value > 0 && tone;
  return (
    <div className="rounded-md border border-border bg-surface p-3">
      <p
        className={cn(
          'text-xs font-medium',
          toned === 'success'
            ? 'text-success'
            : toned === 'warning'
              ? 'text-warning'
              : toned === 'danger'
                ? 'text-danger'
                : 'text-text-muted',
        )}
      >
        {label}
      </p>
      <p className="mt-0.5 text-2xl font-semibold tabular-nums text-text">
        {formatNumber(value)}
      </p>
    </div>
  );
}

function emptyTitle(filter: StocktakeItemFilter): string {
  switch (filter) {
    case 'missing':
      return 'Everything expected has been found';
    case 'unexpected':
      return 'Nothing unexpected turned up';
    case 'wrongLocation':
      return 'Everything is where the register says';
    case 'conditionChanged':
      return 'No condition differences';
    case 'seen':
      return 'Nothing scanned yet';
    default:
      return 'No assets in scope';
  }
}
