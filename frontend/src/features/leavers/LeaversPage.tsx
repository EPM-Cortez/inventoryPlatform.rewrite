import { UserMinus, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { useLeaverCases } from '@/api/hooks';
import { LEAVER_STATUSES, type LeaverCaseSummary } from '@/api/operationsTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { describeDaysFromToday, formatDate, formatNumber } from '@/lib/format';
import { asEnum, useUrlState } from '@/lib/useUrlState';
import { Avatar } from '@/ui/Avatar';
import { Button } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { FilterButton } from '@/ui/FilterButton';
import { ListToolbar } from '@/ui/ListToolbar';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { SearchInput } from '@/ui/SearchInput';
import { StatusDot } from '@/ui/StatusDot';

import { CreateLeaverCaseDialog } from './CreateLeaverCaseDialog';
import { CustodyExceptionsCard } from './CustodyExceptionsCard';
import { LEAVER_STATUS_LABELS, LEAVER_STATUS_TONES } from './leaverLabels';

const DEFAULT_PAGE_SIZE = 25;

const STATUS_OPTIONS = LEAVER_STATUSES.map((status) => ({
  value: status,
  label: LEAVER_STATUS_LABELS[status],
}));

/**
 * People who are leaving, and what they still hold.
 *
 * Opening a case does not return anything — it records the leaving date and
 * gathers their custody into one place so it can be worked through.
 */
export default function LeaversPage() {
  const url = useUrlState();
  const navigate = useNavigate();
  const { can } = useSession();
  const [creating, setCreating] = useState(false);
  // Captured once, so every row judges "passed" against the same moment.
  const [today] = useState(() => new Date());

  const params = useMemo(
    () => ({
      q: url.get('q')?.trim() || undefined,
      status: asEnum(url.get('status'), LEAVER_STATUSES),
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const cases = useLeaverCases(params);
  const canManage = can(Permission.LeaversManage);
  const rows = cases.data?.items ?? [];
  const total = cases.data?.totalCount;

  const columns: Column<LeaverCaseSummary>[] = [
    {
      id: 'person',
      header: 'Person',
      cell: (leaver) => (
        <div className="flex min-w-0 items-center gap-3">
          <Avatar name={leaver.personDisplayName} />
          <div className="min-w-0">
            <Link
              to={`/leavers/${leaver.id}`}
              onClick={(event) => event.stopPropagation()}
              className="block truncate rounded-xs font-medium text-text hover:text-accent hover:underline"
            >
              {leaver.personDisplayName}
            </Link>
            <p className="truncate text-xs text-text-muted">
              {leaver.personExternalId ? (
                <span className="font-mono">{leaver.personExternalId}</span>
              ) : (
                <span className="text-text-subtle">No staff number</span>
              )}
              {!leaver.personIsActive && (
                <span className="text-text-subtle"> · record archived</span>
              )}
            </p>
          </div>
        </div>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      width: 'w-32',
      cell: (leaver) => (
        <StatusDot tone={LEAVER_STATUS_TONES[leaver.status]}>
          {LEAVER_STATUS_LABELS[leaver.status]}
        </StatusDot>
      ),
    },
    {
      id: 'leaving',
      header: 'Last day',
      width: 'w-40',
      cell: (leaver) => {
        const distance = describeDaysFromToday(leaver.leavingOn, today);
        // Only an open case with a date behind it needs chasing.
        const overdue = leaver.status === 'open' && (distance?.days ?? 0) < 0;
        return (
          <div className="min-w-0">
            <p className="text-text">{formatDate(leaver.leavingOn)}</p>
            {distance && (
              <p
                className={
                  overdue ? 'text-xs font-medium text-danger' : 'text-xs text-text-subtle'
                }
              >
                {distance.label}
              </p>
            )}
          </div>
        );
      },
    },
    {
      id: 'custody',
      header: 'Still held',
      width: 'w-44',
      cell: (leaver) =>
        leaver.currentAssetCount === 0 ? (
          <StatusDot tone="success" muted>
            Nothing out
          </StatusDot>
        ) : (
          <StatusDot tone={leaver.overdueAssetCount > 0 ? 'danger' : 'warning'}>
            {formatNumber(leaver.currentAssetCount)}{' '}
            {leaver.currentAssetCount === 1 ? 'item' : 'items'}
            {leaver.overdueAssetCount > 0 && (
              <span className="text-danger">
                {' · '}
                {formatNumber(leaver.overdueAssetCount)} overdue
              </span>
            )}
          </StatusDot>
        ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Leavers"
        description="Staff and pupils who are going, and the equipment still to come back."
        actions={
          canManage && (
            <Button variant="primary" iconLeft={<Plus />} onClick={() => setCreating(true)}>
              New leaver case
            </Button>
          )
        }
      />

      <CustodyExceptionsCard />

      <Card className="overflow-hidden">
        <ListToolbar
          search={
            <SearchInput
              value={url.get('q') ?? ''}
              onChange={(value) => url.set({ q: value })}
              placeholder="Search by name or staff number…"
              aria-label="Search leaver cases"
            />
          }
          count={
            total === undefined
              ? undefined
              : `${formatNumber(total)} ${total === 1 ? 'case' : 'cases'}`
          }
          onClearFilters={url.hasFilters() ? url.clear : undefined}
          filters={
            <FilterButton
              label="Status"
              value={params.status ?? null}
              onChange={(value) => url.set({ status: value ?? undefined })}
              options={STATUS_OPTIONS}
            />
          }
        />

        {cases.isError ? (
          <ErrorState error={cases.error} onRetry={() => void cases.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(leaver) => leaver.id}
              loading={cases.isLoading}
              onRowClick={(leaver) => void navigate(`/leavers/${leaver.id}`)}
              rowClassName={(leaver) =>
                leaver.status === 'cancelled' ? 'opacity-60' : undefined
              }
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<UserMinus />}
                    title="No leaver cases"
                    description="Open one when someone is leaving, so what they hold can be worked through before their last day."
                    action={
                      canManage && (
                        <Button
                          variant="primary"
                          iconLeft={<Plus />}
                          onClick={() => setCreating(true)}
                        >
                          New leaver case
                        </Button>
                      )
                    }
                  />
                )
              }
            />

            {(total ?? 0) > 0 && (
              <Pagination
                page={cases.data?.page ?? 1}
                pageSize={cases.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={total ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="cases"
              />
            )}
          </>
        )}
      </Card>

      {creating && <CreateLeaverCaseDialog onClose={() => setCreating(false)} />}
    </div>
  );
}
