import { ScanLine } from 'lucide-react';
import { useRef, useState, type FormEvent } from 'react';

import { useRecordObservation } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { StocktakeItem, StocktakeSession } from '@/api/stocktakeTypes';
import { ASSET_CONDITIONS, type AssetCondition } from '@/api/types';
import { ASSET_CONDITION_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { Combobox } from '@/ui/Combobox';
import { Field, Input, NativeSelect } from '@/ui/Field';

import { itemDiscrepancies } from './stocktakeLabels';

export interface ScanPanelProps {
  session: StocktakeSession;
  locations: { id: string; name: string; code: string }[];
  locationsLoading: boolean;
}

/**
 * Where assets are recorded as seen.
 *
 * Built for someone standing in a room with a scanner: the tag field keeps focus
 * and clears itself after every entry, so a barcode wedge can fire one after
 * another without a hand leaving the trolley. The location is set once for the
 * room and stays put between scans.
 */
export function ScanPanel({ session, locations, locationsLoading }: ScanPanelProps) {
  const [assetTag, setAssetTag] = useState('');
  const [observedLocationId, setObservedLocationId] = useState<string | null>(
    session.scopeLocation?.id ?? null,
  );
  // The API documents this as nullable but the service rejects null, so every
  // scan has to assert a condition. It behaves like the location: set once for
  // the run, changed only for the odd item that differs.
  const [observedCondition, setObservedCondition] = useState<AssetCondition>('good');
  const [note, setNote] = useState('');
  const [recent, setRecent] = useState<{ item: StocktakeItem; changed: boolean }[]>([]);

  const tagInput = useRef<HTMLInputElement>(null);
  const observe = useRecordObservation(session.id);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const tag = assetTag.trim();
    if (!tag || !observedLocationId) return;

    observe.mutate(
      {
        assetTag: tag,
        observedLocationId,
        observedCondition,
        note: note.trim() || null,
        // The session version, not the asset's. Two people scanning the same
        // session need the server to serialise them.
        version: session.version,
      },
      {
        onSuccess: (result) => {
          // Keep the newest first and cap it: this is a running confirmation,
          // not a log — the item table below is the record.
          setRecent((current) =>
            [{ item: result.item, changed: result.changed }, ...current].slice(0, 8),
          );
          setAssetTag('');
          setNote('');
          tagInput.current?.focus();
        },
      },
    );
  }

  const error = observe.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Card>
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <ScanLine className="size-4 text-text-subtle" aria-hidden />
            Record what you find
          </span>
        }
        description="Scan or type a tag. The location stays set between scans."
      />
      <CardBody className="flex flex-col gap-4">
        <form onSubmit={onSubmit} className="flex flex-col gap-4">
          {apiError?.isConcurrencyConflict ? (
            <Alert tone="danger" title="Someone else scanned at the same moment" role="alert">
              Reload the page to pick up their scans, then carry on. Nothing you recorded
              before this is lost.
            </Alert>
          ) : apiError?.isNotFound ? (
            <Alert tone="warning" title="No asset has that tag" role="alert">
              Check the label. If the item genuinely is not on the register, add it as an
              asset first — a stocktake records what it finds, it does not create assets.
            </Alert>
          ) : error ? (
            <Alert tone="danger" title="Could not record that" role="alert">
              {describeError(error)}
            </Alert>
          ) : null}

          <Field label="Where you are" required hint="Applied to every scan below.">
            {(controlProps) => (
              <Combobox
                {...controlProps}
                value={observedLocationId}
                onChange={setObservedLocationId}
                options={locations.map((location) => ({
                  value: location.id,
                  label: location.name,
                  description: location.code,
                }))}
                loading={locationsLoading}
                placeholder="Choose the location you are counting"
                searchPlaceholder="Search locations…"
              />
            )}
          </Field>

          <div className="grid gap-3 sm:grid-cols-[1fr_12rem]">
            <Field label="Asset tag" required>
              {(controlProps) => (
                <Input
                  {...controlProps}
                  ref={tagInput}
                  value={assetTag}
                  onChange={(event) => setAssetTag(event.target.value)}
                  maxLength={100}
                  required
                  autoFocus
                  mono
                  placeholder="Scan or type a tag"
                  disabled={!observedLocationId}
                />
              )}
            </Field>

            <Field
              label="Condition"
              required
              hint="Recorded on every scan. Change it for an item that differs."
            >
              {(controlProps) => (
                <NativeSelect
                  {...controlProps}
                  value={observedCondition}
                  onChange={(event) =>
                    setObservedCondition(event.target.value as AssetCondition)
                  }
                >
                  {ASSET_CONDITIONS.map((condition) => (
                    <option key={condition} value={condition}>
                      {ASSET_CONDITION_LABELS[condition]}
                    </option>
                  ))}
                </NativeSelect>
              )}
            </Field>
          </div>

          <Field label="Note" hint="Optional, recorded against this sighting.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={note}
                onChange={(event) => setNote(event.target.value)}
                maxLength={1000}
                placeholder="Screen cracked, left with the technician…"
              />
            )}
          </Field>

          <div className="flex justify-end">
            <Button
              type="submit"
              variant="primary"
              loading={observe.isPending}
              disabled={!assetTag.trim() || !observedLocationId}
            >
              Record it
            </Button>
          </div>
        </form>

        {recent.length > 0 && (
          <div className="border-t border-border pt-3">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-text-subtle">
              Just recorded
            </p>
            <ul className="flex flex-col gap-1.5">
              {recent.map(({ item, changed }, index) => {
                const flags = itemDiscrepancies(item);
                return (
                  <li
                    key={`${item.assetId}-${index}`}
                    className="flex flex-wrap items-center gap-2 text-sm"
                  >
                    <span className="font-mono text-text">{item.assetTag}</span>
                    <span className="truncate text-text-muted">
                      {item.deviceName ?? item.category.name}
                    </span>
                    {!changed && (
                      // Worth saying: a scanner that fires twice is common, and
                      // silence would look like the second scan did nothing wrong.
                      <Badge tone="neutral">Already recorded</Badge>
                    )}
                    {flags.map((flag) => (
                      <Badge key={flag} tone="warning">
                        {flag}
                      </Badge>
                    ))}
                  </li>
                );
              })}
            </ul>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
