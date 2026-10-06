import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { invalidateCsrfToken, setUnauthenticatedHandler } from './client';
import { usersApi } from './endpoints';

const VERSION = '11111111-1111-1111-1111-111111111111';
const USER_ID = '22222222-2222-2222-2222-222222222222';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('usersApi version threading', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invalidateCsrfToken();
    setUnauthenticatedHandler(null);
    fetchMock = vi.fn().mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('/auth/csrf')
          ? jsonResponse({ token: 'token' })
          : new Response(null, { status: 204 }),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function lastCall() {
    const calls = fetchMock.mock.calls.filter(
      ([url]) => !String(url).includes('/auth/csrf'),
    );
    const [url, init] = calls[calls.length - 1]!;
    return { url: String(url), init: init as RequestInit };
  }

  // Archive is the odd one out: DELETE carries no body in this API, so the
  // version has to travel in the query string. Sending it as a body would look
  // right and fail with a validation error at runtime.
  it('sends the version as a query parameter when archiving', async () => {
    await usersApi.archive(USER_ID, VERSION);

    const { url, init } = lastCall();
    expect(init.method).toBe('DELETE');
    expect(url).toBe(`/api/v1/users/${USER_ID}?version=${VERSION}`);
    expect(init.body).toBeUndefined();
  });

  it('sends the version in the body when disabling', async () => {
    await usersApi.disable(USER_ID, VERSION);

    const { url, init } = lastCall();
    expect(init.method).toBe('POST');
    expect(url).toBe(`/api/v1/users/${USER_ID}/disable`);
    expect(JSON.parse(String(init.body))).toEqual({ version: VERSION });
  });

  it('sends the version in the body when restoring', async () => {
    await usersApi.restore(USER_ID, VERSION);

    const { init } = lastCall();
    expect(JSON.parse(String(init.body))).toEqual({ version: VERSION });
  });

  it('includes the version alongside the new password on reset', async () => {
    await usersApi.resetPassword(USER_ID, {
      newTemporaryPassword: 'a-temporary-value',
      version: VERSION,
    });

    const { init } = lastCall();
    expect(JSON.parse(String(init.body))).toEqual({
      newTemporaryPassword: 'a-temporary-value',
      version: VERSION,
    });
  });

  it('carries the CSRF token on every account mutation', async () => {
    await usersApi.archive(USER_ID, VERSION);

    const { init } = lastCall();
    expect((init.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('token');
  });
});
