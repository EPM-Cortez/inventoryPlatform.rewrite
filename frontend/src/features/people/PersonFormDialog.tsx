import { useRef, useState, type FormEvent } from 'react';

import { useCreatePerson, useUpdatePerson } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import { PERSON_TYPES, type Person, type PersonType } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { PERSON_TYPE_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { SegmentedControl } from '@/ui/SegmentedControl';
import { useToast } from '@/ui/Toast';

interface FormState {
  displayName: string;
  type: PersonType;
  externalId: string;
  email: string;
  isActive: boolean;
}

const TYPE_OPTIONS = PERSON_TYPES.map((type) => ({
  value: type,
  label: PERSON_TYPE_LABELS[type],
}));

function initialState(person: Person | undefined, type: PersonType = 'staff'): FormState {
  return {
    displayName: person?.displayName ?? '',
    type: person?.type ?? type,
    externalId: person?.externalId ?? '',
    email: person?.email ?? '',
    isActive: person?.isActive ?? true,
  };
}

export interface PersonFormDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  person?: Person;
  /** Not called when "Add another" keeps the dialog open for the next entry. */
  onCreated?: (person: Person) => void;
}

export function PersonFormDialog({ onClose, person, onCreated }: PersonFormDialogProps) {
  const isEdit = Boolean(person);
  const [form, setForm] = useState<FormState>(() => initialState(person));
  const [addAnother, setAddAnother] = useState(false);
  const nameRef = useRef<HTMLInputElement>(null);
  const toast = useToast();
  const { can } = useSession();

  const create = useCreatePerson();
  const update = useUpdatePerson(person?.id ?? '');
  const mutation = isEdit ? update : create;

  // Archiving needs `people.manage`; `people.operate` alone can only edit details.
  const canArchive = can(Permission.PeopleManage);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();

    const body = {
      displayName: form.displayName.trim(),
      type: form.type,
      externalId: form.externalId.trim() || null,
      email: form.email.trim() || null,
    };

    if (person) {
      update.mutate(
        { ...body, isActive: form.isActive, version: person.version },
        {
          onSuccess: (saved) => {
            toast.success('Person saved', saved.displayName);
            onClose();
          },
        },
      );
    } else {
      create.mutate(body, {
        onSuccess: (saved) => {
          toast.success('Person added', saved.displayName);
          if (addAnother) {
            // Keep the type: a batch of entries is usually all staff or all pupils.
            setForm(initialState(undefined, form.type));
            create.reset();
            nameRef.current?.focus();
            return;
          }
          onCreated?.(saved);
          onClose();
        },
      });
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'person-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      size="md"
      title={isEdit ? `Edit ${person?.displayName}` : 'Add a person'}
      description="Someone who can be given equipment. This is a custody record, not a sign-in account."
      dismissible={!mutation.isPending}
      footer={
        <>
          {!isEdit && (
            <Checkbox
              className="mr-auto"
              checked={addAnother}
              onCheckedChange={setAddAnother}
              label="Add another"
            />
          )}
          <Button onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={mutation.isPending}
            disabled={!form.displayName.trim()}
          >
            {isEdit ? 'Save changes' : 'Add person'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="Someone else saved first" role="alert">
            Close and reopen this form to load the current values.
          </Alert>
        ) : apiError?.isForbidden ? (
          <Alert tone="danger" title="Not permitted" role="alert">
            Archiving a person needs the people.manage permission.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not save" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Field
          label="Full name"
          required
          error={apiError?.fieldErrors('displayName')[0] ?? null}
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              ref={nameRef}
              value={form.displayName}
              onChange={(event) => set('displayName', event.target.value)}
              maxLength={200}
              required
              autoFocus
              placeholder="A. Patel"
            />
          )}
        </Field>

        <SegmentedControl
          label="Type"
          value={form.type}
          onChange={(type) => set('type', type)}
          options={TYPE_OPTIONS}
        />

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Email" error={apiError?.fieldErrors('email')[0] ?? null}>
            {(controlProps) => (
              <Input
                {...controlProps}
                type="email"
                value={form.email}
                onChange={(event) => set('email', event.target.value)}
                maxLength={320}
                placeholder="a.patel@example.school"
              />
            )}
          </Field>

          <Field
            label="External ID"
            hint="Payroll or MIS reference."
            error={apiError?.fieldErrors('externalId')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.externalId}
                onChange={(event) => set('externalId', event.target.value)}
                maxLength={100}
                mono
                placeholder="STF-1042"
              />
            )}
          </Field>
        </div>

        {isEdit && (
          <div className="rounded-md border border-border bg-surface-sunken/60 p-3">
            <Checkbox
              checked={form.isActive}
              onCheckedChange={(checked) => set('isActive', checked)}
              disabled={!canArchive}
              label="Active"
              description={
                canArchive
                  ? 'Clearing this archives the person. Their custody history is kept.'
                  : 'Archiving a person needs the people.manage permission.'
              }
            />
          </div>
        )}
      </form>
    </Dialog>
  );
}
