/**
 * RFC 9457 Problem Details, as produced by `InventoryExceptionHandler` and by
 * ASP.NET's built-in `[ApiController]` model validation.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  /** Field-keyed validation messages. Present on 400s only. */
  errors?: Record<string, string[]>;
  /** Machine-readable discriminator the auth endpoints add. */
  code?: string;
  [key: string]: unknown;
}

const PROBLEM_PREFIX = 'https://inventory.local/problems/';

/** Problem `type` slugs the UI branches on. Others are handled generically. */
export const ProblemType = {
  NotFound: `${PROBLEM_PREFIX}not-found`,
  ConcurrencyConflict: `${PROBLEM_PREFIX}concurrency-conflict`,
  AntiforgeryValidationFailed: `${PROBLEM_PREFIX}antiforgery-validation-failed`,
  AuthenticationFailed: `${PROBLEM_PREFIX}authentication-failed`,
  InternalError: `${PROBLEM_PREFIX}internal-error`,
} as const;

/**
 * Every non-2xx response from the API surfaces as one of these. Callers should
 * branch on the `is*` helpers rather than raw status codes, because the same
 * status can mean different things (a 400 is validation *or* a stale CSRF token,
 * a 409 is a stale version *or* a duplicate asset tag).
 */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;
  readonly url: string;

  constructor(status: number, problem: ProblemDetails, url: string) {
    super(problem.detail || problem.title || `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.url = url;
  }

  get type(): string | undefined {
    return this.problem.type;
  }

  get traceId(): string | undefined {
    return this.problem.traceId;
  }

  /** Short slug, e.g. `duplicate-asset-tag`, or undefined for non-inventory problems. */
  get slug(): string | undefined {
    return this.problem.type?.startsWith(PROBLEM_PREFIX)
      ? this.problem.type.slice(PROBLEM_PREFIX.length)
      : undefined;
  }

  get isUnauthenticated(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  get isRateLimited(): boolean {
    return this.status === 429;
  }

  /**
   * Someone else saved this record first. The UI must reload and let the user
   * re-apply their edit; it must never retry with the same version.
   */
  get isConcurrencyConflict(): boolean {
    return this.status === 409 && this.problem.type === ProblemType.ConcurrencyConflict;
  }

  /** A 409 that is not a stale version, e.g. a duplicate asset tag or serial. */
  get isDuplicate(): boolean {
    return this.status === 409 && !this.isConcurrencyConflict;
  }

  get isAntiforgeryFailure(): boolean {
    return (
      this.status === 400 && this.problem.type === ProblemType.AntiforgeryValidationFailed
    );
  }

  /** The password was correct but a second factor is outstanding. */
  get isMfaRequired(): boolean {
    return this.status === 401 && this.problem.code === 'mfa_required';
  }

  get isValidation(): boolean {
    return this.status === 400 && this.fieldErrorCount > 0;
  }

  get fieldErrorCount(): number {
    return Object.keys(this.problem.errors ?? {}).length;
  }

  /**
   * Validation messages for one field.
   *
   * Keys arrive in several shapes depending on where validation failed:
   * DataAnnotations reports `AssetTag`, System.Text.Json reports `$.assetTag`,
   * and the application layer reports `assetTag`. Normalise all three.
   */
  fieldErrors(field: string): string[] {
    const errors = this.problem.errors;
    if (!errors) return [];

    const wanted = normaliseFieldKey(field);
    for (const [key, messages] of Object.entries(errors)) {
      if (normaliseFieldKey(key) === wanted) return messages;
    }
    return [];
  }

  /** All validation messages, flattened, for a form-level summary. */
  allFieldErrors(): string[] {
    return Object.values(this.problem.errors ?? {}).flat();
  }
}

function normaliseFieldKey(key: string): string {
  return key.replace(/^\$\./, '').replace(/[[\]().\s_-]/g, '').toLowerCase();
}

/** Thrown when the network failed outright — no HTTP response was received. */
export class NetworkError extends Error {
  override readonly cause: unknown;

  constructor(url: string, cause: unknown) {
    super(`Could not reach the inventory API (${url}).`);
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/**
 * A message safe and useful to show a user. Prefers the server's `detail`, which
 * the backend writes for humans, and never leaks a stack trace or trace id into
 * the sentence itself.
 */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isConcurrencyConflict) {
      return 'Someone else changed this record while you were editing. Reload to see their changes, then reapply yours.';
    }
    if (error.isForbidden) {
      return 'You do not have permission to do that.';
    }
    if (error.isUnauthenticated) {
      return 'Your session has ended. Sign in again to continue.';
    }
    if (error.isRateLimited) {
      return 'Too many attempts. Wait a minute and try again.';
    }
    if (error.status >= 500) {
      return 'The server could not complete the request. Try again shortly.';
    }
    if (error.isValidation) {
      return error.allFieldErrors()[0] ?? error.message;
    }
    return error.message;
  }
  if (error instanceof NetworkError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return 'Something went wrong.';
}
