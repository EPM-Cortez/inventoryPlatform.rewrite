import type { Location } from '@/api/types';

export interface LocationNode extends Location {
  children: LocationNode[];
}

export interface LocationTree {
  roots: LocationNode[];
  byId: Map<string, LocationNode>;
}

/** The API record without the tree's `children`, for forms and requests. */
export function stripChildren({ children: _children, ...location }: LocationNode): Location {
  return location;
}

/**
 * Natural order, so "EN2" comes before "EN10" and "Block B" before "block C".
 * Room numbering is almost always numeric, and plain string order scatters it.
 */
export function compareLocations(a: Location, b: Location): number {
  return (
    a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }) ||
    a.code.localeCompare(b.code, undefined, { numeric: true, sensitivity: 'base' })
  );
}

/**
 * Builds the parent/child tree the API describes with `parentLocationId`.
 *
 * Anything whose parent is missing from the response — filtered out as inactive,
 * most often — is promoted to the root rather than dropped, so a location can
 * never silently disappear from the page. The same goes for a pre-existing cycle,
 * which would otherwise leave every member unreachable from any root.
 */
export function buildLocationTree(locations: readonly Location[]): LocationTree {
  const byId = new Map<string, LocationNode>(
    locations.map((location) => [location.id, { ...location, children: [] }]),
  );
  const roots: LocationNode[] = [];

  for (const node of byId.values()) {
    const parent = node.parentLocationId ? byId.get(node.parentLocationId) : undefined;
    if (parent && parent !== node) parent.children.push(node);
    else roots.push(node);
  }

  const reached = new Set<string>();
  const mark = (node: LocationNode) => {
    if (reached.has(node.id)) return;
    reached.add(node.id);
    for (const child of node.children) mark(child);
  };
  roots.forEach(mark);

  for (const node of byId.values()) {
    if (reached.has(node.id)) continue;
    const parent = node.parentLocationId ? byId.get(node.parentLocationId) : undefined;
    if (parent) parent.children = parent.children.filter((child) => child !== node);
    roots.push(node);
    mark(node);
  }

  const sortDeep = (list: LocationNode[]) => {
    list.sort(compareLocations);
    for (const node of list) sortDeep(node.children);
  };
  sortDeep(roots);

  return { roots, byId };
}

/** Outermost first, excluding the location itself. Stops on a cycle. */
export function ancestorsOf(
  locationId: string,
  byId: ReadonlyMap<string, Location>,
): Location[] {
  const path: Location[] = [];
  const seen = new Set<string>([locationId]);
  let parentId = byId.get(locationId)?.parentLocationId ?? null;
  while (parentId && !seen.has(parentId)) {
    const parent = byId.get(parentId);
    if (!parent) break;
    path.unshift(parent);
    seen.add(parentId);
    parentId = parent.parentLocationId;
  }
  return path;
}

/**
 * Each location's own count plus everything beneath it, so a building shows the
 * equipment in all of its rooms. Built from `buildLocationTree` output, which is
 * already free of cycles.
 */
export function rollUpTotals(
  roots: readonly LocationNode[],
  direct: ReadonlyMap<string, number>,
): Map<string, number> {
  const totals = new Map<string, number>();
  const visit = (node: LocationNode): number => {
    let sum = direct.get(node.id) ?? 0;
    for (const child of node.children) sum += visit(child);
    totals.set(node.id, sum);
    return sum;
  };
  roots.forEach(visit);
  return totals;
}

/**
 * A location and everything beneath it.
 *
 * A location cannot be its own ancestor, and the server rejects the attempt with
 * `location_cycle`. Removing the whole subtree from a parent picker turns that
 * into something the user cannot select in the first place.
 *
 * The walk tolerates a cycle already present in the data — a corrupt parent
 * chain must not hang the page.
 */
export function descendantsOf(
  locationId: string,
  locations: readonly Location[],
): Set<string> {
  const childrenByParent = new Map<string, Location[]>();
  for (const location of locations) {
    if (!location.parentLocationId) continue;
    const siblings = childrenByParent.get(location.parentLocationId) ?? [];
    siblings.push(location);
    childrenByParent.set(location.parentLocationId, siblings);
  }

  const excluded = new Set<string>([locationId]);
  const queue = [locationId];
  while (queue.length > 0) {
    for (const child of childrenByParent.get(queue.pop()!) ?? []) {
      // `add` returning false means it was already seen, which both dedupes and
      // stops a pre-existing cycle from looping forever.
      if (!excluded.has(child.id)) {
        excluded.add(child.id);
        queue.push(child.id);
      }
    }
  }
  return excluded;
}
