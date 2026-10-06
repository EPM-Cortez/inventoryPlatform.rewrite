import { ClipboardList, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';

import { useStocktakes } from '@/api/hooks';
import {
  STOCKTAKE_STATUSES,
  type StocktakeSessionListItem,
} from '@/api/stocktakeTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatNumber, formatRelative } from '@/lib/format';
import { asEnum, useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { Field, NativeSelect } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { Tooltip } from '@/ui/Tooltip';

import { StartStocktakeDialog } from './StartStocktakeDialog';
import { STOCKTAKE_STATUS_LABELS, seenFraction } from './stocktakeLabels';

const DEFAULT_PAGE_SIZE = 25;

export default function StocktakesPage() {
  const url = useUrlState();
  const navigate = useNavigate();
  const { can } = useSession();
  const [starting, setStarting] = useState(false);

  const params = useMemo(
    () => ({
      status: asEnum(url.get('status'), STOCKTAKE_STATUSES),
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const sessions = useStocktakes(params);
  const canManage = can(Permission.StocktakesManage);
  const rows = sessions.data?.items ?? [];

  const columns: Column<StocktakeSessionListItem>[] = [
    {
      id: 'name',
      header: 'Stocktake',
      cell: (session) => (
        <div className="min-w-0">
          <p className="truncate font-medium text-text">{session.name}</p>
          <p className="truncate text-2xs text-text-subtle">
            {session.scope === 'all'
              ? 'Everything'
              : `${session.scope === 'location' ? 'Location' : 'Category'} · ${session.scopeDisplayName ?? '—'}`}
          </p>
        </div>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      width: 'w-32',
      cell: (session) => (
        <Badge
          tone={
            session.status === 'inProgress'
              ? 'accent'
              : session.status === 'completed'
                ? 'success'
                : 'neutral'
          }
          dot
        >
          {STOCKTAKE_STATUS_LABELS[session.status]}
        </Badge>
      ),
    },
    {
      id: 'progress',
      header: 'Found',
      width: 'w-56',
      cell: (session) => <ProgressBar session={session} />,
    },
    {
      id: 'issues',
      header: 'Discrepancies',
      hideBelow: 'lg',
      width: 'w-44',
      cell: (session) => {
        const { missing, unexpected, wrongLocation, conditionChanged } =
          session.progress;
        // Deliberately not summed: one asset can be in several of these at once,
        // so a total would overstate how much is actually wrong.
        const parts = [
          missing > 0 && `${formatNumber(missing)} not found`,
          unexpected > 0 && `${formatNumber(unexpected)} unexpected`,
          wrongLocation > 0 && `${formatNumber(wrongLocation)} misplaced`,
          conditionChanged > 0 && `${formatNumber(conditionChanged)} condition`,
        ].filter(Boolean) as string[];

        return parts.length === 0 ? (
          <span className="text-sm text-text-subtle">None</span>
        ) : (
          <span className="text-xs text-text-muted">{parts.join(' · ')}</span>
        );
      },
    },
    {
      id: 'started',
      header: 'Started',
      align: 'right',
      width: 'w-40',
      hideBelow: 'md',
      cell: (session) => (
        <div className="min-w-0">
          <p className="truncate text-sm text-text-muted">
            {formatRelative(session.startedAtUtc)}
          </p>
          <p className="truncate text-2xs text-text-subtle">
            {session.startedByDisplayName}
          </p>
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Stocktakes"
        description="Walk a location or category, record what is actually there, and see how it differs from the register."
        actions={
          canManage && (
            <Button variant="primary" iconLeft={<Plus />} onClick={() => setStarting(true)}>
              Start a stocktake
            </Button>
          )
        }
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <Field label="Status" hideLabel className="w-48">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={url.get('status') ?? ''}
                onChange={(event) => url.set({ status: event.target.value || undefined })}
              >
                <option value="">Any status</option>
                {STOCKTAKE_STATUSES.map((status) => (
                  <option key={status} value={status}>
                    {STOCKTAKE_STATUS_LABELS[status]}
                  </option>
                ))}
              </NativeSelect>
            )}
          </Field>
        </div>

        {sessions.isError ? (
          <ErrorState error={sessions.error} onRetry={() => void sessions.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(session) => session.id}
              loading={sessions.isLoading}
              onRowClick={(session) => void navigate(`/stocktakes/${session.id}`)}
              rowClassName={(session) =>
                session.status === 'cancelled' ? 'opacity-60' : undefined
              }
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<ClipboardList />}
                    title="No stocktakes yet"
                    description="Start one to check what is actually in a room or a category against what the register says."
                    action={
                      canManage && (
                        <Button
                          variant="primary"
                          iconLeft={<Plus />}
                          onClick={() => setStarting(true)}
                        >
                          Start a stocktake
                        </Button>
                      )
                    }
                  />
                )
              }
            />

            {(sessions.data?.totalCount ?? 0) > 0 && (
              <Pagination
                page={sessions.data?.page ?? 1}
                pageSize={sessions.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={sessions.data?.totalCount ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="stocktakes"
              />
            )}
          </>
        )}
      </Card>

      {starting && <StartStocktakeDialog onClose={() => setStarting(false)} />}
    </div>
  );
}

function ProgressBar({ session }: { session: StocktakeSessionListItem }) {
  const { expected, seen } = session.progress;
  const fraction = seenFraction(session.progress);
  const complete = expected > 0 && seen >= expected;

  return (
    <Tooltip
      content={`${formatNumber(seen)} of ${formatNumber(expected)} expected assets seen. Unexpected finds are counted separately.`}
    >
      <div className="flex min-w-0 items-center gap-2">
        <div className="h-1.5 min-w-16 flex-1 overflow-hidden rounded-full bg-surface-sunken">
          <div
            className={`h-full rounded-full transition-[width] duration-(--duration-slow) ${
              complete ? 'bg-success' : 'bg-accent'
            }`}
            style={{ width: `${Math.round(fraction * 100)}%` }}
          />
        </div>
        <span className="shrink-0 text-xs tabular-nums text-text-muted">
          {formatNumber(seen)}/{formatNumber(expected)}
        </span>
      </div>
    </Tooltip>
  );
}
