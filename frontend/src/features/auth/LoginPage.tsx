import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router';

import { authApi } from '@/api/endpoints';
import { ApiError } from '@/api/problem';
import { useSession } from '@/auth/session-context';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Field, Input } from '@/ui/Field';

import { AuthLayout } from './AuthLayout';
import { PasswordInput } from './PasswordInput';

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useSession();

  // Where the user was headed before the guard intercepted them.
  const from = (location.state as { from?: string } | null)?.from ?? '/';

  const login = useMutation({
    mutationFn: () => authApi.login({ email, password, rememberMe }),
    onSuccess: async () => {
      await refresh();
      void navigate(from, { replace: true });
    },
    onError: (error) => {
      // The password was accepted; a second factor is outstanding. The partial
      // sign-in lives in a server cookie, so the MFA screen can stand alone.
      if (error instanceof ApiError && error.isMfaRequired) {
        void navigate('/login/mfa', { replace: true, state: { rememberMe, from } });
      }
    },
  });

  const error = login.error;
  const showMfaHandoff = error instanceof ApiError && error.isMfaRequired;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    login.mutate();
  }

  return (
    <AuthLayout title="Sign in" description="Use your school inventory account.">
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        {error && !showMfaHandoff && (
          <Alert tone="danger" role="alert">
            {error instanceof ApiError && error.isRateLimited
              ? 'Too many sign-in attempts. Wait a minute and try again.'
              : error instanceof ApiError && error.isUnauthenticated
                ? 'The email address or password was not accepted.'
                : 'Could not reach the inventory service. Check your connection and try again.'}
          </Alert>
        )}

        <Field label="Email address" required>
          {(controlProps) => (
            <Input
              {...controlProps}
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              autoComplete="username"
              autoFocus
              required
              placeholder="you@example.school"
            />
          )}
        </Field>

        <Field label="Password" required>
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

        <Checkbox
          checked={rememberMe}
          onCheckedChange={setRememberMe}
          label="Keep me signed in"
          description="Only on a device you control."
        />

        <Button
          type="submit"
          variant="primary"
          size="lg"
          loading={login.isPending}
          className="mt-1 w-full"
        >
          Sign in
        </Button>
      </form>
    </AuthLayout>
  );
}
