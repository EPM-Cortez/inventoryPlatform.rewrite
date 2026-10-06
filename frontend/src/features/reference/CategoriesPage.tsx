import { FolderTree, Pencil, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { useCategories } from '@/api/hooks';
import type { Category } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';

import { CategoryFormDialog } from './CategoryFormDialog';

/** `'new'` opens an empty form; a `Category` opens it populated for editing. */
type FormTarget = Category | 'new';

export default function CategoriesPage() {
  const [includeInactive, setIncludeInactive] = useState(false);
  const [form, setForm] = useState<FormTarget | null>(null);
  const { can } = useSession();

  const categories = useCategories(includeInactive);
  const canManage = can(Permission.ReferenceManage);

  const columns: Column<Category>[] = [
    {
      id: 'name',
      header: 'Category',
      cell: (category) => (
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-medium text-text">{category.name}</span>
          {!category.isActive && <Badge tone="neutral">Inactive</Badge>}
        </div>
      ),
    },
    {
      id: 'code',
      header: 'Code',
      width: 'w-28',
      cell: (category) => (
        <span className="font-mono text-xs text-text-muted">{category.code}</span>
      ),
    },
    {
      id: 'description',
      header: 'Description',
      hideBelow: 'md',
      cell: (category) =>
        category.description ? (
          <span className="truncate-2 text-text-muted">{category.description}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'assets',
      header: <span className="sr-only">Assets</span>,
      align: 'right',
      width: canManage ? 'w-44' : 'w-32',
      cell: (category) => (
        <div className="flex items-center justify-end gap-1">
          <Link
            to={`/assets?categoryId=${category.id}`}
            className="rounded-xs text-sm font-medium text-text-muted hover:text-accent hover:underline"
          >
            View assets
          </Link>
          {canManage && (
            <IconButton
              label={`Edit ${category.name}`}
              size="sm"
              onClick={() => setForm(category)}
            >
              <Pencil />
            </IconButton>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Categories"
        description="How assets are classified. Every asset belongs to exactly one category."
        actions={
          canManage && (
            <Button variant="primary" iconLeft={<Plus />} onClick={() => setForm('new')}>
              New category
            </Button>
          )
        }
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <Checkbox
            checked={includeInactive}
            onCheckedChange={setIncludeInactive}
            label="Include inactive"
          />
        </div>

        {categories.isError ? (
          <ErrorState error={categories.error} onRetry={() => void categories.refetch()} />
        ) : (
          <DataTable
            columns={columns}
            rows={categories.data?.items ?? []}
            rowKey={(category) => category.id}
            loading={categories.isLoading}
            rowClassName={(category) => (category.isActive ? undefined : 'opacity-60')}
            empty={
              <EmptyState
                icon={<FolderTree />}
                title="No categories yet"
                description="Categories are required before you can add an asset. Create at least one."
                action={
                  canManage && (
                    <Button
                      variant="primary"
                      iconLeft={<Plus />}
                      onClick={() => setForm('new')}
                    >
                      New category
                    </Button>
                  )
                }
              />
            }
          />
        )}
      </Card>

      {/* Mounted only while open so the form initialises from the row it was
          opened on; a persistent dialog would need an effect to resync. */}
      {form && (
        <CategoryFormDialog
          category={form === 'new' ? undefined : form}
          onClose={() => setForm(null)}
        />
      )}
    </div>
  );
}
