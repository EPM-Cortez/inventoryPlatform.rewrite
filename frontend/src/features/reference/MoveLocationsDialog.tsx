import { FolderInput } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';

import { useCreateLocation, useUpdateLocation } from '@/api/hooks';
import { describeError } from '@/api/problem';
import type { Location } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { SegmentedControl } from '@/ui/SegmentedControl';
import { useToast } from '@/ui/Toast';

import { ancestorsOf, compareLocations, descendantsOf } from './locationTree';

type Mode = 'existing' | 'new';

interface Failure {
  location: Location;
  message: string;
}

export interface MoveLocationsDialogProps {
  /** What to move. Captured on open, so the versions sent are the ones seen. */
  moving: Location[];
  /** Every location, active or not, for the destination picker. */
  all: Location[];
  onClose: () => void;
  /** Called with the destination (null for top level) once anything moved. */
  onMoved: (parentId: string | null) => void;
}

/**
 * Re-parents several locations at once — the quick way to turn a flat imported
 * list of rooms into buildings. There is no bulk endpoint, so each location is
 * saved on its own; one that fails (someone else edited it, say) is reported and
 * the rest still move.
 */
export function MoveLocationsDialog({ moving, all, onClose, onMoved }: MoveLocationsDialogProps) {
  // If a building and one of its rooms are both ticked, only the building moves:
  // re-parenting the room as well would pull it out of the building it came with.
  const [items] = useState(() => {
    const selected = new Set(moving.map((item) => item.id));
    const byId = new Map(all.map((candidate) => [candidate.id, candidate]));
    return moving
      .filter((item) => !ancestorsOf(item.id, byId).some((parent) => selected.has(parent.id)))
      .sort(compareLocations);
  });
  const [mode, setMode] = useState<Mode>('existing');
  const [targetId, setTargetId] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [newCode, setNewCode] = useState('');
  const [newType, setNewType] = useState('Building');
  const [running, setRunning] = useState(false);
  const [progress, setProgress] = useState(0);
  const [failures, setFailures] = useState<Failure[] | null>(null);
  const [setupError, setSetupError] = useState<string | null>(null);

  const create = useCreateLocation();
  const update = useUpdateLocation();
  const toast = useToast();

  const targetOptions = useMemo(() => {
    // Nothing can move inside itself or its own descendants.
    const excluded = new Set<string>();
    for (const item of items) {
      for (const id of descendantsOf(item.id, all)) excluded.add(id);
    }
    return all
      .filter((candidate) => candidate.isActive && !excluded.has(candidate.id))
      .sort(compareLocations)
      .map((candidate) => ({
        value: candidate.id,
        label: candidate.name,
        description: [candidate.code, candidate.type].filter(Boolean).join(' · '),
      }));
  }, [all, items]);

  // Choosing where they already are would "succeed" without changing anything.
  const alreadyThere =
    mode === 'existing' && items.every((item) => item.parentLocationId === targetId);
  const canSubmit =
    !running &&
    !alreadyThere &&
    (mode === 'existing' || (newName.trim().length > 0 && newCode.trim().length > 0));

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setRunning(true);
    setSetupError(null);
    setProgress(0);

    let parentId = mode === 'existing' ? targetId : null;
    if (mode === 'new') {
      try {
        const created = await create.mutateAsync({
          code: newCode.trim(),
          name: newName.trim(),
          type: newType.trim() || null,
          parentLocationId: null,
        });
        parentId = created.id;
      } catch (error) {
        setSetupError(describeError(error));
        setRunning(false);
        return;
      }
    }

    const failed: Failure[] = [];
    let moved = 0;
    for (const item of items) {
      if (item.parentLocationId !== parentId) {
        try {
          await update.mutateAsync({
            locationId: item.id,
            code: item.code,
            name: item.name,
            type: item.type,
            parentLocationId: parentId,
            isActive: item.isActive,
            version: item.version,
          });
          moved += 1;
        } catch (error) {
          failed.push({ location: item, message: describeError(error) });
        }
      }
      setProgress((current) => current + 1);
    }

    setRunning(false);
    if (moved > 0 || mode === 'new') onMoved(parentId);

    if (failed.length === 0) {
      toast.success(
        `Moved ${items.length} ${items.length === 1 ? 'location' : 'locations'}`,
        mode === 'new' ? `Into the new ${newName.trim()}` : undefined,
      );
      onClose();
    } else {
      setFailures(failed);
    }
  }

  const formId = 'move-locations-form';

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={`Move ${items.length} ${items.length === 1 ? 'location' : 'locations'}`}
      description="Choose where they belong. Anything already inside them moves with them."
      size="md"
      dismissible={!running}
      footer={
        failures ? (
          <Button variant="primary" onClick={onClose}>
            Close
          </Button>
        ) : (
          <>
            <Button onClick={onClose} disabled={running}>
              Cancel
            </Button>
            <Button
              type="submit"
              form={formId}
              variant="primary"
              iconLeft={<FolderInput />}
              loading={running}
              disabled={!canSubmit}
            >
              {running ? `Moving ${progress} of ${items.length}…` : 'Move'}
            </Button>
          </>
        )
      }
    >
      {failures ? (
        <Alert tone="warning" title={`${failures.length} could not be moved`} role="alert">
          <p>The others moved. These were left where they were:</p>
          <ul className="mt-2 flex flex-col gap-1">
            {failures.map((failure) => (
              <li key={failure.location.id}>
                <strong>{failure.location.name}</strong> — {failure.message}
              </li>
            ))}
          </ul>
        </Alert>
      ) : (
        <form id={formId} onSubmit={(event) => void onSubmit(event)} className="flex flex-col gap-4">
          <div className="flex max-h-28 flex-wrap gap-1.5 overflow-y-auto rounded-md border border-border bg-surface-sunken/60 p-2">
            {items.map((item) => (
              <span
                key={item.id}
                className="rounded-sm border border-border bg-surface px-1.5 py-0.5 text-xs text-text"
              >
                {item.name}
              </span>
            ))}
          </div>

          {setupError && (
            <Alert tone="danger" title="Could not create the new location" role="alert">
              {setupError}
            </Alert>
          )}

          <SegmentedControl
            label="Move into"
            value={mode}
            onChange={setMode}
            disabled={running}
            options={[
              { value: 'existing', label: 'An existing location' },
              { value: 'new', label: 'A new location' },
            ]}
          />

          {mode === 'existing' ? (
            <Field
              label="Destination"
              hideLabel
              hint="Leave on “Top level” to take them out of any building."
            >
              {(controlProps) => (
                <Combobox
                  {...controlProps}
                  value={targetId}
                  onChange={setTargetId}
                  options={targetOptions}
                  placeholder="Top level"
                  emptyOptionLabel="Top level"
                  searchPlaceholder="Search locations…"
                  disabled={running}
                />
              )}
            </Field>
          ) : (
            <div className="grid gap-4 sm:grid-cols-[1fr_9rem]">
              <Field label="Name" required>
                {(controlProps) => (
                  <Input
                    {...controlProps}
                    value={newName}
                    onChange={(event) => setNewName(event.target.value)}
                    maxLength={150}
                    required
                    autoFocus
                    placeholder="English Block"
                    disabled={running}
                  />
                )}
              </Field>
              <Field label="Code" required>
                {(controlProps) => (
                  <Input
                    {...controlProps}
                    value={newCode}
                    onChange={(event) => setNewCode(event.target.value.toUpperCase())}
                    maxLength={50}
                    required
                    mono
                    placeholder="EN-BLOCK"
                    disabled={running}
                  />
                )}
              </Field>
              <Field label="Type" className="sm:col-span-2">
                {(controlProps) => (
                  <Input
                    {...controlProps}
                    value={newType}
                    onChange={(event) => setNewType(event.target.value)}
                    maxLength={80}
                    disabled={running}
                  />
                )}
              </Field>
            </div>
          )}
        </form>
      )}
    </Dialog>
  );
}
