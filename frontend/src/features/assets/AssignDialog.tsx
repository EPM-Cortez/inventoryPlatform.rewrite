import { Building2, UserRound } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { useAssignAsset, useDepartments, usePeople } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Asset, AssignmentTargetType } from '@/api/types';
import { cn } from '@/lib/cn';
import { PERSON_TYPE_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface AssignDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  asset: Asset;
}

/**
 * Assign or transfer. The API treats both as the same POST — assigning an asset
 * that is already out atomically closes the current custody and opens a new one —
 * so this dialog just relabels itself.
 */
export function AssignDialog({ onClose, asset }: AssignDialogProps) {
  const isTransfer = asset.currentAssignment !== null;
  const [targetType, setTargetType] = useState<AssignmentTargetType>('person');
  const [targetId, setTargetId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [notes, setNotes] = useState('');
  const [expectedReturnOn, setExpectedReturnOn] = useState('');
  const toast = useToast();

  const assign = useAssignAsset(asset.id);

  // Only the visible list is fetched; switching target type starts the other one.
  const people = usePeople(
    { q: search || undefined, pageSize: 50, sort: 'displayName' },
    targetType === 'person',
  );
  const departments = useDepartments(
    { q: search || undefined, pageSize: 50, sort: 'name' },
    targetType === 'department',
  );

  // Switching between people and departments must drop the previous pick,
  // otherwise the request would carry an id of the wrong kind.
  function switchTarget(type: AssignmentTargetType) {
    setTargetType(type);
    setTargetId(null);
    setSearch('');
  }

  const options =
    targetType === 'person'
      ? (people.data?.items ?? []).map((person) => ({
          value: person.id,
          label: person.displayName,
          description: [PERSON_TYPE_LABELS[person.type], person.externalId]
            .filter(Boolean)
            .join(' · '),
        }))
      : (departments.data?.items ?? []).map((department) => ({
          value: department.id,
          label: department.name,
          description: department.code,
        }));

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!targetId) return;

    assign.mutate(
      {
        personId: targetType === 'person' ? targetId : null,
        departmentId: targetType === 'department' ? targetId : null,
        // The server rejects a stale version rather than overwriting.
        assetVersion: asset.version,
        notes: notes.trim() || null,
        // Empty means an open-ended loan, which is the common case.
        expectedReturnOn: expectedReturnOn || null,
      },
      {
        onSuccess: (result) => {
          toast.success(
            isTransfer ? 'Asset transferred' : 'Asset assigned',
            result.assignment.target.displayName,
          );
          onClose();
        },
      },
    );
  }

  const error = assign.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'assign-asset-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title={isTransfer ? 'Transfer asset' : 'Assign asset'}
      description={
        isTransfer
          ? `Currently held by ${asset.currentAssignment?.target.displayName}. Transferring closes that custody and opens a new one.`
          : `Give ${asset.assetTag} to a person or a department.`
      }
      dismissible={!assign.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={assign.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={assign.isPending}
            disabled={!targetId}
          >
            {isTransfer ? 'Transfer' : 'Assign'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This asset just changed" role="alert">
            Someone else updated it while this dialog was open. Close and reopen it to
            work from the current state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not complete" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div role="radiogroup" aria-label="Holder type" className="flex gap-2">
          <TargetTypeOption
            active={targetType === 'person'}
            onSelect={() => switchTarget('person')}
            icon={<UserRound className="size-4" />}
            label="A person"
            description="Staff, pupil or contractor"
          />
          <TargetTypeOption
            active={targetType === 'department'}
            onSelect={() => switchTarget('department')}
            icon={<Building2 className="size-4" />}
            label="A department"
            description="Held collectively"
          />
        </div>

        <Field label={targetType === 'person' ? 'Person' : 'Department'} required>
          {(controlProps) => (
            <Combobox
              {...controlProps}
              value={targetId}
              onChange={setTargetId}
              options={options}
              loading={people.isFetching || departments.isFetching}
              onSearchChange={setSearch}
              serverFiltered
              placeholder={
                targetType === 'person' ? 'Choose a person' : 'Choose a department'
              }
              searchPlaceholder={
                targetType === 'person' ? 'Search people…' : 'Search departments…'
              }
            />
          )}
        </Field>

        <Field
          label="Due back"
          hint="Optional. Set it for a temporary loan and it will show as overdue if it is not returned in time."
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              type="date"
              value={expectedReturnOn}
              onChange={(event) => setExpectedReturnOn(event.target.value)}
              min={today()}
            />
          )}
        </Field>

        <Field label="Notes" hint="Recorded against this custody, e.g. accessories issued.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              maxLength={2000}
              rows={3}
              placeholder="Issued with charger and case"
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}

/** Today as `YYYY-MM-DD` in local time, which is what a date input expects. */
function today(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

function TargetTypeOption({
  active,
  onSelect,
  icon,
  label,
  description,
}: {
  active: boolean;
  onSelect: () => void;
  icon: React.ReactNode;
  label: string;
  description: string;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={active}
      onClick={onSelect}
      className={cn(
        'flex flex-1 items-start gap-2.5 rounded-lg border p-3 text-left transition-colors',
        active
          ? 'border-accent bg-accent-soft'
          : 'border-border bg-surface hover:border-border-strong hover:bg-surface-hover',
      )}
    >
      <span className={cn('mt-0.5 shrink-0', active ? 'text-accent' : 'text-text-subtle')}>
        {icon}
      </span>
      <span className="min-w-0">
        <span
          className={cn(
            'block text-base font-medium',
            active ? 'text-accent-soft-fg' : 'text-text',
          )}
        >
          {label}
        </span>
        <span className="block text-xs text-text-subtle">{description}</span>
      </span>
    </button>
  );
}
