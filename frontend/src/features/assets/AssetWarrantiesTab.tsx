import { Archive, Pencil, Plus, ShieldCheck, Star } from 'lucide-react';
import { useState } from 'react';

import { useArchiveWarranty, useAssetWarranties } from '@/api/hooks';
import type { Warranty } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDate, formatDaysRemaining } from '@/lib/format';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { WarrantyStatePill } from '@/ui/StatusPill';
import { useToast } from '@/ui/Toast';

import { WarrantyFormDialog } from './WarrantyFormDialog';

export function AssetWarrantiesTab({ assetId }: { assetId: string }) {
  const [includeArchived, setIncludeArchived] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Warranty | undefined>();
  const [archiving, setArchiving] = useState<Warranty | null>(null);
  const { can } = useSession();
  const toast = useToast();

  const warranties = useAssetWarranties(assetId, includeArchived);
  const archive = useArchiveWarranty(assetId);
  const canManage = can(Permission.WarrantiesManage);

  function openAdd() {
    setEditing(undefined);
    setFormOpen(true);
  }

  function openEdit(warranty: Warranty) {
    setEditing(warranty);
    setFormOpen(true);
  }

  const columns: Column<Warranty>[] = [
    {
      id: 'provider',
      header: 'Provider',
      cell: (warranty) => (
        <div className="flex min-w-0 items-center gap-2">
          <div className="min-w-0">
            <p className="truncate font-medium text-text">{warranty.provider}</p>
            <p className="truncate text-xs text-text-subtle">{warranty.coverageType}</p>
          </div>
          {warranty.isPrimary && (
            <Badge tone="accent" className="shrink-0">
              <Star className="size-3" />
              Primary
            </Badge>
          )}
        </div>
      ),
    },
    {
      id: 'state',
      header: 'State',
      width: 'w-36',
      cell: (warranty) =>
        warranty.archivedAtUtc ? (
          <Badge tone="neutral">Archived</Badge>
        ) : (
          <WarrantyStatePill state={warranty.state} />
        ),
    },
    {
      id: 'period',
      header: 'Period',
      width: 'w-56',
      cell: (warranty) => (
        <div className="min-w-0">
          <p className="text-text">
            {formatDate(warranty.startDate)} → {formatDate(warranty.endDate)}
          </p>
          {!warranty.archivedAtUtc && warranty.daysRemaining !== null && (
            <p className="text-xs text-text-subtle">
              {formatDaysRemaining(warranty.daysRemaining)}
            </p>
          )}
        </div>
      ),
    },
    {
      id: 'reference',
      header: 'Reference',
      hideBelow: 'lg',
      cell: (warranty) =>
        warranty.reference ? (
          <span className="font-mono text-xs text-text-muted">{warranty.reference}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-24',
      cell: (warranty) =>
        canManage && !warranty.archivedAtUtc ? (
          <div className="flex justify-end gap-1">
            <IconButton label="Edit warranty" size="sm" onClick={() => openEdit(warranty)}>
              <Pencil />
            </IconButton>
            <IconButton
              label="Archive warranty"
              size="sm"
              onClick={() => setArchiving(warranty)}
            >
              <Archive />
            </IconButton>
          </div>
        ) : null,
    },
  ];

  if (warranties.isError) {
    return <ErrorState error={warranties.error} onRetry={() => void warranties.refetch()} />;
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Checkbox
          checked={includeArchived}
          onCheckedChange={setIncludeArchived}
          label="Show archived coverage"
        />
        {canManage && (
          <Button variant="primary" size="sm" iconLeft={<Plus />} onClick={openAdd}>
            Add warranty
          </Button>
        )}
      </div>

      <Card className="overflow-hidden">
        <DataTable
          columns={columns}
          rows={warranties.data ?? []}
          rowKey={(warranty) => warranty.id}
          loading={warranties.isLoading}
          skeletonRows={3}
          rowClassName={(warranty) => (warranty.archivedAtUtc ? 'opacity-60' : undefined)}
          empty={
            <EmptyState
              icon={<ShieldCheck />}
              title="No coverage recorded"
              description="Add a warranty so this asset appears in expiry reporting."
              action={
                canManage && (
                  <Button variant="primary" iconLeft={<Plus />} onClick={openAdd}>
                    Add warranty
                  </Button>
                )
              }
            />
          }
        />
      </Card>

      {formOpen && (
        <WarrantyFormDialog
          onClose={() => setFormOpen(false)}
          assetId={assetId}
          warranty={editing}
        />
      )}

      <ConfirmDialog
        open={archiving !== null}
        onOpenChange={(open) => !open && setArchiving(null)}
        title="Archive this warranty?"
        description={
          <>
            <strong>{archiving?.provider}</strong> coverage will stop counting towards this
            asset&rsquo;s warranty state and drop out of expiry reporting. The record is
            kept and stays visible under &ldquo;Show archived coverage&rdquo;.
          </>
        }
        confirmLabel="Archive"
        destructive
        loading={archive.isPending}
        onConfirm={() => {
          if (!archiving) return;
          archive.mutate(
            { warrantyId: archiving.id, version: archiving.version },
            {
              onSuccess: () => {
                toast.success('Warranty archived');
                setArchiving(null);
              },
              onError: (error) => toast.fromError(error, 'Could not archive'),
            },
          );
        }}
      />
    </div>
  );
}
