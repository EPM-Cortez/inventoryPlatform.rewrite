import { useState, type FormEvent } from 'react';

import { useCancelStocktake } from '@/api/hooks';
import { describeError } from '@/api/problem';
import type { StocktakeSession } from '@/api/stocktakeTypes';
import { formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

/**
 * Abandons a stocktake.
 *
 * A reason is required by the server, and rightly so: an abandoned count with no
 * explanation is indistinguishable from one that was forgotten about. What was
 * already scanned is kept either way.
 */
export function CancelStocktakeDialog({
  session,
  onClose,
}: {
  session: StocktakeSession;
  onClose: () => void;
}) {
  const [reason, setReason] = useState('');
  const cancel = useCancelStocktake(session.id);
  const toast = useToast();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    cancel.mutate(
      { version: session.version, reason: reason.trim() },
      {
        onSuccess: () => {
          toast.success('Stocktake cancelled', session.name);
          onClose();
        },
      },
    );
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="Cancel this stocktake?"
      size="sm"
      dismissible={!cancel.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={cancel.isPending}>
            Keep it open
          </Button>
          <Button
            type="submit"
            form="cancel-stocktake-form"
            variant="danger-subtle"
            loading={cancel.isPending}
            disabled={!reason.trim()}
          >
            Cancel stocktake
          </Button>
        </>
      }
    >
      <form id="cancel-stocktake-form" onSubmit={onSubmit} className="flex flex-col gap-4">
        {cancel.error && (
          <Alert tone="danger" title="Could not cancel it" role="alert">
            {describeError(cancel.error)}
          </Alert>
        )}

        <p className="text-base text-text-muted">
          {session.progress.seen > 0 ? (
            <>
              The {formatNumber(session.progress.seen)}{' '}
              {session.progress.seen === 1 ? 'asset' : 'assets'} already recorded stay on
              this stocktake as a record. No asset is changed.
            </>
          ) : (
            'Nothing has been recorded yet, so nothing is lost.'
          )}
        </p>

        <Field label="Why" required hint="Kept with the stocktake so the gap is explained.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={1000}
              rows={3}
              required
              autoFocus
              placeholder="Started against the wrong location"
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
