import { Boxes, Download, Layers, Plus, Upload } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { useAssets, useCategories, useExportAssets, useLocations } from '@/api/hooks';
import {
  ASSET_CONDITIONS,
  ASSET_SORTS,
  ASSET_STATUSES,
  WARRANTY_STATES,
  type AssetListItem,
  type AssetSearchParams,
  type AssetSort,
} from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import {
  ASSET_CONDITION_LABELS,
  ASSET_STATUS_LABELS,
  formatNumber,
  formatRelative,
  WARRANTY_STATE_LABELS,
} from '@/lib/format';
import { downloadBlob } from '@/lib/download';
import { asEnum, useUrlState } from '@/lib/useUrlState';
import { Button } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { FilterButton } from '@/ui/FilterButton';
import { ListToolbar } from '@/ui/ListToolbar';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { SearchInput } from '@/ui/SearchInput';
import { SelectionBar } from '@/ui/SelectionBar';
import { AssetConditionDot, AssetStatusDot } from '@/ui/StatusPill';
import { useToast } from '@/ui/Toast';

import { AssetIdentity, HolderLink, WarrantyCell } from './AssetCells';
import { BulkChangeDrawer } from './BulkChangeDrawer';
import { SavedSearchMenu } from './SavedSearchMenu';
import { useDefaultSearchRedirect } from './useDefaultSearchRedirect';

const DEFAULT_PAGE_SIZE = 50;

const STATUS_OPTIONS = ASSET_STATUSES.map((status) => ({
  value: status,
  label: ASSET_STATUS_LABELS[status],
}));
const CONDITION_OPTIONS = ASSET_CONDITIONS.map((condition) => ({
  value: condition,
  label: ASSET_CONDITION_LABELS[condition],
}));
const WARRANTY_OPTIONS = WARRANTY_STATES.map((state) => ({
  value: state,
  label: WARRANTY_STATE_LABELS[state],
}));
const CUSTODY_OPTIONS = [
  { value: 'true', label: 'Assigned to someone' },
  { value: 'false', label: 'Not assigned' },
];

export default function AssetsPage() {
  const url = useUrlState();
  const navigate = useNavigate();
  const { can } = useSession();
  // Held by id so a selection survives paging and refetches; the rows behind
  // them are resolved from the current page when the action opens.
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkOpen, setBulkOpen] = useState(false);
  const exportAssets = useExportAssets();
  const canBulk = can(Permission.AssetsBulk);
  // Applies the saved default, but only on a genuinely empty URL.
  useDefaultSearchRedirect(url, can(Permission.AssetsRead));
  const toast = useToast();

  const params = useMemo<AssetSearchParams>(
    () => ({
      q: url.get('q'),
      categoryId: url.get('categoryId'),
      status: asEnum(url.get('status'), ASSET_STATUSES),
      condition: asEnum(url.get('condition'), ASSET_CONDITIONS),
      locationId: url.get('locationId'),
      warrantyState: asEnum(url.get('warrantyState'), WARRANTY_STATES),
      assigned: url.getBoolean('assigned'),
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
      sort: asEnum(url.get('sort'), ASSET_SORTS) ?? 'assetTag',
    }),
    [url],
  );

  const assets = useAssets(params);
  // Only rows on the current page carry the versions a bulk request needs.
  const selectedOnPage = (assets.data?.items ?? []).filter((asset) =>
    selected.has(asset.id),
  );
  const hiddenSelectionCount = selected.size - selectedOnPage.length;
  const categories = useCategories();
  const locations = useLocations();
  const total = assets.data?.totalCount;

  const categoryOptions = (categories.data?.items ?? []).map((category) => ({
    value: category.id,
    label: category.name,
    description: category.code,
  }));
  const locationOptions = (locations.data?.items ?? []).map((location) => ({
    value: location.id,
    label: location.name,
    description: [location.code, location.type].filter(Boolean).join(' · '),
  }));

  const columns: Column<AssetListItem>[] = [
    {
      id: 'asset',
      header: 'Asset',
      sortKey: 'assetTag',
      cell: (asset) => <AssetIdentity asset={asset} />,
    },
    {
      id: 'status',
      header: 'Status',
      sortKey: 'status',
      width: 'w-32',
      cell: (asset) => <AssetStatusDot status={asset.status} />,
    },
    // Secondary columns appear as the width allows: the tag, status and holder
    // always; condition, location and warranty from `xl`; the serial at `2xl`.
    {
      id: 'condition',
      header: 'Condition',
      hideBelow: 'xl',
      width: 'w-28',
      cell: (asset) => <AssetConditionDot condition={asset.condition} />,
    },
    {
      id: 'holder',
      header: 'Held by',
      hideBelow: 'md',
      width: 'w-44',
      cell: (asset) =>
        asset.currentAssignment ? (
          <HolderLink assignment={asset.currentAssignment} />
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'location',
      header: 'Location',
      hideBelow: 'xl',
      width: 'w-36',
      cell: (asset) =>
        asset.location ? (
          <span className="block truncate text-text-muted">{asset.location.name}</span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'serial',
      header: 'Serial',
      hideBelow: '2xl',
      width: 'w-32',
      cell: (asset) =>
        asset.serialNumber ? (
          <span className="block truncate font-mono text-xs text-text-muted">
            {asset.serialNumber}
          </span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'warranty',
      header: 'Warranty',
      hideBelow: 'xl',
      width: 'w-32',
      cell: (asset) => <WarrantyCell summary={asset.warrantySummary} />,
    },
    {
      id: 'updated',
      header: 'Updated',
      sortKey: 'updatedAtUtc',
      align: 'right',
      hideBelow: 'sm',
      width: 'w-24',
      cell: (asset) => (
        <span className="whitespace-nowrap text-xs text-text-subtle">
          {formatRelative(asset.updatedAtUtc)}
        </span>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Assets"
        description="Every item on the register, with its current status, holder and coverage."
        actions={
          <>
            <SavedSearchMenu
              current={params}
              onApply={(next) => url.replaceAll({ ...next })}
              hasFilters={url.hasFilters()}
            />
            {can(Permission.ImportsValidate) && (
              <Button asChild>
                <Link to="/assets/import">
                  <Upload className="mr-1.5 size-4" />
                  Import
                </Link>
              </Button>
            )}
            {can(Permission.ExportsAssets) && (
              <Button
                iconLeft={<Download />}
                loading={exportAssets.isPending}
                onClick={() =>
                  exportAssets.mutate(params, {
                    onSuccess: (file) => {
                      downloadBlob(file.blob, file.fileName);
                      toast.success(
                        'Export ready',
                        `${formatNumber(file.rowCount)} ${file.rowCount === 1 ? 'row' : 'rows'} downloaded.`,
                      );
                    },
                    onError: (error) => toast.fromError(error, 'Could not export'),
                  })
                }
              >
                Export CSV
              </Button>
            )}
            {can(Permission.AssetsCreate) && (
              <Button asChild variant="primary">
                <Link to="/assets/new">
                  <Plus className="mr-1.5 size-4" />
                  New asset
                </Link>
              </Button>
            )}
          </>
        }
      />

      <Card className="overflow-hidden">
        <ListToolbar
          search={
            <SearchInput
              value={params.q ?? ''}
              onChange={(value) => url.set({ q: value })}
              placeholder="Search tag, serial, make or model…"
              aria-label="Search assets"
            />
          }
          count={
            total === undefined
              ? undefined
              : `${formatNumber(total)} ${total === 1 ? 'asset' : 'assets'}`
          }
          onClearFilters={url.hasFilters() ? url.clear : undefined}
          filters={
            <>
              <FilterButton
                label="Status"
                value={params.status ?? null}
                onChange={(value) => url.set({ status: value ?? undefined })}
                options={STATUS_OPTIONS}
              />
              <FilterButton
                label="Category"
                value={params.categoryId ?? null}
                onChange={(value) => url.set({ categoryId: value ?? undefined })}
                options={categoryOptions}
                loading={categories.isLoading}
                searchable
              />
              <FilterButton
                label="Location"
                value={params.locationId ?? null}
                onChange={(value) => url.set({ locationId: value ?? undefined })}
                options={locationOptions}
                loading={locations.isLoading}
                searchable
              />
              <FilterButton
                label="Condition"
                value={params.condition ?? null}
                onChange={(value) => url.set({ condition: value ?? undefined })}
                options={CONDITION_OPTIONS}
              />
              <FilterButton
                label="Warranty"
                value={params.warrantyState ?? null}
                onChange={(value) => url.set({ warrantyState: value ?? undefined })}
                options={WARRANTY_OPTIONS}
              />
              <FilterButton
                label="Custody"
                value={params.assigned === undefined ? null : String(params.assigned)}
                onChange={(value) => url.set({ assigned: value ?? undefined })}
                options={CUSTODY_OPTIONS}
              />
            </>
          }
        />

        {assets.isError ? (
          <ErrorState error={assets.error} onRetry={() => void assets.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={assets.data?.items ?? []}
              rowKey={(asset) => asset.id}
              loading={assets.isLoading}
              skeletonRows={10}
              layout="fixed"
              sort={params.sort}
              onSortChange={(sort) => url.set({ sort: sort as AssetSort })}
              onRowClick={(asset) => void navigate(`/assets/${asset.id}`)}
              selectedKeys={canBulk ? selected : undefined}
              onSelectionChange={canBulk ? setSelected : undefined}
              rowClassName={(asset) =>
                asset.status === 'retired' || asset.status === 'disposed'
                  ? 'opacity-60'
                  : undefined
              }
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<Boxes />}
                    title="The register is empty"
                    description="Add your first asset to start tracking equipment, custody and warranty coverage."
                    action={
                      can(Permission.AssetsCreate) && (
                        <Button asChild variant="primary">
                          <Link to="/assets/new">
                            <Plus className="mr-1.5 size-4" />
                            New asset
                          </Link>
                        </Button>
                      )
                    }
                  />
                )
              }
            />

            {(total ?? 0) > 0 && (
              <Pagination
                page={assets.data?.page ?? 1}
                pageSize={assets.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={total ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="assets"
              />
            )}
          </>
        )}
      </Card>

      {canBulk && (
        <SelectionBar
          count={selected.size}
          noun={{ one: 'asset', other: 'assets' }}
          onClear={() => setSelected(new Set())}
          // A selection made on another page is invisible, and a bulk action
          // that silently ignored it would be the worst kind of surprise. Only
          // rows currently loaded carry the versions the request needs, so the
          // difference is stated rather than quietly resolved.
          note={
            hiddenSelectionCount > 0
              ? `${formatNumber(hiddenSelectionCount)} ${
                  hiddenSelectionCount === 1
                    ? 'is selected on another page and will not be changed'
                    : 'are selected on other pages and will not be changed'
                }.`
              : undefined
          }
        >
          <Button
            size="sm"
            variant="primary"
            iconLeft={<Layers />}
            disabled={selectedOnPage.length === 0}
            onClick={() => setBulkOpen(true)}
          >
            Change {formatNumber(selectedOnPage.length)}
            {hiddenSelectionCount > 0 ? ' on this page' : ''}
          </Button>
        </SelectionBar>
      )}

      {bulkOpen && (
        <BulkChangeDrawer
          assets={selectedOnPage}
          onClose={() => setBulkOpen(false)}
          onCommitted={() => setSelected(new Set())}
        />
      )}
    </div>
  );
}
