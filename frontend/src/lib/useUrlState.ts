import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';

export interface UrlState {
  get: (key: string) => string | undefined;
  getNumber: (key: string, fallback: number) => number;
  getBoolean: (key: string) => boolean | undefined;
  /**
   * Merges a patch into the query string. `undefined` removes a key, and any
   * change other than `page` itself resets paging — landing on page 7 of a
   * freshly narrowed result set is almost never what the user wanted.
   */
  set: (patch: Record<string, string | number | boolean | undefined>) => void;
  /**
   * Replaces the whole query string rather than merging into it.
   *
   * For applying a stored view — a saved search — where anything currently set
   * and absent from the new state must go. Merging would silently keep the old
   * filters and show a result set that matches neither.
   */
  replaceAll: (next: Record<string, string | number | boolean | undefined>) => void;
  clear: () => void;
  /** True when anything beyond paging and sorting is set. */
  hasFilters: (ignore?: string[]) => boolean;
}

const PAGING_KEYS = ['page', 'pageSize', 'sort'];

/**
 * Keeps list filters in the URL rather than component state, so a filtered view
 * can be bookmarked, shared with a colleague, and survives a refresh or a trip
 * into an asset and back.
 */
export function useUrlState(): UrlState {
  const [searchParams, setSearchParams] = useSearchParams();

  const get = useCallback(
    (key: string) => searchParams.get(key) ?? undefined,
    [searchParams],
  );

  const set = useCallback(
    (patch: Record<string, string | number | boolean | undefined>) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current);
          for (const [key, value] of Object.entries(patch)) {
            if (value === undefined || value === '' || value === false) {
              next.delete(key);
            } else {
              next.set(key, String(value));
            }
          }
          const changedFilter = Object.keys(patch).some(
            (key) => !PAGING_KEYS.includes(key),
          );
          if (changedFilter && !('page' in patch)) {
            next.delete('page');
          }
          return next;
        },
        { replace: true },
      );
    },
    [setSearchParams],
  );

  const replaceAll = useCallback(
    (next: Record<string, string | number | boolean | undefined>) => {
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(next)) {
        if (value === undefined || value === '' || value === false) continue;
        params.set(key, String(value));
      }
      setSearchParams(params, { replace: true });
    },
    [setSearchParams],
  );

  const clear = useCallback(() => {
    setSearchParams(new URLSearchParams(), { replace: true });
  }, [setSearchParams]);

  return useMemo(
    () => ({
      get,
      set,
      replaceAll,
      clear,
      getNumber: (key, fallback) => {
        const parsed = Number(searchParams.get(key));
        return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
      },
      getBoolean: (key) => {
        const raw = searchParams.get(key);
        if (raw === null) return undefined;
        return raw === 'true';
      },
      hasFilters: (ignore = []) => {
        const skip = new Set([...PAGING_KEYS, ...ignore]);
        for (const key of searchParams.keys()) {
          if (!skip.has(key)) return true;
        }
        return false;
      },
    }),
    [clear, get, replaceAll, searchParams, set],
  );
}

/** Narrows an arbitrary query-string value to a known union member. */
export function asEnum<T extends string>(
  value: string | undefined,
  allowed: readonly T[],
): T | undefined {
  return value !== undefined && (allowed as readonly string[]).includes(value)
    ? (value as T)
    : undefined;
}
