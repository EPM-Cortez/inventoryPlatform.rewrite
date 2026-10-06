import type { LeaverStatus } from '@/api/operationsTypes';

export const LEAVER_STATUS_LABELS: Record<LeaverStatus, string> = {
  open: 'Open',
  completed: 'Completed',
  cancelled: 'Cancelled',
  rehired: 'Rehired',
};

export const LEAVER_STATUS_TONES: Record<
  LeaverStatus,
  'accent' | 'success' | 'neutral' | 'info'
> = {
  open: 'accent',
  completed: 'success',
  cancelled: 'neutral',
  rehired: 'info',
};

/**
 * Why a case cannot be completed yet, or null when it can.
 *
 * The server refuses with `leaver_has_open_custody`, so the button is disabled
 * with the reason attached rather than letting someone click into a 409 — the
 * outstanding items are already on screen, and the fix is to return them.
 */
export function completionBlocker(currentAssetCount: number): string | null {
  if (currentAssetCount === 0) return null;
  return currentAssetCount === 1
    ? 'One item is still out with them.'
    : `${currentAssetCount} items are still out with them.`;
}
