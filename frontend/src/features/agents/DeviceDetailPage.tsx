import {
  EyeOff,
  Link2, Link2Off,
  Plus,
  ShieldOff,
  ShieldQuestion,
  Undo2,
} from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';

import { useAgent } from '@/api/hooks';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime, formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader, DataItem } from '@/ui/Card';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { Tooltip } from '@/ui/Tooltip';

import { AgentTriageDialog, type AgentTriageAction } from './AgentTriageDialog';
import { CONNECTIVITY_LABELS, CONNECTIVITY_TONES, MATCH_STATE_HINTS } from './agentLabels';
import { CreateAssetFromAgentDialog } from './CreateAssetFromAgentDialog';
import { DeviceSnapshotPanels, SnapshotFreshness } from './DeviceSnapshotPanels';
import { KeyRecoveryPanel } from './KeyRecoveryPanel';
import { LinkAgentDialog } from './LinkAgentDialog';
import { RevokeAgentDialog } from './RevokeAgentDialog';

export default function DeviceDetailPage() {
  const { agentId } = useParams<{ agentId: string }>();
  const { can } = useSession();
  const [linkOpen, setLinkOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [revokeOpen, setRevokeOpen] = useState(false);
  const [triage, setTriage] = useState<AgentTriageAction | null>(null);

  const detail = useAgent(agentId);

  if (detail.isLoading) return <LoadingBlock label="Loading device…" />;
  if (detail.isError) {
    return <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />;
  }
  if (!detail.data) return null;

  const { agent, latestSnapshot } = detail.data;
  const canManage = can(Permission.AgentsManage);
  const isRevoked = agent.status === 'revoked';

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        breadcrumbs={[{ label: 'Devices', to: '/devices' }, { label: agent.hostname }]}
        title={<span className="font-mono tracking-tight">{agent.hostname}</span>}
        description={
          agent.linkedAssetTag
            ? `Linked to asset ${agent.linkedAssetTag}`
            : 'Not linked to any asset on the register'
        }
        meta={
          <>
            {isRevoked ? (
              <Badge tone="danger" dot>
                Revoked
              </Badge>
            ) : (
              <Badge tone="success" dot>
                Active
              </Badge>
            )}
            {agent.keyProtection === 'tpm' ? (
              <Badge tone="success">TPM-backed key</Badge>
            ) : (
              <Badge tone="warning">Software key</Badge>
            )}
            <Tooltip content={MATCH_STATE_HINTS[agent.matchState]}>
              <span className="inline-block">
                <Badge tone={CONNECTIVITY_TONES[agent.connectivityStatus]} dot>
                  {CONNECTIVITY_LABELS[agent.connectivityStatus]}
                </Badge>
              </span>
            </Tooltip>
            {agent.lastCheckInAtUtc && (
              <SnapshotFreshness
                collectedAtUtc={agent.lastCheckInAtUtc}
                agentVersion={agent.lastAgentVersion}
              />
            )}
          </>
        }
        actions={
          canManage && !isRevoked ? (
            <>
              {agent.linkedAssetId ? (
                <>
                  <Button iconLeft={<Link2 />} onClick={() => setLinkOpen(true)}>
                    Change linked asset
                  </Button>
                  <Button iconLeft={<Link2Off />} onClick={() => setTriage('unlink')}>
                    Unlink
                  </Button>
                </>
              ) : (
                <>
                  <Button
                    variant="primary"
                    iconLeft={<Plus />}
                    onClick={() => setCreateOpen(true)}
                  >
                    Create asset
                  </Button>
                  <Button iconLeft={<Link2 />} onClick={() => setLinkOpen(true)}>
                    Link to existing
                  </Button>
                  {agent.matchState === 'dismissed' ? (
                    <Button iconLeft={<Undo2 />} onClick={() => setTriage('restore')}>
                      Put back in review
                    </Button>
                  ) : (
                    <Button iconLeft={<EyeOff />} onClick={() => setTriage('dismiss')}>
                      Dismiss
                    </Button>
                  )}
                </>
              )}
              <Button
                variant="danger-subtle"
                iconLeft={<ShieldOff />}
                onClick={() => setRevokeOpen(true)}
              >
                Revoke
              </Button>
            </>
          ) : undefined
        }
      />

      {isRevoked && (
        <Alert tone="danger" title="This agent has been revoked">
          Its key is no longer trusted and it cannot check in. Everything below is a
          historical record from {formatDateTime(agent.lastCheckInAtUtc)}. To manage this
          machine again, install and enrol the agent afresh.
        </Alert>
      )}

      {!agent.linkedAssetId && !isRevoked && agent.matchState !== 'dismissed' && (
        <Alert
          tone={agent.matchState === 'conflict' ? 'danger' : 'warning'}
          title={
            agent.matchState === 'conflict'
              ? 'More than one asset could be this machine'
              : 'Not linked to an asset'
          }
        >
          {agent.matchReason ? (
            <>
              {agent.matchReason} {MATCH_STATE_HINTS[agent.matchState]}
            </>
          ) : (
            <>
              This machine is reporting in, but the register does not know what it is.
              Link it to an asset so its hardware and software show up alongside the rest
              of that asset&rsquo;s record.
            </>
          )}
        </Alert>
      )}

      {agent.matchState === 'dismissed' && !isRevoked && (
        <Alert tone="info" title="Dismissed from review">
          Someone decided this machine does not need an asset. It is still enrolled and
          still reporting — put it back in review if that was wrong.
        </Alert>
      )}

      <Card>
        <CardHeader
          title="Enrolment"
          description="Identity and trust, established when the agent first enrolled."
        />
        <CardBody>
          <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
            <DataItem label="Device ID" mono>
              {agent.deviceId}
            </DataItem>
            <DataItem label="Serial number" mono>
              {agent.serialNumber}
            </DataItem>
            <DataItem label="BIOS UUID" mono>
              {agent.biosUuid}
            </DataItem>
            <DataItem label="Enrolled">{formatDateTime(agent.enrolledAtUtc)}</DataItem>
            <DataItem label="Last check-in">
              {formatDateTime(agent.lastCheckInAtUtc)}
            </DataItem>
            <DataItem label="Linked asset">
              {agent.linkedAssetId ? (
                <Link
                  to={`/assets/${agent.linkedAssetId}`}
                  className="rounded-xs font-mono text-sm text-accent hover:underline"
                >
                  {agent.linkedAssetTag}
                </Link>
              ) : null}
            </DataItem>
            <DataItem
              label={
                <Tooltip content="Counts up with every accepted snapshot. Stops rising when the machine stops reporting; jumps back only if the agent was re-enrolled.">
                  <span className="cursor-help border-b border-dotted border-text-subtle">
                    Check-in sequence
                  </span>
                </Tooltip>
              }
              mono
            >
              {formatNumber(agent.lastSequence)}
            </DataItem>
          </dl>
        </CardBody>
      </Card>

      <KeyRecoveryPanel agent={agent} />

      {latestSnapshot ? (
        <DeviceSnapshotPanels snapshot={latestSnapshot} />
      ) : (
        <EmptyState
          icon={<ShieldQuestion />}
          title="Enrolled, but never checked in"
          description="The agent registered itself but has not yet sent a snapshot. Hardware and software details appear after the machine's first check-in."
        />
      )}

      {linkOpen && <LinkAgentDialog onClose={() => setLinkOpen(false)} agent={agent} />}
      {createOpen && (
        <CreateAssetFromAgentDialog
          agent={agent}
          snapshot={latestSnapshot}
          onClose={() => setCreateOpen(false)}
        />
      )}
      {triage && (
        <AgentTriageDialog
          action={triage}
          agent={agent}
          onClose={() => setTriage(null)}
        />
      )}
      {revokeOpen && (
        <RevokeAgentDialog onClose={() => setRevokeOpen(false)} agent={agent} />
      )}
    </div>
  );
}
