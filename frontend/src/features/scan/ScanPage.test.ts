import { describe, expect, it } from 'vitest';

import { extractToken } from './ScanPage';

describe('extractToken', () => {
  // Labels encode `{School:QrBaseUrl}/{token}`, and staff paste whatever their
  // camera app gave them — sometimes the full URL, sometimes just the token.
  it('takes the last path segment from a full label URL', () => {
    expect(extractToken('https://inventory.example.school/scan/abc123')).toBe('abc123');
  });

  it('ignores a trailing slash', () => {
    expect(extractToken('https://inventory.example.school/scan/abc123/')).toBe('abc123');
  });

  it('ignores a query string and fragment', () => {
    expect(extractToken('https://inventory.example.school/scan/abc123?x=1#y')).toBe(
      'abc123',
    );
  });

  it('accepts a bare token', () => {
    expect(extractToken('abc123')).toBe('abc123');
  });

  it('accepts a partial path paste', () => {
    expect(extractToken('/scan/abc123')).toBe('abc123');
  });

  it('trims surrounding whitespace', () => {
    expect(extractToken('  abc123  ')).toBe('abc123');
  });

  it('returns empty for nothing usable', () => {
    expect(extractToken('')).toBe('');
    expect(extractToken('   ')).toBe('');
    expect(extractToken('///')).toBe('');
  });
});
