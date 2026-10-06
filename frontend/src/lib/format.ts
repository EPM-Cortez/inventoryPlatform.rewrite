import type {
  AssetCondition,
  AssetStatus,
  IsoDate,
  IsoInstant,
  PersonType,
  WarrantyState,
} from '@/api/types';

/** Single-school UK deployment; dates read as `25 Jul 2026`, not `7/25/2026`. */
const LOCALE = 'en-GB';

const dateFormatter = new Intl.DateTimeFormat(LOCALE, {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
});

const dateTimeFormatter = new Intl.DateTimeFormat(LOCALE, {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

const timeFormatter = new Intl.DateTimeFormat(LOCALE, {
  hour: '2-digit',
  minute: '2-digit',
});

const relativeFormatter = new Intl.RelativeTimeFormat(LOCALE, { numeric: 'auto' });

/**
 * `DateOnly` values carry no time or zone. Parsing them with `new Date('2026-07-25')`
 * yields midnight *UTC*, which renders as the previous day west of Greenwich. Build
 * the date in local time instead so the calendar day is always the one stored.
 */
export function parseIsoDate(value: IsoDate): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const [, year, month, day] = match;
  return new Date(Number(year), Number(month) - 1, Number(day));
}

export function formatDate(value: IsoDate | null | undefined, fallback = '—'): string {
  if (!value) return fallback;
  const date = parseIsoDate(value);
  return date ? dateFormatter.format(date) : fallback;
}

export function formatDateTime(
  value: IsoInstant | null | undefined,
  fallback = '—',
): string {
  if (!value) return fallback;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? fallback : dateTimeFormatter.format(date);
}

export function formatTime(value: IsoInstant | null | undefined, fallback = '—'): string {
  if (!value) return fallback;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? fallback : timeFormatter.format(date);
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 60 * 60 * 1000],
  ['month', 30 * 24 * 60 * 60 * 1000],
  ['week', 7 * 24 * 60 * 60 * 1000],
  ['day', 24 * 60 * 60 * 1000],
  ['hour', 60 * 60 * 1000],
  ['minute', 60 * 1000],
];

/**
 * Whole calendar days from `today` to a date, as words: `today`, `tomorrow`,
 * `in 5 days`, `3 days ago`. `today` is passed in — captured once when the view
 * mounts — because reading the clock during render is impure.
 */
export function describeDaysFromToday(
  value: IsoDate | null | undefined,
  today: Date,
): { days: number; label: string } | null {
  if (!value) return null;
  const date = parseIsoDate(value);
  if (!date) return null;
  const midnight = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  // Rounded because a clock change makes one day 23 or 25 hours long.
  const days = Math.round((date.getTime() - midnight.getTime()) / 86_400_000);
  return { days, label: relativeFormatter.format(days, 'day') };
}

/** `3 days ago`, `in 2 months`. Used where the exact instant is noise. */
export function formatRelative(
  value: IsoInstant | null | undefined,
  fallback = '—',
): string {
  if (!value) return fallback;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return fallback;

  const deltaMs = date.getTime() - Date.now();
  for (const [unit, unitMs] of RELATIVE_UNITS) {
    if (Math.abs(deltaMs) >= unitMs) {
      return relativeFormatter.format(Math.round(deltaMs / unitMs), unit);
    }
  }
  return 'just now';
}

/**
 * The API sends purchase cost as a decimal with a separate ISO currency code.
 * Missing currency falls back to GBP, matching the single-school deployment.
 */
export function formatMoney(
  amount: number | null | undefined,
  currency: string | null | undefined,
  fallback = '—',
): string {
  if (amount === null || amount === undefined) return fallback;
  try {
    return new Intl.NumberFormat(LOCALE, {
      style: 'currency',
      currency: currency || 'GBP',
      minimumFractionDigits: 2,
    }).format(amount);
  } catch {
    // An unrecognised currency code should not blank out the number.
    return `${amount.toFixed(2)} ${currency ?? ''}`.trim();
  }
}

export function formatNumber(value: number): string {
  return new Intl.NumberFormat(LOCALE).format(value);
}

/**
 * Storage and memory, in the units the hardware is sold in.
 *
 * Uses binary steps (1024) but the familiar GB/TB labels, because "8 GB of RAM"
 * is what a technician expects to read even though the machine reports
 * 8,589,934,592 bytes. Precision drops as the unit grows — nobody needs
 * `476.94 GB` to two decimals when `477 GB` identifies the disk.
 */
export function formatBytes(bytes: number | null | undefined, fallback = '—'): string {
  if (bytes === null || bytes === undefined) return fallback;
  if (bytes === 0) return '0 bytes';

  const units = ['bytes', 'KB', 'MB', 'GB', 'TB', 'PB'];
  const exponent = Math.min(
    Math.floor(Math.log(Math.abs(bytes)) / Math.log(1024)),
    units.length - 1,
  );
  const value = bytes / 1024 ** exponent;
  const decimals = exponent <= 1 ? 0 : value >= 100 ? 0 : 1;

  return `${value.toFixed(decimals)} ${units[exponent]}`;
}

/** `expires in 14 days`, `expired 3 days ago`, given the API's `daysRemaining`. */
export function formatDaysRemaining(days: number | null | undefined): string | null {
  if (days === null || days === undefined) return null;
  if (days === 0) return 'expires today';
  if (days > 0) return `expires in ${days} day${days === 1 ? '' : 's'}`;
  const past = Math.abs(days);
  return `expired ${past} day${past === 1 ? '' : 's'} ago`;
}

export function initials(displayName: string): string {
  const parts = displayName.trim().split(/[\s.]+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return (parts[0] ?? '?').slice(0, 2).toUpperCase();
  return `${parts[0]?.[0] ?? ''}${parts[parts.length - 1]?.[0] ?? ''}`.toUpperCase();
}

// ---------------------------------------------------------------------------
// Enum labels
//
// The wire values are camel-case; these are the words staff actually read.
// ---------------------------------------------------------------------------

export const ASSET_STATUS_LABELS: Record<AssetStatus, string> = {
  inStock: 'In stock',
  reserved: 'Reserved',
  assigned: 'Assigned',
  repair: 'In repair',
  lost: 'Lost',
  stolen: 'Stolen',
  retired: 'Retired',
  disposed: 'Disposed',
};

export const ASSET_CONDITION_LABELS: Record<AssetCondition, string> = {
  new: 'New',
  good: 'Good',
  fair: 'Fair',
  damaged: 'Damaged',
  unserviceable: 'Unserviceable',
};

export const WARRANTY_STATE_LABELS: Record<WarrantyState, string> = {
  notRecorded: 'Not recorded',
  future: 'Not started',
  active: 'Active',
  expiringSoon: 'Expiring soon',
  expired: 'Expired',
};

export const PERSON_TYPE_LABELS: Record<PersonType, string> = {
  staff: 'Staff',
  pupil: 'Pupil',
  contractor: 'Contractor',
  other: 'Other',
};

/**
 * Asset history `type` values are PascalCase domain event names such as
 * `AssetAssigned`. Split them into words rather than maintaining a map that
 * silently falls behind the backend when a new event type ships.
 */
export function formatEventType(type: string): string {
  const words = type.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  return words.charAt(0).toUpperCase() + words.slice(1).toLowerCase();
}
