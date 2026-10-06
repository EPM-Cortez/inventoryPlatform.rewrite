import { describe, expect, it } from 'vitest';

import { ApiError } from '@/api/problem';

import { describeAccountError } from './accountErrors';

function conflict(slug: string): ApiError {
  return new ApiError(
    409,
    { type: `https://inventory.local/problems/${slug}`, status: 409 },
    '/api/v1/users/1',
  );
}

describe('describeAccountError', () => {
  // Each of these is a distinct situation with a distinct fix, and the server's
  // own wording does not tell the administrator what to do next.
  it('explains the last-administrator guard with the way out', () => {
    const message = describeAccountError(conflict('last-administrator'));
    expect(message.title).toContain('last administrator');
    expect(message.detail).toContain('Administrator role');
  });

  it('distinguishes self-archive from self-disable', () => {
    expect(describeAccountError(conflict('cannot-archive-self')).title).toContain(
      'archive your own',
    );
    expect(describeAccountError(conflict('cannot-disable-self')).title).toContain(
      'disable your own',
    );
  });

  it('tells the user an archived account must be restored first', () => {
    expect(describeAccountError(conflict('user-archived')).detail).toContain('Restore');
  });

  it('mentions archived accounts when an email collides', () => {
    expect(describeAccountError(conflict('duplicate-user-email')).detail).toContain(
      'archived',
    );
  });

  it('says nothing was changed on a concurrency conflict', () => {
    const message = describeAccountError(conflict('concurrency-conflict'));
    expect(message.detail).toContain('nothing has been changed');
  });

  it('surfaces Identity password-policy messages verbatim', () => {
    const error = new ApiError(
      400,
      {
        type: 'https://inventory.local/problems/identity-operation-failed',
        status: 400,
        errors: { PasswordTooShort: ['Passwords must be at least 12 characters.'] },
      },
      '/api/v1/users',
    );
    expect(describeAccountError(error).detail).toContain('at least 12 characters');
  });

  it('falls back for an unrecognised failure', () => {
    expect(describeAccountError(new Error('boom')).title).toBe('Could not save');
  });
});
