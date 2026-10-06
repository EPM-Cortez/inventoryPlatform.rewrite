import { KeyRound } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import type { CreatedAgentEnrollmentProfile } from '@/api/agentTypes';
import { useCreateAgentEnrollmentProfile } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import { downloadBlob } from '@/lib/download';
import { formatDateTime } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { CopyButton } from '@/ui/CopyButton';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';

/** The server default; it is configurable, so the API is still the authority. */
const DEFAULT_MAX_DAYS = 30;
const DEFAULT_DAYS = 14;

export interface CreateEnrollmentProfileDialogProps {
  onClose: () => void;
}

export function CreateEnrollmentProfileDialog({
  onClose,
}: CreateEnrollmentProfileDialogProps) {
  const [created, setCreated] = useState<CreatedAgentEnrollmentProfile | null>(null);

  // Once the token exists it must be shown before anything else can happen —
  // there is no second chance to read it.
  return created ? (
    <IssuedToken created={created} onClose={onClose} />
  ) : (
    <ProfileForm onCreated={setCreated} onClose={onClose} />
  );
}

function ProfileForm({
  onCreated,
  onClose,
}: {
  onCreated: (created: CreatedAgentEnrollmentProfile) => void;
  onClose: () => void;
}) {
  const [name, setName] = useState('');
  const [expiresOn, setExpiresOn] = useState(() => isoDate(daysFromNow(DEFAULT_DAYS)));
  const [maxEnrollments, setMaxEnrollments] = useState('1');
  const [requireTpm, setRequireTpm] = useState(true);
  const [allowedHostnameSuffix, setAllowedHostnameSuffix] = useState('');

  const create = useCreateAgentEnrollmentProfile();

  const count = Number(maxEnrollments);
  const countValid = Number.isInteger(count) && count >= 1 && count <= 5000;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    create.mutate(
      {
        name: name.trim(),
        // The API requires UTC and rejects anything else; end-of-day local keeps
        // "expires on the 14th" meaning the whole of the 14th here.
        expiresAtUtc: new Date(`${expiresOn}T23:59:59`).toISOString(),
        maxEnrollments: count,
        requireTpm,
        allowedHostnameSuffix: allowedHostnameSuffix.trim() || null,
      },
      { onSuccess: onCreated },
    );
  }

  const error = create.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'enrollment-profile-form';

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="New enrollment token"
      size="sm"
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
            iconLeft={<KeyRound />}
            loading={create.isPending}
            disabled={!name.trim() || !countValid || !expiresOn}
          >
            Create token
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.slug === 'agent-enrollment-profile-expiry-invalid' ? (
          <Alert tone="danger" title="That expiry is not allowed" role="alert">
            {apiError.message}
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not create the token" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Field label="Name" required hint="What this batch of machines is, for the log.">
          {(controlProps) => (
            <Input
              {...controlProps}
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={150}
              required
              autoFocus
              placeholder="ICT Suite 1 rebuild"
            />
          )}
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label="Expires"
            required
            hint={`Usually within ${DEFAULT_MAX_DAYS} days.`}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                value={expiresOn}
                min={isoDate(daysFromNow(1))}
                onChange={(event) => setExpiresOn(event.target.value)}
                required
              />
            )}
          </Field>

          <Field
            label="Machine limit"
            required
            hint="1 to 5000."
            error={
              maxEnrollments !== '' && !countValid
                ? 'Enter a whole number between 1 and 5000.'
                : undefined
            }
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                type="number"
                min={1}
                max={5000}
                step={1}
                value={maxEnrollments}
                onChange={(event) => setMaxEnrollments(event.target.value)}
                required
              />
            )}
          </Field>
        </div>

        <Field
          label="Hostname must end with"
          hint="Optional. Blocks the token being used on a machine outside this naming."
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              value={allowedHostnameSuffix}
              onChange={(event) => setAllowedHostnameSuffix(event.target.value)}
              maxLength={150}
              mono
              placeholder=".school.internal"
            />
          )}
        </Field>

        <div className="border-t border-border pt-4">
          <Checkbox
            checked={requireTpm}
            onCheckedChange={setRequireTpm}
            label="Require a TPM"
            description="The machine keeps its signing key in hardware, where it cannot be copied off. Turn this off only for machines that have no TPM."
          />
          {!requireTpm && (
            <Alert tone="warning" title="Software keys can be copied" className="mt-3">
              A machine enrolling with this token may keep its identity key in a file.
              Anyone who takes that file can impersonate the machine to this server.
            </Alert>
          )}
        </div>
      </form>
    </Dialog>
  );
}

/**
 * The token, shown once.
 *
 * The server stores only a hash, so this dialog is the only opportunity to
 * capture it. Dismissal is deliberately a single explicit button rather than a
 * backdrop click.
 */
function IssuedToken({
  created,
  onClose,
}: {
  created: CreatedAgentEnrollmentProfile;
  onClose: () => void;
}) {
  return (
    <Dialog
      open
      onOpenChange={() => {
        /* Closing is only via the button below, so the token is not lost to a
           stray click on the backdrop. */
      }}
      title="Enrollment token created"
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
          The server keeps only a hash of this token. If you lose it, revoke the token
          and create another.
        </Alert>

        <div>
          <p className="mb-1.5 text-sm font-medium text-text">
            Token for {created.profile.name}
          </p>
          <div className="flex items-start gap-2">
            <code className="min-w-0 flex-1 select-all break-all rounded-md border border-border bg-surface-sunken p-3 font-mono text-xs leading-relaxed text-text">
              {created.enrollmentToken}
            </code>
            <CopyButton value={created.enrollmentToken} label="Copy enrollment token" />
          </div>
        </div>

        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
          <dt className="text-text-subtle">Expires</dt>
          <dd className="text-text">{formatDateTime(created.profile.expiresAtUtc)}</dd>
          <dt className="text-text-subtle">Machine limit</dt>
          <dd className="text-text">{created.profile.maxEnrollments}</dd>
          <dt className="text-text-subtle">TPM</dt>
          <dd className="text-text">
            {created.profile.requireTpm ? 'Required' : 'Not required'}
          </dd>
          {created.profile.allowedHostnameSuffix && (
            <>
              <dt className="text-text-subtle">Hostname</dt>
              <dd className="font-mono text-text">
                *{created.profile.allowedHostnameSuffix}
              </dd>
            </>
          )}
        </dl>

        <div className="flex justify-start">
          <Button
            size="sm"
            onClick={() =>
              downloadBlob(
                new Blob(
                  [
                    `Inventory agent enrollment token\n`,
                    `Name: ${created.profile.name}\n`,
                    `Expires: ${created.profile.expiresAtUtc}\n`,
                    `Machine limit: ${created.profile.maxEnrollments}\n`,
                    `TPM required: ${created.profile.requireTpm ? 'yes' : 'no'}\n\n`,
                    `${created.enrollmentToken}\n`,
                  ],
                  { type: 'text/plain' },
                ),
                `enrollment-token-${created.profile.name.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.txt`,
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

function daysFromNow(days: number): Date {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date;
}

/** `YYYY-MM-DD` in local time, which is what a `date` input expects. */
function isoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
