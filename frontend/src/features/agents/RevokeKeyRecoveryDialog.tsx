import { useState, type FormEvent } from 'react';

import type {
  AgentKeyRecoveryAuthorization,
  ManagedAgentListItem,
} from '@/api/agentTypes';
import { useRevokeAgentKeyRecovery } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

/**
 * Withdraws an outstanding key-recovery authorisation.
 *
 * Unlike revoking the agent itself this costs nothing — the machine keeps its
 * current key and carries on reporting; only the unused token stops working. It
 * is the right move whenever a token has gone astray or is simply no longer
 * needed, so the dialog is deliberately light.
 */
export function RevokeKeyRecoveryDialog({
  agent,
  authorization,
  onClose,
}: {
  agent: ManagedAgentListItem;
  authorization: AgentKeyRecoveryAuthorization;
  onClose: () => void;
}) {
  const [reason, setReason] = useState('');
  const toast = useToast();
  const revoke = useRevokeAgentKeyRecovery(agent.id);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = reason.trim();
    if (!trimmed) return;

    revoke.mutate(
      {
        authorizationId: authorization.authorizationId,
        body: { reason: trimmed, version: authorization.version },
      },
      {
        onSuccess: () => {
          toast.success('Key recovery revoked', agent.hostname);
          onClose();
        },
      },
    );
  }

  const error = revoke.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'revoke-key-recovery-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Revoke this authorisation"
      description={agent.hostname}
      size="sm"
      dismissible={!revoke.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={revoke.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="danger"
            loading={revoke.isPending}
            disabled={!reason.trim()}
          >
            Revoke
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This authorisation just changed" role="alert">
            It may already have been used or revoked. Close this dialog and reopen the
            list to see where it stands.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not revoke it" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <p className="text-sm text-text-muted">
          The token stops working immediately. The machine keeps its current key and
          carries on reporting — only the unused authorisation is withdrawn.
        </p>

        <div className="rounded-md border border-border bg-surface-sunken/60 p-3 text-sm">
          <p className="text-text">{authorization.reason}</p>
          <p className="mt-1 text-2xs text-text-subtle">
            Authorised by {authorization.createdByDisplayName}, usable until{' '}
            {formatDateTime(authorization.expiresAtUtc)}
          </p>
        </div>

        <Field label="Reason" required hint="Recorded in the audit trail.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              rows={3}
              required
              autoFocus
              placeholder="Machine was repaired without needing to re-key"
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
