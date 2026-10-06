import {
  Activity,
  CircleCheck,
  DatabaseBackup,
  HardDrive,
  RefreshCw,
  ShieldAlert,
  TriangleAlert,
} from 'lucide-react';
import type { ReactNode } from 'react';

import type { OperationalAlert, OperationalStatus } from '@/api/adminTypes';
import { useOperationalStatus } from '@/api/hooks';
import { formatBytes, formatDateTime, formatNumber, formatRelative } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { ErrorState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { Tooltip } from '@/ui/Tooltip';

/**
 * Server health as the API reports it.
 *
 * Every counter here is cumulative since the process started, not a rate and not
 * a total for all time — a restart zeroes them. The uptime is shown alongside so
 * a low number is not mistaken for a quiet week.
 */
export default function OperationsPage() {
  const status = useOperationalStatus();

  if (status.isLoading) return <LoadingBlock label="Reading server status…" />;
  if (status.isError) {
    return <ErrorState error={status.error} onRetry={() => void status.refetch()} />;
  }
  if (!status.data) return null;

  const { metrics, alerts } = status.data;
  const critical = alerts.filter((alert) => alert.severity === 'critical');

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Server status"
        description="Counters, storage and alerts from the running API process."
        actions={
          <Button
            iconLeft={<RefreshCw />}
            loading={status.isFetching}
            onClick={() => void status.refetch()}
          >
            Refresh
          </Button>
        }
      />

      {alerts.length === 0 ? (
        <Alert tone="success" title="Nothing is raising an alert">
          No configured threshold has been crossed since the server started.
        </Alert>
      ) : (
        <Card
          className={
            critical.length > 0
              ? 'border-danger-border bg-danger-soft/30'
              : 'border-warning-border bg-warning-soft/30'
          }
        >
          <CardHeader
            title={`${formatNumber(alerts.length)} ${alerts.length === 1 ? 'alert' : 'alerts'}`}
            description="Raised when a counter crosses its configured threshold."
          />
          <CardBody className="flex flex-col gap-2.5">
            {alerts.map((alert) => (
              <AlertRow key={alert.code} alert={alert} />
            ))}
          </CardBody>
        </Card>
      )}

      <Card>
        <CardHeader
          title="Since the server started"
          description={`Running for ${formatDuration(metrics.uptimeSeconds)}, since ${formatDateTime(metrics.startedAtUtc)}. Restarting resets every counter below.`}
          actions={
            <Badge tone="neutral">{formatRelative(metrics.startedAtUtc)}</Badge>
          }
        />
        <CardBody className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Metric
            label="Failed sign-ins"
            value={metrics.failedLogins}
            hint="Wrong password, unknown email or a disabled account."
            alerted={hasAlert(alerts, 'failed_login_threshold')}
          />
          <Metric
            label="Failed two-step codes"
            value={metrics.failedMfaChallenges}
            hint="Rejected authenticator or recovery codes."
            alerted={hasAlert(alerts, 'failed_mfa_threshold')}
          />
          <Metric
            label="Import rejections"
            value={metrics.importRejections}
            hint="Import commits the server refused."
            alerted={hasAlert(alerts, 'import_rejection_threshold')}
          />
          <Metric
            label="QR failures"
            value={metrics.qrFailures}
            hint="Scans of tokens that could not be resolved."
            alerted={hasAlert(alerts, 'qr_failure_threshold')}
          />
          <Metric
            label="Database contention"
            value={metrics.sqliteContentionEvents}
            hint="SQLite busy or locked errors. A sustained rise means writes are queueing."
            alerted={hasAlert(alerts, 'sqlite_contention_threshold')}
          />
          <Metric
            label="Readiness failures"
            value={metrics.readinessFailures}
            hint="Times the health check reported the server not ready."
          />
        </CardBody>
      </Card>

      <StorageCard storage={status.data.storage} alerts={alerts} />
    </div>
  );
}

function StorageCard({
  storage,
  alerts,
}: {
  storage: OperationalStatus['storage'];
  alerts: OperationalAlert[];
}) {
  if (!storage.configured) {
    return (
      <Card>
        <CardHeader title="Backups and storage" />
        <CardBody>
          <Alert tone="warning" title="No backup path is configured">
            The server is not watching a backup location, so nothing here can tell you
            whether the register is being backed up. This is set in the server's
            configuration, not in the app.
          </Alert>
        </CardBody>
      </Card>
    );
  }

  const backupMissing = hasAlert(alerts, 'backup_missing');
  const backupStale = hasAlert(alerts, 'backup_stale');
  const spaceLow = hasAlert(alerts, 'storage_capacity_low');

  return (
    <Card>
      <CardHeader
        title="Backups and storage"
        description="The backup location the server is watching."
      />
      <CardBody className="grid gap-4 sm:grid-cols-2">
        <div className="flex items-start gap-3">
          <HardDrive
            className={`mt-0.5 size-4 shrink-0 ${spaceLow ? 'text-danger' : 'text-text-subtle'}`}
            aria-hidden
          />
          <div>
            <p className="text-xs text-text-subtle">Free space</p>
            <p
              className={`text-lg font-semibold ${spaceLow ? 'text-danger' : 'text-text'}`}
            >
              {formatBytes(storage.availableFreeSpaceBytes)}
            </p>
            {spaceLow && (
              <p className="mt-0.5 text-xs text-danger">
                Below the configured minimum.
              </p>
            )}
          </div>
        </div>

        <div className="flex items-start gap-3">
          <DatabaseBackup
            className={`mt-0.5 size-4 shrink-0 ${backupMissing || backupStale ? 'text-danger' : 'text-text-subtle'}`}
            aria-hidden
          />
          <div>
            <p className="text-xs text-text-subtle">Latest backup</p>
            {storage.latestBackupAtUtc ? (
              <>
                <p
                  className={`text-lg font-semibold ${backupStale ? 'text-danger' : 'text-text'}`}
                >
                  {formatRelative(storage.latestBackupAtUtc)}
                </p>
                <p className="mt-0.5 text-xs text-text-subtle">
                  {formatDateTime(storage.latestBackupAtUtc)}
                  {storage.latestBackupAgeHours !== null &&
                    ` · ${formatNumber(Math.round(storage.latestBackupAgeHours))}h old`}
                </p>
              </>
            ) : (
              <p className="text-lg font-semibold text-danger">None found</p>
            )}
          </div>
        </div>
      </CardBody>
    </Card>
  );
}

function AlertRow({ alert }: { alert: OperationalAlert }) {
  const critical = alert.severity === 'critical';
  return (
    <div className="flex items-start gap-2.5">
      {critical ? (
        <ShieldAlert className="mt-0.5 size-4 shrink-0 text-danger" aria-hidden />
      ) : (
        <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
      )}
      <div className="min-w-0">
        <p className="text-base text-text">{alert.message}</p>
        <p className="mt-0.5 font-mono text-2xs text-text-subtle">
          {alert.severity} · {alert.code}
        </p>
      </div>
    </div>
  );
}

function Metric({
  label,
  value,
  hint,
  alerted,
}: {
  label: string;
  value: number;
  hint: string;
  alerted?: boolean;
}) {
  const body: ReactNode = (
    <div className="rounded-md border border-border bg-surface-sunken p-3">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs text-text-subtle">{label}</p>
        {alerted ? (
          <TriangleAlert className="size-3.5 shrink-0 text-warning" aria-hidden />
        ) : value === 0 ? (
          <CircleCheck className="size-3.5 shrink-0 text-success" aria-hidden />
        ) : (
          <Activity className="size-3.5 shrink-0 text-text-subtle" aria-hidden />
        )}
      </div>
      <p
        className={`mt-1 text-2xl font-semibold ${alerted ? 'text-warning' : 'text-text'}`}
      >
        {formatNumber(value)}
      </p>
    </div>
  );

  return (
    <Tooltip content={hint}>
      <div>{body}</div>
    </Tooltip>
  );
}

function hasAlert(alerts: OperationalAlert[], code: string): boolean {
  return alerts.some((alert) => alert.code === code);
}

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)} seconds`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) {
    const rest = minutes % 60;
    return rest === 0
      ? `${hours} ${hours === 1 ? 'hour' : 'hours'}`
      : `${hours}h ${rest}m`;
  }
  const days = Math.floor(hours / 24);
  return `${days} days`;
}
