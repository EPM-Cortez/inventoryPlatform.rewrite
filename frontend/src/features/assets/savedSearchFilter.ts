import type { AssetSearchParams, SavedAssetFilter } from '@/api/types';

/**
 * Translates between the asset list's query parameters and the shape a saved
 * search stores.
 *
 * They are almost the same, and the two differences are exactly the kind that
 * fail quietly:
 *
 * - the list calls the free-text parameter `q`, the saved filter calls it
 *   `query`. Miss it and a saved search silently loses its search text.
 * - the saved filter has no `page`. A saved search is a set of criteria, not a
 *   position in a result set, so restoring one must always start at page 1.
 */

export function toSavedFilter(params: AssetSearchParams): SavedAssetFilter {
  const { q, page: _page, ...rest } = params;
  return { ...rest, query: q ?? null };
}

export function fromSavedFilter(filter: SavedAssetFilter): AssetSearchParams {
  const { query, ...rest } = filter;

  // Nulls are dropped rather than passed through, so a restored search produces
  // the same URL a user would get by setting those filters by hand — otherwise
  // `?status=` litters the address bar and `hasFilters()` reports filters that
  // are not really set. The cast is safe because every remaining value is
  // non-null and the two shapes agree on names beyond `query`.
  const params: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(rest)) {
    if (value === null || value === undefined || value === '') continue;
    params[key] = value;
  }
  if (query) params.q = query;
  // Restoring a saved search always lands on the first page.
  params.page = 1;

  return params as AssetSearchParams;
}

/** A short human summary of what a saved search actually filters on. */
export function describeSavedFilter(filter: SavedAssetFilter): string {
  const parts: string[] = [];
  if (filter.query) parts.push(`“${filter.query}”`);
  if (filter.assetTag) parts.push(`tag ${filter.assetTag}`);
  if (filter.serialNumber) parts.push(`serial ${filter.serialNumber}`);
  if (filter.status) parts.push(filter.status);
  if (filter.condition) parts.push(filter.condition);
  if (filter.categoryId) parts.push('a category');
  if (filter.locationId) parts.push('a location');
  if (filter.departmentId) parts.push('a department');
  if (filter.assignedPersonId) parts.push('a holder');
  if (filter.assigned === true) parts.push('assigned');
  if (filter.assigned === false) parts.push('unassigned');
  if (filter.warrantyState) parts.push(`warranty ${filter.warrantyState}`);
  if (filter.warrantyEndsBefore) parts.push(`warranty before ${filter.warrantyEndsBefore}`);
  if (filter.legacyId) parts.push(`legacy ${filter.legacyId}`);
  return parts.length > 0 ? parts.join(' · ') : 'Everything';
}
