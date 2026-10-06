import {
  ChevronRight,
  ChevronsDownUp,
  ChevronsUpDown,
  FolderInput,
  MapPin,
  Pencil,
  Plus,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { useExportLocations, useLocationAssetTotals, useLocations } from '@/api/hooks';
import type { Location } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Alert } from '@/ui/Alert';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { FilterButton } from '@/ui/FilterButton';
import { ListToolbar } from '@/ui/ListToolbar';
import { PageHeader } from '@/ui/PageHeader';
import { SearchInput } from '@/ui/SearchInput';
import { SelectionBar } from '@/ui/SelectionBar';
import { StatusDot } from '@/ui/StatusDot';

import { ImportExportActions } from '../imports/ImportExportActions';
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

interface VisibleRow {
  node: LocationNode;
  depth: number;
  expanded: boolean;
}

const INACTIVE_OPTIONS = [{ value: 'true', label: 'Included' }];

/** Which branches are open survives a trip into a location and back. */
const EXPANDED_KEY = 'inventory.locations.expanded';

function readExpanded(): ReadonlySet<string> {
  try {
    const stored = sessionStorage.getItem(EXPANDED_KEY);
    return new Set(stored ? (JSON.parse(stored) as string[]) : []);
  } catch {
    return new Set();
  }
}

function writeExpanded(ids: ReadonlySet<string>) {
  try {
    sessionStorage.setItem(EXPANDED_KEY, JSON.stringify([...ids]));
  } catch {
    // Storage is optional; the tree just reopens collapsed.
  }
}

function matchesQuery(location: Location, query: string): boolean {
  return [location.name, location.code, location.type ?? ''].some((value) =>
    value.toLowerCase().includes(query),
  );
}

/**
 * Every location as a table that nests: buildings open to show their rooms.
 *
 * Only the top level is open to begin with, so a school with a hundred rooms
 * sees its handful of buildings rather than one long list. Ticking rooms and
 * using "Move into" is how a flat import gets organised into buildings. A row
 * opens that location's own page, the way an asset row does.
 */
export default function LocationsPage() {
  const { can } = useSession();
  const canManage = can(Permission.ReferenceManage);
  const navigate = useNavigate();
  const url = useUrlState();
  const query = url.get('q') ?? '';
  const includeInactive = url.getBoolean('inactive') ?? false;

  const [expanded, setExpandedState] = useState<ReadonlySet<string>>(readExpanded);
  const [checked, setChecked] = useState<Set<string>>(() => new Set());
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<Location | null>(null);
  const [moving, setMoving] = useState<Location[] | null>(null);

  const locations = useLocations(includeInactive);
  // The move picker needs inactive ones too, to judge what can go where.
  const allLocations = useLocations(true, canManage);
  const assetTotals = useLocationAssetTotals();
  const exportLocations = useExportLocations();

  const tree = useMemo(
    () => buildLocationTree(locations.data?.items ?? []),
    [locations.data],
  );
  const totals = useMemo(
    () => rollUpTotals(tree.roots, assetTotals.data?.totals ?? new Map()),
    [tree, assetTotals.data],
  );

  const trimmedQuery = query.trim().toLowerCase();

  const rows = useMemo(() => {
    let visible: Set<string> | null = null;
    let open: ReadonlySet<string> = expanded;

    if (trimmedQuery) {
      // A match shows with its whole path, opened, so it is never out of context.
      visible = new Set();
      const forced = new Set<string>();
      for (const node of tree.byId.values()) {
        if (!matchesQuery(node, trimmedQuery)) continue;
        visible.add(node.id);
        for (const ancestor of ancestorsOf(node.id, tree.byId)) {
          visible.add(ancestor.id);
          forced.add(ancestor.id);
        }
      }
      open = forced;
    }

    const result: VisibleRow[] = [];
    const walk = (nodes: LocationNode[], depth: number) => {
      for (const node of nodes) {
        if (visible && !visible.has(node.id)) continue;
        const isOpen = open.has(node.id) && node.children.length > 0;
        result.push({ node, depth, expanded: isOpen });
        if (isOpen) walk(node.children, depth + 1);
      }
    };
    walk(tree.roots, 0);
    return result;
  }, [tree, expanded, trimmedQuery]);

  const nestedCount = [...tree.byId.values()].filter((node) =>
    node.parentLocationId ? tree.byId.has(node.parentLocationId) : false,
  ).length;
  const parentIds = [...tree.byId.values()]
    .filter((node) => node.children.length > 0)
    .map((node) => node.id);

  function setExpanded(next: ReadonlySet<string>) {
    setExpandedState(next);
    writeExpanded(next);
  }

  function toggleExpanded(id: string) {
    const next = new Set(expanded);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setExpanded(next);
  }

  function openMove(ids: Iterable<string>) {
    const pool = allLocations.data?.items ?? locations.data?.items ?? [];
    const wanted = new Set(ids);
    setMoving(pool.filter((location) => wanted.has(location.id)));
  }

  const checkedList = [...checked].filter((id) => tree.byId.has(id));
  // In a flat list no row has a chevron, so no row needs a space kept for one.
  const anyExpandable = !trimmedQuery && rows.some((row) => row.node.children.length > 0);

  const columns: Column<VisibleRow>[] = [
    {
      id: 'location',
      header: 'Location',
      cell: ({ node, depth, expanded: isOpen }) => {
        const hasChildren = node.children.length > 0;
        return (
          <div
            className="flex min-w-0 items-center gap-2"
            // Indent by nesting depth; school sites are rarely more than three deep.
            style={{ paddingLeft: `${depth * 1.5}rem` }}
          >
            {hasChildren && !trimmedQuery ? (
              <button
                type="button"
                onClick={(event) => {
                  // Opening a branch must not also open the location's page.
                  event.stopPropagation();
                  toggleExpanded(node.id);
                }}
                aria-expanded={isOpen}
                aria-label={isOpen ? `Collapse ${node.name}` : `Expand ${node.name}`}
                className="flex size-6 shrink-0 items-center justify-center rounded-sm text-text-subtle hover:bg-surface-sunken hover:text-text"
              >
                <ChevronRight
                  className={cn(
                    'size-4 transition-transform duration-(--duration-fast)',
                    isOpen && 'rotate-90',
                  )}
                />
              </button>
            ) : anyExpandable ? (
              // Keeps leaf rows lined up with their expandable siblings.
              <span className="size-6 shrink-0" aria-hidden="true" />
            ) : null}
            <LocationGlyph type={node.type} hasChildren={hasChildren} />
            <div className="min-w-0">
              <Link
                to={`/locations/${node.id}`}
                onClick={(event) => event.stopPropagation()}
                className="block truncate rounded-xs font-medium text-text hover:text-accent hover:underline"
              >
                {node.name}
              </Link>
              <p className="truncate text-xs text-text-muted">
                {node.type ?? <span className="text-text-subtle">No type</span>}
                {hasChildren && (
                  <span className="text-text-subtle">
                    {' · '}
                    {node.children.length} inside
                  </span>
                )}
              </p>
            </div>
          </div>
        );
      },
    },
    {
      id: 'code',
      header: 'Code',
      hideBelow: 'md',
      width: 'w-40',
      cell: ({ node }) => (
        <span className="block truncate font-mono text-xs text-text-muted">{node.code}</span>
      ),
    },
    {
      id: 'assets',
      header: 'Assets',
      align: 'right',
      width: 'w-28',
      cell: ({ node }) => {
        if (!assetTotals.data) return <span className="text-text-subtle">…</span>;
        const total = totals.get(node.id) ?? 0;
        return total === 0 ? (
          <span className="text-text-subtle">—</span>
        ) : (
          <span
            className="tabular-nums text-text"
            title={
              node.children.length > 0
                ? 'Active assets here and in everything inside it'
                : 'Active assets here'
            }
          >
            {formatNumber(total)}
          </span>
        );
      },
    },
    ...(includeInactive
      ? ([
          {
            id: 'status',
            header: 'Status',
            width: 'w-28',
            cell: ({ node }) =>
              node.isActive ? (
                <StatusDot tone="success" muted>
                  Active
                </StatusDot>
              ) : (
                <StatusDot tone="neutral" muted>
                  Inactive
                </StatusDot>
              ),
          },
        ] satisfies Column<VisibleRow>[])
      : []),
    ...(canManage
      ? ([
          {
            id: 'actions',
            header: <span className="sr-only">Actions</span>,
            align: 'right',
            width: 'w-14',
            cell: ({ node }) => (
              <IconButton
                label={`Edit ${node.name}`}
                size="sm"
                onClick={(event) => {
                  event.stopPropagation();
                  setEditing(stripChildren(node));
                }}
              >
                <Pencil />
              </IconButton>
            ),
          },
        ] satisfies Column<VisibleRow>[])
      : []),
  ];

  const isEmpty = !locations.isLoading && tree.byId.size === 0;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Locations"
        description="Buildings, rooms and stores. Put rooms inside their building so equipment can be found by place."
        actions={
          <>
            <ImportExportActions
              importPath="/locations/import"
              exportPermission={Permission.ExportsLocations}
              exportMutation={exportLocations}
            />
            {canManage && (
              <Button variant="primary" iconLeft={<Plus />} onClick={() => setCreating(true)}>
                New location
              </Button>
            )}
          </>
        }
      />

      {canManage && nestedCount === 0 && tree.byId.size > 8 && (
        <Alert tone="info" title="Every location is at the top level">
          Group rooms into their buildings to make this list manageable: tick the rooms
          that belong together and choose <strong>Move into…</strong>, creating the
          building in the same step. Assets keep their room; the building just gathers
          them.
        </Alert>
      )}

      <Card className="overflow-hidden">
        <ListToolbar
          search={
            <SearchInput
              value={query}
              onChange={(value) => url.set({ q: value })}
              debounceMs={150}
              placeholder="Find a room, building or code…"
              aria-label="Find a location"
            />
          }
          count={
            locations.data
              ? `${formatNumber(tree.byId.size)} ${tree.byId.size === 1 ? 'location' : 'locations'} · ${formatNumber(nestedCount)} nested`
              : undefined
          }
          onClearFilters={url.hasFilters() ? url.clear : undefined}
          filters={
            <FilterButton
              label="Inactive"
              emptyLabel="Hidden"
              value={includeInactive ? 'true' : null}
              onChange={(value) => url.set({ inactive: value === 'true' })}
              options={INACTIVE_OPTIONS}
            />
          }
          actions={
            parentIds.length > 0 &&
            !trimmedQuery && (
              <>
                <Button
                  size="sm"
                  variant="ghost"
                  iconLeft={<ChevronsUpDown />}
                  onClick={() => setExpanded(new Set(parentIds))}
                >
                  Expand all
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  iconLeft={<ChevronsDownUp />}
                  onClick={() => setExpanded(new Set())}
                >
                  Collapse all
                </Button>
              </>
            )
          }
        />

        {locations.isError ? (
          <ErrorState error={locations.error} onRetry={() => void locations.refetch()} />
        ) : (
          <DataTable
            columns={columns}
            rows={rows}
            rowKey={(row) => row.node.id}
            loading={locations.isLoading}
            onRowClick={(row) => void navigate(`/locations/${row.node.id}`)}
            selectedKeys={canManage ? checked : undefined}
            onSelectionChange={canManage ? setChecked : undefined}
            rowClassName={(row) => (row.node.isActive ? undefined : 'opacity-60')}
            empty={
              isEmpty && !trimmedQuery ? (
                <EmptyState
                  icon={<MapPin />}
                  title="No locations yet"
                  description="Add the buildings, rooms and stores where equipment lives, so assets can be tracked to a place."
                  action={
                    canManage && (
                      <Button
                        variant="primary"
                        iconLeft={<Plus />}
                        onClick={() => setCreating(true)}
                      >
                        New location
                      </Button>
                    )
                  }
                />
              ) : (
                <NoResults onClear={url.clear} />
              )
            }
          />
        )}
      </Card>

      {canManage && (
        <SelectionBar
          count={checkedList.length}
          noun={{ one: 'location', other: 'locations' }}
          onClear={() => setChecked(new Set())}
        >
          <Button
            size="sm"
            variant="primary"
            iconLeft={<FolderInput />}
            onClick={() => openMove(checkedList)}
          >
            Move into…
          </Button>
        </SelectionBar>
      )}

      {/* Mounted only while open so each form initialises from what it was opened
          on; a persistent dialog would need an effect to resync. */}
      {creating && <LocationFormDialog onClose={() => setCreating(false)} />}
      {editing && <LocationFormDialog location={editing} onClose={() => setEditing(null)} />}
      {moving && (
        <MoveLocationsDialog
          moving={moving}
          all={allLocations.data?.items ?? locations.data?.items ?? []}
          onClose={() => setMoving(null)}
          onMoved={(parentId) => {
            setChecked(new Set());
            // Open the destination so the rooms just moved are in view.
            if (parentId) setExpanded(new Set([...expanded, parentId]));
          }}
        />
      )}
    </div>
  );
}
