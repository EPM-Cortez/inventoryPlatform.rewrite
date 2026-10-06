import { describe, expect, it } from 'vitest';

import { ApiError, describeError, NetworkError } from './problem';

function validationError(errors: Record<string, string[]>): ApiError {
  return new ApiError(400, { status: 400, errors }, '/api/v1/assets');
}

describe('ApiError.fieldErrors', () => {
  // Validation keys arrive in three different shapes depending on which layer
  // rejected the request, and a form binds to exactly one field name.
  it('matches DataAnnotations PascalCase keys', () => {
    const error = validationError({ AssetTag: ['The AssetTag field is required.'] });
    expect(error.fieldErrors('assetTag')).toEqual(['The AssetTag field is required.']);
  });

  it('matches System.Text.Json $. paths', () => {
    const error = validationError({ '$.serialNumber': ['Invalid value.'] });
    expect(error.fieldErrors('serialNumber')).toEqual(['Invalid value.']);
  });

  it('matches application-layer camelCase keys', () => {
    const error = validationError({ purchaseCost: ['Too many decimal places.'] });
    expect(error.fieldErrors('purchaseCost')).toEqual(['Too many decimal places.']);
  });

  it('returns nothing for a field with no errors', () => {
    expect(validationError({ assetTag: ['x'] }).fieldErrors('model')).toEqual([]);
  });

  it('flattens every message for a form-level summary', () => {
    const error = validationError({ a: ['one', 'two'], b: ['three'] });
    expect(error.allFieldErrors()).toEqual(['one', 'two', 'three']);
  });
});

describe('ApiError classification', () => {
  it('separates a stale version from a duplicate', () => {
    const stale = new ApiError(
      409,
      { type: 'https://inventory.local/problems/concurrency-conflict' },
      '/x',
    );
    const duplicate = new ApiError(
      409,
      { type: 'https://inventory.local/problems/duplicate-serial-number' },
      '/x',
    );

    expect(stale.isConcurrencyConflict).toBe(true);
    expect(stale.isDuplicate).toBe(false);
    expect(duplicate.isConcurrencyConflict).toBe(false);
    expect(duplicate.isDuplicate).toBe(true);
  });

  it('only treats an antiforgery 400 as a token failure', () => {
    const antiforgery = new ApiError(
      400,
      { type: 'https://inventory.local/problems/antiforgery-validation-failed' },
      '/x',
    );
    const validation = validationError({ assetTag: ['required'] });

    expect(antiforgery.isAntiforgeryFailure).toBe(true);
    expect(validation.isAntiforgeryFailure).toBe(false);
  });

  it('exposes no slug for a problem type outside the inventory namespace', () => {
    const error = new ApiError(400, { type: 'https://example.com/other' }, '/x');
    expect(error.slug).toBeUndefined();
  });
});

describe('describeError', () => {
  it('explains a concurrency conflict in terms of what to do next', () => {
    const error = new ApiError(
      409,
      { type: 'https://inventory.local/problems/concurrency-conflict' },
      '/x',
    );
    expect(describeError(error)).toContain('Reload');
  });

  it('does not surface a server detail for a 500', () => {
    const error = new ApiError(
      500,
      { detail: 'NullReferenceException at Inventory.Api...' },
      '/x',
    );
    expect(describeError(error)).not.toContain('NullReference');
  });

  it('prefers the first validation message', () => {
    expect(describeError(validationError({ assetTag: ['Asset tag is required.'] }))).toBe(
      'Asset tag is required.',
    );
  });

  it('handles a transport failure', () => {
    expect(describeError(new NetworkError('/api/v1/assets', new Error('boom')))).toContain(
      'Could not reach',
    );
  });

  it('falls back for a non-Error value', () => {
    expect(describeError('something odd')).toBe('Something went wrong.');
  });
});
