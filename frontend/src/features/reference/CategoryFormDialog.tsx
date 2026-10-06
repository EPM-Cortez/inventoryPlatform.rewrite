import { useState, type FormEvent } from 'react';
import { Link } from 'react-router';

import { useCreateCategory, useUpdateCategory } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Category } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface CategoryFormDialogProps {
  /** Omitted when creating. */
  category?: Category;
  onClose: () => void;
}

/**
 * Create or edit a category.
 *
 * Mounted only while open, so every field initialises from `category` on the way
 * in and there is no effect syncing props into state.
 */
export function CategoryFormDialog({ category, onClose }: CategoryFormDialogProps) {
  const editing = category !== undefined;

  const [code, setCode] = useState(category?.code ?? '');
  const [name, setName] = useState(category?.name ?? '');
  const [description, setDescription] = useState(category?.description ?? '');
  const [isActive, setIsActive] = useState(category?.isActive ?? true);

  const create = useCreateCategory();
  const update = useUpdateCategory();
  const toast = useToast();
  const mutation = editing ? update : create;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const shared = {
      code: code.trim(),
      name: name.trim(),
      description: description.trim() || null,
    };

    if (category) {
      update.mutate(
        { categoryId: category.id, ...shared, isActive, version: category.version },
        {
          onSuccess: (saved) => {
            toast.success('Category saved', saved.name);
            onClose();
          },
        },
      );
    } else {
      create.mutate(shared, {
        onSuccess: (saved) => {
          toast.success('Category created', saved.name);
          onClose();
        },
      });
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'category-form';
  const deactivating = editing && category.isActive && !isActive;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={editing ? 'Edit category' : 'New category'}
      size="sm"
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
            disabled={!code.trim() || !name.trim()}
          >
            {editing ? 'Save changes' : 'Create category'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="danger" title="Someone else changed this category" role="alert">
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

        <Field label="Code" required hint="Short identifier, unique across categories.">
          {(controlProps) => (
            <Input
              {...controlProps}
              value={code}
              onChange={(event) => setCode(event.target.value.toUpperCase())}
              maxLength={50}
              required
              autoFocus
              mono
              placeholder="LAP"
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
              placeholder="Laptop"
            />
          )}
        </Field>

        <Field label="Description">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              maxLength={1000}
              rows={3}
              placeholder="Portable computers issued to staff and pupils"
            />
          )}
        </Field>

        {editing && (
          <div className="flex flex-col gap-2 border-t border-border pt-4">
            <Checkbox
              checked={isActive}
              onCheckedChange={setIsActive}
              label="Active"
              description="Inactive categories cannot be chosen for an asset."
            />
            {/* The server re-checks the category on every asset write, so this is
                not just a picker filter — it blocks edits to assets already in
                the category until they are moved. */}
            {deactivating && (
              <Alert tone="warning" title="Assets in this category will be locked">
                Existing assets keep this category, but nobody will be able to save any
                edit to them until they are moved to an active one.{' '}
                <Link
                  to={`/assets?categoryId=${category.id}`}
                  className="font-medium underline"
                >
                  Check which assets are affected
                </Link>
                .
              </Alert>
            )}
          </div>
        )}
      </form>
    </Dialog>
  );
}
