import { KeyRound, MonitorSmartphone } from 'lucide-react';
import { useMemo } from 'react';
import { Link, useNavigate } from 'react-router';

import { useAgentFleetSummary, useAgents } from '@/api/hooks';
import type {
  ManagedAgentConnectivity,
  ManagedAgentListItem,
  ManagedAgentMatchState,
  ManagedAgentStatus,
} from '@/api/agentTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { formatNumber, formatRelative } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { SearchInput } from '@/ui/SearchInput';
import { Tooltip } from '@/ui/Tooltip';

import {
  CONNECTIVITY_LABELS,
  CONNECTIVITY_TONES,
  FLEET_TILES,
  MATCH_STATE_HINTS,
  MATCH_STATE_LABELS,
  MATCH_STATE_TONES,
} from './agentLabels';
import { InstallerCard } from './InstallerCard';

const DEFAULT_PAGE_SIZE = 50;

/**
 * Machines running the Windows agent.
 *
 * Everything here is collected automatically and cannot be edited. The fleet
 * summary is the entry point: each tile is a filter, so a count is never a dead
 * end — clicking it shows the machines behind it.
 *
 * Connectivity comes from the server, judged against the interval each agent is
 * expected to report on. It is deliberately not recomputed here from the last
 * check-in: a fixed client-side threshold would disagree with the summary counts.
 */
export default function DevicesPage() {
  const url = useUrlState();
  const navigate = useNavigate();
  const { can } = useSession();

  const params = useMemo(
    () => ({
      q: url.get('q')?.trim() || undefined,
      matchState: url.get('matchState') as ManagedAgentMatchState | undefined,
      connectivity: url.get('connectivity') as ManagedAgentConnectivity | undefined,
      trustStatus: url.get('trustStatus') as ManagedAgentStatus | undefined,
      // Revoked machines stay out of the way unless they are what was asked for.
      includeRevoked: url.get('trustStatus') === 'revoked',
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const agents = useAgents(params);
  const summary = useAgentFleetSummary();
  const rows = agents.data?.items ?? [];

  const columns: Column<ManagedAgentListItem>[] = [
    {
      id: 'machine',
      header: 'Machine',
      cell: (agent) => (
        <div className="min-w-0">
          <Link
            to={`/devices/${agent.id}`}
            onClick={(event) => event.stopPropagation()}
            className="truncate rounded-xs font-mono text-sm font-medium text-accent hover:underline"
          >
            {agent.hostname}
          </Link>
          <p className="truncate text-2xs text-text-subtle">
            {[agent.manufacturer, agent.model].filter(Boolean).join(' ') ||
              agent.serialNumber ||
              agent.deviceId.slice(0, 12)}
          </p>
        </div>
      ),
    },
    {
      id: 'link',
      header: 'Asset',
      cell: (agent) =>
        agent.linkedAssetId ? (
          <div className="min-w-0">
            <Link
              to={`/assets/${agent.linkedAssetId}`}
              onClick={(event) => event.stopPropagation()}
              className="truncate rounded-xs font-mono text-sm text-accent hover:underline"
            >
              {agent.linkedAssetTag}
            </Link>
            {agent.linkedAssetDeviceName && (
              <p className="truncate text-2xs text-text-subtle">
                {agent.linkedAssetDeviceName}
              </p>
            )}
          </div>
        ) : (
          <Tooltip content={agent.matchReason ?? MATCH_STATE_HINTS[agent.matchState]}>
            <span className="inline-block">
              <Badge tone={MATCH_STATE_TONES[agent.matchState]}>
                {MATCH_STATE_LABELS[agent.matchState]}
              </Badge>
            </span>
          </Tooltip>
        ),
    },
    {
      id: 'connectivity',
      header: 'Reporting',
      width: 'w-40',
      cell: (agent) => (
        <Tooltip
          content={
            agent.lastCheckInAtUtc
              ? `Last check-in ${formatRelative(agent.lastCheckInAtUtc)}.${
                  agent.nextExpectedCheckInAtUtc
                    ? ` Next expected ${formatRelative(agent.nextExpectedCheckInAtUtc)}.`
                    : ''
                }`
              : 'Enrolled but has never sent a snapshot.'
          }
        >
          <span className="inline-flex min-w-0 flex-col items-start">
            <Badge tone={CONNECTIVITY_TONES[agent.connectivityStatus]} dot>
              {CONNECTIVITY_LABELS[agent.connectivityStatus]}
            </Badge>
            {agent.lastCheckInAtUtc && (
              <span className="mt-0.5 truncate text-2xs text-text-subtle">
                {formatRelative(agent.lastCheckInAtUtc)}
              </span>
            )}
          </span>
        </Tooltip>
      ),
    },
    {
      id: 'trust',
      header: 'Key',
      width: 'w-28',
      hideBelow: 'lg',
      cell: (agent) =>
        agent.status === 'revoked' ? (
          <Tooltip content={agent.revokedReason ?? 'This agent has been revoked.'}>
            <span className="inline-block">
              <Badge tone="danger">Revoked</Badge>
            </span>
          </Tooltip>
        ) : agent.keyProtection === 'tpm' ? (
          <Tooltip content="The signing key is held in the machine's TPM and cannot be exported.">
            <span className="inline-block">
              <Badge tone="success">TPM</Badge>
            </span>
          </Tooltip>
        ) : (
          <Tooltip content="The signing key is stored in software and could in principle be copied off the machine.">
            <span className="inline-block">
              <Badge tone="warning">Software</Badge>
            </span>
          </Tooltip>
        ),
    },
    {
      id: 'enrolment',
      header: 'Enrolled with',
      hideBelow: 'xl',
      width: 'w-44',
      cell: (agent) => (
        <span className="truncate text-xs text-text-muted">
          {agent.enrollmentProfileName}
        </span>
      ),
    },
    {
      id: 'ip',
      header: 'Seen from',
      hideBelow: 'xl',
      align: 'right',
      width: 'w-36',
      cell: (agent) =>
        agent.lastSeenIpAddress ? (
          <Tooltip content="Observed by the server, so it sits outside the signed snapshot — a machine cannot assert its own address.">
            <span className="inline-block truncate font-mono text-2xs text-text-subtle">
              {agent.lastSeenIpAddress}
            </span>
          </Tooltip>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
  ];

  const activeTile = FLEET_TILES.find(
    (tile) =>
      tile.filter &&
      Object.entries(tile.filter).every(([key, value]) => url.get(key) === value),
  );

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Devices"
        description="Windows machines running the inventory agent. Everything here is collected automatically and cannot be edited."
        actions={
          can(Permission.AgentsManage) && (
            <Button asChild>
              <Link to="/enrollment-tokens">
                <KeyRound className="mr-1.5 size-4" />
                Enrollment tokens
              </Link>
            </Button>
          )
        }
      />

      <section
        aria-label="Fleet summary"
        className="grid gap-3 sm:grid-cols-4 xl:grid-cols-8"
      >
        {FLEET_TILES.map((tile) => {
          const value = summary.data?.[tile.key] ?? 0;
          const active = tile === activeTile || (!tile.filter && !activeTile);
          return (
            <button
              key={tile.key}
              type="button"
              aria-pressed={active}
              onClick={() =>
                // Replaces the filter rather than merging, so two tiles can never
                // combine into a view nobody asked for. The search is kept.
                url.replaceAll({ ...(tile.filter ?? {}), q: url.get('q') })
              }
              className={cn(
                'rounded-md border p-3 text-left transition-colors duration-(--duration-fast)',
                active
                  ? 'border-accent bg-accent-soft/40'
                  : 'border-border bg-surface hover:border-border-strong hover:bg-surface-hover',
              )}
            >
              <span
                className={cn(
                  'block text-xs font-medium',
                  value === 0
                    ? 'text-text-subtle'
                    : tile.tone === 'danger'
                      ? 'text-danger'
                      : tile.tone === 'warning'
                        ? 'text-warning'
                        : 'text-text-muted',
                )}
              >
                {tile.label}
              </span>
              <span className="mt-0.5 block text-2xl font-semibold tabular-nums text-text">
                {summary.isLoading ? '—' : formatNumber(value)}
              </span>
            </button>
          );
        })}
      </section>

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <SearchInput
            value={url.get('q') ?? ''}
            onChange={(value) => url.set({ q: value })}
            placeholder="Search hostname, serial or model…"
            className="min-w-56 flex-1"
            aria-label="Search devices"
          />
          {url.hasFilters() && (
            <Button size="sm" onClick={url.clear}>
              Clear filters
            </Button>
          )}
        </div>

        {agents.isError ? (
          <ErrorState error={agents.error} onRetry={() => void agents.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(agent) => agent.id}
              loading={agents.isLoading}
              onRowClick={(agent) => void navigate(`/devices/${agent.id}`)}
              rowClassName={(agent) =>
                agent.status === 'revoked' || agent.matchState === 'dismissed'
                  ? 'opacity-60'
                  : undefined
              }
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<MonitorSmartphone />}
                    title="No devices have enrolled"
                    description="Machines appear here once the agent is installed and has enrolled with a valid token."
                  />
                )
              }
            />

            {(agents.data?.totalCount ?? 0) > 0 && (
              <Pagination
                page={agents.data?.page ?? 1}
                pageSize={agents.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={agents.data?.totalCount ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="devices"
              />
            )}
          </>
        )}
      </Card>

      {can(Permission.AgentsManage) && <InstallerCard />}
    </div>
  );
}
