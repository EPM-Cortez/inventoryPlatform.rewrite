import { useState, type FormEvent } from 'react';

import { useUpdateExpectedReturn } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { AssetAssignment, Guid } from '@/api/types';
import { formatDate } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface ExpectedReturnDialogProps {
  assetId: Guid;
  assignment: AssetAssignment;
  onClose: () => void;
}

/**
 * Sets, changes or clears when a loan is due back.
 *
 * Versioned on the assignment rather than the asset — extending a loan is not a
 * change to the equipment, and two people editing different assignments of the
 * same asset should not collide.
 */
export function ExpectedReturnDialog({
  assetId,
  assignment,
  onClose,
}: ExpectedReturnDialogProps) {
  const [date, setDate] = useState(assignment.expectedReturnOn ?? '');
  const update = useUpdateExpectedReturn(assetId);
  const toast = useToast();

  const hadDate = assignment.expectedReturnOn !== null;
  const clearing = hadDate && date === '';

  function submit(event: FormEvent) {
    event.preventDefault();
    update.mutate(
      {
        assignmentId: assignment.id,
        // Empty clears it, which makes the loan open-ended again.
        body: { expectedReturnOn: date || null, assignmentVersion: assignment.version },
      },
      {
        onSuccess: () => {
          toast.success(
            clearing ? 'Due date removed' : 'Due date saved',
            assignment.target.displayName,
          );
          onClose();
        },
      },
    );
  }

  const error = update.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'expected-return-form';

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={hadDate ? 'Change the due date' : 'Set a due date'}
      description={`${assignment.target.displayName} has had this since ${formatDate(
        assignment.assignedAtUtc.slice(0, 10),
      )}.`}
      size="sm"
      dismissible={!update.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={update.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant={clearing ? 'danger-subtle' : 'primary'}
            loading={update.isPending}
          >
            {clearing ? 'Remove the due date' : 'Save due date'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="danger" title="Someone else changed this loan" role="alert">
            Close this and reopen it to pick up their version, then re-apply your change.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not save" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Field
          label="Due back"
          hint="Leave it empty to make the loan open-ended, which is never overdue."
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              type="date"
              value={date}
              onChange={(event) => setDate(event.target.value)}
              autoFocus
            />
          )}
        </Field>

        {clearing && (
          <Alert tone="warning" title="This loan will stop being tracked">
            With no due date it cannot be overdue, so it will drop off the overdue list
            and nobody will be prompted to chase it.
          </Alert>
        )}
      </form>
    </Dialog>
  );
}
