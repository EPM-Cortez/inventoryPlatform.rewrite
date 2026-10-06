import { KeyRound, ShieldPlus } from 'lucide-react';
import { useState } from 'react';

import type {
  AgentKeyRecoveryAuthorization,
  AgentKeyRecoveryStatus,
  ManagedAgentListItem,
} from '@/api/agentTypes';
import { useAgentKeyRecovery } from '@/api/hooks';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime, formatRelative } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge, type BadgeTone } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { LoadingBlock } from '@/ui/Spinner';
import { Tooltip } from '@/ui/Tooltip';

import { CreateKeyRecoveryDialog } from './CreateKeyRecoveryDialog';
import { RevokeKeyRecoveryDialog } from './RevokeKeyRecoveryDialog';

const STATUS_LABELS: Record<AgentKeyRecoveryStatus, string> = {
  pending: 'Awaiting the machine',
  consumed: 'Used',
  expired: 'Expired unused',
  revoked: 'Revoked',
};

const STATUS_TONES: Record<AgentKeyRecoveryStatus, BadgeTone> = {
  // Amber rather than green: an unused authorisation is a live credential, and
  // the wanted end state is `consumed`, not `pending`.
  pending: 'warning',
  consumed: 'success',
  expired: 'neutral',
  revoked: 'neutral',
};

const STATUS_HINTS: Record<AgentKeyRecoveryStatus, string> = {
  pending: 'Issued and not yet used. Revoke it if the machine no longer needs it.',
  consumed: 'The machine used it to replace its key. It cannot be used again.',
  expired: 'The window closed before the machine used it.',
  revoked: 'Withdrawn by hand before it could be used.',
};

/**
 * Key-recovery authorisations for one machine.
 *
 * A recovery lets a machine replace its signing key without enrolling again —
 * for a motherboard swap or a cleared TPM, where the hardware is still the same
 * asset but can no longer prove it is. That is a credential, not a record edit,
 * so it sits behind its own permission and every authorisation stays listed with
 * who approved it and why.
 *
 * The list never carries the token, its hash, or a key thumbprint. What is
 * auditable is that someone authorised a re-key, not the material behind it.
 */
export function KeyRecoveryPanel({ agent }: { agent: ManagedAgentListItem }) {
  const { can } = useSession();
  const [includeClosed, setIncludeClosed] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [revoking, setRevoking] = useState<AgentKeyRecoveryAuthorization | null>(null);

  const canRecover = can(Permission.AgentsRecover);
  const recoveries = useAgentKeyRecovery(agent.id, includeClosed, canRecover);

  // Not a permission the UI should apologise for: without it there is nothing
  // useful to show, so the panel simply is not there.
  if (!canRecover) return null;

  const items = recoveries.data?.items ?? [];
  const isRevoked = agent.status === 'revoked';

  return (
    <Card>
      <CardHeader
        title="Key recovery"
        description="Lets this machine replace its signing key without enrolling again."
        actions={
          !isRevoked && (
            <Button onClick={() => setCreateOpen(true)}>
              <ShieldPlus className="mr-1.5 size-4" />
              Authorise recovery
            </Button>
          )
        }
      />
      <CardBody>
        {isRevoked && (
          <Alert tone="warning" title="This agent is revoked" className="mb-4">
            A revoked machine cannot recover a key — there is no trusted identity left
            to recover. It has to install and enrol the agent again.
          </Alert>
        )}

        <div className="mb-3 flex items-center justify-between gap-3">
          <Checkbox
            checked={includeClosed}
            onCheckedChange={setIncludeClosed}
            label="Show used, expired and revoked"
          />
          {items.length > 0 && (
            <p className="text-2xs text-text-subtle">
              Tokens are never shown again after they are created.
            </p>
          )}
        </div>

        {recoveries.isError ? (
          <ErrorState
            error={recoveries.error}
            onRetry={() => void recoveries.refetch()}
          />
        ) : recoveries.isLoading ? (
          <LoadingBlock label="Loading authorisations…" />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<KeyRound />}
            title={
              includeClosed
                ? 'No key recoveries have been authorised'
                : 'Nothing outstanding'
            }
            description={
              includeClosed
                ? 'Nobody has authorised this machine to replace its signing key.'
                : 'No authorisation is waiting to be used. Tick the box above to see closed ones.'
            }
          />
        ) : (
          <ul className="flex flex-col gap-2">
            {items.map((item) => (
              <li
                key={item.authorizationId}
                className="flex flex-wrap items-start justify-between gap-3 rounded-md border border-border p-3"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <Tooltip content={STATUS_HINTS[item.status]}>
                      <span className="inline-block">
                        <Badge tone={STATUS_TONES[item.status]} dot>
                          {STATUS_LABELS[item.status]}
                        </Badge>
                      </span>
                    </Tooltip>
                    {item.requireTpm && (
                      <Tooltip content="The replacement key must be held in the machine's TPM, as the original was.">
                        <span className="inline-block">
                          <Badge tone="neutral">TPM required</Badge>
                        </span>
                      </Tooltip>
                    )}
                  </div>

                  <p className="mt-1.5 text-sm text-text">{item.reason}</p>

                  <p className="mt-1 text-2xs text-text-subtle">
                    Authorised by {item.createdByDisplayName}{' '}
                    {formatRelative(item.createdAtUtc)}
                    {item.status === 'pending' && (
                      <> · expires {formatDateTime(item.expiresAtUtc)}</>
                    )}
                    {item.consumedAtUtc && (
                      <> · used {formatRelative(item.consumedAtUtc)}</>
                    )}
                    {item.revokedAtUtc && (
                      <>
                        {' '}
                        · revoked {formatRelative(item.revokedAtUtc)}
                        {item.revokedReason && <> — {item.revokedReason}</>}
                      </>
                    )}
                  </p>
                </div>

                {/* Only a pending authorisation can be acted on; the rest are
                    history, and offering revoke on them would promise something
                    the server refuses. */}
                {item.status === 'pending' && (
                  <Button size="sm" onClick={() => setRevoking(item)}>
                    Revoke
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </CardBody>

      {createOpen && (
        <CreateKeyRecoveryDialog agent={agent} onClose={() => setCreateOpen(false)} />
      )}
      {revoking && (
        <RevokeKeyRecoveryDialog
          agent={agent}
          authorization={revoking}
          onClose={() => setRevoking(null)}
        />
      )}
    </Card>
  );
}
