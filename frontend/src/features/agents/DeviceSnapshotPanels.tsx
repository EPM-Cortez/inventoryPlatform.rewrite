import {
  Cpu,
  HardDrive,
  Info,
  MonitorSmartphone,
  Network,
  Package,
  ShieldCheck,
} from 'lucide-react';
import { useMemo, useState } from 'react';

import type {
  AgentDeviceSnapshot,
  AgentInstalledSoftware,
  AgentNetworkAdapter,
  AgentStorageVolume,
} from '@/api/agentTypes';
import { cn } from '@/lib/cn';
import { stalenessTone, useAgeInDays } from '@/lib/useAgeInDays';
import { formatBytes, formatDate, formatDateTime, formatNumber, formatRelative } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Card, CardBody, CardHeader, DataItem } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState } from '@/ui/EmptyState';
import { SearchInput } from '@/ui/SearchInput';
import { Tooltip } from '@/ui/Tooltip';

/**
 * Everything the agent observed on a machine.
 *
 * This is evidence, not a form: nothing here is editable, and the whole panel is
 * qualified by when it was collected. A snapshot from three months ago describing
 * a machine that has since been rebuilt is worse than no snapshot at all if the
 * reader does not notice the date, so staleness is shown at the top.
 */
export function DeviceSnapshotPanels({ snapshot }: { snapshot: AgentDeviceSnapshot }) {
  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 lg:grid-cols-2">
        <HardwarePanel snapshot={snapshot} />
        <OperatingSystemPanel snapshot={snapshot} />
      </div>
      <SecurityPanel snapshot={snapshot} />
      <div className="grid gap-4 xl:grid-cols-2">
        <NetworkPanel adapters={snapshot.networkAdapters} />
        <StoragePanel volumes={snapshot.storageVolumes} />
      </div>
      <SoftwarePanel software={snapshot.installedSoftware} />
      <CollectionScope snapshot={snapshot} />
    </div>
  );
}

/** The cap the agent applies to the installed-software list. */
const SOFTWARE_LIMIT = 750;

/**
 * What the agent collected, and what it deliberately did not.
 *
 * Staff do ask whether the thing on their laptop is watching them, and the
 * honest answer is a short list rather than a reassuring adjective. It also
 * closes off a real misreading of the software panel: an empty-looking list is
 * not proof nothing is installed if the cap was reached.
 */
function CollectionScope({ snapshot }: { snapshot: AgentDeviceSnapshot }) {
  const atLimit = snapshot.installedSoftware.length >= SOFTWARE_LIMIT;

  return (
    <Card>
      <CardHeader
        title="What this covers"
        description="The agent reads the machine's own configuration. It never writes to the register."
      />
      <CardBody className="flex flex-col gap-3 text-sm">
        {atLimit && (
          <Alert tone="warning" title="The software list hit its limit">
            The agent reports at most {formatNumber(SOFTWARE_LIMIT)} programs and this
            machine reached that, so the list above is incomplete. Treat it as a sample
            rather than a full inventory.
          </Alert>
        )}

        <div>
          <p className="font-medium text-text">Not collected</p>
          <p className="mt-1 text-text-muted">
            Signed-in users, documents and personal files, browser history, and product
            keys or licence numbers. Only machine-wide installed programs are listed —
            not anything installed under a single user account.
          </p>
        </div>

        <div className="flex flex-wrap gap-x-6 gap-y-1 border-t border-border pt-3 text-xs text-text-subtle">
          <span>
            Snapshot{' '}
            <code className="font-mono text-text-muted">{snapshot.snapshotId}</code>
          </span>
          <span>
            Collected {formatDateTime(snapshot.collectedAtUtc)} by agent{' '}
            {snapshot.agentVersion}
          </span>
          {snapshot.domain && <span>Domain {snapshot.domain}</span>}
        </div>
      </CardBody>
    </Card>
  );
}

/**
 * How old the reading is.
 *
 * The agent checks in on a schedule, so "yesterday" is healthy and "two months
 * ago" means the machine has been off, off-network, or the agent has stopped.
 */
export function SnapshotFreshness({
  collectedAtUtc,
  agentVersion,
}: {
  collectedAtUtc: string;
  agentVersion?: string | null;
}) {
  const ageDays = useAgeInDays(collectedAtUtc);
  const tone = stalenessTone(ageDays);

  return (
    <span className="flex flex-wrap items-center gap-2">
      <Tooltip content={`Collected ${formatDateTime(collectedAtUtc)}`}>
        <span className="inline-block">
          <Badge tone={tone} dot>
            {tone === 'danger' ? 'Stale · ' : ''}
            {formatRelative(collectedAtUtc)}
          </Badge>
        </span>
      </Tooltip>
      {agentVersion && (
        <span className="font-mono text-2xs text-text-subtle">agent {agentVersion}</span>
      )}
    </span>
  );
}

function HardwarePanel({ snapshot }: { snapshot: AgentDeviceSnapshot }) {
  const { hardware } = snapshot;
  const cores = hardware.processorCores;
  const logical = hardware.logicalProcessors;

  return (
    <Card>
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <Cpu className="size-4 text-text-subtle" aria-hidden />
            Hardware
          </span>
        }
      />
      <CardBody>
        <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
          <DataItem label="Manufacturer">{hardware.manufacturer}</DataItem>
          <DataItem label="Model">{hardware.model}</DataItem>
          <DataItem label="Serial number" mono>
            {hardware.serialNumber}
          </DataItem>
          <DataItem label="BIOS UUID" mono>
            {hardware.biosUuid}
          </DataItem>
          <DataItem label="BIOS version" mono>
            {hardware.biosVersion}
          </DataItem>
          <DataItem label="Memory">{formatBytes(hardware.memoryBytes)}</DataItem>
          <DataItem label="Processor" className="sm:col-span-2">
            {hardware.processor}
          </DataItem>
          <DataItem label="Cores">
            {cores === null
              ? null
              : logical !== null && logical !== cores
                ? `${formatNumber(cores)} physical · ${formatNumber(logical)} logical`
                : formatNumber(cores)}
          </DataItem>
        </dl>
      </CardBody>
    </Card>
  );
}

function OperatingSystemPanel({ snapshot }: { snapshot: AgentDeviceSnapshot }) {
  const os = snapshot.operatingSystem;

  return (
    <Card>
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <MonitorSmartphone className="size-4 text-text-subtle" aria-hidden />
            Operating system
          </span>
        }
      />
      <CardBody>
        <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
          <DataItem label="Name" className="sm:col-span-2">
            {os.name}
          </DataItem>
          <DataItem label="Version" mono>
            {os.version}
          </DataItem>
          <DataItem label="Build" mono>
            {os.build}
          </DataItem>
          <DataItem label="Architecture" mono>
            {os.architecture}
          </DataItem>
          <DataItem label="Installed">{formatDateTime(os.installedAtUtc)}</DataItem>
          <DataItem label="Hostname" mono>
            {snapshot.hostname}
          </DataItem>
          <DataItem label="Domain" mono>
            {snapshot.domain}
          </DataItem>
          <DataItem label="Last booted" className="sm:col-span-2">
            {os.lastBootAtUtc ? (
              <span className="flex flex-wrap items-baseline gap-2">
                {formatDateTime(os.lastBootAtUtc)}
                <span className="text-xs text-text-subtle">
                  ({formatRelative(os.lastBootAtUtc)})
                </span>
              </span>
            ) : null}
          </DataItem>
        </dl>
      </CardBody>
    </Card>
  );
}

/**
 * Security posture.
 *
 * Each item is tri-state — the agent may not have been able to read it — so
 * "unknown" is rendered distinctly from "off". Reporting an unknown as a failure
 * would send someone chasing a problem that may not exist.
 */
function SecurityPanel({ snapshot }: { snapshot: AgentDeviceSnapshot }) {
  const { security } = snapshot;

  return (
    <Card>
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <ShieldCheck className="size-4 text-text-subtle" aria-hidden />
            Security
          </span>
        }
        description="Reported by the machine at the last check-in."
      />
      <CardBody>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <SecurityState label="Secure Boot" value={security.secureBootEnabled} />
          <SecurityState
            label="Drive encryption"
            value={security.systemDriveProtected}
            onLabel="On"
            offLabel="Off"
          />
          <SecurityState
            label="TPM present"
            value={security.tpmPresent}
            onLabel="Yes"
            offLabel="No"
          />
          <SecurityState
            label="TPM enabled"
            value={security.tpmEnabled}
            detail={security.tpmVersion ? `v${security.tpmVersion}` : undefined}
          />
        </div>

        {security.antivirusProducts && (
          <div className="mt-4 border-t border-border pt-4">
            <p className="text-xs font-medium uppercase tracking-wide text-text-subtle">
              Antivirus
            </p>
            <div className="mt-1.5 flex flex-wrap gap-1.5">
              {security.antivirusProducts
                .split(',')
                .map((product) => product.trim())
                .filter(Boolean)
                .map((product) => (
                  <Badge key={product} tone="neutral">
                    {product}
                  </Badge>
                ))}
            </div>
          </div>
        )}
      </CardBody>
    </Card>
  );
}

function SecurityState({
  label,
  value,
  onLabel = 'Enabled',
  offLabel = 'Disabled',
  detail,
}: {
  label: string;
  value: boolean | null;
  onLabel?: string;
  offLabel?: string;
  detail?: string;
}) {
  return (
    <div className="rounded-md border border-border bg-surface p-3">
      <p className="text-xs font-medium text-text-muted">{label}</p>
      <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
        {value === null ? (
          <Tooltip content="The agent could not read this setting.">
            <span className="inline-block">
              <Badge tone="neutral">
                <Info className="size-3" />
                Unknown
              </Badge>
            </span>
          </Tooltip>
        ) : value ? (
          <Badge tone="success" dot>
            {onLabel}
          </Badge>
        ) : (
          <Badge tone="warning" dot>
            {offLabel}
          </Badge>
        )}
        {detail && <span className="font-mono text-2xs text-text-subtle">{detail}</span>}
      </div>
    </div>
  );
}

function NetworkPanel({ adapters }: { adapters: AgentNetworkAdapter[] }) {
  const columns: Column<AgentNetworkAdapter>[] = [
    {
      id: 'name',
      header: 'Adapter',
      cell: (adapter) => (
        <div className="min-w-0">
          <p className="truncate text-text">{adapter.name}</p>
          {adapter.type && (
            <p className="truncate text-2xs text-text-subtle">{adapter.type}</p>
          )}
        </div>
      ),
    },
    {
      id: 'mac',
      header: 'MAC',
      width: 'w-44',
      cell: (adapter) =>
        adapter.macAddress ? (
          <span className="font-mono text-xs text-text-muted">{adapter.macAddress}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'ipv4',
      header: 'IPv4',
      width: 'w-40',
      cell: (adapter) =>
        adapter.ipv4Addresses.length > 0 ? (
          <span className="flex flex-col gap-0.5">
            {adapter.ipv4Addresses.map((address) => (
              <span key={address} className="font-mono text-xs text-text-muted">
                {address}
              </span>
            ))}
          </span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
  ];

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <Network className="size-4 text-text-subtle" aria-hidden />
            Network adapters
          </span>
        }
      />
      <DataTable
        columns={columns}
        rows={adapters}
        rowKey={(adapter) => `${adapter.name}-${adapter.macAddress ?? ''}`}
        empty={
          <EmptyState
            icon={<Network />}
            title="No adapters reported"
            className="py-8"
          />
        }
      />
    </Card>
  );
}

function StoragePanel({ volumes }: { volumes: AgentStorageVolume[] }) {
  const columns: Column<AgentStorageVolume>[] = [
    {
      id: 'name',
      header: 'Volume',
      width: 'w-32',
      cell: (volume) => (
        <div className="min-w-0">
          <p className="truncate font-mono text-sm text-text">{volume.name}</p>
          {volume.label && (
            <p className="truncate text-2xs text-text-subtle">{volume.label}</p>
          )}
        </div>
      ),
    },
    {
      id: 'fs',
      header: 'Format',
      hideBelow: 'lg',
      width: 'w-24',
      cell: (volume) => volume.fileSystem ?? <span className="text-text-subtle">—</span>,
    },
    {
      id: 'usage',
      header: 'Used',
      cell: (volume) => <VolumeUsage volume={volume} />,
    },
  ];

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <HardDrive className="size-4 text-text-subtle" aria-hidden />
            Storage
          </span>
        }
      />
      <DataTable
        columns={columns}
        rows={volumes}
        rowKey={(volume) => volume.name}
        empty={
          <EmptyState icon={<HardDrive />} title="No volumes reported" className="py-8" />
        }
      />
    </Card>
  );
}

/**
 * A disk with almost no space left is the single most actionable thing on this
 * page, so the bar changes tone as it fills rather than staying decorative. The
 * figures are always spelled out beside it — the bar alone is not the signal.
 */
function VolumeUsage({ volume }: { volume: AgentStorageVolume }) {
  if (volume.totalBytes <= 0) {
    return <span className="text-text-subtle">—</span>;
  }

  const usedBytes = Math.max(volume.totalBytes - volume.freeBytes, 0);
  const usedFraction = usedBytes / volume.totalBytes;
  const percent = Math.round(usedFraction * 100);
  const tone =
    usedFraction >= 0.9 ? 'danger' : usedFraction >= 0.75 ? 'warning' : 'accent';

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <div className="flex flex-wrap items-baseline justify-between gap-x-2 text-2xs">
        <span className="text-text-muted">
          {formatBytes(usedBytes)} of {formatBytes(volume.totalBytes)}
        </span>
        <span
          className={cn(
            'tabular-nums',
            tone === 'danger'
              ? 'text-danger'
              : tone === 'warning'
                ? 'text-warning'
                : 'text-text-subtle',
          )}
        >
          {percent}% full
        </span>
      </div>
      <div
        className="h-1.5 w-full overflow-hidden rounded-full bg-surface-sunken"
        role="img"
        aria-label={`${percent}% full, ${formatBytes(volume.freeBytes)} free`}
      >
        <div
          className={cn(
            'h-full rounded-full',
            tone === 'danger'
              ? 'bg-danger'
              : tone === 'warning'
                ? 'bg-warning'
                : 'bg-accent',
          )}
          style={{ width: `${Math.min(percent, 100)}%` }}
        />
      </div>
    </div>
  );
}

/**
 * Installed software, searchable.
 *
 * The agent sends up to 5000 entries, which is far too many to scan, so this is
 * a filtered list rather than a dump. The count is always visible so nobody
 * mistakes a filtered view for the whole picture.
 */
function SoftwarePanel({ software }: { software: AgentInstalledSoftware[] }) {
  const [query, setQuery] = useState('');

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return software;
    return software.filter(
      (item) =>
        item.name.toLowerCase().includes(needle) ||
        item.publisher?.toLowerCase().includes(needle) ||
        item.version?.toLowerCase().includes(needle),
    );
  }, [query, software]);

  const columns: Column<AgentInstalledSoftware>[] = [
    {
      id: 'name',
      header: 'Name',
      cell: (item) => <span className="truncate text-text">{item.name}</span>,
    },
    {
      id: 'version',
      header: 'Version',
      width: 'w-40',
      cell: (item) =>
        item.version ? (
          <span className="font-mono text-xs text-text-muted">{item.version}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'publisher',
      header: 'Publisher',
      hideBelow: 'md',
      cell: (item) => item.publisher ?? <span className="text-text-subtle">—</span>,
    },
    {
      id: 'architecture',
      header: 'Arch',
      hideBelow: 'lg',
      width: 'w-20',
      cell: (item) =>
        item.architecture ? (
          <span className="font-mono text-xs text-text-muted">{item.architecture}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'installedOn',
      header: 'Installed',
      hideBelow: 'lg',
      align: 'right',
      width: 'w-28',
      cell: (item) => (
        <span className="whitespace-nowrap text-xs text-text-subtle">
          {formatDate(item.installedOn)}
        </span>
      ),
    },
  ];

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <Package className="size-4 text-text-subtle" aria-hidden />
            Installed software
          </span>
        }
        description={
          query.trim()
            ? `${formatNumber(filtered.length)} of ${formatNumber(software.length)} matching`
            : `${formatNumber(software.length)} ${software.length === 1 ? 'program' : 'programs'}`
        }
        actions={
          software.length > 0 && (
            <SearchInput
              value={query}
              onChange={setQuery}
              placeholder="Filter software…"
              className="w-56"
              aria-label="Filter installed software"
            />
          )
        }
      />
      <DataTable
        columns={columns}
        rows={filtered}
        rowKey={(item) => `${item.name}-${item.version ?? ''}-${item.publisher ?? ''}`}
        empty={
          <EmptyState
            icon={<Package />}
            title={query.trim() ? 'No matches' : 'No software reported'}
            description={
              query.trim()
                ? 'Nothing installed matches that filter.'
                : 'The agent did not report any installed programs.'
            }
            className="py-8"
          />
        }
      />
    </Card>
  );
}
