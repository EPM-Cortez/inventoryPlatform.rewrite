import { CalendarClock, CircleCheck } from 'lucide-react';
import { useMemo } from 'react';
import { Link } from 'react-router';

import { useOverdueLoans } from '@/api/hooks';
import type { OverdueLoan } from '@/api/types';
import { formatDate, formatNumber, formatRelative } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { Tooltip } from '@/ui/Tooltip';

const DEFAULT_PAGE_SIZE = 50;

/**
 * Loans that are past their due date.
 *
 * `daysOverdue` comes from the server, computed against the school time zone —
 * the client must not recompute it from `expectedReturnOn`, because a browser in
 * another zone would disagree about which day it is and show a loan due today as
 * already a day late.
 */
export default function OverdueLoansPage() {
  const url = useUrlState();

  const params = useMemo(
    () => ({
      personId: url.get('personId'),
      departmentId: url.get('departmentId'),
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const loans = useOverdueLoans(params);
  const rows = loans.data?.items ?? [];
  const total = loans.data?.totalCount ?? 0;

  const columns: Column<OverdueLoan>[] = [
    {
      id: 'asset',
      header: 'Asset',
      cell: (loan) => (
        <div className="min-w-0">
          <Link
            to={`/assets/${loan.assetId}`}
            className="truncate rounded-xs font-mono text-sm font-medium text-accent hover:underline"
          >
            {loan.assetTag}
          </Link>
          {loan.deviceName && (
            <p className="truncate text-2xs text-text-subtle">{loan.deviceName}</p>
          )}
        </div>
      ),
    },
    {
      id: 'holder',
      header: 'Held by',
      cell: (loan) => (
        <Link
          to={
            loan.target.type === 'person'
              ? `/people/${loan.target.id}`
              : `/departments/${loan.target.id}`
          }
          className="truncate rounded-xs text-text hover:text-accent hover:underline"
        >
          {loan.target.displayName}
        </Link>
      ),
    },
    {
      id: 'since',
      header: 'Out since',
      hideBelow: 'md',
      width: 'w-40',
      cell: (loan) => (
        <span className="text-sm text-text-muted">
          {formatRelative(loan.assignedAtUtc)}
        </span>
      ),
    },
    {
      id: 'due',
      header: 'Was due',
      align: 'right',
      width: 'w-36',
      cell: (loan) => (
        <span className="text-sm text-text-muted">{formatDate(loan.expectedReturnOn)}</span>
      ),
    },
    {
      id: 'overdue',
      header: 'Overdue by',
      align: 'right',
      width: 'w-32',
      cell: (loan) => (
        <Tooltip content={`Due ${formatDate(loan.expectedReturnOn)}, judged in the school time zone.`}>
          <span className="inline-block">
            <Badge tone={overdueTone(loan.daysOverdue)} dot>
              {loan.daysOverdue === 1 ? '1 day' : `${formatNumber(loan.daysOverdue)} days`}
            </Badge>
          </span>
        </Tooltip>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Overdue loans"
        description="Equipment that was due back and has not been returned."
      />

      <Card className="overflow-hidden">
        {loans.isError ? (
          <ErrorState error={loans.error} onRetry={() => void loans.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(loan) => loan.assignmentId}
              loading={loans.isLoading}
              empty={
                <EmptyState
                  icon={<CircleCheck />}
                  title="Nothing is overdue"
                  description="Every loan with a due date has either been returned or is still within it."
                />
              }
            />

            {total > 0 && (
              <Pagination
                page={loans.data?.page ?? 1}
                pageSize={loans.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={total}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="loans"
              />
            )}
          </>
        )}
      </Card>

      {total > 0 && (
        <p className="flex items-center gap-2 text-sm text-text-subtle">
          <CalendarClock className="size-4 shrink-0" aria-hidden />
          Only loans given a due date appear here. An open-ended assignment is never
          overdue.
        </p>
      )}
    </div>
  );
}

/** A week late is chasing; a month late is probably gone. */
function overdueTone(days: number): 'warning' | 'danger' {
  return days > 30 ? 'danger' : 'warning';
}
