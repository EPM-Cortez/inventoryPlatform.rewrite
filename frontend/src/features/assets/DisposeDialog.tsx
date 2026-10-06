import { useState, type FormEvent } from 'react';

import { useDisposeAsset } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Asset } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface DisposeDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  asset: Asset;
}

/**
 * Disposal is the end of the asset's life.
 *
 * Unlike retirement it is **not reversible** — `Restore` only accepts a retired
 * asset — so this asks the user to type the asset tag before the button unlocks.
 * The history and the record are kept; what is gone is the ability to bring it
 * back onto the register.
 */
export function DisposeDialog({ onClose, asset }: DisposeDialogProps) {
  const [reason, setReason] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const toast = useToast();
  const dispose = useDisposeAsset(asset.id);

  const tagMatches = confirmation.trim().toLowerCase() === asset.assetTag.toLowerCase();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim() || !tagMatches) return;

    dispose.mutate(
      { version: asset.version, reason: reason.trim() },
      {
        onSuccess: () => {
          toast.success('Asset disposed', asset.assetTag);
          onClose();
        },
      },
    );
  }

  const error = dispose.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'dispose-asset-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Dispose of asset"
      description="Records the permanent end of this asset's life."
      size="sm"
      dismissible={!dispose.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={dispose.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="danger"
            loading={dispose.isPending}
            disabled={!reason.trim() || !tagMatches}
          >
            Dispose of asset
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This asset just changed" role="alert">
            Nothing was disposed. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not dispose" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Alert tone="danger" title="This cannot be undone">
          A disposed asset cannot be restored to the register — retirement is the
          reversible option. The record and its full history are kept for audit.
        </Alert>

        {asset.currentAssignment && (
          <Alert tone="warning">
            This asset is currently held by{' '}
            <strong>{asset.currentAssignment.target.displayName}</strong>. Disposing of it
            closes that custody.
          </Alert>
        )}

        <Field label="Reason" required hint="Recorded permanently in the asset history.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={1000}
              rows={3}
              required
              autoFocus
              placeholder="Beyond economic repair, sent for WEEE recycling"
            />
          )}
        </Field>

        <Field
          label={`Type ${asset.assetTag} to confirm`}
          required
          error={
            confirmation.length > 0 && !tagMatches ? 'That does not match the tag.' : null
          }
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              mono
              autoComplete="off"
              placeholder={asset.assetTag}
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
