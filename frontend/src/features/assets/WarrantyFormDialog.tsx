import { useState, type FormEvent } from 'react';

import { useAddWarranty, useUpdateWarranty } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Warranty } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

/** Free text on the wire, but these cover almost every real record. */
const COVERAGE_SUGGESTIONS = [
  'manufacturer',
  'extended',
  'accidental damage',
  'on-site',
  'return to base',
];

interface FormState {
  provider: string;
  coverageType: string;
  startDate: string;
  endDate: string;
  reference: string;
  notes: string;
  isPrimary: boolean;
}

function emptyForm(): FormState {
  return {
    provider: '',
    coverageType: '',
    startDate: '',
    endDate: '',
    reference: '',
    notes: '',
    isPrimary: false,
  };
}

export interface WarrantyFormDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  assetId: string;
  /** Present for edit, absent for add. */
  warranty?: Warranty;
}

export function WarrantyFormDialog({
  onClose,
  assetId,
  warranty,
}: WarrantyFormDialogProps) {
  const isEdit = Boolean(warranty);
  const [form, setForm] = useState<FormState>(emptyForm);
  const toast = useToast();

  const add = useAddWarranty(assetId);
  const update = useUpdateWarranty(assetId);
  const mutation = isEdit ? update : add;

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  const datesInverted =
    form.startDate !== '' && form.endDate !== '' && form.endDate < form.startDate;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (datesInverted) return;

    const body = {
      provider: form.provider.trim(),
      coverageType: form.coverageType.trim(),
      startDate: form.startDate || null,
      endDate: form.endDate || null,
      reference: form.reference.trim() || null,
      notes: form.notes.trim() || null,
      isPrimary: form.isPrimary,
    };

    if (warranty) {
      update.mutate(
        { warrantyId: warranty.id, body: { ...body, version: warranty.version } },
        {
          onSuccess: () => {
            toast.success('Warranty saved');
            onClose();
          },
        },
      );
    } else {
      add.mutate(body, {
        onSuccess: () => {
          toast.success('Warranty added');
          onClose();
        },
      });
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'warranty-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title={isEdit ? 'Edit warranty' : 'Add warranty'}
      description="Coverage drives the expiring-soon report and the asset's warranty state."
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
            disabled={!form.provider.trim() || !form.coverageType.trim() || datesInverted}
          >
            {isEdit ? 'Save changes' : 'Add warranty'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="Someone else saved first" role="alert">
            Close and reopen this dialog to load the current values.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not save" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Provider" required>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.provider}
                onChange={(event) => set('provider', event.target.value)}
                maxLength={200}
                required
                autoFocus
                placeholder="Dell UK"
              />
            )}
          </Field>

          <Field label="Coverage type" required>
            {(controlProps) => (
              <>
                <Input
                  {...controlProps}
                  list="coverage-types"
                  value={form.coverageType}
                  onChange={(event) => set('coverageType', event.target.value)}
                  maxLength={100}
                  required
                  placeholder="manufacturer"
                />
                <datalist id="coverage-types">
                  {COVERAGE_SUGGESTIONS.map((suggestion) => (
                    <option key={suggestion} value={suggestion} />
                  ))}
                </datalist>
              </>
            )}
          </Field>

          <Field label="Starts">
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                value={form.startDate}
                onChange={(event) => set('startDate', event.target.value)}
              />
            )}
          </Field>

          <Field
            label="Ends"
            error={datesInverted ? 'The end date must be on or after the start date.' : null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                value={form.endDate}
                onChange={(event) => set('endDate', event.target.value)}
              />
            )}
          </Field>
        </div>

        <Field label="Reference" hint="Contract or policy number.">
          {(controlProps) => (
            <Input
              {...controlProps}
              value={form.reference}
              onChange={(event) => set('reference', event.target.value)}
              maxLength={200}
              mono
              placeholder="WAR-12345"
            />
          )}
        </Field>

        <Field label="Notes">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={form.notes}
              onChange={(event) => set('notes', event.target.value)}
              maxLength={2000}
              rows={3}
            />
          )}
        </Field>

        <Checkbox
          checked={form.isPrimary}
          onCheckedChange={(checked) => set('isPrimary', checked)}
          label="Primary coverage"
          description="Shown on the asset and used for the warranty state. Marking this demotes any existing primary."
        />
      </form>
    </Dialog>
  );
}
