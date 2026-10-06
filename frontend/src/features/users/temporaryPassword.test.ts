import { describe, expect, it } from 'vitest';

import { generateTemporaryPassword, MIN_PASSWORD_LENGTH } from './temporaryPassword';

/**
 * These assertions are the Identity policy from
 * `Inventory.Infrastructure/DependencyInjection.cs`. A generator that misses any
 * one of them makes account creation and password reset fail outright with
 * `identity-operation-failed` — which is exactly what happened before this test
 * existed.
 *
 * Run over many samples: the original bug was that a whole character class was
 * absent from the alphabet, but a subtler one would only show up occasionally.
 */
const SAMPLES = 500;

function samples(): string[] {
  return Array.from({ length: SAMPLES }, () => generateTemporaryPassword());
}

describe('generateTemporaryPassword', () => {
  it('is at least the required length', () => {
    for (const password of samples()) {
      expect(password.length).toBeGreaterThanOrEqual(MIN_PASSWORD_LENGTH);
    }
  });

  it('always contains a digit', () => {
    for (const password of samples()) {
      expect(password, password).toMatch(/[0-9]/);
    }
  });

  it('always contains a lowercase letter', () => {
    for (const password of samples()) {
      expect(password, password).toMatch(/[a-z]/);
    }
  });

  it('always contains an uppercase letter', () => {
    for (const password of samples()) {
      expect(password, password).toMatch(/[A-Z]/);
    }
  });

  it('always contains a non-alphanumeric character', () => {
    for (const password of samples()) {
      expect(password, password).toMatch(/[^a-zA-Z0-9]/);
    }
  });

  it('has at least the four unique characters the policy requires', () => {
    for (const password of samples()) {
      expect(new Set(password).size, password).toBeGreaterThanOrEqual(4);
    }
  });

  it('avoids characters that are misread when transcribed', () => {
    for (const password of samples()) {
      expect(password, password).not.toMatch(/[O0Il1]/);
    }
  });

  it('does not include quoting characters that break shells and spreadsheets', () => {
    for (const password of samples()) {
      expect(password, password).not.toMatch(/['"`\\]/);
    }
  });

  it('does not repeat itself', () => {
    const generated = samples();
    expect(new Set(generated).size).toBe(generated.length);
  });

  it('does not put the required classes in a predictable order', () => {
    // Without the shuffle, position 0 would always be uppercase.
    const firstCharacters = new Set(samples().map((password) => password[0]));
    expect(firstCharacters.size).toBeGreaterThan(5);
  });
});
