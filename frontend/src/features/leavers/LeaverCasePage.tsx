import { CheckCircle2, PackageCheck, RotateCcw, XCircle } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';

import {
  useCancelLeaverCase,
  useCompleteLeaverCase,
  useLeaverCase,
  useRehireLeaver,
} from '@/api/hooks';
import type { LeaverCustodyAsset } from '@/api/operationsTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import {
  describeDaysFromToday,
  formatDate,
  formatDateTime,
  formatNumber,
  formatRelative,
} from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Avatar } from '@/ui/Avatar';
import { Button } from '@/ui/Button';
import { Card, CardHeader } from '@/ui/Card';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Breadcrumbs } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { StatusDot } from '@/ui/StatusDot';
import { SummaryFact, SummaryPanel } from '@/ui/SummaryPanel';
import { Tooltip } from '@/ui/Tooltip';
import { useToast } from '@/ui/Toast';

import {
  LEAVER_STATUS_LABELS,
  LEAVER_STATUS_TONES,
  completionBlocker,
} from './leaverLabels';

type PendingAction = 'complete' | 'cancel' | 'rehire';

export default function LeaverCasePage() {
  const { leaverCaseId } = useParams<{ leaverCaseId: string }>();
  const { can } = useSession();
  const toast = useToast();
  const [pending, setPending] = useState<PendingAction | null>(null);
  const [today] = useState(() => new Date());

  const leaver = useLeaverCase(leaverCaseId);
  const complete = useCompleteLeaverCase(leaverCaseId ?? '');
  const cancel = useCancelLeaverCase(leaverCaseId ?? '');
  const rehire = useRehireLeaver(leaverCaseId ?? '');

  if (leaver.isLoading) return <LoadingBlock label="Loading leaver case…" />;
  if (leaver.isError) {
    return <ErrorState error={leaver.error} onRetry={() => void leaver.refetch()} />;
  }
  if (!leaver.data) return null;

  const data = leaver.data;
  const open = data.status === 'open';
  const canManage = can(Permission.LeaversManage);
  const blocker = completionBlocker(data.currentAssetCount);
  const overdueCount = data.currentAssets.filter((item) => item.isOverdue).length;
  const distance = describeDaysFromToday(data.leavingOn, today);
  const lastDayPassed = open && (distance?.days ?? 0) < 0;

  const columns: Column<LeaverCustodyAsset>[] = [
    {
      id: 'asset',
      header: 'Asset',
      cell: (item) => (
        <div className="min-w-0">
          <Link
            to={`/assets/${item.assetId}`}
            className="block truncate rounded-xs font-medium tabular-nums text-text hover:text-accent hover:underline"
          >
            {item.assetTag}
          </Link>
          {item.deviceName && item.deviceName !== item.assetTag && (
            <p className="truncate font-mono text-xs text-text-muted">{item.deviceName}</p>
          )}
        </div>
      ),
    },
    {
      id: 'since',
      header: 'Out since',
      width: 'w-40',
      cell: (item) => <span className="text-text-muted">{formatRelative(item.assignedAtUtc)}</span>,
    },
    {
      id: 'due',
      header: 'Due back',
      width: 'w-48',
      cell: (item) =>
        item.expectedReturnOn ? (
          item.isOverdue ? (
            <StatusDot tone="danger">Overdue · {formatDate(item.expectedReturnOn)}</StatusDot>
          ) : (
            <span className="text-text-muted">{formatDate(item.expectedReturnOn)}</span>
          )
        ) : (
          <span className="text-text-subtle">Open-ended</span>
        ),
    },
    {
      id: 'action',
      header: <span className="sr-only">Return</span>,
      align: 'right',
      width: 'w-32',
      cell: (item) => (
        // Returning happens on the asset, where the condition and notes belong.
        // Duplicating that form here would create a second, thinner way to do
        // the same thing and the two would drift.
        <Button size="sm" asChild>
          <Link to={`/assets/${item.assetId}`}>Return it</Link>
        </Button>
      ),
    },
  ];

  const actions =
    canManage && open ? (
      <>
        <Button
          variant="danger-subtle"
          iconLeft={<XCircle />}
          onClick={() => setPending('cancel')}
        >
          Cancel case
        </Button>
        {blocker ? (
          <Tooltip content={`${blocker} Return everything first.`}>
            <span className="inline-block">
              <Button variant="primary" iconLeft={<CheckCircle2 />} disabled>
                Complete
              </Button>
            </span>
          </Tooltip>
        ) : (
          <Button
            variant="primary"
            iconLeft={<CheckCircle2 />}
            onClick={() => setPending('complete')}
          >
            Complete
          </Button>
        )}
      </>
    ) : canManage && data.status === 'completed' ? (
      <Button iconLeft={<RotateCcw />} onClick={() => setPending('rehire')}>
        They are coming back
      </Button>
    ) : undefined;

  return (
    <div className="flex flex-col gap-5">
      <div className="-mb-2">
        <Breadcrumbs
          items={[{ label: 'Leavers', to: '/leavers' }, { label: data.personDisplayName }]}
        />
      </div>

      <SummaryPanel
        label="Leaver case summary"
        media={<Avatar name={data.personDisplayName} size="xl" />}
        title={data.personDisplayName}
        subtitle={
          <>
            Leaving {formatDate(data.leavingOn)}
            {' · '}
            <Link
              to={`/people/${data.personId}`}
              className="rounded-xs text-accent hover:underline"
            >
              View person
            </Link>
          </>
        }
        status={
          <>
            <StatusDot tone={LEAVER_STATUS_TONES[data.status]}>
              {LEAVER_STATUS_LABELS[data.status]}
            </StatusDot>
            {!data.personIsActive && (
              <StatusDot tone="neutral" muted>
                Person record archived
              </StatusDot>
            )}
          </>
        }
        actions={actions}
        facts={
          <>
            <SummaryFact label="Staff number">
              {data.personExternalId && (
                <span className="font-mono text-sm">{data.personExternalId}</span>
              )}
            </SummaryFact>
            <SummaryFact
              label="Last day"
              secondary={
                distance && (
                  <span className={lastDayPassed ? 'font-medium text-danger' : undefined}>
                    {distance.label}
                  </span>
                )
              }
            >
              {formatDate(data.leavingOn)}
            </SummaryFact>
            <SummaryFact label="Opened" secondary={`by ${data.createdByDisplayName}`}>
              {formatDateTime(data.createdAtUtc)}
            </SummaryFact>
            <SummaryFact label="Still held">
              {data.currentAssetCount === 0 ? (
                <StatusDot tone="success" muted>
                  Nothing out
                </StatusDot>
              ) : (
                <StatusDot tone={overdueCount > 0 ? 'danger' : 'warning'}>
                  {formatNumber(data.currentAssetCount)}{' '}
                  {data.currentAssetCount === 1 ? 'item' : 'items'}
                  {overdueCount > 0 && (
                    <span className="text-danger"> · {formatNumber(overdueCount)} overdue</span>
                  )}
                </StatusDot>
              )}
            </SummaryFact>
          </>
        }
      >
        {data.notes && (
          <div className="border-t border-border pt-4">
            <p className="text-sm text-text-muted">Notes</p>
            <p className="mt-1 whitespace-pre-wrap text-base text-text">{data.notes}</p>
          </div>
        )}
      </SummaryPanel>

      {data.status === 'completed' && (
        <Alert tone="success" title="Completed">
          Everything was handed back. Closed by {data.completedByDisplayName},{' '}
          {formatDateTime(data.completedAtUtc)}.
        </Alert>
      )}

      {data.status === 'cancelled' && (
        <Alert tone="info" title="This case was cancelled">
          Cancelled by {data.cancelledByDisplayName},{' '}
          {formatDateTime(data.cancelledAtUtc)}. Any custody they hold is unaffected.
        </Alert>
      )}

      {data.status === 'rehired' && (
        <Alert tone="info" title="They came back">
          The case was reopened as a rehire, so it no longer counts as a leaving.
        </Alert>
      )}

      <Card className="overflow-hidden">
        <CardHeader
          title="What they still have"
          description={
            blocker
              ? 'The case cannot be completed until all of this is back.'
              : 'Nothing is outstanding.'
          }
        />
        <DataTable
          columns={columns}
          rows={data.currentAssets}
          rowKey={(item) => item.assignmentId}
          empty={
            <EmptyState
              icon={<PackageCheck />}
              title="Everything is back"
              description="They are not holding anything on the register."
            />
          }
        />
        {data.currentAssetsTruncated && (
          // The detail embeds at most 100; saying so beats showing a partial list
          // as if it were the whole thing.
          <div className="border-t border-border px-4 py-2.5">
            <p className="text-xs text-text-subtle">
              Showing the first {formatNumber(data.currentAssets.length)} of{' '}
              {formatNumber(data.currentAssetCount)}. Return some to see the rest.
            </p>
          </div>
        )}
      </Card>

      {pending && (
        <ConfirmDialog
          open
          onOpenChange={(next) => !next && setPending(null)}
          title={
            pending === 'complete'
              ? 'Complete this case?'
              : pending === 'cancel'
                ? 'Cancel this case?'
                : 'Mark them as returning?'
          }
          description={
            pending === 'complete' ? (
              <>
                <strong>{data.personDisplayName}</strong> is holding nothing, so the case
                can be closed. This records that their equipment was all accounted for.
              </>
            ) : pending === 'cancel' ? (
              <>
                The case is closed without being completed — for someone whose leaving
                was called off, or a case opened by mistake. Any equipment they hold
                stays exactly as it is.
              </>
            ) : (
              <>
                <strong>{data.personDisplayName}</strong> is coming back, so this stops
                counting as a leaving. Nothing about their equipment changes.
              </>
            )
          }
          confirmLabel={
            pending === 'complete'
              ? 'Complete the case'
              : pending === 'cancel'
                ? 'Cancel the case'
                : 'Mark as returning'
          }
          destructive={pending === 'cancel'}
          loading={complete.isPending || cancel.isPending || rehire.isPending}
          onConfirm={() => {
            const mutation =
              pending === 'complete' ? complete : pending === 'cancel' ? cancel : rehire;
            const message =
              pending === 'complete'
                ? 'Leaver case completed'
                : pending === 'cancel'
                  ? 'Leaver case cancelled'
                  : 'Marked as returning';
            mutation.mutate(
              { version: data.version },
              {
                onSuccess: () => {
                  toast.success(message, data.personDisplayName);
                  setPending(null);
                },
                onError: (error) => {
                  toast.fromError(error, 'That did not work');
                  setPending(null);
                },
              },
            );
          }}
        />
      )}
    </div>
  );
}
