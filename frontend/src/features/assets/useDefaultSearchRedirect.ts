import { useEffect, useRef } from 'react';

import { useDefaultSavedSearch } from '@/api/hooks';
import type { UrlState } from '@/lib/useUrlState';

import { fromSavedFilter } from './savedSearchFilter';

/**
 * Applies the user's default saved search when they arrive at an unfiltered list.
 *
 * Two conditions make this safe rather than annoying:
 *
 * - it only fires when the URL carries no filters at all, so a bookmarked or
 *   shared link is never overwritten;
 * - it fires **once** per mount. Without the ref, clearing the filters by hand
 *   would immediately re-apply the default, and the user could never get back to
 *   an unfiltered list.
 *
 * The redirect replaces the history entry, so Back does not bounce between the
 * empty list and the default view.
 */
export function useDefaultSearchRedirect(url: UrlState, enabled: boolean): void {
  const defaultSearch = useDefaultSavedSearch(enabled);
  const applied = useRef(false);

  const hasAnyParam = url.hasFilters([]) || url.get('page') !== undefined;
  const search = defaultSearch.data;

  useEffect(() => {
    if (applied.current || !search || hasAnyParam) return;
    applied.current = true;
    url.replaceAll({ ...fromSavedFilter(search.filter) });
  }, [hasAnyParam, search, url]);
}
