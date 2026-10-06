import { ExternalLink, MonitorOff, ShieldQuestion } from 'lucide-react';
import { Link } from 'react-router';

import { useAssetDeviceInventory } from '@/api/hooks';
import { ApiError } from '@/api/problem';
import type { Asset } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader, DataItem } from '@/ui/Card';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { LoadingBlock } from '@/ui/Spinner';

import { CuratedVsObserved } from './CuratedVsObserved';
import { DeviceSnapshotPanels, SnapshotFreshness } from './DeviceSnapshotPanels';

/**
 * What the Windows agent reports for this asset.
 *
 * Most assets in a school are not managed machines — projectors, furniture,
 * musical instruments — so "no agent" is an ordinary state, not a problem, and
 * the empty state says so without sounding like a failure.
 */
export function AssetDeviceTab({ asset }: { asset: Asset }) {
  const { can } = useSession();
  const inventory = useAssetDeviceInventory(asset.id);

  if (inventory.isLoading) return <LoadingBlock label="Loading device inventory…" />;

  // A 404 means no agent is linked, which is expected for most assets.
  if (inventory.error instanceof ApiError && inventory.error.isNotFound) {
    return (
      <EmptyState
        icon={<MonitorOff />}
        title="No device agent is linked"
        description={
          can(Permission.AgentsManage)
            ? 'Nothing has reported in for this asset. If it is a managed Windows machine, link its agent from the Devices page.'
            : 'Nothing has reported in for this asset. Only managed Windows machines run the agent.'
        }
        action={
          can(Permission.AgentsRead) && (
            <Button asChild>
              <Link to="/devices">
                Go to Devices <ExternalLink className="ml-1.5 size-3.5" />
              </Link>
            </Button>
          )
        }
      />
    );
  }

  if (inventory.isError) {
    return <ErrorState error={inventory.error} onRetry={() => void inventory.refetch()} />;
  }
  if (!inventory.data) return null;

  const { agent, latestSnapshot } = inventory.data;

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader
          title="Device agent"
          description="Collected automatically from the machine. Nothing here can be edited."
          actions={
            can(Permission.AgentsRead) && (
              <Button size="sm" asChild>
                <Link to={`/devices/${agent.id}`}>
                  Agent details <ExternalLink className="ml-1.5 size-3.5" />
                </Link>
              </Button>
            )
          }
        />
        <CardBody>
          <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2 lg:grid-cols-4">
            <DataItem label="Hostname" mono>
              {agent.hostname}
            </DataItem>
            <DataItem label="Status">
              {agent.status === 'revoked' ? (
                <Badge tone="danger">Revoked</Badge>
              ) : (
                <Badge tone="success" dot>
                  Active
                </Badge>
              )}
            </DataItem>
            <DataItem label="Key protection">
              {agent.keyProtection === 'tpm' ? (
                <Badge tone="success">TPM-backed</Badge>
              ) : (
                <Badge tone="warning">Software key</Badge>
              )}
            </DataItem>
            <DataItem label="Last check-in">
              {agent.lastCheckInAtUtc ? (
                <SnapshotFreshness
                  collectedAtUtc={agent.lastCheckInAtUtc}
                  agentVersion={agent.lastAgentVersion}
                />
              ) : null}
            </DataItem>
          </dl>
        </CardBody>
      </Card>

      {latestSnapshot && <CuratedVsObserved asset={asset} snapshot={latestSnapshot} />}

      {agent.status === 'revoked' && (
        <Alert tone="danger" title="This agent has been revoked">
          It can no longer check in, so everything below is a historical record from{' '}
          {formatDateTime(agent.lastCheckInAtUtc)} and will not change.
        </Alert>
      )}

      {latestSnapshot ? (
        <DeviceSnapshotPanels snapshot={latestSnapshot} />
      ) : (
        <EmptyState
          icon={<ShieldQuestion />}
          title="Enrolled, but never checked in"
          description="The agent registered itself but has not yet sent a snapshot. It will appear here after the machine's first check-in."
        />
      )}
    </div>
  );
}
