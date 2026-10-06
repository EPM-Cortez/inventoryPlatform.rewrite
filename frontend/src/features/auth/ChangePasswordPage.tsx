import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';

import { authApi } from '@/api/endpoints';
import { ApiError, describeError } from '@/api/problem';
import { useSession } from '@/auth/session-context';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { Field } from '@/ui/Field';
import { PageHeader } from '@/ui/PageHeader';
import { useToast } from '@/ui/Toast';

import { PasswordInput } from './PasswordInput';

/** Matches the API's `[StringLength(512, MinimumLength = 12)]` on new passwords. */
const MIN_PASSWORD_LENGTH = 12;

export function ChangePasswordPage() {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const { mustChangePassword, refresh } = useSession();
  const toast = useToast();
  const navigate = useNavigate();

  const mismatch = confirmPassword.length > 0 && newPassword !== confirmPassword;
  const tooShort = newPassword.length > 0 && newPassword.length < MIN_PASSWORD_LENGTH;

  const change = useMutation({
    mutationFn: () => authApi.changePassword({ currentPassword, newPassword }),
    onSuccess: async () => {
      toast.success('Password changed');
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      // Clears `mustChangePassword`, which releases the routing guard.
      await refresh();
      void navigate('/', { replace: true });
    },
  });

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (mismatch || tooShort) return;
    change.mutate();
  }

  const error = change.error;

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-5">
      <PageHeader
        title="Change password"
        description={
          mustChangePassword
            ? 'Set a new password before you continue.'
            : 'Choose a new password for your account.'
        }
      />

      {mustChangePassword && (
        <Alert tone="warning" title="A password change is required">
          Your account was created or reset with a temporary password. Set your own before
          using the inventory.
        </Alert>
      )}

      <Card>
        <CardHeader title="New password" />
        <CardBody>
          <form onSubmit={onSubmit} className="flex flex-col gap-4">
            {error && (
              <Alert tone="danger" role="alert">
                {error instanceof ApiError && error.isValidation
                  ? error.allFieldErrors().join(' ')
                  : describeError(error)}
              </Alert>
            )}

            <Field label="Current password" required>
              {(controlProps) => (
                <PasswordInput
                  {...controlProps}
                  value={currentPassword}
                  onChange={(event) => setCurrentPassword(event.target.value)}
                  autoComplete="current-password"
                  required
                />
              )}
            </Field>

            <Field
              label="New password"
              required
              hint={`At least ${MIN_PASSWORD_LENGTH} characters. A passphrase of three or four unrelated words works well.`}
              error={tooShort ? `Use at least ${MIN_PASSWORD_LENGTH} characters.` : null}
            >
              {(controlProps) => (
                <PasswordInput
                  {...controlProps}
                  value={newPassword}
                  onChange={(event) => setNewPassword(event.target.value)}
                  autoComplete="new-password"
                  minLength={MIN_PASSWORD_LENGTH}
                  required
                />
              )}
            </Field>

            <Field
              label="Confirm new password"
              required
              error={mismatch ? 'The two passwords do not match.' : null}
            >
              {(controlProps) => (
                <PasswordInput
                  {...controlProps}
                  value={confirmPassword}
                  onChange={(event) => setConfirmPassword(event.target.value)}
                  autoComplete="new-password"
                  required
                />
              )}
            </Field>

            <div className="mt-1 flex justify-end">
              <Button
                type="submit"
                variant="primary"
                loading={change.isPending}
                disabled={mismatch || tooShort}
              >
                Change password
              </Button>
            </div>
          </form>
        </CardBody>
      </Card>
    </div>
  );
}
