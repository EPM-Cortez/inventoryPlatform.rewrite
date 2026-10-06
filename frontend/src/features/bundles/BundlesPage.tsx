import { ArchiveRestore, Boxes, MoreHorizontal, Pencil, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';

import { useArchiveBundle, useBundles, useRestoreBundle } from '@/api/hooks';
import type { AssetBundleListItem } from '@/api/operationsTypes';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatNumber, formatRelative } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Menu, MenuContent, MenuItem, MenuTrigger } from '@/ui/Menu';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { useToast } from '@/ui/Toast';

import { BundleEditorDrawer } from './BundleEditorDrawer';

const DEFAULT_PAGE_SIZE = 25;

/**
 * Kits that travel together — a laptop with its charger, dock and case.
 *
 * Members stay independent assets throughout: a bundle is a grouping for issuing
 * and checking, not a merge. Nothing here changes an asset.
 */
export default function BundlesPage() {
  const url = useUrlState();
  const { can } = useSession();
  const [editing, setEditing] = useState<string | 'new' | null>(null);

  const params = useMemo(
    () => ({
      includeArchived: url.getBoolean('includeArchived') ?? false,
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const bundles = useBundles(params);
  const archive = useArchiveBundle();
  const restore = useRestoreBundle();
  const toast = useToast();

  const canManage = can(Permission.BundlesManage);
  const rows = bundles.data?.items ?? [];

  const columns: Column<AssetBundleListItem>[] = [
    {
      id: 'bundle',
      header: 'Bundle',
      cell: (bundle) => (
        <div className="min-w-0">
          <div className="flex min-w-0 items-center gap-2">
            <span className="truncate font-medium text-text">{bundle.name}</span>
            {bundle.isArchived && <Badge tone="neutral">Archived</Badge>}
          </div>
          <p className="truncate text-2xs text-text-subtle">
            <span className="font-mono">{bundle.code}</span>
            {bundle.description ? ` · ${bundle.description}` : ''}
          </p>
        </div>
      ),
    },
    {
      id: 'members',
      header: 'Items',
      align: 'right',
      width: 'w-28',
      cell: (bundle) => (
        <span className="text-sm text-text-muted">
          {formatNumber(bundle.memberCount)}
        </span>
      ),
    },
    {
      id: 'updated',
      header: 'Updated',
      align: 'right',
      width: 'w-40',
      hideBelow: 'md',
      cell: (bundle) => (
        <span className="text-sm text-text-muted">
          {formatRelative(bundle.updatedAtUtc)}
        </span>
      ),
    },
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-14',
      cell: (bundle) =>
        canManage ? (
          <Menu>
            <MenuTrigger asChild>
              <IconButton label={`Actions for ${bundle.name}`} size="sm">
                <MoreHorizontal />
              </IconButton>
            </MenuTrigger>
            <MenuContent>
              {bundle.isArchived ? (
                <MenuItem
                  onSelect={() =>
                    restore.mutate(
                      { bundleId: bundle.id, version: bundle.version },
                      {
                        onSuccess: () => toast.success('Bundle restored', bundle.name),
                        onError: (error) =>
                          toast.fromError(error, 'Could not restore it'),
                      },
                    )
                  }
                >
                  <ArchiveRestore />
                  Restore
                </MenuItem>
              ) : (
                <>
                  <MenuItem onSelect={() => setEditing(bundle.id)}>
                    <Pencil />
                    Edit
                  </MenuItem>
                  <MenuItem
                    destructive
                    onSelect={() =>
                      archive.mutate(
                        { bundleId: bundle.id, version: bundle.version },
                        {
                          onSuccess: () => toast.success('Bundle archived', bundle.name),
                          onError: (error) =>
                            toast.fromError(error, 'Could not archive it'),
                        },
                      )
                    }
                  >
                    <Trash2 />
                    Archive
                  </MenuItem>
                </>
              )}
            </MenuContent>
          </Menu>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Bundles"
        description="Kit that goes out together. The items stay separate assets — this just groups them."
        actions={
          canManage && (
            <Button variant="primary" iconLeft={<Plus />} onClick={() => setEditing('new')}>
              New bundle
            </Button>
          )
        }
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <Checkbox
            checked={params.includeArchived}
            onCheckedChange={(checked) =>
              url.set({ includeArchived: checked, page: 1 })
            }
            label="Include archived"
          />
        </div>

        {bundles.isError ? (
          <ErrorState error={bundles.error} onRetry={() => void bundles.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(bundle) => bundle.id}
              loading={bundles.isLoading}
              rowClassName={(bundle) => (bundle.isArchived ? 'opacity-60' : undefined)}
              empty={
                <EmptyState
                  icon={<Boxes />}
                  title="No bundles yet"
                  description="Group a laptop with its charger and dock so the whole kit can be issued and checked as one."
                  action={
                    canManage && (
                      <Button
                        variant="primary"
                        iconLeft={<Plus />}
                        onClick={() => setEditing('new')}
                      >
                        New bundle
                      </Button>
                    )
                  }
                />
              }
            />

            {(bundles.data?.totalCount ?? 0) > 0 && (
              <Pagination
                page={bundles.data?.page ?? 1}
                pageSize={bundles.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={bundles.data?.totalCount ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="bundles"
              />
            )}
          </>
        )}
      </Card>

      {editing && (
        <BundleEditorDrawer
          bundleId={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}
