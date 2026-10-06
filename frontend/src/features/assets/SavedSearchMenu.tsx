import { Bookmark, BookmarkPlus, Check, MoreHorizontal, Star, Trash2 } from 'lucide-react';
import { useState } from 'react';

import {
  useArchiveSavedSearch,
  useCreateSavedSearch,
  useSavedSearches,
  useSetDefaultSavedSearch,
} from '@/api/hooks';
import {
  SAVED_SEARCH_SCHEMA_VERSION,
  type AssetSearchParams,
  type SavedAssetSearch,
} from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button, IconButton } from '@/ui/Button';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { Menu, MenuContent, MenuItem, MenuSeparator, MenuTrigger } from '@/ui/Menu';
import { useToast } from '@/ui/Toast';

import { describeSavedFilter, fromSavedFilter, toSavedFilter } from './savedSearchFilter';

export interface SavedSearchMenuProps {
  /** The filter currently in the URL, offered as the thing to save. */
  current: AssetSearchParams;
  onApply: (params: AssetSearchParams) => void;
  /** True when there is something worth saving. */
  hasFilters: boolean;
}

/**
 * Saved searches for the signed-in user.
 *
 * These are private by construction — the API has no endpoint that reads or
 * writes another user's searches — so the copy never implies they are shared.
 */
export function SavedSearchMenu({ current, onApply, hasFilters }: SavedSearchMenuProps) {
  const searches = useSavedSearches();
  const [saving, setSaving] = useState(false);
  const [archiving, setArchiving] = useState<SavedAssetSearch | null>(null);

  const setDefault = useSetDefaultSavedSearch();
  const archive = useArchiveSavedSearch();
  const toast = useToast();

  const items = searches.data?.items ?? [];

  return (
    <>
      <Menu>
        <MenuTrigger asChild>
          <Button iconLeft={<Bookmark />}>
            Saved
            {items.length > 0 && (
              <span className="ml-1.5 text-xs text-text-subtle">{items.length}</span>
            )}
          </Button>
        </MenuTrigger>
        <MenuContent className="min-w-72">
          {items.length === 0 ? (
            <p className="px-2 py-3 text-sm text-text-subtle">
              No saved searches yet. Filter the list, then save it.
            </p>
          ) : (
            items.map((search) => (
              <MenuItem
                key={search.id}
                onSelect={() => onApply(fromSavedFilter(search.filter))}
              >
                {search.isDefault ? (
                  <Star className="fill-current text-warning" />
                ) : (
                  <Bookmark />
                )}
                <span className="min-w-0 flex-1">
                  <span className="block truncate">{search.name}</span>
                  <span className="block truncate text-2xs text-text-subtle">
                    {describeSavedFilter(search.filter)}
                  </span>
                </span>
              </MenuItem>
            ))
          )}

          <MenuSeparator />
          <MenuItem disabled={!hasFilters} onSelect={() => setSaving(true)}>
            <BookmarkPlus />
            {hasFilters ? 'Save this search' : 'Filter something first'}
          </MenuItem>
        </MenuContent>
      </Menu>

      {/* Managing is separated from applying: a menu whose items both navigate
          and destroy is a menu people stop trusting. */}
      {items.length > 0 && (
        <Menu>
          <MenuTrigger asChild>
            <IconButton label="Manage saved searches">
              <MoreHorizontal />
            </IconButton>
          </MenuTrigger>
          <MenuContent className="min-w-64">
            <p className="px-2 pb-1 pt-2 text-2xs font-medium uppercase tracking-wide text-text-subtle">
              Load automatically
            </p>
            {items.map((search) => (
              <MenuItem
                key={`default-${search.id}`}
                disabled={search.isDefault}
                onSelect={() =>
                  setDefault.mutate(
                    { savedSearchId: search.id, version: search.version },
                    {
                      onSuccess: () => toast.success('Default search set', search.name),
                      onError: (error) =>
                        toast.fromError(error, 'Could not set the default'),
                    },
                  )
                }
              >
                {search.isDefault ? <Check className="text-success" /> : <Star />}
                <span className="truncate">{search.name}</span>
              </MenuItem>
            ))}
            <MenuSeparator />
            {items.map((search) => (
              <MenuItem
                key={`archive-${search.id}`}
                destructive
                onSelect={() => setArchiving(search)}
              >
                <Trash2 />
                <span className="truncate">Remove {search.name}</span>
              </MenuItem>
            ))}
          </MenuContent>
        </Menu>
      )}

      {saving && (
        <SaveSearchDialog current={current} onClose={() => setSaving(false)} />
      )}

      {archiving && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setArchiving(null)}
          title="Remove this saved search?"
          description={
            <>
              <strong>{archiving.name}</strong> will no longer appear in your list. It is
              only yours, so nobody else is affected, and the assets it matched are not
              touched.
            </>
          }
          confirmLabel="Remove it"
          destructive
          loading={archive.isPending}
          onConfirm={() =>
            archive.mutate(
              { savedSearchId: archiving.id, version: archiving.version },
              {
                onSuccess: () => {
                  toast.success('Saved search removed', archiving.name);
                  setArchiving(null);
                },
                onError: (error) => {
                  toast.fromError(error, 'Could not remove it');
                  setArchiving(null);
                },
              },
            )
          }
        />
      )}
    </>
  );
}

function SaveSearchDialog({
  current,
  onClose,
}: {
  current: AssetSearchParams;
  onClose: () => void;
}) {
  const [name, setName] = useState('');
  const create = useCreateSavedSearch();
  const toast = useToast();

  const filter = toSavedFilter(current);

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="Save this search"
      size="sm"
      dismissible={!create.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="save-search-form"
            variant="primary"
            loading={create.isPending}
            disabled={!name.trim()}
          >
            Save it
          </Button>
        </>
      }
    >
      <form
        id="save-search-form"
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate(
            {
              name: name.trim(),
              schemaVersion: SAVED_SEARCH_SCHEMA_VERSION,
              filter,
            },
            {
              onSuccess: (saved) => {
                toast.success('Search saved', saved.name);
                onClose();
              },
            },
          );
        }}
      >
        {create.error && (
          <Alert tone="danger" title="Could not save the search" role="alert">
            A search with that name may already exist. Try a different one.
          </Alert>
        )}

        <Field label="Name" required>
          {(controlProps) => (
            <Input
              {...controlProps}
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={100}
              required
              autoFocus
              placeholder="Laptops out with staff"
            />
          )}
        </Field>

        <div className="rounded-md border border-border bg-surface-sunken p-3">
          <p className="text-xs text-text-subtle">This will save</p>
          <p className="mt-0.5 text-sm text-text">{describeSavedFilter(filter)}</p>
          <p className="mt-2 text-xs text-text-subtle">
            Only for you. Saved searches are never shared, and this stores the filter
            rather than the results, so it stays current.
          </p>
        </div>
      </form>
    </Dialog>
  );
}
