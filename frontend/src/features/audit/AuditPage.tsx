import { Download, ScrollText } from 'lucide-react';
import { useMemo, useState } from 'react';

import { useAuditLog, useAuditVocabulary, useExportAudit, useUsers } from '@/api/hooks';
import type { AuditLogEntry, AuditSearchParams } from '@/api/adminTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { downloadBlob } from '@/lib/download';
import { formatDateTime, formatNumber, formatRelative } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { Field, Input, NativeSelect } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { Tooltip } from '@/ui/Tooltip';
import { useToast } from '@/ui/Toast';

import {
  describeAuditAction,
  describeOutcome,
  describeTargetType,
  groupAuditActions,
  isKnownAuditAction,
  outcomeOptions,
  outcomeTone,
  targetTypeOptions,
} from './auditActions';
import { AuditEntryDrawer } from './AuditEntryDrawer';

const DEFAULT_PAGE_SIZE = 50;

function isInVocabulary(
  value: string | undefined,
  groups: { actions: { value: string }[] }[],
): boolean {
  return groups.some((group) => group.actions.some((action) => action.value === value));
}

/**
 * The audit trail.
 *
 * Filters live in the URL so a specific view — one account, one day, failures
 * only — can be pasted into a ticket and reopened exactly as it was.
 *
 * The filter options come from `GET /audit/vocabulary` rather than a list
 * mirrored here, so they describe this database: only values with rows behind
 * them are offered, and a new kind of event becomes filterable the first time it
 * happens.
 */
export default function AuditPage() {
  const url = useUrlState();
  const toast = useToast();
  const { can } = useSession();

  const [selected, setSelected] = useState<AuditLogEntry | null>(null);

  const params = useMemo<AuditSearchParams>(
    () => ({
      action: url.get('action'),
      targetType: url.get('targetType'),
      targetId: url.get('targetId')?.trim() || undefined,
      actorUserId: url.get('actorUserId'),
      outcome: url.get('outcome'),
      from: startOfDay(url.get('from')),
      to: endOfDay(url.get('to')),
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const audit = useAuditLog(params);
  const exportAudit = useExportAudit();
  // The filter values that are actually in the trail, so the dropdowns never
  // offer something with no rows behind it.
  const vocabulary = useAuditVocabulary();
  const actionGroups = useMemo(
    () => groupAuditActions(vocabulary.data?.actions ?? []),
    [vocabulary.data],
  );
  const targetTypes = useMemo(
    () => targetTypeOptions(vocabulary.data?.targetTypes ?? []),
    [vocabulary.data],
  );
  const outcomes = useMemo(
    () => outcomeOptions(vocabulary.data?.outcomes ?? []),
    [vocabulary.data],
  );
  // Only for turning actor ids into names in the filter; the entries carry their
  // own `actorDisplayName` snapshot from the moment the action happened.
  const users = useUsers(can(Permission.UsersManage));

  const rows = audit.data?.items ?? [];
  const canExport = can(Permission.AuditExport);

  const columns: Column<AuditLogEntry>[] = [
    {
      id: 'when',
      header: 'When',
      width: 'w-48',
      cell: (entry) => (
        <div className="min-w-0">
          <p className="truncate text-sm text-text">
            {formatDateTime(entry.occurredAtUtc)}
          </p>
          <p className="truncate text-2xs text-text-subtle">
            {formatRelative(entry.occurredAtUtc)}
          </p>
        </div>
      ),
    },
    {
      id: 'actor',
      header: 'Who',
      hideBelow: 'md',
      cell: (entry) =>
        entry.actorDisplayName ? (
          <span className="truncate text-text">{entry.actorDisplayName}</span>
        ) : (
          <Tooltip content="Recorded without a signed-in user — an agent check-in or a system action.">
            <span className="inline-block text-text-subtle">System</span>
          </Tooltip>
        ),
    },
    {
      id: 'action',
      header: 'Action',
      cell: (entry) => (
        <div className="min-w-0">
          <p className="truncate text-text">{describeAuditAction(entry.action)}</p>
          {!isKnownAuditAction(entry.action) && (
            // No curated label, so show the raw value rather than nothing.
            <p className="truncate font-mono text-2xs text-text-subtle">{entry.action}</p>
          )}
        </div>
      ),
    },
    {
      id: 'target',
      header: 'Target',
      hideBelow: 'lg',
      width: 'w-44',
      cell: (entry) => (
        <div className="min-w-0">
          <p className="truncate text-text-muted">
            {describeTargetType(entry.targetType)}
          </p>
          {entry.targetId && (
            <p className="truncate font-mono text-2xs text-text-subtle">
              {entry.targetId}
            </p>
          )}
        </div>
      ),
    },
    {
      id: 'outcome',
      header: 'Outcome',
      align: 'right',
      width: 'w-32',
      cell: (entry) => (
        <Badge tone={outcomeTone(entry.outcome)} dot>
          {describeOutcome(entry.outcome)}
        </Badge>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Audit trail"
        description="Who did what, and whether it worked. Entries are written by the server and cannot be edited or removed."
        actions={
          canExport && (
            <Button
              iconLeft={<Download />}
              loading={exportAudit.isPending}
              onClick={() =>
                exportAudit.mutate(params, {
                  onSuccess: (file) => {
                    downloadBlob(file.blob, file.fileName);
                    toast.success(
                      'Export ready',
                      `${formatNumber(file.rowCount)} ${file.rowCount === 1 ? 'row' : 'rows'} downloaded.`,
                    );
                  },
                  onError: (error) => toast.fromError(error, 'Could not export'),
                })
              }
            >
              Export these results
            </Button>
          )
        }
      />

      <Card className="overflow-hidden">
        <div className="grid gap-3 border-b border-border p-3 sm:grid-cols-2 lg:grid-cols-3">
          <Field label="Action">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={url.get('action') ?? ''}
                onChange={(event) => url.set({ action: event.target.value || undefined })}
              >
                <option value="">Any action</option>
                {actionGroups.map((group) => (
                  <optgroup key={group.label} label={group.label}>
                    {group.actions.map((action) => (
                      <option key={action.value} value={action.value}>
                        {action.label}
                      </option>
                    ))}
                  </optgroup>
                ))}
                {/* A pasted URL may name an action the vocabulary no longer
                    lists — a pre-canonicalisation spelling, or one whose rows
                    have aged out. The API still filters on it, so it must stay
                    selectable or opening the link would silently widen it. */}
                {url.get('action') && !isInVocabulary(url.get('action'), actionGroups) && (
                  <option value={url.get('action')}>
                    {describeAuditAction(url.get('action')!)}
                  </option>
                )}
              </NativeSelect>
            )}
          </Field>

          <Field label="Outcome">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={url.get('outcome') ?? ''}
                onChange={(event) => url.set({ outcome: event.target.value || undefined })}
              >
                <option value="">Any outcome</option>
                {outcomes.map((outcome) => (
                  <option key={outcome.value} value={outcome.value}>
                    {outcome.label}
                  </option>
                ))}
                {url.get('outcome') &&
                  !outcomes.some((o) => o.value === url.get('outcome')) && (
                    <option value={url.get('outcome')}>
                      {describeOutcome(url.get('outcome')!)}
                    </option>
                  )}
              </NativeSelect>
            )}
          </Field>

          <Field label="Who">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={url.get('actorUserId') ?? ''}
                disabled={!can(Permission.UsersManage)}
                onChange={(event) =>
                  url.set({ actorUserId: event.target.value || undefined })
                }
              >
                <option value="">Anyone</option>
                {(users.data?.items ?? []).map((user) => (
                  <option key={user.id} value={user.id}>
                    {user.displayName}
                  </option>
                ))}
              </NativeSelect>
            )}
          </Field>

          <Field label="Target type">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={url.get('targetType') ?? ''}
                onChange={(event) =>
                  url.set({ targetType: event.target.value || undefined })
                }
              >
                <option value="">Anything</option>
                {targetTypes.map((target) => (
                  <option key={target.value} value={target.value}>
                    {target.label}
                  </option>
                ))}
                {url.get('targetType') &&
                  !targetTypes.some((t) => t.value === url.get('targetType')) && (
                    <option value={url.get('targetType')}>
                      {describeTargetType(url.get('targetType')!)}
                    </option>
                  )}
              </NativeSelect>
            )}
          </Field>

          <Field label="Target id" hint="Exact match, e.g. an asset or account id.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={url.get('targetId') ?? ''}
                onChange={(event) =>
                  url.set({ targetId: event.target.value || undefined })
                }
                mono
                placeholder="Any"
              />
            )}
          </Field>

          <Field label="From">
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                value={url.get('from') ?? ''}
                max={url.get('to')}
                onChange={(event) => url.set({ from: event.target.value || undefined })}
              />
            )}
          </Field>

          <Field label="To">
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                value={url.get('to') ?? ''}
                min={url.get('from')}
                onChange={(event) => url.set({ to: event.target.value || undefined })}
              />
            )}
          </Field>

          {vocabulary.data?.truncated && (
            <p className="text-2xs text-text-subtle sm:col-span-2 lg:col-span-3">
              The server capped the filter lists, so some values that exist in the trail
              are not offered above. Filtering by target id, dates or the account still
              reaches them.
            </p>
          )}
        </div>

        {url.hasFilters() && (
          <div className="flex items-center justify-between gap-3 border-b border-border px-3 py-2">
            <p className="text-sm text-text-muted">
              {formatNumber(audit.data?.totalCount ?? 0)} matching{' '}
              {(audit.data?.totalCount ?? 0) === 1 ? 'entry' : 'entries'}
            </p>
            <Button size="sm" onClick={url.clear}>
              Clear filters
            </Button>
          </div>
        )}

        {audit.isError ? (
          <ErrorState error={audit.error} onRetry={() => void audit.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(entry) => entry.id}
              loading={audit.isLoading}
              skeletonRows={12}
              onRowClick={setSelected}
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<ScrollText />}
                    title="Nothing recorded yet"
                    description="Sign-ins, account changes, imports and label prints all appear here as they happen."
                  />
                )
              }
            />

            {(audit.data?.totalCount ?? 0) > 0 && (
              <Pagination
                page={audit.data?.page ?? 1}
                pageSize={audit.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={audit.data?.totalCount ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="entries"
              />
            )}
          </>
        )}
      </Card>

      {selected && (
        <AuditEntryDrawer entry={selected} onClose={() => setSelected(null)} />
      )}
    </div>
  );
}

/**
 * The date inputs collect a local calendar day; the API filters on instants.
 * Widening each end to cover the whole local day keeps "1 July to 1 July" from
 * returning nothing.
 */
function startOfDay(value: string | undefined): string | undefined {
  if (!value) return undefined;
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

function endOfDay(value: string | undefined): string | undefined {
  if (!value) return undefined;
  const date = new Date(`${value}T23:59:59.999`);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}
