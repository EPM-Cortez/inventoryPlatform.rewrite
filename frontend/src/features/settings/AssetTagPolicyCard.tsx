import { Hash, Save } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { useAssetTagPolicy, useUpdateAssetTagPolicy } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { AssetTagPolicy } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { ErrorState } from '@/ui/EmptyState';
import { Field, Input } from '@/ui/Field';
import { LoadingBlock } from '@/ui/Spinner';
import { useToast } from '@/ui/Toast';

const MIN_WIDTH = 2;
const MAX_WIDTH = 12;
const MAX_NEXT_NUMBER = 999_999_999_999;

/**
 * How the server composes a generated asset tag.
 *
 * Separately permissioned from the rest of settings (`asset-tags.manage`), so it
 * renders on its own rather than inside the school settings form.
 */
export function AssetTagPolicyCard() {
  const { can } = useSession();
  const policy = useAssetTagPolicy(can(Permission.AssetTagsManage));

  if (!can(Permission.AssetTagsManage)) return null;
  if (policy.isLoading) return <LoadingBlock label="Loading asset tag policy…" />;
  if (policy.isError) {
    return <ErrorState error={policy.error} onRetry={() => void policy.refetch()} />;
  }
  if (!policy.data) return null;

  // Remounts on save so the form picks up the version the server just issued.
  return <PolicyForm key={policy.data.version} policy={policy.data} />;
}

function PolicyForm({ policy }: { policy: AssetTagPolicy }) {
  const [prefix, setPrefix] = useState(policy.prefix);
  const [numericWidth, setNumericWidth] = useState(String(policy.numericWidth));
  const [nextNumber, setNextNumber] = useState(String(policy.nextNumber));

  const update = useUpdateAssetTagPolicy();
  const toast = useToast();

  const width = Number(numericWidth);
  const next = Number(nextNumber);
  const widthValid = Number.isInteger(width) && width >= MIN_WIDTH && width <= MAX_WIDTH;
  const nextValid = Number.isInteger(next) && next >= 1 && next <= MAX_NEXT_NUMBER;

  // Composed the same way the server does, so the preview is not a guess.
  const preview = widthValid && nextValid ? prefix + String(next).padStart(width, '0') : null;
  // A number that no longer fits its width is the one way to produce a tag that
  // breaks the sequence's sort order, so it is worth saying out loud.
  const overflows = widthValid && nextValid && String(next).length > width;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    update.mutate(
      {
        prefix: prefix.trim(),
        numericWidth: width,
        nextNumber: next,
        version: policy.version,
      },
      { onSuccess: () => toast.success('Asset tag policy saved') },
    );
  }

  const error = update.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Card>
      <CardHeader
        title="Asset tag policy"
        description="How a tag is composed when the server issues one."
      />
      <CardBody>
        <form onSubmit={onSubmit} className="flex flex-col gap-4">
          {apiError?.isConcurrencyConflict ? (
            <Alert tone="danger" title="Someone else changed this policy" role="alert">
              Reload the page to pick up their version, then re-apply your change.
            </Alert>
          ) : error ? (
            <Alert tone="danger" title="Could not save" role="alert">
              {describeError(error)}
            </Alert>
          ) : null}

          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Prefix" hint="Text before the number. May be empty.">
              {(controlProps) => (
                <Input
                  {...controlProps}
                  value={prefix}
                  onChange={(event) => setPrefix(event.target.value)}
                  maxLength={50}
                  mono
                  placeholder="SCH-"
                />
              )}
            </Field>

            <Field
              label="Number width"
              required
              hint={`Digits, padded with zeros. ${MIN_WIDTH} to ${MAX_WIDTH}.`}
              error={
                numericWidth !== '' && !widthValid
                  ? `Enter a whole number between ${MIN_WIDTH} and ${MAX_WIDTH}.`
                  : undefined
              }
            >
              {(controlProps) => (
                <Input
                  {...controlProps}
                  type="number"
                  min={MIN_WIDTH}
                  max={MAX_WIDTH}
                  step={1}
                  value={numericWidth}
                  onChange={(event) => setNumericWidth(event.target.value)}
                  required
                />
              )}
            </Field>

            <Field
              label="Next number"
              required
              hint="The number the next generated tag will use."
              error={
                nextNumber !== '' && !nextValid
                  ? `Enter a whole number between 1 and ${formatNumber(MAX_NEXT_NUMBER)}.`
                  : undefined
              }
            >
              {(controlProps) => (
                <Input
                  {...controlProps}
                  type="number"
                  min={1}
                  max={MAX_NEXT_NUMBER}
                  step={1}
                  value={nextNumber}
                  onChange={(event) => setNextNumber(event.target.value)}
                  required
                />
              )}
            </Field>
          </div>

          <div className="flex flex-wrap items-center gap-3 rounded-md border border-border bg-surface-sunken p-3">
            <Hash className="size-4 shrink-0 text-text-subtle" aria-hidden />
            <div className="min-w-0">
              <p className="text-xs text-text-subtle">Next tag will be</p>
              <p className="font-mono text-lg font-semibold text-text">
                {preview ?? '—'}
              </p>
            </div>
            {policy.nextAssetTag !== preview && preview && (
              <p className="text-xs text-text-muted">
                Currently <code className="font-mono">{policy.nextAssetTag}</code>
              </p>
            )}
          </div>

          {overflows && (
            <Alert tone="warning" title="The number is wider than the padding">
              {formatNumber(next)} has more digits than the width allows, so the tag will
              be longer than the ones before it and will not sort alongside them.
            </Alert>
          )}

          <Alert tone="info" title="Existing assets keep the tags they have">
            This only affects tags issued from now on. Nothing is renamed, and labels
            already stuck to equipment stay correct.
          </Alert>

          <div className="flex justify-end">
            <Button
              type="submit"
              variant="primary"
              iconLeft={<Save />}
              loading={update.isPending}
              disabled={!widthValid || !nextValid}
            >
              Save tag policy
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  );
}
