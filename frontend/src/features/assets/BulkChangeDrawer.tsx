import { ArrowRight, Layers, TriangleAlert } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { usePreviewBulkChange, useCommitBulkChange, useCategories, useLocations } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  BULK_OPERATIONS,
  type BulkChangeOperation,
  type BulkChangePreview,
  type BulkOperationKind,
} from '@/api/operationsTypes';
import {
  ASSET_CONDITIONS,
  ASSET_STATUSES,
  type AssetCondition,
  type AssetListItem,
  type AssetStatus,
} from '@/api/types';
import { ASSET_CONDITION_LABELS, ASSET_STATUS_LABELS, formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Combobox } from '@/ui/Combobox';
import { DataTable, type Column } from '@/ui/DataTable';
import { Drawer } from '@/ui/Drawer';
import { Field, Input, NativeSelect, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

const OPERATION_LABELS: Record<BulkOperationKind, string> = {
  setLocation: 'Move to a location',
  setCategory: 'Change category',
  setCondition: 'Set condition',
  setStatus: 'Set status',
  addNote: 'Add a note',
};

export interface BulkChangeDrawerProps {
  /** The assets ticked on the list, with the versions they were read at. */
  assets: AssetListItem[];
  onClose: () => void;
  onCommitted: () => void;
}

/**
 * Change many assets at once, in two deliberate steps.
 *
 * The preview is not a formality. It returns the exact before and after for
 * every asset plus a fingerprint, and the commit quotes that fingerprint back —
 * so what gets applied is provably what was shown. If any asset has been edited
 * in between, the **whole batch** is refused rather than half-applied, because
 * nobody could tell afterwards which half went through.
 */
export function BulkChangeDrawer({ assets, onClose, onCommitted }: BulkChangeDrawerProps) {
  const [preview, setPreview] = useState<BulkChangePreview | null>(null);

  return preview ? (
    <PreviewStep
      preview={preview}
      onBack={() => setPreview(null)}
      onClose={onClose}
      onCommitted={onCommitted}
    />
  ) : (
    <ChooseStep assets={assets} onClose={onClose} onPreviewed={setPreview} />
  );
}

function ChooseStep({
  assets,
  onClose,
  onPreviewed,
}: {
  assets: AssetListItem[];
  onClose: () => void;
  onPreviewed: (preview: BulkChangePreview) => void;
}) {
  const [kind, setKind] = useState<BulkOperationKind>('setLocation');
  const [locationId, setLocationId] = useState<string | null>(null);
  const [clearLocation, setClearLocation] = useState(false);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [condition, setCondition] = useState<AssetCondition>('good');
  const [status, setStatus] = useState<AssetStatus>('inStock');
  const [note, setNote] = useState('');
  const [reason, setReason] = useState('');

  const locations = useLocations();
  const categories = useCategories();
  const previewMutation = usePreviewBulkChange();

  function operation(): BulkChangeOperation {
    switch (kind) {
      case 'setLocation':
        return { kind, locationId: clearLocation ? null : locationId, clearLocation };
      case 'setCategory':
        return { kind, categoryId };
      case 'setCondition':
        return { kind, condition };
      case 'setStatus':
        return { kind, status };
      case 'addNote':
        return { kind, note: note.trim() };
    }
  }

  const targetChosen =
    (kind === 'setLocation' && (clearLocation || locationId)) ||
    (kind === 'setCategory' && categoryId) ||
    kind === 'setCondition' ||
    kind === 'setStatus' ||
    (kind === 'addNote' && note.trim().length > 0);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    previewMutation.mutate(
      {
        assets: assets.map((asset) => ({ assetId: asset.id, version: asset.version })),
        operation: operation(),
        reason: reason.trim(),
      },
      { onSuccess: onPreviewed },
    );
  }

  const error = previewMutation.error;

  return (
    <Drawer
      open
      onOpenChange={(next) => !next && onClose()}
      title="Change these assets"
      description={`${formatNumber(assets.length)} selected`}
      width="lg"
      footer={
        <>
          <Button onClick={onClose} disabled={previewMutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="bulk-change-form"
            variant="primary"
            loading={previewMutation.isPending}
            disabled={!targetChosen || !reason.trim()}
          >
            Preview the change
          </Button>
        </>
      }
    >
      <form id="bulk-change-form" onSubmit={onSubmit} className="flex flex-col gap-4">
        {error && (
          <Alert tone="danger" title="Could not work out the change" role="alert">
            {describeError(error)}
          </Alert>
        )}

        <Field label="What to change" required>
          {(controlProps) => (
            <NativeSelect
              {...controlProps}
              value={kind}
              onChange={(event) => setKind(event.target.value as BulkOperationKind)}
            >
              {BULK_OPERATIONS.map((operationKind) => (
                <option key={operationKind} value={operationKind}>
                  {OPERATION_LABELS[operationKind]}
                </option>
              ))}
            </NativeSelect>
          )}
        </Field>

        {kind === 'setLocation' && (
          <>
            <Field label="New location" required={!clearLocation}>
              {(controlProps) => (
                <Combobox
                  {...controlProps}
                  value={locationId}
                  onChange={setLocationId}
                  options={(locations.data?.items ?? []).map((location) => ({
                    value: location.id,
                    label: location.name,
                    description: location.code,
                  }))}
                  loading={locations.isLoading}
                  disabled={clearLocation}
                  placeholder="Choose a location"
                  searchPlaceholder="Search locations…"
                />
              )}
            </Field>
            <Checkbox
              checked={clearLocation}
              onCheckedChange={setClearLocation}
              label="Remove the location instead"
              description="Leaves these assets with no location recorded."
            />
          </>
        )}

        {kind === 'setCategory' && (
          <Field label="New category" required>
            {(controlProps) => (
              <Combobox
                {...controlProps}
                value={categoryId}
                onChange={setCategoryId}
                options={(categories.data?.items ?? []).map((category) => ({
                  value: category.id,
                  label: category.name,
                  description: category.code,
                }))}
                loading={categories.isLoading}
                placeholder="Choose a category"
                searchPlaceholder="Search categories…"
              />
            )}
          </Field>
        )}

        {kind === 'setCondition' && (
          <Field label="New condition" required>
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={condition}
                onChange={(event) => setCondition(event.target.value as AssetCondition)}
              >
                {ASSET_CONDITIONS.map((value) => (
                  <option key={value} value={value}>
                    {ASSET_CONDITION_LABELS[value]}
                  </option>
                ))}
              </NativeSelect>
            )}
          </Field>
        )}

        {kind === 'setStatus' && (
          <Field
            label="New status"
            required
            hint="Assignment and retirement are owned by their own workflows and are not offered here."
          >
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={status}
                onChange={(event) => setStatus(event.target.value as AssetStatus)}
              >
                {ASSET_STATUSES.filter(
                  (value) =>
                    value !== 'assigned' && value !== 'retired' && value !== 'disposed',
                ).map((value) => (
                  <option key={value} value={value}>
                    {ASSET_STATUS_LABELS[value]}
                  </option>
                ))}
              </NativeSelect>
            )}
          </Field>
        )}

        {kind === 'addNote' && (
          <Field label="Note" required hint="Added to every selected asset.">
            {(controlProps) => (
              <Textarea
                {...controlProps}
                value={note}
                onChange={(event) => setNote(event.target.value)}
                maxLength={4000}
                rows={3}
                required
                placeholder="Moved during the summer refurbishment"
              />
            )}
          </Field>
        )}

        <Field
          label="Why"
          required
          hint="Recorded against the batch in the audit trail."
        >
          {(controlProps) => (
            <Input
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              required
              placeholder="Room reorganisation, ticket 4821"
            />
          )}
        </Field>

        <Alert tone="info" title="Nothing changes yet">
          The next step shows exactly what would happen to each of the{' '}
          {formatNumber(assets.length)} assets. Nothing is written until you confirm it.
        </Alert>
      </form>
    </Drawer>
  );
}

function PreviewStep({
  preview,
  onBack,
  onClose,
  onCommitted,
}: {
  preview: BulkChangePreview;
  onBack: () => void;
  onClose: () => void;
  onCommitted: () => void;
}) {
  const commit = useCommitBulkChange();
  const toast = useToast();

  const changing = preview.items.filter((item) => !item.isNoOp);
  const noOps = preview.items.length - changing.length;

  const columns: Column<(typeof preview.items)[number]>[] = [
    {
      id: 'asset',
      header: 'Asset',
      cell: (item) => (
        <span className="truncate font-mono text-sm text-text">{item.assetTag}</span>
      ),
    },
    {
      id: 'from',
      header: 'Now',
      cell: (item) => (
        <span className="truncate text-sm text-text-muted">
          {item.from ?? <span className="text-text-subtle">Not set</span>}
        </span>
      ),
    },
    {
      id: 'arrow',
      header: <span className="sr-only">becomes</span>,
      width: 'w-8',
      cell: () => <ArrowRight className="size-3.5 text-text-subtle" aria-hidden />,
    },
    {
      id: 'to',
      header: 'After',
      cell: (item) =>
        item.isNoOp ? (
          <span className="text-sm text-text-subtle">No change</span>
        ) : (
          <span className="truncate text-sm font-medium text-text">
            {item.to ?? <span className="text-text-subtle">Not set</span>}
          </span>
        ),
    },
  ];

  return (
    <Drawer
      open
      onOpenChange={(next) => !next && onClose()}
      title="Check before applying"
      description={`${formatNumber(changing.length)} of ${formatNumber(
        preview.assetCount,
      )} would change`}
      width="lg"
      footer={
        <>
          <Button onClick={onBack} disabled={commit.isPending}>
            Back
          </Button>
          <Button
            variant="primary"
            iconLeft={<Layers />}
            loading={commit.isPending}
            disabled={changing.length === 0}
            onClick={() =>
              commit.mutate(
                {
                  jobId: preview.jobId,
                  // Quoting the fingerprint is what ties this commit to the plan
                  // that was displayed.
                  body: { fingerprint: preview.fingerprint, version: preview.version },
                },
                {
                  onSuccess: (result) => {
                    toast.success(
                      'Assets updated',
                      `${formatNumber(result.assetCount)} changed`,
                    );
                    onCommitted();
                    onClose();
                  },
                },
              )
            }
          >
            Apply to {formatNumber(changing.length)}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {commit.error instanceof ApiError && commit.error.isConcurrencyConflict ? (
          <Alert tone="danger" title="One of these assets changed while you were looking" role="alert">
            Nothing was applied — the batch is all or nothing, so there is no half-done
            state to undo. Go back, re-select and preview again.
          </Alert>
        ) : commit.error ? (
          <Alert tone="danger" title="Could not apply the change" role="alert">
            {describeError(commit.error)}
          </Alert>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          <Badge tone="accent">{OPERATION_LABELS[preview.operation.kind]}</Badge>
          {noOps > 0 && (
            <Badge tone="neutral">
              {formatNumber(noOps)} already correct
            </Badge>
          )}
        </div>

        {changing.length === 0 && (
          <Alert tone="info" title="Nothing would change">
            Every selected asset already has that value, so there is nothing to apply.
          </Alert>
        )}

        <p className="text-sm text-text-muted">{preview.reason}</p>

        <div className="-mx-5">
          <DataTable
            columns={columns}
            rows={preview.items}
            rowKey={(item) => item.assetId}
            rowClassName={(item) => (item.isNoOp ? 'opacity-50' : undefined)}
          />
        </div>

        <Alert tone="warning" title="Applied as one batch">
          <span className="flex items-start gap-2">
            <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span>
              If any of these assets is edited by someone else before you confirm, the
              entire batch is refused and nothing changes.
            </span>
          </span>
        </Alert>
      </div>
    </Drawer>
  );
}
