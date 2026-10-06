import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  api,
  buildQuery,
  invalidateCsrfToken,
  setUnauthenticatedHandler,
} from './client';
import { ApiError, NetworkError } from './problem';

const CSRF_URL = '/api/v1/auth/csrf';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function problemResponse(problem: Record<string, unknown>, status: number): Response {
  return new Response(JSON.stringify(problem), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

function antiforgeryProblem(): Response {
  return problemResponse(
    {
      type: 'https://inventory.local/problems/antiforgery-validation-failed',
      title: 'The antiforgery token was not accepted.',
      status: 400,
    },
    400,
  );
}

describe('api client', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invalidateCsrfToken();
    setUnauthenticatedHandler(null);
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('safe requests', () => {
    it('does not fetch a CSRF token for GET', async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));

      await api.get('/assets');

      expect(fetchMock).toHaveBeenCalledTimes(1);
      const [url, init] = fetchMock.mock.calls[0]!;
      expect(url).toBe('/api/v1/assets');
      expect((init as RequestInit).method).toBe('GET');
      expect((init as RequestInit).headers).not.toHaveProperty('X-CSRF-TOKEN');
    });

    it('sends cookies same-origin', async () => {
      fetchMock.mockResolvedValueOnce(jsonResponse({}));

      await api.get('/auth/me');

      const [, init] = fetchMock.mock.calls[0]!;
      expect((init as RequestInit).credentials).toBe('same-origin');
    });
  });

  describe('CSRF handling', () => {
    it('fetches a token once and attaches it to unsafe requests', async () => {
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ token: 'token-1' }))
        .mockResolvedValueOnce(jsonResponse({ id: 'a' }, 201))
        .mockResolvedValueOnce(jsonResponse({ id: 'b' }, 201));

      await api.post('/assets', { body: { assetTag: 'A' } });
      await api.post('/assets', { body: { assetTag: 'B' } });

      expect(fetchMock.mock.calls[0]![0]).toBe(CSRF_URL);
      // The second POST reuses the cached token rather than re-fetching.
      expect(fetchMock).toHaveBeenCalledTimes(3);

      for (const index of [1, 2]) {
        const init = fetchMock.mock.calls[index]![1] as RequestInit;
        expect((init.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('token-1');
      }
    });

    it('fetches the token only once when two mutations start together', async () => {
      let releaseCsrf: (value: Response) => void = () => {};
      const pendingCsrf = new Promise<Response>((resolve) => {
        releaseCsrf = resolve;
      });

      fetchMock.mockImplementation((url: string) =>
        url === CSRF_URL ? pendingCsrf : Promise.resolve(jsonResponse({}, 201)),
      );

      const both = Promise.all([
        api.post('/assets', { body: {} }),
        api.post('/people', { body: {} }),
      ]);
      releaseCsrf(jsonResponse({ token: 'shared' }));
      await both;

      const csrfCalls = fetchMock.mock.calls.filter(([url]) => url === CSRF_URL);
      expect(csrfCalls).toHaveLength(1);
    });

    it('refreshes the token and replays once when the server rejects it', async () => {
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ token: 'stale' }))
        .mockResolvedValueOnce(antiforgeryProblem())
        .mockResolvedValueOnce(jsonResponse({ token: 'fresh' }))
        .mockResolvedValueOnce(jsonResponse({ ok: true }));

      await expect(api.post('/assets', { body: {} })).resolves.toEqual({ ok: true });

      const tokens = fetchMock.mock.calls
        .filter(([url]) => url !== CSRF_URL)
        .map(([, init]) => (init as RequestInit).headers as Record<string, string>)
        .map((headers) => headers['X-CSRF-TOKEN']);
      expect(tokens).toEqual(['stale', 'fresh']);
    });

    it('gives up after a single replay rather than looping', async () => {
      fetchMock.mockImplementation((url: string) =>
        Promise.resolve(
          url === CSRF_URL ? jsonResponse({ token: 'never-good' }) : antiforgeryProblem(),
        ),
      );

      await expect(api.post('/assets', { body: {} })).rejects.toThrow(ApiError);

      const mutationCalls = fetchMock.mock.calls.filter(([url]) => url !== CSRF_URL);
      expect(mutationCalls).toHaveLength(2);
    });
  });

  describe('error mapping', () => {
    it('raises a concurrency conflict for a stale version', async () => {
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ token: 't' }))
        .mockResolvedValueOnce(
          problemResponse(
            {
              type: 'https://inventory.local/problems/concurrency-conflict',
              title: 'The record was changed by another user.',
              detail: 'The asset was changed by another user.',
              status: 409,
              traceId: 'trace-123',
            },
            409,
          ),
        );

      const error = await api
        .put('/assets/1', { body: {} })
        .catch((caught: unknown) => caught);

      expect(error).toBeInstanceOf(ApiError);
      const apiError = error as ApiError;
      expect(apiError.isConcurrencyConflict).toBe(true);
      expect(apiError.isDuplicate).toBe(false);
      expect(apiError.traceId).toBe('trace-123');
    });

    it('treats a non-concurrency 409 as a duplicate', async () => {
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ token: 't' }))
        .mockResolvedValueOnce(
          problemResponse(
            {
              type: 'https://inventory.local/problems/duplicate-asset-tag',
              detail: 'Asset tag SCH-1 is already in use.',
              status: 409,
            },
            409,
          ),
        );

      const error = (await api
        .post('/assets', { body: {} })
        .catch((caught: unknown) => caught)) as ApiError;

      expect(error.isDuplicate).toBe(true);
      expect(error.isConcurrencyConflict).toBe(false);
      expect(error.slug).toBe('duplicate-asset-tag');
    });

    it('flags an outstanding second factor', async () => {
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ token: 't' }))
        .mockResolvedValueOnce(
          problemResponse(
            {
              type: 'https://inventory.local/problems/authentication-failed',
              status: 401,
              code: 'mfa_required',
            },
            401,
          ),
        );

      const error = (await api
        .post('/auth/login', { body: {}, skipUnauthenticatedHandler: true })
        .catch((caught: unknown) => caught)) as ApiError;

      expect(error.isMfaRequired).toBe(true);
    });

    it('wraps a transport failure rather than leaking it', async () => {
      fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));

      await expect(api.get('/assets')).rejects.toBeInstanceOf(NetworkError);
    });

    it('returns undefined for a 204', async () => {
      fetchMock
        .mockResolvedValueOnce(jsonResponse({ token: 't' }))
        .mockResolvedValueOnce(new Response(null, { status: 204 }));

      await expect(api.post('/auth/logout')).resolves.toBeUndefined();
    });
  });

  describe('unauthenticated handling', () => {
    it('notifies the session layer on a 401', async () => {
      const onUnauthenticated = vi.fn();
      setUnauthenticatedHandler(onUnauthenticated);
      fetchMock.mockResolvedValueOnce(problemResponse({ status: 401 }, 401));

      await expect(api.get('/assets')).rejects.toBeInstanceOf(ApiError);

      expect(onUnauthenticated).toHaveBeenCalledTimes(1);
    });

    it('stays quiet when the caller opts out', async () => {
      const onUnauthenticated = vi.fn();
      setUnauthenticatedHandler(onUnauthenticated);
      fetchMock.mockResolvedValueOnce(problemResponse({ status: 401 }, 401));

      await expect(
        api.get('/auth/me', { skipUnauthenticatedHandler: true }),
      ).rejects.toBeInstanceOf(ApiError);

      expect(onUnauthenticated).not.toHaveBeenCalled();
    });
  });
});

describe('buildQuery', () => {
  it('omits absent and blank values so they are not read as filters', () => {
    expect(
      buildQuery({ q: '', status: undefined, categoryId: null, page: 1 }),
    ).toBe('?page=1');
  });

  it('keeps false, which is a meaningful filter value', () => {
    expect(buildQuery({ assigned: false })).toBe('?assigned=false');
  });

  it('returns an empty string when there is nothing to send', () => {
    expect(buildQuery(undefined)).toBe('');
    expect(buildQuery({})).toBe('');
  });
});
