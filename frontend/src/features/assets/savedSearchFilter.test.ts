import { describe, expect, it } from 'vitest';

import type { AssetSearchParams, SavedAssetFilter } from '@/api/types';

import { describeSavedFilter, fromSavedFilter, toSavedFilter } from './savedSearchFilter';

describe('toSavedFilter', () => {
  it('renames the free-text parameter, which the two shapes disagree on', () => {
    // The whole reason this module exists: the list calls it `q`, the saved
    // filter calls it `query`. Getting it wrong loses the search text silently.
    expect(toSavedFilter({ q: 'keyboard' }).query).toBe('keyboard');
    expect('q' in toSavedFilter({ q: 'keyboard' })).toBe(false);
  });

  it('drops the page, because a saved search is criteria not a position', () => {
    const saved = toSavedFilter({ q: 'x', page: 7, pageSize: 50 });
    expect('page' in saved).toBe(false);
    expect(saved.pageSize).toBe(50);
  });

  it('keeps every other filter as it is', () => {
    const params: AssetSearchParams = {
      assetTag: 'SCH-1',
      serialNumber: 'ABC',
      categoryId: 'cat-1',
      status: 'inStock',
      condition: 'good',
      locationId: 'loc-1',
      departmentId: 'dep-1',
      assignedPersonId: 'per-1',
      assigned: false,
      warrantyState: 'expired',
      warrantyEndsBefore: '2026-01-01',
      legacyId: 'L-9',
      sort: '-updatedAtUtc',
    };
    expect(toSavedFilter(params)).toMatchObject({ ...params, query: null });
  });
});

describe('fromSavedFilter', () => {
  it('restores the search text under the name the list expects', () => {
    expect(fromSavedFilter({ query: 'laptop' }).q).toBe('laptop');
  });

  it('always starts at the first page', () => {
    expect(fromSavedFilter({ query: 'laptop' }).page).toBe(1);
  });

  it('drops nulls so the address bar matches a hand-set filter', () => {
    const params = fromSavedFilter({
      query: null,
      status: null,
      assetTag: 'SCH-1',
      condition: undefined,
    });
    expect(params).toEqual({ assetTag: 'SCH-1', page: 1 });
  });

  it('keeps a false `assigned`, which is a real filter not an empty one', () => {
    // `false` means "show unassigned only" — dropping it as falsy would turn a
    // saved search for unassigned assets into a search for everything.
    expect(fromSavedFilter({ assigned: false }).assigned).toBe(false);
  });

  it('round-trips a filled-in filter', () => {
    const original: AssetSearchParams = {
      q: 'dell',
      status: 'assigned',
      categoryId: 'cat-1',
      assigned: true,
      pageSize: 25,
      sort: 'assetTag',
    };
    const restored = fromSavedFilter(toSavedFilter(original));
    expect(restored).toEqual({ ...original, page: 1 });
  });
});

describe('describeSavedFilter', () => {
  it('says so when nothing is filtered', () => {
    expect(describeSavedFilter({})).toBe('Everything');
  });

  it('distinguishes assigned from unassigned', () => {
    expect(describeSavedFilter({ assigned: true })).toBe('assigned');
    expect(describeSavedFilter({ assigned: false })).toBe('unassigned');
  });

  it('lists what is set', () => {
    const filter: SavedAssetFilter = { query: 'ipad', status: 'inStock' };
    expect(describeSavedFilter(filter)).toBe('“ipad” · inStock');
  });
});
