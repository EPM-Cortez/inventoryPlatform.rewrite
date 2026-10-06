import { Plus, X } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { useAssets, useBundle, useCreateBundle, useUpdateBundle } from '@/api/hooks';
import {
  BUNDLE_ROLES,
  type BundleMemberRequest,
  type BundleRole,
} from '@/api/operationsTypes';
import { ApiError, describeError } from '@/api/problem';
import { Alert } from '@/ui/Alert';
import { Button, IconButton } from '@/ui/Button';
import { Combobox } from '@/ui/Combobox';
import { Drawer } from '@/ui/Drawer';
import { Field, Input, NativeSelect, Textarea } from '@/ui/Field';
import { LoadingBlock } from '@/ui/Spinner';
import { useToast } from '@/ui/Toast';

const ROLE_LABELS: Record<BundleRole, string> = {
  laptop: 'Laptop',
  charger: 'Charger',
  dock: 'Dock',
  case: 'Case',
  other: 'Other',
};

/** A member plus the tag, so the list can be read without another lookup. */
interface DraftMember extends BundleMemberRequest {
  assetTag: string;
}

export function BundleEditorDrawer({
  bundleId,
  onClose,
}: {
  bundleId?: string;
  onClose: () => void;
}) {
  const existing = useBundle(bundleId);

  if (bundleId && existing.isLoading) {
    return (
      <Drawer open onOpenChange={(next) => !next && onClose()} title="Edit bundle" width="lg">
        <LoadingBlock label="Loading bundle…" />
      </Drawer>
    );
  }

  return (
    <Editor
      key={existing.data?.version ?? 'new'}
      bundle={existing.data}
      onClose={onClose}
    />
  );
}

function Editor({
  bundle,
  onClose,
}: {
  bundle: ReturnType<typeof useBundle>['data'];
  onClose: () => void;
}) {
  const [code, setCode] = useState(bundle?.code ?? '');
  const [name, setName] = useState(bundle?.name ?? '');
  const [description, setDescription] = useState(bundle?.description ?? '');
  const [members, setMembers] = useState<DraftMember[]>(
    bundle?.members.map((member) => ({
      assetId: member.assetId,
      assetTag: member.assetTag,
      role: member.role,
      otherRole: member.otherRole,
    })) ?? [],
  );

  const [search, setSearch] = useState('');
  const [pendingAssetId, setPendingAssetId] = useState<string | null>(null);
  const [pendingRole, setPendingRole] = useState<BundleRole>('laptop');

  const assets = useAssets({ q: search || undefined, pageSize: 25 });
  const create = useCreateBundle();
  const update = useUpdateBundle(bundle?.id ?? '');
  const mutation = bundle ? update : create;
  const toast = useToast();

  function addMember() {
    const asset = (assets.data?.items ?? []).find((item) => item.id === pendingAssetId);
    if (!asset || members.some((member) => member.assetId === asset.id)) return;
    setMembers((current) => [
      ...current,
      { assetId: asset.id, assetTag: asset.assetTag, role: pendingRole, otherRole: null },
    ]);
    setPendingAssetId(null);
    setSearch('');
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    // The API replaces the member list wholesale, so the complete set goes every
    // time — sending a delta would silently drop everything left out.
    const payload = {
      code: code.trim(),
      name: name.trim(),
      description: description.trim() || null,
      members: members.map(({ assetId, role, otherRole }) => ({
        assetId,
        role,
        otherRole: role === 'other' ? (otherRole?.trim() || null) : null,
      })),
    };

    if (bundle) {
      update.mutate(
        { ...payload, version: bundle.version },
        {
          onSuccess: (saved) => {
            toast.success('Bundle saved', saved.name);
            onClose();
          },
        },
      );
    } else {
      create.mutate(payload, {
        onSuccess: (saved) => {
          toast.success('Bundle created', saved.name);
          onClose();
        },
      });
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Drawer
      open
      onOpenChange={(next) => !next && onClose()}
      title={bundle ? 'Edit bundle' : 'New bundle'}
      description={bundle ? bundle.code : 'Group items that go out together'}
      width="lg"
      dismissible={!mutation.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="bundle-form"
            variant="primary"
            loading={mutation.isPending}
            disabled={!code.trim() || !name.trim()}
          >
            {bundle ? 'Save changes' : 'Create bundle'}
          </Button>
        </>
      }
    >
      <form id="bundle-form" onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="danger" title="Someone else changed this bundle" role="alert">
            Close and reopen it to pick up their version, then re-apply your edit.
          </Alert>
        ) : apiError?.isDuplicate ? (
          <Alert tone="danger" title="That code is already in use" role="alert">
            {apiError.message}
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not save the bundle" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="grid gap-4 sm:grid-cols-[12rem_1fr]">
          <Field label="Code" required hint="Short, unique.">
            {(controlProps) => (
              <Input
                {...controlProps}
                value={code}
                onChange={(event) => setCode(event.target.value.toUpperCase())}
                maxLength={50}
                required
                mono
                placeholder="KIT-STAFF"
              />
            )}
          </Field>

          <Field label="Name" required>
            {(controlProps) => (
              <Input
                {...controlProps}
                value={name}
                onChange={(event) => setName(event.target.value)}
                maxLength={150}
                required
                placeholder="Staff laptop kit"
              />
            )}
          </Field>
        </div>

        <Field label="Description">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              maxLength={1000}
              rows={2}
              placeholder="Issued to teaching staff on appointment"
            />
          )}
        </Field>

        <div className="border-t border-border pt-4">
          <p className="text-sm font-medium text-text">What is in it</p>
          <p className="mt-0.5 text-sm text-text-muted">
            Each item stays its own asset with its own history and custody. Grouping
            them here does not merge them.
          </p>

          <div className="mt-3 flex flex-wrap items-end gap-2">
            <Field label="Asset" className="min-w-56 flex-1">
              {(controlProps) => (
                <Combobox
                  {...controlProps}
                  value={pendingAssetId}
                  onChange={setPendingAssetId}
                  options={(assets.data?.items ?? [])
                    .filter(
                      (asset) => !members.some((member) => member.assetId === asset.id),
                    )
                    .map((asset) => ({
                      value: asset.id,
                      label: asset.assetTag,
                      description:
                        [asset.manufacturer, asset.model].filter(Boolean).join(' ') ||
                        asset.category.name,
                    }))}
                  loading={assets.isLoading}
                  onSearchChange={setSearch}
                  serverFiltered
                  placeholder="Search for an asset"
                  searchPlaceholder="Tag, make or model…"
                />
              )}
            </Field>

            <Field label="Role" className="w-40">
              {(controlProps) => (
                <NativeSelect
                  {...controlProps}
                  value={pendingRole}
                  onChange={(event) => setPendingRole(event.target.value as BundleRole)}
                >
                  {BUNDLE_ROLES.map((role) => (
                    <option key={role} value={role}>
                      {ROLE_LABELS[role]}
                    </option>
                  ))}
                </NativeSelect>
              )}
            </Field>

            <Button iconLeft={<Plus />} disabled={!pendingAssetId} onClick={addMember}>
              Add
            </Button>
          </div>

          {members.length === 0 ? (
            <p className="mt-3 rounded-md border border-dashed border-border p-4 text-center text-sm text-text-subtle">
              No items yet. A bundle with nothing in it is allowed, but not much use.
            </p>
          ) : (
            <ul className="mt-3 flex flex-col gap-1.5">
              {members.map((member) => (
                <li
                  key={member.assetId}
                  className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-surface-sunken px-3 py-2"
                >
                  <span className="font-mono text-sm text-text">{member.assetTag}</span>
                  <NativeSelect
                    aria-label={`Role for ${member.assetTag}`}
                    value={member.role}
                    className="h-7 w-32 text-sm"
                    onChange={(event) =>
                      setMembers((current) =>
                        current.map((item) =>
                          item.assetId === member.assetId
                            ? { ...item, role: event.target.value as BundleRole }
                            : item,
                        ),
                      )
                    }
                  >
                    {BUNDLE_ROLES.map((role) => (
                      <option key={role} value={role}>
                        {ROLE_LABELS[role]}
                      </option>
                    ))}
                  </NativeSelect>

                  {member.role === 'other' && (
                    <Input
                      aria-label={`Describe the role of ${member.assetTag}`}
                      value={member.otherRole ?? ''}
                      onChange={(event) =>
                        setMembers((current) =>
                          current.map((item) =>
                            item.assetId === member.assetId
                              ? { ...item, otherRole: event.target.value }
                              : item,
                          ),
                        )
                      }
                      maxLength={100}
                      className="h-7 w-40 text-sm"
                      placeholder="What it is"
                    />
                  )}

                  <IconButton
                    label={`Remove ${member.assetTag}`}
                    size="sm"
                    className="ml-auto"
                    onClick={() =>
                      setMembers((current) =>
                        current.filter((item) => item.assetId !== member.assetId),
                      )
                    }
                  >
                    <X />
                  </IconButton>
                </li>
              ))}
            </ul>
          )}
        </div>
      </form>
    </Drawer>
  );
}
