import { KeyRound, RefreshCw, ShieldCheck, Smartphone } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import {
  useBeginMfaEnrollment,
  useConfirmMfaEnrollment,
  useMfaStatus,
  useRegenerateRecoveryCodes,
} from '@/api/hooks';
import type { MfaEnrollment, MfaRecoveryCodes } from '@/api/adminTypes';
import { ApiError, describeError } from '@/api/problem';
import { useSession } from '@/auth/session-context';
import { formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { CopyButton } from '@/ui/CopyButton';
import { ErrorState } from '@/ui/EmptyState';
import { Field, Input } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { QrCode } from '@/ui/QrCode';
import { LoadingBlock } from '@/ui/Spinner';
import { useToast } from '@/ui/Toast';

import { PasswordInput } from './PasswordInput';
import { RecoveryCodeList } from './RecoveryCodeList';

/**
 * Self-service account security.
 *
 * Enrollment is deliberately three visible steps — prove it's you, scan, confirm —
 * because a half-finished enrollment that silently "worked" would lock someone out
 * of their own account at the next sign-in.
 */
export default function SecurityPage() {
  const { user, mfaEnrollmentRequired } = useSession();
  const status = useMfaStatus();

  const [enrollment, setEnrollment] = useState<MfaEnrollment | null>(null);
  const [freshCodes, setFreshCodes] = useState<MfaRecoveryCodes | null>(null);

  if (status.isLoading) return <LoadingBlock label="Loading security settings…" />;
  if (status.isError) {
    return <ErrorState error={status.error} onRetry={() => void status.refetch()} />;
  }
  if (!status.data) return null;

  const mfa = status.data;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-5">
      <PageHeader
        title="Security"
        description="Two-step verification and recovery codes for your own account."
        actions={
          <Button asChild>
            <Link to="/change-password">
              <KeyRound className="mr-1.5 size-4" />
              Change password
            </Link>
          </Button>
        }
      />

      {/* The server refuses every permission check while this holds, so the rest
          of the app is genuinely unreachable — not merely restricted. */}
      {mfaEnrollmentRequired && (
        <Alert tone="warning" title="Set this up to unlock the rest of the app">
          Your role requires two-step verification. Until you finish setting it up
          below, every other page will refuse to load — this screen and changing your
          password are all you can reach.
        </Alert>
      )}

      {freshCodes && (
        <Card>
          <CardHeader title="Your recovery codes" />
          <CardBody>
            <RecoveryCodeList
              codes={freshCodes.recoveryCodes}
              accountName={user?.displayName ?? 'account'}
            />
            <div className="mt-4 flex justify-end">
              <Button variant="primary" onClick={() => setFreshCodes(null)}>
                I have saved them
              </Button>
            </div>
          </CardBody>
        </Card>
      )}

      <Card>
        <CardHeader
          title="Two-step verification"
          description="An authenticator app generates a code that changes every 30 seconds."
          actions={
            mfa.enabled ? (
              <Badge tone="success" dot>
                On
              </Badge>
            ) : (
              <Badge tone="warning" dot>
                Off
              </Badge>
            )
          }
        />
        <CardBody>
          {mfa.enabled ? (
            <div className="flex flex-col gap-3">
              <p className="flex items-center gap-2 text-base text-text">
                <ShieldCheck className="size-4 text-success" aria-hidden />
                Your account asks for an authenticator code when you sign in.
              </p>
              <p className="text-sm text-text-muted">
                To move to a different phone, ask an administrator to reset two-step
                verification for you, then set it up again here.
              </p>
            </div>
          ) : enrollment ? (
            <ConfirmEnrollment
              enrollment={enrollment}
              onCancel={() => setEnrollment(null)}
              onConfirmed={(codes) => {
                setEnrollment(null);
                setFreshCodes(codes);
              }}
            />
          ) : (
            <BeginEnrollment onStarted={setEnrollment} />
          )}
        </CardBody>
      </Card>

      {mfa.enabled && (
        <Card>
          <CardHeader
            title="Recovery codes"
            description="Single-use codes that get you in if you lose your authenticator."
            actions={
              <Badge tone={mfa.recoveryCodesRemaining > 2 ? 'neutral' : 'warning'}>
                {formatNumber(mfa.recoveryCodesRemaining)} left
              </Badge>
            }
          />
          <CardBody className="flex flex-col gap-3">
            {mfa.recoveryCodesRemaining === 0 ? (
              <Alert tone="danger" title="You have no recovery codes left">
                If you lose your authenticator you will not be able to sign in without an
                administrator resetting your account. Generate a new set now.
              </Alert>
            ) : mfa.recoveryCodesRemaining <= 2 ? (
              <Alert tone="warning" title="You are running low on recovery codes">
                Generate a fresh set so you are not locked out.
              </Alert>
            ) : null}
            <RegenerateRecoveryCodes onGenerated={setFreshCodes} />
          </CardBody>
        </Card>
      )}
    </div>
  );
}

function BeginEnrollment({
  onStarted,
}: {
  onStarted: (enrollment: MfaEnrollment) => void;
}) {
  const [password, setPassword] = useState('');
  const begin = useBeginMfaEnrollment();

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        begin.mutate({ currentPassword: password }, { onSuccess: onStarted });
      }}
    >
      {begin.error && (
        <Alert tone="danger" title="Could not start setup" role="alert">
          {begin.error instanceof ApiError && begin.error.isUnauthenticated
            ? 'That password was not accepted.'
            : describeError(begin.error)}
        </Alert>
      )}

      <p className="text-base text-text-muted">
        Adding a second step means a stolen password alone is not enough to reach the
        register. You will need an authenticator app such as Microsoft Authenticator,
        Google Authenticator or 1Password.
      </p>

      <Field
        label="Confirm your password to continue"
        required
        hint="Proves it is you before a new sign-in factor is added."
      >
        {(controlProps) => (
          <PasswordInput
            {...controlProps}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            required
          />
        )}
      </Field>

      <div className="flex justify-end">
        <Button
          type="submit"
          variant="primary"
          iconLeft={<Smartphone />}
          loading={begin.isPending}
          disabled={!password}
        >
          Set up two-step verification
        </Button>
      </div>
    </form>
  );
}

function ConfirmEnrollment({
  enrollment,
  onCancel,
  onConfirmed,
}: {
  enrollment: MfaEnrollment;
  onCancel: () => void;
  onConfirmed: (codes: MfaRecoveryCodes) => void;
}) {
  const [code, setCode] = useState('');
  const confirm = useConfirmMfaEnrollment();
  const toast = useToast();

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        confirm.mutate(
          { code: code.trim() },
          {
            onSuccess: (codes) => {
              toast.success('Two-step verification is on');
              onConfirmed(codes);
            },
          },
        );
      }}
    >
      {confirm.error && (
        <Alert tone="danger" title="That code was not accepted" role="alert">
          Codes expire after about 30 seconds. Wait for the next one and try again.
        </Alert>
      )}

      <Alert tone="info" title="Nothing is switched on yet">
        Two-step verification turns on only once you enter a working code below.
      </Alert>

      <div>
        <p className="text-sm font-medium text-text">
          1. Add the account to your authenticator
        </p>
        <p className="mt-1 text-sm text-text-muted">
          Scan this with the app on your phone.
        </p>

        <div className="mt-3 flex flex-col gap-4 sm:flex-row sm:items-start">
          {/* Rendered from `authenticatorUri` in the browser. The URI contains
              the TOTP secret, so it must never be handed to an image service. */}
          <QrCode
            value={enrollment.authenticatorUri}
            label="QR code to add this account to your authenticator app"
            className="shrink-0 self-center rounded-md border border-border p-2 sm:self-start"
          />

          <div className="flex min-w-0 flex-col gap-3">
            <div>
              <p className="text-xs text-text-subtle">
                Cannot scan? Type this key into the app by hand:
              </p>
              <div className="mt-1 flex items-center gap-1.5">
                <code className="select-all break-all rounded-sm bg-surface-sunken px-2 py-1 font-mono text-sm tracking-wider text-text">
                  {enrollment.sharedKey}
                </code>
                <CopyButton value={enrollment.sharedKey} label="Copy setup key" />
              </div>
            </div>

            <div>
              <p className="text-xs text-text-subtle">
                On the phone already? Open it directly:
              </p>
              <a
                href={enrollment.authenticatorUri}
                className="mt-1 inline-block w-fit rounded-md border border-border bg-surface px-3 py-2 text-sm font-medium text-accent hover:bg-surface-hover"
              >
                Open in authenticator app
              </a>
            </div>
          </div>
        </div>
      </div>

      <Field
        label="2. Enter the code the app shows"
        required
        hint="Six digits, refreshing every 30 seconds."
      >
        {(controlProps) => (
          <Input
            {...controlProps}
            value={code}
            onChange={(event) => setCode(event.target.value)}
            autoComplete="one-time-code"
            inputMode="numeric"
            maxLength={20}
            required
            autoFocus
            mono
            className="max-w-40 text-center text-lg tracking-[0.35em]"
            placeholder="000000"
          />
        )}
      </Field>

      <div className="flex justify-end gap-2">
        <Button onClick={onCancel} disabled={confirm.isPending}>
          Cancel
        </Button>
        <Button
          type="submit"
          variant="primary"
          loading={confirm.isPending}
          disabled={!code.trim()}
        >
          Turn on two-step verification
        </Button>
      </div>
    </form>
  );
}

function RegenerateRecoveryCodes({
  onGenerated,
}: {
  onGenerated: (codes: MfaRecoveryCodes) => void;
}) {
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const regenerate = useRegenerateRecoveryCodes();
  const toast = useToast();

  return (
    <form
      className="flex flex-col gap-4 border-t border-border pt-4"
      onSubmit={(event) => {
        event.preventDefault();
        regenerate.mutate(
          { currentPassword: password, code: code.trim() },
          {
            onSuccess: (codes) => {
              toast.success('New recovery codes generated');
              setPassword('');
              setCode('');
              onGenerated(codes);
            },
          },
        );
      }}
    >
      {regenerate.error && (
        <Alert tone="danger" title="Could not generate new codes" role="alert">
          {describeError(regenerate.error)}
        </Alert>
      )}

      <p className="text-sm text-text-muted">
        Generating a new set <strong>replaces every existing code</strong>. Any you have
        written down stop working immediately.
      </p>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Your password" required>
          {(controlProps) => (
            <PasswordInput
              {...controlProps}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              autoComplete="current-password"
              required
            />
          )}
        </Field>
        <Field label="Authenticator code" required>
          {(controlProps) => (
            <Input
              {...controlProps}
              value={code}
              onChange={(event) => setCode(event.target.value)}
              autoComplete="one-time-code"
              inputMode="numeric"
              maxLength={20}
              required
              mono
              placeholder="000000"
            />
          )}
        </Field>
      </div>

      <div className="flex justify-end">
        <Button
          type="submit"
          variant="danger-subtle"
          iconLeft={<RefreshCw />}
          loading={regenerate.isPending}
          disabled={!password || !code.trim()}
        >
          Replace recovery codes
        </Button>
      </div>
    </form>
  );
}
