import { UserMinus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';

import { useCreateLeaverCase, usePeople } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Person } from '@/api/types';
import { PERSON_TYPE_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Avatar } from '@/ui/Avatar';
import { Button } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export function CreateLeaverCaseDialog({
  onClose,
  person,
}: {
  onClose: () => void;
  /** Fixes who is leaving, when opened from that person's own row or page. */
  person?: Person;
}) {
  const [personId, setPersonId] = useState<string | null>(person?.id ?? null);
  const [search, setSearch] = useState('');
  const [leavingOn, setLeavingOn] = useState('');
  const [notes, setNotes] = useState('');

  // Server-filtered: a school roll is far too long to send to the browser.
  const people = usePeople({ q: search || undefined, pageSize: 25 }, !person);
  const create = useCreateLeaverCase();
  const navigate = useNavigate();
  const toast = useToast();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!personId || !leavingOn) return;
    create.mutate(
      { personId, leavingOn, notes: notes.trim() || null },
      {
        onSuccess: (leaverCase) => {
          toast.success('Leaver case opened', leaverCase.personDisplayName);
          onClose();
          void navigate(`/leavers/${leaverCase.id}`);
        },
      },
    );
  }

  const error = create.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="Open a leaver case"
      size="sm"
      dismissible={!create.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="leaver-form"
            variant="primary"
            iconLeft={<UserMinus />}
            loading={create.isPending}
            disabled={!personId || !leavingOn}
          >
            Open the case
          </Button>
        </>
      }
    >
      <form id="leaver-form" onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isDuplicate ? (
          <Alert tone="danger" title="They already have an open case" role="alert">
            Find the existing case rather than opening a second one for the same person.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not open the case" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        {person ? (
          <div className="flex items-center gap-3 rounded-md border border-border bg-surface-sunken/60 p-3">
            <Avatar name={person.displayName} />
            <div className="min-w-0">
              <p className="truncate font-medium text-text">{person.displayName}</p>
              <p className="truncate text-xs text-text-subtle">
                {[PERSON_TYPE_LABELS[person.type], person.externalId].filter(Boolean).join(' · ')}
              </p>
            </div>
          </div>
        ) : (
          <Field label="Who is leaving" required>
            {(controlProps) => (
              <Combobox
                {...controlProps}
                value={personId}
                onChange={setPersonId}
                options={(people.data?.items ?? []).map((candidate) => ({
                  value: candidate.id,
                  label: candidate.displayName,
                  description: candidate.externalId ?? undefined,
                }))}
                loading={people.isLoading}
                onSearchChange={setSearch}
                serverFiltered
                placeholder="Search for a person"
                searchPlaceholder="Type a name or staff number…"
              />
            )}
          </Field>
        )}

        <Field label="Last day" required hint="Used to sort and chase cases.">
          {(controlProps) => (
            <Input
              {...controlProps}
              type="date"
              value={leavingOn}
              onChange={(event) => setLeavingOn(event.target.value)}
              required
            />
          )}
        </Field>

        <Field label="Notes" hint="Anything the person collecting the kit should know.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              maxLength={2000}
              rows={3}
              placeholder="Laptop to go to the new starter in September"
            />
          )}
        </Field>

        <Alert tone="info" title="Nothing is returned by opening a case">
          This records that they are going and gathers what they hold. Each item still
          has to be handed back and returned on its own — the case cannot be completed
          until they all are.
        </Alert>
      </form>
    </Dialog>
  );
}
