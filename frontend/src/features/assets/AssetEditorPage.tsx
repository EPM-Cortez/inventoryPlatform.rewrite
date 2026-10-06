import { RotateCw, Save } from 'lucide-react';
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';

import {
  useAsset,
  useAssetTagPolicy,
  useCategories,
  useCreateAsset,
  useLocations,
  useUpdateAsset,
} from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  ASSET_CONDITIONS,
  ASSET_STATUSES,
  type Asset,
  type AssetCondition,
  type AssetFields,
  type AssetStatus,
  type CreateAssetRequest,
} from '@/api/types';
import { ASSET_CONDITION_LABELS, ASSET_STATUS_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Combobox } from '@/ui/Combobox';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Field, Input, NativeSelect, Textarea } from '@/ui/Field';
import { Breadcrumbs } from '@/ui/PageHeader';
import { SegmentedControl } from '@/ui/SegmentedControl';
import { LoadingBlock } from '@/ui/Spinner';
import { useToast } from '@/ui/Toast';

import { makeAndModel } from './assetText';
import { DeviceIllustration } from './DeviceIllustration';
import { DEVICE_KIND_LABELS, deviceKindFor } from './deviceKind';

/**
 * Status values the register cannot set directly.
 *
 * `assigned` is owned by the assignment workflow, and on an existing asset
 * `retired`/`disposed` are owned by the retire endpoint, which also demands a
 * reason. The domain rejects all of these with a 400, so the form must not offer
 * them — see `Asset.Apply` in `Inventory.Domain`.
 */
function selectableStatuses(isEdit: boolean): readonly AssetStatus[] {
  return ASSET_STATUSES.filter((status) => {
    if (status === 'assigned') return false;
    if (isEdit && (status === 'retired' || status === 'disposed')) return false;
    return true;
  });
}

interface FormState {
  assetTag: string;
  /** Create only: let the server issue the next tag from the policy. */
  generateAssetTag: boolean;
  deviceName: string;
  categoryId: string | null;
  description: string;
  manufacturer: string;
  model: string;
  serialNumber: string;
  lanMacAddress: string;
  biosGuid: string;
  ownership: string;
  status: AssetStatus;
  condition: AssetCondition;
  locationId: string | null;
  purchaseDate: string;
  purchaseCost: string;
  currency: string;
  supplier: string;
  operatingSystem: string;
  operatingSystemVersion: string;
  /** `'unknown'` is the API's `null`, which is not the same as "no". */
  windows11Compliant: 'unknown' | 'true' | 'false';
  notes: string;
  legacyId: string;
}

function emptyForm(): FormState {
  return {
    assetTag: '',
    generateAssetTag: false,
    deviceName: '',
    categoryId: null,
    description: '',
    manufacturer: '',
    model: '',
    serialNumber: '',
    lanMacAddress: '',
    biosGuid: '',
    ownership: '',
    status: 'inStock',
    condition: 'good',
    locationId: null,
    purchaseDate: '',
    purchaseCost: '',
    currency: 'GBP',
    supplier: '',
    operatingSystem: '',
    operatingSystemVersion: '',
    windows11Compliant: 'unknown',
    notes: '',
    legacyId: '',
  };
}

function formFromAsset(asset: Asset): FormState {
  return {
    assetTag: asset.assetTag,
    generateAssetTag: false,
    deviceName: asset.deviceName ?? '',
    categoryId: asset.category.id,
    description: asset.description ?? '',
    manufacturer: asset.manufacturer ?? '',
    model: asset.model ?? '',
    serialNumber: asset.serialNumber ?? '',
    lanMacAddress: asset.lanMacAddress ?? '',
    biosGuid: asset.biosGuid ?? '',
    ownership: asset.ownership ?? '',
    status: asset.status,
    condition: asset.condition,
    locationId: asset.location?.id ?? null,
    purchaseDate: asset.purchaseDate ?? '',
    purchaseCost: asset.purchaseCost?.toString() ?? '',
    currency: asset.currency ?? 'GBP',
    supplier: asset.supplier ?? '',
    operatingSystem: asset.operatingSystem ?? '',
    operatingSystemVersion: asset.operatingSystemVersion ?? '',
    windows11Compliant:
      asset.windows11Compliant === null ? 'unknown' : asset.windows11Compliant ? 'true' : 'false',
    notes: asset.notes ?? '',
    legacyId: asset.legacyId ?? '',
  };
}

/** Blank optional strings must go over the wire as `null`, not `""`. */
function orNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** BIOS GUID is a real `Guid` server-side, so a typo would 400 with no context. */
function isValidBiosGuid(value: string): boolean {
  return value.trim().length === 0 || GUID_PATTERN.test(value.trim());
}

/** Everything except the tag, which create and update source differently. */
function toFields(form: FormState): AssetFields {
  return {
    deviceName: orNull(form.deviceName),
    categoryId: form.categoryId!,
    description: orNull(form.description),
    manufacturer: orNull(form.manufacturer),
    model: orNull(form.model),
    serialNumber: orNull(form.serialNumber),
    lanMacAddress: orNull(form.lanMacAddress),
    biosGuid: orNull(form.biosGuid),
    ownership: orNull(form.ownership),
    status: form.status,
    condition: form.condition,
    locationId: form.locationId,
    purchaseDate: orNull(form.purchaseDate),
    purchaseCost: form.purchaseCost.trim() ? Number(form.purchaseCost) : null,
    currency: orNull(form.currency)?.toUpperCase() ?? null,
    supplier: orNull(form.supplier),
    operatingSystem: orNull(form.operatingSystem),
    operatingSystemVersion: orNull(form.operatingSystemVersion),
    windows11Compliant:
      form.windows11Compliant === 'unknown' ? null : form.windows11Compliant === 'true',
    notes: orNull(form.notes),
    legacyId: orNull(form.legacyId),
  };
}

/**
 * The server takes the tag from exactly one source and rejects anything else
 * with `asset_tag_source_invalid`, so the two cases are built separately rather
 * than by spreading a shared object and hoping the fields line up.
 */
function toCreatePayload(form: FormState): CreateAssetRequest {
  return form.generateAssetTag
    ? { ...toFields(form), generateAssetTag: true }
    : { ...toFields(form), assetTag: form.assetTag.trim() };
}

const CONDITION_OPTIONS = ASSET_CONDITIONS.map((condition) => ({
  value: condition,
  label: ASSET_CONDITION_LABELS[condition],
}));

const WINDOWS_11_OPTIONS = [
  { value: 'unknown', label: 'Not assessed' },
  { value: 'true', label: 'Ready' },
  { value: 'false', label: 'Not ready' },
] as const;

/**
 * Create and edit, as a page rather than a side panel: an asset has some
 * twenty fields, and a narrow panel turned them into one long scroll. Routed at
 * `/assets/new` and `/assets/:assetId/edit`.
 */
export default function AssetEditorPage() {
  const { assetId } = useParams<{ assetId: string }>();
  const asset = useAsset(assetId);

  if (!assetId) return <AssetEditor key="new" />;
  if (asset.isLoading) return <LoadingBlock label="Loading asset…" />;
  if (asset.isError) {
    return <ErrorState error={asset.error} onRetry={() => void asset.refetch()} />;
  }
  if (!asset.data) return null;

  if (asset.data.status === 'retired' || asset.data.status === 'disposed') {
    return (
      <EmptyState
        title="This asset is off the register"
        description="Retired and disposed assets keep their record as it was. Restore a retired asset to stock before editing it."
        action={
          <Button asChild>
            <Link to={`/assets/${asset.data.id}`}>Back to the asset</Link>
          </Button>
        }
      />
    );
  }

  // Keyed so moving between assets starts a fresh form from that asset.
  return <AssetEditor key={asset.data.id} asset={asset.data} />;
}

function AssetEditor({ asset }: { asset?: Asset }) {
  const isEdit = asset !== undefined;
  // The record as it was when editing began. Its version is what the save sends,
  // so a change made by someone else in the meantime is a 409, never overwritten.
  const [base] = useState(asset);
  const [initial] = useState<FormState>(() => (asset ? formFromAsset(asset) : emptyForm()));
  const [form, setForm] = useState<FormState>(initial);
  const [discardOpen, setDiscardOpen] = useState(false);

  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();

  const categories = useCategories();
  const locations = useLocations();
  // Only needed to preview the next tag, so it is not fetched on an edit.
  const tagPolicy = useAssetTagPolicy(!isEdit);
  const create = useCreateAsset();
  const update = useUpdateAsset(asset?.id ?? '');
  const mutation = isEdit ? update : create;

  const statuses = useMemo(() => selectableStatuses(isEdit), [isEdit]);
  // The assignment workflow owns the status of an asset that is out with someone.
  const statusLocked = base?.status === 'assigned';

  const dirty = useMemo(() => JSON.stringify(form) !== JSON.stringify(initial), [form, initial]);
  const backPath = base ? `/assets/${base.id}` : '/assets';

  const category = categories.data?.items.find((candidate) => candidate.id === form.categoryId);
  const kind = deviceKindFor({
    category: category ?? null,
    manufacturer: form.manufacturer || null,
    model: form.model || null,
    operatingSystem: form.operatingSystem || null,
  });
  const product = makeAndModel({
    manufacturer: form.manufacturer.trim() || null,
    model: form.model.trim() || null,
  });

  // Closing the tab or reloading would lose the edit without a word otherwise.
  useEffect(() => {
    if (!dirty) return;
    function onBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault();
    }
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);

  // "Edit" beside the notes links here with `#notes`; land on that section.
  useEffect(() => {
    const id = location.hash.slice(1);
    if (!id) return;
    const section = document.getElementById(id);
    section?.scrollIntoView({ block: 'start' });
    section?.querySelector<HTMLElement>('input, textarea, select')?.focus({ preventScroll: true });
  }, [location.hash]);

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function leave() {
    if (dirty) setDiscardOpen(true);
    else void navigate(backPath);
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!form.categoryId) return;

    if (base) {
      update.mutate(
        { ...toFields(form), assetTag: form.assetTag.trim(), version: base.version },
        {
          onSuccess: (saved) => {
            toast.success('Asset saved', saved.assetTag);
            void navigate(`/assets/${saved.id}`, { replace: true });
          },
        },
      );
    } else {
      create.mutate(toCreatePayload(form), {
        onSuccess: (saved) => {
          toast.success('Asset created', saved.assetTag);
          void navigate(`/assets/${saved.id}`, { replace: true });
        },
      });
    }
  }

  const canSubmit =
    (form.generateAssetTag || form.assetTag.trim().length > 0) &&
    form.categoryId !== null &&
    isValidBiosGuid(form.biosGuid) &&
    (dirty || !isEdit);

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-5">
      <Breadcrumbs
        items={
          base
            ? [
                { label: 'Assets', to: '/assets' },
                { label: base.assetTag, to: `/assets/${base.id}` },
                { label: 'Edit' },
              ]
            : [{ label: 'Assets', to: '/assets' }, { label: 'New asset' }]
        }
      />

      <header className="flex items-center gap-4">
        {/* Redrawn as the category and model change, so the picture on the
            asset page is never a surprise. */}
        <div className="w-24 shrink-0 rounded-xl bg-device-backdrop p-2 sm:w-28">
          <DeviceIllustration kind={kind} />
        </div>
        <div className="min-w-0">
          <h1 className="truncate text-3xl font-semibold tracking-[-0.02em] text-text">
            {base ? `Edit ${base.assetTag}` : 'New asset'}
          </h1>
          <p className="mt-1 truncate text-base text-text-muted">
            {product ?? category?.name ?? 'Choose a category to begin'}
            <span className="text-text-subtle"> · {DEVICE_KIND_LABELS[kind]}</span>
          </p>
        </div>
      </header>

      {apiError?.isConcurrencyConflict ? (
        <Alert tone="warning" title="Someone else saved this asset first" role="alert">
          <p>
            It changed while you were editing, so your version was not saved. Copy anything
            you need, then load the latest and reapply it.
          </p>
          <Button
            size="sm"
            className="mt-2"
            iconLeft={<RotateCw />}
            onClick={() => void navigate(0)}
          >
            Load the latest
          </Button>
        </Alert>
      ) : apiError?.isDuplicate ? (
        <Alert tone="danger" title="That identifier is already in use" role="alert">
          {apiError.message}
        </Alert>
      ) : error ? (
        <Alert tone="danger" title="Could not save" role="alert">
          {describeError(error)}
        </Alert>
      ) : null}

      <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-sm">
        <FormSection
          id="identity"
          title="Identity"
          description="What the item is and the label stuck on it. Tags and serial numbers are unique across the register."
        >
          <Field
            label="Asset tag"
            required={!form.generateAssetTag}
            hint={
              form.generateAssetTag
                ? tagPolicy.data
                  ? `The server will issue ${tagPolicy.data.nextAssetTag}.`
                  : 'The server will issue the next tag in the sequence.'
                : 'The label on the item.'
            }
            error={apiError?.fieldErrors('assetTag')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={
                  form.generateAssetTag ? (tagPolicy.data?.nextAssetTag ?? '') : form.assetTag
                }
                onChange={(event) => set('assetTag', event.target.value)}
                maxLength={100}
                required={!form.generateAssetTag}
                disabled={form.generateAssetTag}
                mono
                autoFocus={!location.hash}
                placeholder="SCH-000123"
              />
            )}
          </Field>

          {/* Create only. An existing asset is never retagged, so offering it on
              an edit would promise something the API does not do. */}
          {!isEdit ? (
            <div className="flex items-end pb-1">
              <Checkbox
                checked={form.generateAssetTag}
                onCheckedChange={(checked) => set('generateAssetTag', checked)}
                label="Let the server pick the tag"
                description="Uses the next number from the tag policy, consumed only when the asset is created."
              />
            </div>
          ) : (
            <Field label="Device name" hint="Hostname or the name in your management tool.">
              {(controlProps) => (
                <Input
                  {...controlProps}
                  value={form.deviceName}
                  onChange={(event) => set('deviceName', event.target.value)}
                  maxLength={150}
                  mono
                  placeholder="SCH-LAP-0123"
                />
              )}
            </Field>
          )}

          <Field label="Category" required error={apiError?.fieldErrors('categoryId')[0] ?? null}>
            {(controlProps) => (
              <Combobox
                {...controlProps}
                value={form.categoryId}
                onChange={(value) => set('categoryId', value)}
                options={(categories.data?.items ?? []).map((candidate) => ({
                  value: candidate.id,
                  label: candidate.name,
                  description: candidate.code,
                }))}
                loading={categories.isLoading}
                placeholder="Choose a category"
                searchPlaceholder="Search categories…"
              />
            )}
          </Field>

          {!isEdit && (
            <Field label="Device name" hint="Hostname or the name in your management tool.">
              {(controlProps) => (
                <Input
                  {...controlProps}
                  value={form.deviceName}
                  onChange={(event) => set('deviceName', event.target.value)}
                  maxLength={150}
                  mono
                  placeholder="SCH-LAP-0123"
                />
              )}
            </Field>
          )}

          <Field label="Manufacturer">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.manufacturer}
                onChange={(event) => set('manufacturer', event.target.value)}
                maxLength={150}
                placeholder="Dell"
              />
            )}
          </Field>

          <Field label="Model">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.model}
                onChange={(event) => set('model', event.target.value)}
                maxLength={150}
                placeholder="Latitude 5450"
              />
            )}
          </Field>

          <Field
            label="Serial number"
            hint="Unique when present."
            error={apiError?.fieldErrors('serialNumber')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.serialNumber}
                onChange={(event) => set('serialNumber', event.target.value)}
                maxLength={200}
                mono
              />
            )}
          </Field>

          <Field label="Ownership" hint="Who funded or owns it, e.g. School, Trust, Leased.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.ownership}
                onChange={(event) => set('ownership', event.target.value)}
                maxLength={100}
                placeholder="School"
              />
            )}
          </Field>

          <Field label="Legacy ID" hint="Reference from the previous system.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.legacyId}
                onChange={(event) => set('legacyId', event.target.value)}
                maxLength={200}
                mono
              />
            )}
          </Field>
        </FormSection>

        <FormSection
          id="status"
          title="Status and location"
          description={
            statusLocked
              ? 'This asset is out with a holder, so its status follows the loan. Return it to change the status.'
              : 'Use Retire on the asset page to take an asset off the register.'
          }
        >
          <Field label="Status">
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                value={form.status}
                onChange={(event) => set('status', event.target.value as AssetStatus)}
                disabled={statusLocked}
              >
                {statusLocked && <option value="assigned">Assigned</option>}
                {statuses.map((status) => (
                  <option key={status} value={status}>
                    {ASSET_STATUS_LABELS[status]}
                  </option>
                ))}
              </NativeSelect>
            )}
          </Field>

          <Field label="Location">
            {(controlProps) => (
              <Combobox
                {...controlProps}
                value={form.locationId}
                onChange={(value) => set('locationId', value)}
                options={(locations.data?.items ?? []).map((candidate) => ({
                  value: candidate.id,
                  label: candidate.name,
                  description: [candidate.code, candidate.type].filter(Boolean).join(' · '),
                }))}
                loading={locations.isLoading}
                placeholder="No location"
                emptyOptionLabel="No location"
                searchPlaceholder="Search locations…"
              />
            )}
          </Field>

          <SegmentedControl
            className="sm:col-span-2"
            label="Condition"
            value={form.condition}
            onChange={(condition) => set('condition', condition)}
            options={CONDITION_OPTIONS}
          />
        </FormSection>

        <FormSection
          id="hardware"
          title="Hardware and software"
          description="Identifiers the device agent and network tools match on. Leave blank for kit without them."
        >
          <Field
            label="LAN MAC address"
            hint="Wired adapter."
            error={apiError?.fieldErrors('lanMacAddress')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.lanMacAddress}
                onChange={(event) => set('lanMacAddress', event.target.value)}
                maxLength={17}
                mono
                placeholder="A4:BB:6D:1F:20:3C"
              />
            )}
          </Field>

          <Field
            label="BIOS GUID"
            hint="SMBIOS system UUID."
            error={
              apiError?.fieldErrors('biosGuid')[0] ??
              (isValidBiosGuid(form.biosGuid) ? null : 'Enter a full UUID, or leave this empty.')
            }
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.biosGuid}
                onChange={(event) => set('biosGuid', event.target.value)}
                maxLength={36}
                mono
                placeholder="4c4c4544-0037-5010-8046-b7c04f503332"
              />
            )}
          </Field>

          <Field label="Operating system">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.operatingSystem}
                onChange={(event) => set('operatingSystem', event.target.value)}
                maxLength={100}
                placeholder="Windows 11 Pro"
              />
            )}
          </Field>

          <Field label="OS version">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.operatingSystemVersion}
                onChange={(event) => set('operatingSystemVersion', event.target.value)}
                maxLength={100}
                mono
                placeholder="24H2"
              />
            )}
          </Field>

          <SegmentedControl
            className="sm:col-span-2"
            label="Windows 11 ready"
            value={form.windows11Compliant}
            onChange={(value) => set('windows11Compliant', value)}
            options={WINDOWS_11_OPTIONS}
          />
        </FormSection>

        <FormSection id="purchase" title="Purchase">
          <Field label="Purchase date">
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                value={form.purchaseDate}
                onChange={(event) => set('purchaseDate', event.target.value)}
              />
            )}
          </Field>

          <div className="grid grid-cols-[1fr_6rem] gap-3">
            <Field
              label="Cost"
              hint="Up to two decimal places."
              error={apiError?.fieldErrors('purchaseCost')[0] ?? null}
            >
              {(controlProps) => (
                <Input
                  {...controlProps}
                  type="number"
                  min="0"
                  step="0.01"
                  value={form.purchaseCost}
                  onChange={(event) => set('purchaseCost', event.target.value)}
                  placeholder="899.00"
                />
              )}
            </Field>
            <Field label="Currency">
              {(controlProps) => (
                <Input
                  {...controlProps}
                  value={form.currency}
                  onChange={(event) => set('currency', event.target.value.toUpperCase())}
                  maxLength={3}
                  minLength={3}
                  mono
                  placeholder="GBP"
                />
              )}
            </Field>
          </div>

          <Field label="Supplier" className="sm:col-span-2">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={form.supplier}
                onChange={(event) => set('supplier', event.target.value)}
                maxLength={200}
              />
            )}
          </Field>
        </FormSection>

        <FormSection
          id="notes"
          title="Description and notes"
          description="Notes stay with the record. Anything worth keeping as history belongs in a note on the Activity tab instead."
        >
          <Field
            label="Description"
            hint="What this item is, in words. Carried over from imported registers."
            className="sm:col-span-2"
          >
            {(controlProps) => (
              <Textarea
                {...controlProps}
                value={form.description}
                onChange={(event) => set('description', event.target.value)}
                maxLength={2000}
                rows={2}
                placeholder="14-inch staff laptop"
              />
            )}
          </Field>

          <Field label="Notes" className="sm:col-span-2">
            {(controlProps) => (
              <Textarea
                {...controlProps}
                value={form.notes}
                onChange={(event) => set('notes', event.target.value)}
                maxLength={4000}
                rows={4}
                placeholder="Anything the next person should know about this item."
              />
            )}
          </Field>
        </FormSection>
      </div>

      {/* Pinned to the bottom of the scroll area, so saving never needs a trip
          back up a long form. */}
      <div className="sticky bottom-0 z-20 -mx-4 -mb-5 flex items-center justify-end gap-2 border-t border-border bg-canvas/90 px-4 py-3 backdrop-blur-md sm:-mx-6 sm:px-6">
        <p className="mr-auto text-sm text-text-muted" aria-live="polite">
          {dirty ? 'Unsaved changes' : isEdit ? 'No changes yet' : ''}
        </p>
        <Button onClick={leave} disabled={mutation.isPending}>
          Cancel
        </Button>
        <Button
          type="submit"
          variant="primary"
          iconLeft={<Save />}
          loading={mutation.isPending}
          disabled={!canSubmit}
        >
          {isEdit ? 'Save changes' : 'Create asset'}
        </Button>
      </div>

      <ConfirmDialog
        open={discardOpen}
        onOpenChange={setDiscardOpen}
        title="Discard your changes?"
        description="Nothing you changed on this page has been saved."
        confirmLabel="Discard changes"
        destructive
        onConfirm={() => {
          setDiscardOpen(false);
          void navigate(backPath);
        }}
      />
    </form>
  );
}

/**
 * Settings-style section: the heading and its explanation on the left, the
 * fields on the right, rules between sections instead of a box around each.
 */
function FormSection({
  id,
  title,
  description,
  children,
}: {
  id: string;
  title: string;
  description?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section
      id={id}
      // Clears the sticky top bar when jumped to from a link.
      className="grid scroll-mt-20 gap-x-8 gap-y-4 border-t border-border p-5 first:border-t-0 lg:grid-cols-[16rem_minmax(0,1fr)]"
    >
      <div>
        <h2 className="text-lg font-semibold tracking-[-0.01em] text-text">{title}</h2>
        {description && <p className="mt-1 text-sm text-text-muted">{description}</p>}
      </div>
      <div className="grid content-start gap-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}
