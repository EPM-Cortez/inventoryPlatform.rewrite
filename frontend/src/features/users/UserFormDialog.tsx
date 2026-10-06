import { useState, type FormEvent } from 'react';

import { useCreateUser, useUpdateUser } from '@/api/hooks';
import { ApiError } from '@/api/problem';
import type { User } from '@/api/types';
import { Role } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

import { describeAccountError } from './accountErrors';
import { RolePicker } from './RolePicker';
import { TemporaryPasswordField } from './TemporaryPasswordField';
import { generateTemporaryPassword, MIN_PASSWORD_LENGTH } from './temporaryPassword';

export interface UserFormDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  /** Present for edit, absent for create. */
  user?: User;
  onCreated?: (user: User) => void;
}

export function UserFormDialog({ onClose, user, onCreated }: UserFormDialogProps) {
  const isEdit = Boolean(user);
  const [email, setEmail] = useState(user?.email ?? '');
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [isEnabled, setIsEnabled] = useState(user?.isEnabled ?? true);
  const [roles, setRoles] = useState<string[]>(user?.roles ?? []);
  // Only created accounts set a password here; resets have their own dialog.
  const [temporaryPassword, setTemporaryPassword] = useState(generateTemporaryPassword);

  const toast = useToast();
  const { user: currentUser } = useSession();

  const create = useCreateUser();
  const update = useUpdateUser();
  const mutation = isEdit ? update : create;

  const isSelf = user?.id === currentUser?.id;
  // The server rejects self-disable with 409; say so before they try.
  const disableLocked = isSelf;
  const losingOwnAdmin =
    isSelf &&
    (user?.roles.includes(Role.Administrator) ?? false) &&
    !roles.includes(Role.Administrator);

  function onSubmit(event: FormEvent) {
    event.preventDefault();

    if (user) {
      update.mutate(
        {
          userId: user.id,
          body: {
            email: email.trim(),
            displayName: displayName.trim(),
            isEnabled,
            roles,
            version: user.version,
          },
        },
        {
          onSuccess: (saved) => {
            toast.success('Account saved', saved.displayName);
            onClose();
          },
        },
      );
    } else {
      create.mutate(
        {
          email: email.trim(),
          displayName: displayName.trim(),
          temporaryPassword,
          roles,
        },
        {
          onSuccess: (saved) => {
            toast.success(
              'Account created',
              `Give ${saved.displayName} the temporary password securely.`,
            );
            onCreated?.(saved);
            onClose();
          },
        },
      );
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const problem = error ? describeAccountError(error) : null;
  const formId = 'user-form';

  const canSubmit =
    email.trim().length > 0 &&
    displayName.trim().length > 0 &&
    (isEdit || temporaryPassword.length >= MIN_PASSWORD_LENGTH);

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      size="lg"
      title={isEdit ? `Edit ${user?.displayName}` : 'New sign-in account'}
      description={
        isEdit
          ? 'Changing an account signs it out of existing sessions where security requires it.'
          : 'A sign-in account. To record who holds equipment, add a person instead.'
      }
      dismissible={!mutation.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={mutation.isPending}
            disabled={!canSubmit}
          >
            {isEdit ? 'Save changes' : 'Create account'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-5">
        {problem && (
          <Alert
            tone={apiError?.slug === 'concurrency-conflict' ? 'warning' : 'danger'}
            title={problem.title}
            role="alert"
          >
            {problem.detail}
          </Alert>
        )}

        {losingOwnAdmin && (
          <Alert tone="warning" title="You are removing your own Administrator role">
            You will lose access to account administration as soon as this saves. If you
            are the last enabled administrator the server will refuse the change.
          </Alert>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label="Email address"
            required
            hint="Used to sign in. Unique across all accounts, including archived ones."
            error={apiError?.fieldErrors('email')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                type="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                maxLength={320}
                required
                autoFocus
                placeholder="a.patel@example.school"
              />
            )}
          </Field>

          <Field
            label="Display name"
            required
            hint="Shown against everything this account does in history."
            error={apiError?.fieldErrors('displayName')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
                maxLength={200}
                required
                placeholder="A. Patel"
              />
            )}
          </Field>

          {!isEdit && (
            <div className="sm:col-span-2">
              <TemporaryPasswordField
                value={temporaryPassword}
                onRegenerate={() => setTemporaryPassword(generateTemporaryPassword())}
                error={apiError?.fieldErrors('temporaryPassword')[0] ?? null}
              />
            </div>
          )}
        </div>

        <fieldset className="flex flex-col gap-3">
          <legend className="mb-1 text-sm font-medium text-text">Roles</legend>
          <RolePicker value={roles} onChange={setRoles} disabled={mutation.isPending} />
        </fieldset>

        {isEdit && (
          <div className="rounded-md border border-border bg-surface-sunken/60 p-3">
            <Checkbox
              checked={isEnabled}
              onCheckedChange={setIsEnabled}
              disabled={disableLocked}
              label="Can sign in"
              description={
                disableLocked
                  ? 'You cannot disable your own account. Ask another administrator.'
                  : 'Clearing this blocks sign-in and ends existing sessions, without archiving the account.'
              }
            />
          </div>
        )}
      </form>
    </Dialog>
  );
}
