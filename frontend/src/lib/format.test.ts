import { describe, expect, it } from 'vitest';

import {
  describeDaysFromToday,
  formatDate,
  formatDaysRemaining,
  formatEventType,
  formatMoney,
  initials,
  parseIsoDate,
} from './format';

describe('describeDaysFromToday', () => {
  // Late in the evening, so a sloppy "now minus date" would round the wrong way.
  const today = new Date(2026, 9, 6, 22, 30);

  it('counts whole calendar days, ignoring the time of day', () => {
    expect(describeDaysFromToday('2026-10-06', today)).toEqual({ days: 0, label: 'today' });
    expect(describeDaysFromToday('2026-10-07', today)?.label).toBe('tomorrow');
    expect(describeDaysFromToday('2026-10-11', today)).toEqual({
      days: 5,
      label: 'in 5 days',
    });
    expect(describeDaysFromToday('2026-10-03', today)).toEqual({
      days: -3,
      label: '3 days ago',
    });
  });

  it('survives the clocks going back in October', () => {
    // 25 October 2026 is 25 hours long in the UK.
    expect(describeDaysFromToday('2026-10-30', new Date(2026, 9, 20))?.days).toBe(10);
  });

  it('returns null for a missing or malformed date', () => {
    expect(describeDaysFromToday(null, today)).toBeNull();
    expect(describeDaysFromToday('06/10/2026', today)).toBeNull();
  });
});

describe('parseIsoDate', () => {
  // `new Date('2026-07-25')` is midnight UTC, which renders as the 24th in any
  // timezone west of Greenwich. A purchase date must never shift by a day.
  it('builds the date in local time so the calendar day is preserved', () => {
    const date = parseIsoDate('2026-07-25');
    expect(date?.getFullYear()).toBe(2026);
    expect(date?.getMonth()).toBe(6);
    expect(date?.getDate()).toBe(25);
  });

  it('rejects anything that is not a plain calendar date', () => {
    expect(parseIsoDate('2026-07-25T10:00:00Z')).toBeNull();
    expect(parseIsoDate('not a date')).toBeNull();
  });
});

describe('formatDate', () => {
  it('renders a UK-style date', () => {
    expect(formatDate('2026-07-25')).toBe('25 Jul 2026');
  });

  it('falls back for missing values', () => {
    expect(formatDate(null)).toBe('—');
    expect(formatDate(undefined)).toBe('—');
    expect(formatDate('', 'Not set')).toBe('Not set');
  });
});

describe('formatMoney', () => {
  it('defaults to GBP for the single-school deployment', () => {
    expect(formatMoney(899, null)).toContain('899.00');
    expect(formatMoney(899, null)).toContain('£');
  });

  it('distinguishes zero from absent', () => {
    expect(formatMoney(0, 'GBP')).toContain('0.00');
    expect(formatMoney(null, 'GBP')).toBe('—');
  });

  it('still shows the number when the currency code is unusable', () => {
    expect(formatMoney(12.5, 'NOTACODE')).toBe('12.50 NOTACODE');
  });
});

describe('formatDaysRemaining', () => {
  it('describes future, present and past coverage', () => {
    expect(formatDaysRemaining(14)).toBe('expires in 14 days');
    expect(formatDaysRemaining(1)).toBe('expires in 1 day');
    expect(formatDaysRemaining(0)).toBe('expires today');
    expect(formatDaysRemaining(-3)).toBe('expired 3 days ago');
    expect(formatDaysRemaining(null)).toBeNull();
  });
});

describe('formatEventType', () => {
  it('splits PascalCase domain event names into words', () => {
    expect(formatEventType('AssetAssigned')).toBe('Asset assigned');
    expect(formatEventType('QrIdentifierRotated')).toBe('Qr identifier rotated');
  });
});

describe('initials', () => {
  it('takes the first and last initial', () => {
    expect(initials('Amara Patel')).toBe('AP');
    expect(initials('A. Patel')).toBe('AP');
  });

  it('handles a single name and empty input', () => {
    expect(initials('Sam')).toBe('SA');
    expect(initials('   ')).toBe('?');
  });
});
