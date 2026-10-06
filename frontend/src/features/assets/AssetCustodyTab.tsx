import { CalendarClock, UserRoundPlus } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { useAssetAssignments } from '@/api/hooks';
import type { Asset, AssetAssignment } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { ASSET_CONDITION_LABELS, formatDate, formatDateTime } from '@/lib/format';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Pagination } from '@/ui/Pagination';

import { holderPath } from './AssetCells';
import { ExpectedReturnDialog } from './ExpectedReturnDialog';

const PAGE_SIZE = 25;

export function AssetCustodyTab({
  asset,
  onAssign,
}: {
  asset: Asset;
  onAssign: () => void;
}) {
  const [page, setPage] = useState(1);
  const [editingDue, setEditingDue] = useState<AssetAssignment | null>(null);
  const { can } = useSession();
  const canManage = can(Permission.AssignmentsManage);
  const assignments = useAssetAssignments(asset.id, page, PAGE_SIZE);

  const columns: Column<AssetAssignment>[] = [
    {
      id: 'holder',
      header: 'Holder',
      cell: (assignment) => (
        <div className="min-w-0">
          <Link
            to={holderPath(assignment.target)}
            className="rounded-xs font-medium text-text hover:text-accent hover:underline"
          >
            {assignment.target.displayName}
          </Link>
          <p className="text-xs text-text-subtle">
            {assignment.target.type === 'person' ? 'Person' : 'Department'}
            {assignment.target.reference ? ` · ${assignment.target.reference}` : ''}
          </p>
        </div>
      ),
    },
    {
      id: 'out',
      header: 'Issued',
      width: 'w-48',
      cell: (assignment) => (
        <div className="min-w-0">
          <p className="text-text">{formatDateTime(assignment.assignedAtUtc)}</p>
          <p className="truncate text-xs text-text-subtle">
            by {assignment.assignedByDisplayName} ·{' '}
            {ASSET_CONDITION_LABELS[assignment.assignedCondition]}
          </p>
        </div>
      ),
    },
    {
      id: 'in',
      header: 'Returned',
      width: 'w-48',
      cell: (assignment) =>
        assignment.returnedAtUtc ? (
          <div className="min-w-0">
            <p className="text-text">{formatDateTime(assignment.returnedAtUtc)}</p>
            <p className="truncate text-xs text-text-subtle">
              by {assignment.returnedByDisplayName}
              {assignment.returnCondition
                ? ` · ${ASSET_CONDITION_LABELS[assignment.returnCondition]}`
                : ''}
            </p>
          </div>
        ) : (
          <Badge tone="accent" dot>
            Still out
          </Badge>
        ),
    },
    {
      id: 'due',
      header: 'Due back',
      width: 'w-44',
      cell: (assignment) => {
        // Only an open loan can be overdue or have its date changed; a closed one
        // keeps its due date as a historical record.
        const closed = assignment.returnedAtUtc !== null;

        if (!assignment.expectedReturnOn) {
          return closed ? (
            <span className="text-text-subtle">—</span>
          ) : canManage ? (
            <button
              type="button"
              onClick={() => setEditingDue(assignment)}
              className="rounded-xs text-sm text-text-subtle hover:text-accent hover:underline"
            >
              Set a date
            </button>
          ) : (
            <span className="text-text-subtle">Open-ended</span>
          );
        }

        return (
          <div className="flex min-w-0 items-center gap-2">
            <span className="text-sm text-text-muted">
              {formatDate(assignment.expectedReturnOn)}
            </span>
            {assignment.isOverdue && !closed && (
              <Badge tone="danger" dot>
                Overdue
              </Badge>
            )}
            {canManage && !closed && (
              <IconButton
                label={`Change due date for ${assignment.target.displayName}`}
                size="sm"
                onClick={() => setEditingDue(assignment)}
              >
                <CalendarClock />
              </IconButton>
            )}
          </div>
        );
      },
    },
    {
      id: 'notes',
      header: 'Notes',
      hideBelow: 'lg',
      cell: (assignment) => {
        const notes = [assignment.assignmentNotes, assignment.returnNotes]
          .filter(Boolean)
          .join(' → ');
        return notes ? (
          <span className="truncate-2 text-sm text-text-muted">{notes}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        );
      },
    },
  ];

  if (assignments.isError) {
    return (
      <ErrorState error={assignments.error} onRetry={() => void assignments.refetch()} />
    );
  }

  return (
    <Card className="overflow-hidden">
      <DataTable
        columns={columns}
        rows={assignments.data?.items ?? []}
        rowKey={(assignment) => assignment.id}
        loading={assignments.isLoading}
        skeletonRows={4}
        empty={
          <EmptyState
            icon={<UserRoundPlus />}
            title="No custody recorded"
            description="This asset has never been issued to a person or a department."
            action={
              can(Permission.AssignmentsManage) &&
              asset.status !== 'retired' &&
              asset.status !== 'disposed' && (
                <Button variant="primary" onClick={onAssign}>
                  Assign it
                </Button>
              )
            }
          />
        }
      />
      {editingDue && (
        <ExpectedReturnDialog
          assetId={asset.id}
          assignment={editingDue}
          onClose={() => setEditingDue(null)}
        />
      )}

      {(assignments.data?.totalCount ?? 0) > PAGE_SIZE && (
        <Pagination
          page={assignments.data?.page ?? 1}
          pageSize={assignments.data?.pageSize ?? PAGE_SIZE}
          totalCount={assignments.data?.totalCount ?? 0}
          onPageChange={setPage}
          itemLabel="custody records"
        />
      )}
    </Card>
  );
}
