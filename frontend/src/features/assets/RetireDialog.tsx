import { useState, type FormEvent } from 'react';

import { useRetireAsset } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Asset } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface RetireDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  asset: Asset;
}

/**
 * Retirement is reversible (`assets.restore`) and never deletes anything, but it
 * closes any open custody and takes the asset off the active register, so it
 * asks for a reason that goes into the permanent history.
 */
export function RetireDialog({ onClose, asset }: RetireDialogProps) {
  const [reason, setReason] = useState('');
  const toast = useToast();
  const retire = useRetireAsset(asset.id);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim()) return;

    retire.mutate(
      { version: asset.version, reason: reason.trim() },
      {
        onSuccess: () => {
          toast.success('Asset retired', asset.assetTag);
          onClose();
        },
      },
    );
  }

  const error = retire.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'retire-asset-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Retire asset"
      description={`${asset.assetTag} will come off the active register. Its history is kept, and an administrator can restore it later.`}
      size="sm"
      dismissible={!retire.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={retire.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="danger"
            loading={retire.isPending}
            disabled={!reason.trim()}
          >
            Retire asset
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This asset just changed" role="alert">
            Nothing was retired. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not retire" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        {asset.currentAssignment && (
          <Alert tone="warning">
            This asset is currently held by{' '}
            <strong>{asset.currentAssignment.target.displayName}</strong>. Retiring it
            closes that custody.
          </Alert>
        )}

        <Field
          label="Reason"
          required
          hint="Recorded permanently in the asset history."
        >
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={1000}
              rows={3}
              required
              autoFocus
              placeholder="End of useful life"
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
