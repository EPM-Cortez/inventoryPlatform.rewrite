import { useMemo, useState, type FormEvent } from 'react';
import { Link } from 'react-router';

import { useCreateLocation, useLocations, useUpdateLocation } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Location } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

import { descendantsOf } from './locationTree';

export interface LocationFormDialogProps {
  /** Omitted when creating. */
  location?: Location;
  /** Pre-selects "Inside" when creating, e.g. a new room from its building. */
  defaultParentId?: string | null;
  onClose: () => void;
  /** Lets the page select the location it just created. */
  onCreated?: (location: Location) => void;
}

export function LocationFormDialog({
  location,
  defaultParentId = null,
  onClose,
  onCreated,
}: LocationFormDialogProps) {
  const editing = location !== undefined;

  // Deliberately the *unfiltered* list rather than whatever the page is showing:
  // the location being edited may already sit inside an inactive parent, and
  // dropping that parent from the options would silently re-home it on save.
  const all = useLocations(true);
  const locations = useMemo(() => all.data?.items ?? [], [all.data]);

  const [code, setCode] = useState(location?.code ?? '');
  const [name, setName] = useState(location?.name ?? '');
  const [type, setType] = useState(location?.type ?? '');
  const [parentLocationId, setParentLocationId] = useState<string | null>(
    location ? location.parentLocationId : defaultParentId,
  );
  const [isActive, setIsActive] = useState(location?.isActive ?? true);

  const create = useCreateLocation();
  const update = useUpdateLocation();
  const toast = useToast();
  const mutation = editing ? update : create;

  const parentOptions = useMemo(() => {
    const excluded = location ? descendantsOf(location.id, locations) : new Set<string>();
    return locations
      .filter((candidate) => !excluded.has(candidate.id))
      .map((candidate) => ({
        value: candidate.id,
        label: candidate.name,
        description: [candidate.code, candidate.type, !candidate.isActive && 'Inactive']
          .filter(Boolean)
          .join(' · '),
      }));
  }, [location, locations]);

  const typeSuggestions = useMemo(() => {
    const seen = new Map<string, string>();
    for (const candidate of ['Building', 'Block', 'Floor', 'Room', 'Store']) {
      seen.set(candidate.toLowerCase(), candidate);
    }
    for (const candidate of locations) {
      const value = candidate.type?.trim();
      if (value && !seen.has(value.toLowerCase())) seen.set(value.toLowerCase(), value);
    }
    return [...seen.values()].sort((a, b) => a.localeCompare(b));
  }, [locations]);

  // An active location cannot sit inside an inactive one, so the pairing is
  // caught here rather than as a 400 from the server.
  const parent = locations.find((candidate) => candidate.id === parentLocationId);
  const parentIsInactive = Boolean(parent && !parent.isActive && isActive);

  const deactivating = editing && location.isActive && !isActive;
  // The server refuses to deactivate a location that still has active children.
  const blockedByChildren = deactivating
    ? locations.filter(
        (candidate) => candidate.parentLocationId === location.id && candidate.isActive,
      )
    : [];

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const shared = {
      code: code.trim(),
      name: name.trim(),
      type: type.trim() || null,
      parentLocationId,
    };

    if (location) {
      update.mutate(
        { locationId: location.id, ...shared, isActive, version: location.version },
        {
          onSuccess: (saved) => {
            toast.success('Location saved', saved.name);
            onClose();
          },
        },
      );
    } else {
      create.mutate(shared, {
        onSuccess: (saved) => {
          toast.success('Location created', saved.name);
          onCreated?.(saved);
          onClose();
        },
      });
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'location-form';
  const blocked = parentIsInactive || blockedByChildren.length > 0;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={editing ? `Edit ${location.name}` : defaultParentId ? 'New sub-location' : 'New location'}
      size="md"
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
            disabled={!code.trim() || !name.trim() || blocked}
          >
            {editing ? 'Save changes' : 'Create location'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="danger" title="Someone else changed this location" role="alert">
            Close this and reopen it to pick up their version, then re-apply your edit.
          </Alert>
        ) : apiError?.isDuplicate ? (
          <Alert tone="danger" title="That code is already in use" role="alert">
            {apiError.message}
          </Alert>
        ) : error ? (
          <Alert
            tone="danger"
            title={editing ? 'Could not save' : 'Could not create'}
            role="alert"
          >
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="grid gap-4 sm:grid-cols-[1fr_11rem]">
          <Field label="Name" required>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={name}
                onChange={(event) => setName(event.target.value)}
                maxLength={150}
                required
                autoFocus
                placeholder="ICT Suite 1"
              />
            )}
          </Field>

          <Field label="Code" required hint="Unique short code.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={code}
                onChange={(event) => setCode(event.target.value.toUpperCase())}
                maxLength={50}
                required
                mono
                placeholder="B2-105"
              />
            )}
          </Field>
        </div>

        <Field label="Type" hint="Free text, e.g. Building, Room, Store.">
          {(controlProps) => (
            <Input
              {...controlProps}
              value={type}
              onChange={(event) => setType(event.target.value)}
              maxLength={80}
              placeholder="Room"
              list="location-type-suggestions"
            />
          )}
        </Field>
        {/* Suggestions only: the field stays free text, but offering the types
            already in use keeps "Room" from drifting into "room" and "Rm". */}
        <datalist id="location-type-suggestions">
          {typeSuggestions.map((suggestion) => (
            <option key={suggestion} value={suggestion} />
          ))}
        </datalist>

        <Field
          label="Inside"
          hint={
            editing
              ? 'Leave empty for a top-level location. This location and anything under it are not offered.'
              : 'Leave empty for a top-level location.'
          }
          error={
            parentIsInactive
              ? 'An active location cannot sit inside an inactive one.'
              : undefined
          }
        >
          {(controlProps) => (
            <Combobox
              {...controlProps}
              value={parentLocationId}
              onChange={setParentLocationId}
              options={parentOptions}
              loading={all.isLoading}
              placeholder="No parent"
              emptyOptionLabel="No parent"
              searchPlaceholder="Search locations…"
            />
          )}
        </Field>

        {editing && (
          <div className="flex flex-col gap-2 border-t border-border pt-4">
            <Checkbox
              checked={isActive}
              onCheckedChange={setIsActive}
              label="Active"
              description="Inactive locations cannot be chosen for an asset."
            />
            {blockedByChildren.length > 0 ? (
              // The server refuses this outright with `location_has_active_children`.
              <Alert tone="danger" title="Deal with what is inside this location first">
                {blockedByChildren.length === 1
                  ? `${blockedByChildren[0]!.name} is still active inside this location.`
                  : `${blockedByChildren.length} active locations are still inside this one.`}{' '}
                Move or deactivate them, then come back.
              </Alert>
            ) : deactivating ? (
              <Alert tone="warning" title="Assets here will be locked">
                Existing assets keep this location, but nobody will be able to save any
                edit to them until they are moved somewhere active.{' '}
                <Link
                  to={`/assets?locationId=${location.id}`}
                  className="font-medium underline"
                >
                  Check which assets are affected
                </Link>
                .
              </Alert>
            ) : null}
          </div>
        )}
      </form>
    </Dialog>
  );
}
