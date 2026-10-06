import { useState, type FormEvent } from 'react';

import { useLocations, useReturnAsset } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  ASSET_CONDITIONS,
  type Asset,
  type AssetCondition,
  type CurrentAssignment,
} from '@/api/types';
import { ASSET_CONDITION_LABELS, formatDateTime } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, NativeSelect, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface ReturnDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  asset: Asset;
  assignment: CurrentAssignment;
}

/**
 * Closing a custody. Condition and location are optional here; when supplied the
 * API applies them to the asset inside the same transaction, which is how a
 * damaged return gets recorded in one step.
 */
export function ReturnDialog({
  onClose,
  asset,
  assignment,
}: ReturnDialogProps) {
  const [condition, setCondition] = useState<AssetCondition | ''>('');
  const [locationId, setLocationId] = useState<string | null>(null);
  const [notes, setNotes] = useState('');
  const toast = useToast();

  const locations = useLocations();
  const returnAsset = useReturnAsset(asset.id);

  function onSubmit(event: FormEvent) {
    event.preventDefault();

    returnAsset.mutate(
      {
        assignmentId: assignment.assignmentId,
        body: {
          // Both versions are checked; a stale one aborts the whole return.
          assetVersion: asset.version,
          assignmentVersion: assignment.version,
          condition: condition || null,
          locationId,
          notes: notes.trim() || null,
        },
      },
      {
        onSuccess: () => {
          toast.success('Asset returned', `${asset.assetTag} is back in stock`);
          onClose();
        },
      },
    );
  }

  const error = returnAsset.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'return-asset-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Return asset"
      description={`Held by ${assignment.target.displayName} since ${formatDateTime(assignment.assignedAtUtc)}.`}
      dismissible={!returnAsset.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={returnAsset.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={returnAsset.isPending}
          >
            Record return
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This asset just changed" role="alert">
            Nothing was recorded. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not record the return" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Field
          label="Condition on return"
          hint="Leave unchanged unless the item came back different."
        >
          {(controlProps) => (
            <NativeSelect
              {...controlProps}
              value={condition}
              onChange={(event) => setCondition(event.target.value as AssetCondition | '')}
            >
              <option value="">
                Unchanged ({ASSET_CONDITION_LABELS[asset.condition]})
              </option>
              {ASSET_CONDITIONS.map((value) => (
                <option key={value} value={value}>
                  {ASSET_CONDITION_LABELS[value]}
                </option>
              ))}
            </NativeSelect>
          )}
        </Field>

        <Field label="Returned to location">
          {(controlProps) => (
            <Combobox
              {...controlProps}
              value={locationId}
              onChange={setLocationId}
              options={(locations.data?.items ?? []).map((location) => ({
                value: location.id,
                label: location.name,
                description: [location.code, location.type].filter(Boolean).join(' · '),
              }))}
              loading={locations.isLoading}
              placeholder="No location"
              emptyOptionLabel="No location"
              searchPlaceholder="Search locations…"
            />
          )}
        </Field>

        <Field label="Notes">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              maxLength={2000}
              rows={3}
              placeholder="Screen cracked, sent for repair"
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
