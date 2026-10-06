import { useRef, useState, type FormEvent } from 'react';

import { useCreateDepartment, useUpdateDepartment } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Department } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { Dialog } from '@/ui/Dialog';
import { Field, Input } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export interface DepartmentFormDialogProps {
  /** Rendered only while open; the parent mounts and unmounts it. */
  onClose: () => void;
  department?: Department;
  /** Not called when "Add another" keeps the dialog open for the next entry. */
  onCreated?: (department: Department) => void;
}

export function DepartmentFormDialog({
  onClose,
  department,
  onCreated,
}: DepartmentFormDialogProps) {
  const isEdit = Boolean(department);
  const [name, setName] = useState(department?.name ?? '');
  const [code, setCode] = useState(department?.code ?? '');
  const [isActive, setIsActive] = useState(department?.isActive ?? true);
  const [addAnother, setAddAnother] = useState(false);
  const nameRef = useRef<HTMLInputElement>(null);
  const toast = useToast();

  const create = useCreateDepartment();
  const update = useUpdateDepartment(department?.id ?? '');
  const mutation = isEdit ? update : create;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const body = { code: code.trim(), name: name.trim() };

    if (department) {
      update.mutate(
        { ...body, isActive, version: department.version },
        {
          onSuccess: (saved) => {
            toast.success('Department saved', saved.name);
            onClose();
          },
        },
      );
    } else {
      create.mutate(body, {
        onSuccess: (saved) => {
          toast.success('Department added', saved.name);
          if (addAnother) {
            setName('');
            setCode('');
            create.reset();
            nameRef.current?.focus();
            return;
          }
          onCreated?.(saved);
          onClose();
        },
      });
    }
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'department-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      size="md"
      title={isEdit ? `Edit ${department?.name}` : 'Add a department'}
      description="A team that holds shared equipment collectively, rather than one person."
      dismissible={!mutation.isPending}
      footer={
        <>
          {!isEdit && (
            <Checkbox
              className="mr-auto"
              checked={addAnother}
              onCheckedChange={setAddAnother}
              label="Add another"
            />
          )}
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
            {isEdit ? 'Save changes' : 'Add department'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="Someone else saved first" role="alert">
            Close and reopen this form to load the current values.
          </Alert>
        ) : apiError?.isDuplicate ? (
          <Alert tone="danger" title="That code is already in use" role="alert">
            {apiError.message}
          </Alert>
        ) : error ? (
          <Alert tone="danger" title="Could not save" role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <div className="grid gap-4 sm:grid-cols-[1fr_10rem]">
          <Field label="Name" required error={apiError?.fieldErrors('name')[0] ?? null}>
            {(controlProps) => (
              <Input
                {...controlProps}
                ref={nameRef}
                value={name}
                onChange={(event) => setName(event.target.value)}
                maxLength={150}
                required
                autoFocus
                placeholder="ICT Services"
              />
            )}
          </Field>

          <Field
            label="Code"
            required
            hint="Unique short code."
            error={apiError?.fieldErrors('code')[0] ?? null}
          >
            {(controlProps) => (
              <Input
                {...controlProps}
                value={code}
                onChange={(event) => setCode(event.target.value.toUpperCase())}
                maxLength={50}
                required
                mono
                placeholder="ICT"
              />
            )}
          </Field>
        </div>

        {isEdit && (
          <div className="rounded-md border border-border bg-surface-sunken/60 p-3">
            <Checkbox
              checked={isActive}
              onCheckedChange={setIsActive}
              label="Active"
              description="Clearing this archives the department. Its custody history is kept."
            />
          </div>
        )}
      </form>
    </Dialog>
  );
}
