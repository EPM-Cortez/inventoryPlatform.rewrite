import { ApiError, NetworkError, type ProblemDetails } from './problem';

/**
 * The API is always same-origin. Its session and antiforgery cookies are
 * `__Host-` prefixed with `SameSite=Strict`, so a cross-origin deployment cannot
 * authenticate at all — the browser would refuse to send them. In development the
 * Vite proxy provides the shared origin; in production the built assets are served
 * from the same host as `/api`.
 */
export const API_BASE = '/api/v1';

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue>;

export interface RequestOptions {
  query?: QueryParams;
  body?: unknown;
  signal?: AbortSignal;
  headers?: Record<string, string>;
  /** Overrides `Accept`. Used by the QR renderer to ask for SVG or PNG. */
  accept?: string;
  /**
   * Suppresses the global 401 handler. Set it on the endpoints that *expect* a
   * 401 as a normal answer — login, MFA, and the session probe — so an
   * unauthenticated visitor does not bounce through a redirect loop.
   */
  skipUnauthenticatedHandler?: boolean;
}

// ---------------------------------------------------------------------------
// Antiforgery token
//
// `GET /auth/csrf` sets the antiforgery cookie and returns the paired request
// token. The pair is bound to the current identity, so the token must be
// discarded whenever the identity changes — that is, after login and logout.
// ---------------------------------------------------------------------------

let csrfToken: string | null = null;
let csrfInFlight: Promise<string> | null = null;

/** Drop the cached token. Call after any operation that changes who is signed in. */
export function invalidateCsrfToken(): void {
  csrfToken = null;
  csrfInFlight = null;
}

async function getCsrfToken(signal?: AbortSignal): Promise<string> {
  if (csrfToken) return csrfToken;

  // Concurrent mutations on page load must not each trigger their own fetch;
  // the second one would rotate the cookie out from under the first.
  csrfInFlight ??= (async () => {
    const response = await fetch(`${API_BASE}/auth/csrf`, {
      method: 'GET',
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
      signal,
    });
    if (!response.ok) {
      throw new ApiError(
        response.status,
        await readProblem(response),
        `${API_BASE}/auth/csrf`,
      );
    }
    const payload = (await response.json()) as { token: string };
    csrfToken = payload.token;
    return payload.token;
  })().finally(() => {
    csrfInFlight = null;
  });

  return csrfInFlight;
}

// ---------------------------------------------------------------------------
// Global 401 handling
// ---------------------------------------------------------------------------

type UnauthenticatedListener = () => void;
let onUnauthenticated: UnauthenticatedListener | null = null;

/**
 * Registered once by the session provider so an expired or revoked cookie routes
 * the user to sign-in from anywhere, without every call site checking for 401.
 * Login endpoints opt out via `skipUnauthenticatedHandler`.
 */
export function setUnauthenticatedHandler(listener: UnauthenticatedListener | null): void {
  onUnauthenticated = listener;
}

// ---------------------------------------------------------------------------
// Core request
// ---------------------------------------------------------------------------

export function buildQuery(params: QueryParams | undefined): string {
  if (!params) return '';
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    // Omit rather than send empty: the API treats a present-but-blank filter as
    // a real filter value and would return nothing.
    if (value === undefined || value === null || value === '') continue;
    search.append(key, String(value));
  }
  const query = search.toString();
  return query ? `?${query}` : '';
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    const text = await response.text();
    if (!text) return { status: response.status, title: response.statusText };
    const parsed: unknown = JSON.parse(text);
    if (parsed && typeof parsed === 'object') {
      return parsed as ProblemDetails;
    }
    return { status: response.status, title: response.statusText, detail: text };
  } catch {
    return { status: response.status, title: response.statusText };
  }
}

async function send(
  method: HttpMethod,
  path: string,
  options: RequestOptions,
  isCsrfRetry = false,
): Promise<Response> {
  const url = `${API_BASE}${path}${buildQuery(options.query)}`;

  const headers: Record<string, string> = {
    Accept: options.accept ?? 'application/json',
    ...options.headers,
  };

  let body: string | FormData | undefined;
  if (options.body instanceof FormData) {
    // Must NOT set Content-Type: the browser has to add the multipart boundary,
    // and overriding it makes the server unable to parse the upload.
    body = options.body;
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }

  if (UNSAFE_METHODS.has(method)) {
    headers['X-CSRF-TOKEN'] = await getCsrfToken(options.signal);
  }

  let response: Response;
  try {
    response = await fetch(url, {
      method,
      credentials: 'same-origin',
      headers,
      body,
      signal: options.signal,
    });
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause;
    throw new NetworkError(url, cause);
  }

  if (response.ok) return response;

  const problem = await readProblem(response);
  const error = new ApiError(response.status, problem, url);

  // A rejected token usually means the cookie rotated (another tab signed in, or
  // the session was renewed). Fetch a fresh pair and replay exactly once — a
  // second failure is a real problem, not a stale token.
  if (error.isAntiforgeryFailure && !isCsrfRetry) {
    invalidateCsrfToken();
    return send(method, path, options, true);
  }

  if (error.isUnauthenticated && !options.skipUnauthenticatedHandler) {
    invalidateCsrfToken();
    onUnauthenticated?.();
  }

  throw error;
}

async function parse<T>(response: Response): Promise<T> {
  // 204 is the documented success shape for login, logout and change-password.
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T;
  }
  const text = await response.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
}

export const api = {
  async get<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return parse<T>(await send('GET', path, options));
  },

  async post<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return parse<T>(await send('POST', path, options));
  },

  async put<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return parse<T>(await send('PUT', path, options));
  },

  async delete<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return parse<T>(await send('DELETE', path, options));
  },

  /**
   * Escape hatch for non-JSON payloads — QR images and generated label PDFs.
   * Returns the whole `Response` so callers can read binary content and the
   * custom headers that accompany it.
   */
  async raw(
    path: string,
    options: RequestOptions = {},
    method: HttpMethod = 'GET',
  ): Promise<Response> {
    return send(method, path, options);
  },
};

/** Exposed for tests only. */
export const __testing = {
  getCsrfToken,
  peekCsrfToken: () => csrfToken,
};
