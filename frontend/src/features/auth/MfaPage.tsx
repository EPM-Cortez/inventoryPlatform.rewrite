import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';

import { authApi, mfaApi } from '@/api/endpoints';
import { ApiError } from '@/api/problem';
import { useSession } from '@/auth/session-context';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Field, Input } from '@/ui/Field';

import { AuthLayout } from './AuthLayout';

interface MfaState {
  rememberMe?: boolean;
  from?: string;
}

export function MfaPage() {
  const [code, setCode] = useState('');
  const [rememberMachine, setRememberMachine] = useState(false);
  // Losing a phone is the common case for needing this, so the fallback is one
  // click away rather than a separate journey.
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useSession();

  const state = (location.state as MfaState | null) ?? {};
  const from = state.from ?? '/';

  const complete = useMutation({
    mutationFn: () =>
      useRecoveryCode
        ? mfaApi.completeRecovery({
            recoveryCode: code.trim(),
            rememberMe: state.rememberMe ?? false,
          })
        : authApi.completeMfa({
            code: code.trim(),
            rememberMe: state.rememberMe ?? false,
            rememberMachine,
          }),
    onSuccess: async () => {
      await refresh();
      void navigate(from, { replace: true });
    },
  });

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    complete.mutate();
  }

  function switchMode() {
    setUseRecoveryCode((current) => !current);
    setCode('');
    complete.reset();
  }

  return (
    <AuthLayout
      title="Two-step verification"
      description={
        useRecoveryCode
          ? 'Enter one of the recovery codes you saved when you set this up.'
          : 'Enter the code from your authenticator app.'
      }
      footer={
        <Link to="/login" className="rounded-xs text-accent hover:underline">
          Back to sign in
        </Link>
      }
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        {complete.error && (
          <Alert tone="danger" role="alert">
            {complete.error instanceof ApiError && complete.error.isRateLimited
              ? 'Too many attempts. Wait a minute and try again.'
              : useRecoveryCode
                ? 'That recovery code was not accepted. Each one works only once.'
                : 'That code was not accepted. Check your authenticator and try again.'}
          </Alert>
        )}

        <Field
          label={useRecoveryCode ? 'Recovery code' : 'Authentication code'}
          required
          hint={
            useRecoveryCode
              ? 'One of the codes you saved. It will be used up.'
              : 'Six digits from your app.'
          }
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              value={code}
              onChange={(event) => setCode(event.target.value)}
              // `one-time-code` lets password managers and iOS autofill the code.
              autoComplete="one-time-code"
              inputMode={useRecoveryCode ? 'text' : 'numeric'}
              // The backend allows up to 20 characters so recovery codes fit.
              maxLength={20}
              autoFocus
              required
              mono
              className={
                useRecoveryCode
                  ? 'text-center text-lg'
                  : 'text-center text-lg tracking-[0.35em]'
              }
              placeholder={useRecoveryCode ? 'XXXX-XXXX' : '000000'}
            />
          )}
        </Field>

        {!useRecoveryCode && (
          <Checkbox
            checked={rememberMachine}
            onCheckedChange={setRememberMachine}
            label="Trust this device"
            description="Skip the code on this browser next time."
          />
        )}

        <Button
          type="submit"
          variant="primary"
          size="lg"
          loading={complete.isPending}
          className="mt-1 w-full"
        >
          Verify
        </Button>

        <button
          type="button"
          onClick={switchMode}
          className="rounded-xs text-center text-sm text-text-muted hover:text-text hover:underline"
        >
          {useRecoveryCode
            ? 'Use your authenticator app instead'
            : "I've lost my authenticator — use a recovery code"}
        </button>
      </form>
    </AuthLayout>
  );
}
