import { useState, type FormEvent } from 'react';

import type { CreatedAgentKeyRecovery, ManagedAgentListItem } from '@/api/agentTypes';
import { useCreateAgentKeyRecovery } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import { downloadBlob } from '@/lib/download';
import { formatDateTime } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { CopyButton } from '@/ui/CopyButton';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';

const DEFAULT_WINDOW_HOURS = 24;

/** `YYYY-MM-DDTHH:mm` in local time, which is what a `datetime-local` expects. */
function localInputValue(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

function hoursFromNow(hours: number): Date {
  return new Date(Date.now() + hours * 60 * 60 * 1000);
}

/**
 * Authorises one machine to replace its signing key.
 *
 * This mints a live credential, so the window defaults to a day rather than the
 * weeks an enrollment token gets: the machine is expected to be in front of
 * someone right now. Requiring a TPM again by default keeps the replacement key
 * at least as well protected as the one it replaces.
 */
export function CreateKeyRecoveryDialog({
  agent,
  onClose,
}: {
  agent: ManagedAgentListItem;
  onClose: () => void;
}) {
  const create = useCreateAgentKeyRecovery(agent.id);
  const [reason, setReason] = useState('');
  const [expiresAt, setExpiresAt] = useState(
    localInputValue(hoursFromNow(DEFAULT_WINDOW_HOURS)),
  );
  const [requireTpm, setRequireTpm] = useState(agent.keyProtection === 'tpm');
  const [created, setCreated] = useState<CreatedAgentKeyRecovery | null>(null);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = reason.trim();
    if (!trimmed || !expiresAt) return;

    create.mutate(
      {
        reason: trimmed,
        expiresAtUtc: new Date(expiresAt).toISOString(),
        requireTpm,
        version: agent.version,
      },
      { onSuccess: setCreated },
    );
  }

  if (created) return <IssuedRecoveryToken created={created} onClose={onClose} />;

  const error = create.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'create-key-recovery-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Authorise key recovery"
      description={agent.hostname}
      dismissible={!create.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={create.isPending}
            disabled={!reason.trim() || !expiresAt}
          >
            Authorise
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This device just changed" role="alert">
            Nothing was authorised. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not authorise recovery" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Alert tone="warning" title="This issues a credential">
          Anyone holding the token can make a machine present itself as{' '}
          {agent.hostname} with a new key. Authorise it when the machine is in front of
          you, and keep the window short.
        </Alert>

        <Field
          label="Why this machine needs to re-key"
          required
          hint="Recorded against the authorisation and in the audit trail."
        >
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              rows={3}
              required
              autoFocus
              placeholder="Motherboard replaced under warranty — TPM cleared"
            />
          )}
        </Field>

        <Field
          label="Usable until"
          required
          hint="Shorter is safer. The machine only needs long enough to check in once."
          error={apiError?.fieldErrors('expiresAtUtc')[0] ?? null}
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              type="datetime-local"
              value={expiresAt}
              min={localInputValue(new Date())}
              onChange={(event) => setExpiresAt(event.target.value)}
              required
            />
          )}
        </Field>

        <Checkbox
          checked={requireTpm}
          onCheckedChange={setRequireTpm}
          label="Require the new key to be TPM-held"
          description={
            agent.keyProtection === 'tpm'
              ? 'This machine holds its current key in the TPM. Turning this off would let it come back with a weaker key than it has now.'
              : 'This machine currently holds its key in software. Requiring a TPM will refuse the recovery unless it now has one.'
          }
        />
      </form>
    </Dialog>
  );
}

/**
 * The token, shown once.
 *
 * The server keeps only a hash, so this dialog is the sole opportunity to
 * capture it. Dismissal is one explicit button — a backdrop click must not be
 * able to lose it.
 */
function IssuedRecoveryToken({
  created,
  onClose,
}: {
  created: CreatedAgentKeyRecovery;
  onClose: () => void;
}) {
  return (
    <Dialog
      open
      onOpenChange={() => {
        /* Closing is only via the button below. */
      }}
      title="Key recovery authorised"
      size="md"
      dismissible={false}
      footer={
        <Button variant="primary" onClick={onClose}>
          I have copied it
        </Button>
      }
    >
      <div className="flex flex-col gap-4">
        <Alert tone="warning" title="Copy this now — it is not shown again">
          The server keeps only a hash. If you lose it, revoke this authorisation and
          create another.
        </Alert>

        <div>
          <p className="mb-1.5 text-sm font-medium text-text">Recovery token</p>
          <div className="flex items-start gap-2">
            <code className="min-w-0 flex-1 select-all break-all rounded-md border border-border bg-surface-sunken p-3 font-mono text-xs leading-relaxed text-text">
              {created.recoveryToken}
            </code>
            <CopyButton value={created.recoveryToken} label="Copy recovery token" />
          </div>
        </div>

        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
          <dt className="text-text-subtle">Usable until</dt>
          <dd className="text-text">{formatDateTime(created.expiresAtUtc)}</dd>
          <dt className="text-text-subtle">TPM</dt>
          <dd className="text-text">
            {created.requireTpm ? 'Required for the new key' : 'Not required'}
          </dd>
        </dl>

        <div className="flex justify-start">
          <Button
            size="sm"
            onClick={() =>
              downloadBlob(
                new Blob(
                  [
                    `Inventory agent key recovery token\n`,
                    `Agent: ${created.agentId}\n`,
                    `Authorisation: ${created.authorizationId}\n`,
                    `Usable until: ${created.expiresAtUtc}\n`,
                    `TPM required: ${created.requireTpm ? 'yes' : 'no'}\n\n`,
                    `${created.recoveryToken}\n`,
                  ],
                  { type: 'text/plain' },
                ),
                `key-recovery-${created.authorizationId}.txt`,
              )
            }
          >
            Download as a file
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
