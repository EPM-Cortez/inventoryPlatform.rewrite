import { useState, type FormEvent } from 'react';

import { useAssets, useLinkAgent } from '@/api/hooks';
import type { ManagedAgentListItem } from '@/api/agentTypes';
import { ApiError, describeError } from '@/api/problem';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface LinkAgentDialogProps {
  onClose: () => void;
  agent: ManagedAgentListItem;
}

/**
 * Ties a reporting machine to an asset on the register.
 *
 * This is a trust decision, not a lookup: it says "the hardware reporting this
 * serial number *is* that asset tag". Getting it wrong attaches one machine's
 * evidence to another's record, so the dialog shows what the machine says about
 * itself to help the operator confirm the match.
 */
export function LinkAgentDialog({ onClose, agent }: LinkAgentDialogProps) {
  const [assetId, setAssetId] = useState<string | null>(agent.linkedAssetId);
  const [search, setSearch] = useState('');
  const [reason, setReason] = useState('');
  const toast = useToast();

  const link = useLinkAgent();
  // Search by the machine's own serial first — usually the fastest way to find
  // the right asset, since the register often records the same serial.
  const assets = useAssets({
    q: search || agent.serialNumber || undefined,
    pageSize: 25,
    sort: 'assetTag',
  });

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!assetId) return;

    link.mutate(
      {
        agentId: agent.id,
        body: { assetId, reason: reason.trim() || null, version: agent.version },
      },
      {
        onSuccess: (updated) => {
          toast.success('Device linked', updated.linkedAssetTag ?? agent.hostname);
          onClose();
        },
      },
    );
  }

  const error = link.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'link-agent-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Link device to an asset"
      description={agent.hostname}
      dismissible={!link.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={link.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            loading={link.isPending}
            disabled={!assetId || assetId === agent.linkedAssetId}
          >
            Link device
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This device just changed" role="alert">
            Close and reopen this dialog to work from the current state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not link" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="rounded-md border border-border bg-surface-sunken/60 p-3">
          <p className="text-xs font-medium uppercase tracking-wide text-text-subtle">
            What the machine reports
          </p>
          <dl className="mt-2 grid gap-2 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-text-subtle">Hostname</dt>
              <dd className="font-mono text-text">{agent.hostname}</dd>
            </div>
            <div>
              <dt className="text-text-subtle">Serial number</dt>
              <dd className="font-mono text-text">{agent.serialNumber ?? '—'}</dd>
            </div>
          </dl>
          <p className="mt-2 text-xs text-text-muted">
            Check these match the asset you pick — linking attaches this machine&rsquo;s
            hardware and software record to that asset.
          </p>
        </div>

        <Field label="Asset" required>
          {(controlProps) => (
            <Combobox
              {...controlProps}
              value={assetId}
              onChange={setAssetId}
              options={(assets.data?.items ?? []).map((asset) => ({
                value: asset.id,
                label: asset.assetTag,
                description:
                  [asset.manufacturer, asset.model].filter(Boolean).join(' ') ||
                  asset.category.name,
              }))}
              loading={assets.isFetching}
              onSearchChange={setSearch}
              serverFiltered
              placeholder="Choose an asset"
              searchPlaceholder="Search by tag, serial or model…"
            />
          )}
        </Field>

        <Field label="Reason" hint="Recorded in the audit trail.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              rows={2}
              placeholder="Matched by serial number during rollout"
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
