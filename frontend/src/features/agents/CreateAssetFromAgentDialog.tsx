import { Sparkles } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';

import type { AgentDeviceSnapshot, ManagedAgentListItem } from '@/api/agentTypes';
import {
  useAssetTagPolicy,
  useCategories,
  useCreateAndLinkAgentAsset,
  useLocations,
} from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import { ASSET_CONDITIONS, type AssetCondition } from '@/api/types';
import { ASSET_CONDITION_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, NativeSelect, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

import { buildCreateAndLinkRequest, seedFromAgent } from './agentAssetSeed';

export interface CreateAssetFromAgentDialogProps {
  agent: ManagedAgentListItem;
  /** Absent until the machine has checked in at least once. */
  snapshot?: AgentDeviceSnapshot | null;
  onClose: () => void;
}

/**
 * Creates an asset for a machine that has none, and links the agent to it in one
 * server-side transaction.
 *
 * The fields are seeded from what the machine reports, but they are the
 * operator's to accept or change — that is the whole distinction between this and
 * the agent writing to the register itself. Seeded values are marked so nobody
 * mistakes an unreviewed guess for something a person entered.
 *
 * The agent's `version` goes with the request, so if the machine changed
 * underneath — someone else linked it, or it was revoked — the whole thing is
 * rejected and no orphan asset is left behind.
 */
export function CreateAssetFromAgentDialog({
  agent,
  snapshot,
  onClose,
}: CreateAssetFromAgentDialogProps) {
  const toast = useToast();
  const navigate = useNavigate();
  const create = useCreateAndLinkAgentAsset();
  // Inactive categories and locations are excluded: the server re-validates both
  // on write, so offering one would build a request it is going to reject.
  const categories = useCategories();
  const locations = useLocations();
  const tagPolicy = useAssetTagPolicy();

  const seed = seedFromAgent(agent);
  const [generateAssetTag, setGenerateAssetTag] = useState(true);
  const [assetTag, setAssetTag] = useState('');
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [locationId, setLocationId] = useState<string | null>(null);
  const [condition, setCondition] = useState<AssetCondition>('good');
  const [deviceName, setDeviceName] = useState(seed.deviceName);
  const [manufacturer, setManufacturer] = useState(seed.manufacturer);
  const [model, setModel] = useState(seed.model);
  const [serialNumber, setSerialNumber] = useState(seed.serialNumber);
  const [reason, setReason] = useState('');

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!categoryId) return;
    if (!generateAssetTag && !assetTag.trim()) return;

    const body = buildCreateAndLinkRequest(
      {
        generateAssetTag,
        assetTag,
        categoryId,
        locationId,
        condition,
        deviceName,
        manufacturer,
        model,
        serialNumber,
        reason,
      },
      agent,
      snapshot,
    );

    create.mutate(
      { agentId: agent.id, body },
      {
        onSuccess: (result) => {
          toast.success('Asset created and linked', result.asset.assetTag);
          onClose();
          void navigate(`/assets/${result.asset.id}`);
        },
      },
    );
  }

  const error = create.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'create-and-link-agent-asset-form';
  const seeded = 'Seeded from what the machine reports. Change it if it is wrong.';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Create an asset for this machine"
      description={agent.hostname}
      dismissible={!create.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={create.isPending}
            disabled={!categoryId || (!generateAssetTag && !assetTag.trim())}
          >
            Create and link
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This device just changed" role="alert">
            No asset was created. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : apiError?.isDuplicate ? (
          <Alert tone="danger" title="That identifier is already in use" role="alert">
            {apiError.message} Nothing was created — if the machine is already on the
            register, link it to the existing asset instead.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not create the asset" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="flex items-start gap-2 rounded-md border border-border bg-surface-sunken/60 p-3 text-sm text-text-muted">
          <Sparkles className="mt-0.5 size-4 shrink-0 text-accent" aria-hidden="true" />
          <p>
            These values come from the machine and have not been checked by anyone. Read
            them before creating the asset — from then on they are the register&rsquo;s,
            and the agent will not change them.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label="Asset tag"
            required={!generateAssetTag}
            hint={
              generateAssetTag
                ? tagPolicy.data
                  ? `The server will issue ${tagPolicy.data.nextAssetTag}.`
                  : 'The server will issue the next tag in the sequence.'
                : 'The label on the item. Case-insensitively unique.'
            }
            error={apiError?.fieldErrors('assetTag')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={generateAssetTag ? (tagPolicy.data?.nextAssetTag ?? '') : assetTag}
                onChange={(event) => setAssetTag(event.target.value)}
                maxLength={100}
                required={!generateAssetTag}
                disabled={generateAssetTag}
                mono
                placeholder="SCH-000123"
              />
            )}
          </Field>

          <Checkbox
            checked={generateAssetTag}
            onCheckedChange={setGenerateAssetTag}
            label="Let the server pick the tag"
            description="Uses the next number from the asset tag policy. The number is only consumed once the asset is created."
          />

          <Field
            label="Category"
            required
            error={apiError?.fieldErrors('categoryId')[0] ?? null}
          >
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

          <Field label="Location" hint="Where the register should say it lives.">
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
                placeholder="Choose a location"
                searchPlaceholder="Search locations…"
                emptyOptionLabel="No location"
              />
            )}
          </Field>

          <Field label="Device name" hint={seeded}>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={deviceName}
                onChange={(event) => setDeviceName(event.target.value)}
                maxLength={150}
                mono
              />
            )}
          </Field>

          <Field label="Condition">
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

          <Field label="Manufacturer" hint={agent.manufacturer ? seeded : undefined}>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={manufacturer}
                onChange={(event) => setManufacturer(event.target.value)}
                maxLength={100}
              />
            )}
          </Field>

          <Field label="Model" hint={agent.model ? seeded : undefined}>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={model}
                onChange={(event) => setModel(event.target.value)}
                maxLength={100}
              />
            )}
          </Field>

          <Field
            label="Serial number"
            hint={agent.serialNumber ? seeded : 'The machine did not report one.'}
            error={apiError?.fieldErrors('serialNumber')[0] ?? null}
            className="sm:col-span-2"
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={serialNumber}
                onChange={(event) => setSerialNumber(event.target.value)}
                maxLength={100}
                mono
              />
            )}
          </Field>
        </div>

        <Field label="Reason" hint="Recorded in the audit trail.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              rows={2}
              placeholder="New machine picked up from the rollout"
            />
          )}
        </Field>

        <p className="text-xs text-text-subtle">
          The BIOS UUID, MAC address and operating system are copied from the latest
          snapshot as well. They are how the server recognises this machine on its next
          check-in.
        </p>
      </form>
    </Dialog>
  );
}
