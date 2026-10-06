import { useState } from 'react';

import { useResetUserPassword } from '@/api/hooks';
import type { User } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { useToast } from '@/ui/Toast';

import { describeAccountError } from './accountErrors';
import { TemporaryPasswordField } from './TemporaryPasswordField';
import { generateTemporaryPassword } from './temporaryPassword';

export interface ResetPasswordDialogProps {
  onClose: () => void;
  user: User;
}

/**
 * Two-phase: confirm, then hand over the value.
 *
 * After the reset succeeds the password stops being regenerable and the dialog
 * switches to "copy this now" — it exists nowhere else, and closing without
 * copying means doing the whole thing again.
 */
export function ResetPasswordDialog({ onClose, user }: ResetPasswordDialogProps) {
  const [password] = useState(generateTemporaryPassword);
  const [done, setDone] = useState(false);
  const reset = useResetUserPassword();
  const toast = useToast();

  const problem = reset.error ? describeAccountError(reset.error) : null;

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title={`Reset password for ${user.displayName}`}
      description={
        done
          ? undefined
          : 'Sets a new single-use password and requires a change at next sign-in.'
      }
      size="sm"
      dismissible={!reset.isPending}
      footer={
        done ? (
          <Button variant="primary" onClick={onClose}>
            I have copied it
          </Button>
        ) : (
          <>
            <Button onClick={onClose} disabled={reset.isPending}>
              Cancel
            </Button>
            <Button
              variant="danger"
              loading={reset.isPending}
              onClick={() =>
                reset.mutate(
                  {
                    userId: user.id,
                    body: { newTemporaryPassword: password, version: user.version },
                  },
                  {
                    onSuccess: () => {
                      setDone(true);
                      toast.success('Password reset', user.displayName);
                    },
                  },
                )
              }
            >
              Reset password
            </Button>
          </>
        )
      }
    >
      <div className="flex flex-col gap-4">
        {problem && !done && (
          <Alert tone="danger" title={problem.title} role="alert">
            {problem.detail}
          </Alert>
        )}

        {done ? (
          <Alert tone="success" title="Done — copy this now">
            It is shown only here and cannot be retrieved again. Give it to{' '}
            {user.displayName} over a channel you trust.
          </Alert>
        ) : (
          <Alert tone="warning">
            This immediately ends every active session for {user.email}.
          </Alert>
        )}

        <TemporaryPasswordField value={password} />
      </div>
    </Dialog>
  );
}
