import { ArrowRight, Boxes, FolderInput, MapPin, Pencil, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { useAssets, useLocationAssetTotals, useLocations } from '@/api/hooks';
import type { AssetListItem } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardHeader } from '@/ui/Card';
import { CopyButton } from '@/ui/CopyButton';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Breadcrumbs } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { StatusDot } from '@/ui/StatusDot';
import { AssetStatusDot } from '@/ui/StatusPill';
import { SummaryFact, SummaryPanel } from '@/ui/SummaryPanel';

import { AssetIdentity, HolderLink } from '../assets/AssetCells';
import { LocationFormDialog } from './LocationFormDialog';
import { LocationGlyph } from './LocationGlyph';
import {
  ancestorsOf,
  buildLocationTree,
  rollUpTotals,
  stripChildren,
  type LocationNode,
} from './locationTree';
import { MoveLocationsDialog } from './MoveLocationsDialog';

/** Enough to see what is in a room; the asset register has the rest. */
const ASSET_PREVIEW_SIZE = 10;

/**
 * One location, laid out like an asset: a summary of where it sits and what it
 * holds, then the places inside it and the equipment recorded against it.
 */
export default function LocationDetailPage() {
  const { locationId } = useParams<{ locationId: string }>();
  const { can } = useSession();
  const navigate = useNavigate();
  const canManage = can(Permission.ReferenceManage);

  const [editing, setEditing] = useState(false);
  const [addingInside, setAddingInside] = useState(false);
  const [moving, setMoving] = useState(false);

  // The full list, inactive included, so a location inside an inactive building
  // still has its whole path. It is cached for every picker in the app.
  const locations = useLocations(true);
  const assetTotals = useLocationAssetTotals();
  const assets = useAssets(
    { locationId, pageSize: ASSET_PREVIEW_SIZE, sort: 'assetTag' },
    Boolean(locationId) && can(Permission.AssetsRead),
  );

  const tree = useMemo(
    () => buildLocationTree(locations.data?.items ?? []),
    [locations.data],
  );
  const totals = useMemo(
    () => rollUpTotals(tree.roots, assetTotals.data?.totals ?? new Map()),
    [tree, assetTotals.data],
  );

  if (locations.isLoading) return <LoadingBlock label="Loading location…" />;
  if (locations.isError) {
    return <ErrorState error={locations.error} onRetry={() => void locations.refetch()} />;
  }

  const node = locationId ? tree.byId.get(locationId) : undefined;
  if (!node) {
    return (
      <EmptyState
        icon={<MapPin />}
        title="Location not found"
        description="It may have been removed, or the link is wrong."
        action={
          <Button asChild>
            <Link to="/locations">Back to locations</Link>
          </Button>
        }
      />
    );
  }

  const path = ancestorsOf(node.id, tree.byId);
  const parent = path.at(-1);
  const direct = assetTotals.data?.totals.get(node.id) ?? 0;
  const rolled = totals.get(node.id) ?? 0;
  const descendants = rolled === direct ? null : rolled;
  const hasChildren = node.children.length > 0;

  const childColumns: Column<LocationNode>[] = [
    {
      id: 'location',
      header: 'Location',
      cell: (child) => (
        <div className="flex min-w-0 items-center gap-3">
          <LocationGlyph type={child.type} hasChildren={child.children.length > 0} />
          <div className="min-w-0">
            <Link
              to={`/locations/${child.id}`}
              onClick={(event) => event.stopPropagation()}
              className="block truncate rounded-xs font-medium text-text hover:text-accent hover:underline"
            >
              {child.name}
            </Link>
            <p className="truncate text-xs text-text-muted">
              {child.type ?? <span className="text-text-subtle">No type</span>}
              {child.children.length > 0 && (
                <span className="text-text-subtle"> · {child.children.length} inside</span>
              )}
            </p>
          </div>
        </div>
      ),
    },
    {
      id: 'code',
      header: 'Code',
      hideBelow: 'md',
      width: 'w-40',
      cell: (child) => (
        <span className="block truncate font-mono text-xs text-text-muted">{child.code}</span>
      ),
    },
    {
      id: 'assets',
      header: 'Assets',
      align: 'right',
      width: 'w-28',
      cell: (child) => {
        const total = totals.get(child.id) ?? 0;
        return total === 0 ? (
          <span className="text-text-subtle">—</span>
        ) : (
          <span className="tabular-nums text-text">{formatNumber(total)}</span>
        );
      },
    },
  ];

  const assetColumns: Column<AssetListItem>[] = [
    { id: 'asset', header: 'Asset', cell: (asset) => <AssetIdentity asset={asset} /> },
    {
      id: 'status',
      header: 'Status',
      width: 'w-32',
      cell: (asset) => <AssetStatusDot status={asset.status} />,
    },
    {
      id: 'holder',
      header: 'Held by',
      hideBelow: 'md',
      width: 'w-52',
      cell: (asset) =>
        asset.currentAssignment ? (
          <HolderLink assignment={asset.currentAssignment} />
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
  ];

  const insideSection = (
    <InsideSection
      node={node}
      columns={childColumns}
      canAdd={canManage && node.isActive}
      onAdd={() => setAddingInside(true)}
      onOpen={(id) => void navigate(`/locations/${id}`)}
    />
  );
  const assetsSection = can(Permission.AssetsRead) ? (
    <AssetsSection
      node={node}
      columns={assetColumns}
      query={assets}
      onOpen={(id) => void navigate(`/assets/${id}`)}
    />
  ) : null;

  return (
    <div className="flex flex-col gap-5">
      <div className="-mb-2">
        <Breadcrumbs
          items={[
            { label: 'Locations', to: '/locations' },
            ...path.map((ancestor) => ({ label: ancestor.name, to: `/locations/${ancestor.id}` })),
            { label: node.name },
          ]}
        />
      </div>

      <SummaryPanel
        label="Location summary"
        media={
          <LocationGlyph type={node.type} hasChildren={node.children.length > 0} size="xl" />
        }
        title={node.name}
        subtitle={
          <>
            {node.type ?? 'Location'}
            {parent && (
              <span className="text-text-subtle">
                {' · in '}
                <Link
                  to={`/locations/${parent.id}`}
                  className="rounded-xs text-text-muted hover:text-accent hover:underline"
                >
                  {parent.name}
                </Link>
              </span>
            )}
          </>
        }
        status={
          node.isActive ? (
            <StatusDot tone="success" muted>
              Active
            </StatusDot>
          ) : (
            <StatusDot tone="neutral" muted>
              Inactive
            </StatusDot>
          )
        }
        actions={
          canManage && (
            <>
              <Button iconLeft={<FolderInput />} onClick={() => setMoving(true)}>
                Move
              </Button>
              <Button iconLeft={<Pencil />} onClick={() => setEditing(true)}>
                Edit
              </Button>
              {node.isActive && (
                <Button
                  // The headline action for a building; an aside for a room.
                  variant={hasChildren ? 'primary' : 'secondary'}
                  iconLeft={<Plus />}
                  onClick={() => setAddingInside(true)}
                >
                  Add inside
                </Button>
              )}
            </>
          )
        }
        facts={
          <>
            <SummaryFact label="Code">
              <span className="inline-flex items-center gap-1">
                <span className="font-mono text-sm">{node.code}</span>
                <CopyButton value={node.code} label="Copy location code" />
              </span>
            </SummaryFact>
            <SummaryFact label="Inside" empty="Top level">
              {parent && (
                <Link
                  to={`/locations/${parent.id}`}
                  className="inline-flex min-w-0 items-center gap-1.5 rounded-xs hover:text-accent hover:underline"
                >
                  <MapPin className="size-3.5 shrink-0 text-text-subtle" aria-hidden />
                  <span className="truncate">{parent.name}</span>
                </Link>
              )}
            </SummaryFact>
            <SummaryFact label="Locations inside" empty="None">
              {node.children.length > 0 ? formatNumber(node.children.length) : null}
            </SummaryFact>
            <SummaryFact
              label="Assets here"
              empty={assetTotals.data ? 'None' : '…'}
              secondary={
                descendants !== null && assetTotals.data
                  ? `${formatNumber(descendants)} including everything inside`
                  : undefined
              }
            >
              {assetTotals.data && direct > 0 ? formatNumber(direct) : null}
            </SummaryFact>
          </>
        }
      />

      {!node.isActive && (
        <Alert tone="warning" title="This location is inactive">
          It cannot be chosen for an asset, and assets already here cannot be saved until
          they are moved somewhere active.
        </Alert>
      )}

      {/* A building is mostly about the rooms in it; a room, about its kit. */}
      {hasChildren && insideSection}
      {assetsSection}
      {!hasChildren && insideSection}

      {editing && (
        <LocationFormDialog location={stripChildren(node)} onClose={() => setEditing(false)} />
      )}
      {addingInside && (
        <LocationFormDialog defaultParentId={node.id} onClose={() => setAddingInside(false)} />
      )}
      {moving && (
        <MoveLocationsDialog
          moving={[stripChildren(node)]}
          all={locations.data?.items ?? []}
          onClose={() => setMoving(false)}
          onMoved={() => undefined}
        />
      )}
    </div>
  );
}

function InsideSection({
  node,
  columns,
  canAdd,
  onAdd,
  onOpen,
}: {
  node: LocationNode;
  columns: Column<LocationNode>[];
  canAdd: boolean;
  onAdd: () => void;
  onOpen: (id: string) => void;
}) {
  if (node.children.length === 0) {
    // Most rooms contain nothing else; one quiet line says so without a panel
    // of empty state above the equipment that actually matters here.
    return (
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-dashed border-border px-4 py-3">
        <p className="text-sm text-text-muted">
          No other locations sit inside {node.name}.
        </p>
        {canAdd && (
          <Button size="sm" iconLeft={<Plus />} onClick={onAdd}>
            Add inside
          </Button>
        )}
      </div>
    );
  }

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={`Inside ${node.name}`}
        description={`${node.children.length} ${node.children.length === 1 ? 'location' : 'locations'}`}
      />
      <DataTable
        columns={columns}
        rows={node.children}
        rowKey={(child) => child.id}
        onRowClick={(child) => onOpen(child.id)}
        rowClassName={(child) => (child.isActive ? undefined : 'opacity-60')}
      />
    </Card>
  );
}

function AssetsSection({
  node,
  columns,
  query,
  onOpen,
}: {
  node: LocationNode;
  columns: Column<AssetListItem>[];
  query: ReturnType<typeof useAssets>;
  onOpen: (id: string) => void;
}) {
  const total = query.data?.totalCount ?? 0;
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={`Assets in ${node.name}`}
        description={
          node.children.length > 0
            ? 'Recorded against this location itself. Each location inside has its own list.'
            : undefined
        }
        actions={
          total > 0 && (
            <Button size="sm" asChild>
              <Link to={`/assets?locationId=${node.id}`}>
                {total > ASSET_PREVIEW_SIZE
                  ? `View all ${formatNumber(total)}`
                  : 'Open in the register'}
                <ArrowRight className="ml-1.5 size-3.5" aria-hidden />
              </Link>
            </Button>
          )
        }
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          rows={query.data?.items ?? []}
          rowKey={(asset) => asset.id}
          loading={query.isLoading}
          skeletonRows={3}
          onRowClick={(asset) => onOpen(asset.id)}
          empty={
            <EmptyState
              icon={<Boxes />}
              title="No assets recorded here"
              description={
                node.children.length > 0
                  ? 'Equipment in the locations inside this one is listed on their pages.'
                  : 'Set an asset’s location to this one to see it here.'
              }
            />
          }
        />
      )}
    </Card>
  );
}
