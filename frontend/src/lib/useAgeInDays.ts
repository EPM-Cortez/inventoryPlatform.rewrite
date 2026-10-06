import { useState } from 'react';

/**
 * Age of an instant in days, measured from when the view was first rendered.
 *
 * Reading `Date.now()` during render is impure — it makes the same props produce
 * different output, which React's compiler rules reject and which would let a
 * badge flip between "6 days" and "7 days" on an unrelated re-render. Capturing
 * the reference time once at mount keeps render pure and the reading stable for
 * as long as the user is looking at it.
 */
export function useAgeInDays(instant: string | null | undefined): number | null {
  const [now] = useState(() => Date.now());

  if (!instant) return null;
  const time = new Date(instant).getTime();
  if (Number.isNaN(time)) return null;

  return (now - time) / 86_400_000;
}

/** Shared thresholds so staleness means the same thing everywhere. */
export function stalenessTone(
  ageDays: number | null,
): 'neutral' | 'success' | 'warning' | 'danger' {
  if (ageDays === null) return 'neutral';
  if (ageDays > 30) return 'danger';
  if (ageDays > 7) return 'warning';
  return 'success';
}
