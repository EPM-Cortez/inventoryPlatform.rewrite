import { ClipboardList } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';

import { useCategories, useLocations, useStartStocktake } from '@/api/hooks';
import type { StocktakeScope } from '@/api/stocktakeTypes';
import { ApiError, describeError } from '@/api/problem';
import { cn } from '@/lib/cn';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

const SCOPES: { value: StocktakeScope; label: string; detail: string }[] = [
  {
    value: 'location',
    label: 'One location',
    detail: 'A room, block or store. The usual choice — you can walk it in one go.',
  },
  {
    value: 'category',
    label: 'One category',
    detail: 'Every laptop, say, wherever it lives. Good for auditing a class of kit.',
  },
  {
    value: 'all',
    label: 'Everything',
    detail: 'The whole register. Only realistic for a small site or a full audit.',
  },
];

export function StartStocktakeDialog({ onClose }: { onClose: () => void }) {
  const [name, setName] = useState('');
  const [scope, setScope] = useState<StocktakeScope>('location');
  const [locationId, setLocationId] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);

  const locations = useLocations();
  const categories = useCategories();
  const start = useStartStocktake();
  const navigate = useNavigate();
  const toast = useToast();

  const needsLocation = scope === 'location';
  const needsCategory = scope === 'category';
  const targetChosen =
    (needsLocation && locationId) || (needsCategory && categoryId) || scope === 'all';

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    start.mutate(
      {
        name: name.trim(),
        scope,
        locationId: needsLocation ? locationId : null,
        categoryId: needsCategory ? categoryId : null,
      },
      {
        onSuccess: (session) => {
          toast.success('Stocktake started', session.name);
          onClose();
          void navigate(`/stocktakes/${session.id}`);
        },
      },
    );
  }

  const error = start.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'start-stocktake-form';

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="Start a stocktake"
      size="md"
      dismissible={!start.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={start.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant="primary"
            iconLeft={<ClipboardList />}
            loading={start.isPending}
            disabled={!name.trim() || !targetChosen}
          >
            Start
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.slug === 'stocktake-duplicate-active' ? (
          <Alert tone="danger" title="One is already open for this scope" role="alert">
            Finish or cancel the existing stocktake before starting another for the same
            place — two open at once would double-count what is found.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not start the stocktake" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <Field label="Name" required hint="What this count is, for the record.">
          {(controlProps) => (
            <Input
              {...controlProps}
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={200}
              required
              autoFocus
              placeholder="ICT Suite 1 — summer audit"
            />
          )}
        </Field>

        <div>
          <p className="mb-2 text-sm font-medium text-text">What to count</p>
          <div role="radiogroup" aria-label="Scope" className="flex flex-col gap-2">
            {SCOPES.map((option) => (
              <button
                key={option.value}
                type="button"
                role="radio"
                aria-checked={scope === option.value}
                onClick={() => setScope(option.value)}
                className={cn(
                  'rounded-lg border p-3 text-left transition-colors',
                  scope === option.value
                    ? 'border-accent bg-accent-soft/40'
                    : 'border-border hover:border-border-strong hover:bg-surface-hover',
                )}
              >
                <span className="block text-base font-medium text-text">
                  {option.label}
                </span>
                <span className="block text-sm text-text-muted">{option.detail}</span>
              </button>
            ))}
          </div>
        </div>

        {needsLocation && (
          <Field label="Location" required>
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
              />
            )}
          </Field>
        )}

        {needsCategory && (
          <Field label="Category" required>
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

        <Alert tone="info" title="Counting changes nothing on its own">
          Scanning records what you found against this stocktake. No asset moves
          location, changes condition or changes status because of it — those stay as
          they are until someone edits them deliberately.
        </Alert>
      </form>
    </Dialog>
  );
}
