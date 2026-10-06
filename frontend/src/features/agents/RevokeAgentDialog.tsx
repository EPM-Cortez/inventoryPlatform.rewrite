import { useState, type FormEvent } from 'react';

import { useRevokeAgent } from '@/api/hooks';
import type { ManagedAgentListItem } from '@/api/agentTypes';
import { ApiError, describeError } from '@/api/problem';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface RevokeAgentDialogProps {
  onClose: () => void;
  agent: ManagedAgentListItem;
}

/**
 * Withdraws trust from a machine's signing key.
 *
 * The agent can no longer check in, and there is no un-revoke — the machine must
 * enrol again with a fresh token and key. That makes this closer to disposal than
 * to disabling, so it asks for the hostname to be typed.
 */
export function RevokeAgentDialog({ onClose, agent }: RevokeAgentDialogProps) {
  const [reason, setReason] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const toast = useToast();
  const revoke = useRevokeAgent();

  const hostnameMatches =
    confirmation.trim().toLowerCase() === agent.hostname.trim().toLowerCase();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim() || !hostnameMatches) return;

    revoke.mutate(
      { agentId: agent.id, body: { reason: reason.trim(), version: agent.version } },
      {
        onSuccess: () => {
          toast.success('Device revoked', agent.hostname);
          onClose();
        },
      },
    );
  }

  const error = revoke.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'revoke-agent-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Revoke this device"
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
            disabled={!reason.trim() || !hostnameMatches}
          >
            Revoke device
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This device just changed" role="alert">
            Nothing was revoked. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not revoke" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Alert tone="danger" title="This cannot be undone">
          The machine&rsquo;s key stops being trusted immediately and it can no longer
          report in. To manage it again someone must reinstall and re-enrol the agent on
          the device itself. Its existing snapshots are kept as a historical record.
        </Alert>

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
              placeholder="Machine decommissioned and wiped"
            />
          )}
        </Field>

        <Field
          label={`Type ${agent.hostname} to confirm`}
          required
          error={
            confirmation.length > 0 && !hostnameMatches
              ? 'That does not match the hostname.'
              : null
          }
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              mono
              autoComplete="off"
              placeholder={agent.hostname}
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
