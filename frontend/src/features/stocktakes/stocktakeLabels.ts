import type {
  StocktakeItem,
  StocktakeItemFilter,
  StocktakeProgress,
  StocktakeStatus,
} from '@/api/stocktakeTypes';

export const STOCKTAKE_STATUS_LABELS: Record<StocktakeStatus, string> = {
  inProgress: 'In progress',
  completed: 'Completed',
  cancelled: 'Cancelled',
};

/**
 * The item filters, in the order they are worth looking at.
 *
 * `missing` first because it is the reason to do a stocktake at all; `all` last
 * because it is the fallback rather than the starting point.
 */
export const STOCKTAKE_FILTER_TABS: {
  value: StocktakeItemFilter;
  label: string;
  /** Which progress figure counts this filter, for the tab badge. */
  count: keyof StocktakeProgress | null;
  hint: string;
}[] = [
  {
    value: 'missing',
    label: 'Not found',
    count: 'missing',
    hint: 'Expected in this scope but not seen yet.',
  },
  {
    value: 'unexpected',
    label: 'Unexpected',
    count: 'unexpected',
    hint: 'Found here but belongs to another scope.',
  },
  {
    value: 'wrongLocation',
    label: 'Wrong place',
    count: 'wrongLocation',
    hint: 'Seen somewhere other than the location on the register.',
  },
  {
    value: 'conditionChanged',
    label: 'Condition differs',
    count: 'conditionChanged',
    hint: 'Recorded in a different condition to the register.',
  },
  {
    value: 'seen',
    label: 'Seen',
    count: 'seen',
    hint: 'Everything scanned in this session.',
  },
  { value: 'all', label: 'Everything', count: null, hint: 'Every asset in scope.' },
];

/**
 * The discrepancies on one item.
 *
 * An item can carry more than one at once — found in the wrong room *and* in a
 * worse condition — so this returns a list rather than picking a single state.
 */
export function itemDiscrepancies(item: StocktakeItem): string[] {
  const flags: string[] = [];
  if (item.isUnexpected) flags.push('Unexpected here');
  if (item.isWrongLocation) flags.push('Wrong place');
  if (item.isConditionChanged) flags.push('Condition differs');
  if (item.isExpected && !item.isSeen) flags.push('Not found');
  return flags;
}

/**
 * How far through the expected set the session is.
 *
 * Deliberately measured against `expected` only. Unexpected finds are real
 * findings but they are not progress through the scope, and counting them would
 * let a session report more than 100% complete.
 */
export function seenFraction(progress: StocktakeProgress): number {
  if (progress.expected === 0) return 0;
  return Math.min(1, progress.seen / progress.expected);
}
