import { ApiError, describeError } from '@/api/problem';

/**
 * The account endpoints reject with a small, well-known set of problem slugs.
 * Each one means something specific that the user can act on, and the server's
 * own `detail` is written for developers ("The final enabled administrator cannot
 * be removed or disabled.") rather than for the person clicking the button.
 *
 * Slugs come from `IdentityService`; see `docs/api/permissions.md`.
 */
export function describeAccountError(error: unknown): { title: string; detail: string } {
  if (!(error instanceof ApiError)) {
    return { title: 'Could not save', detail: describeError(error) };
  }

  switch (error.slug) {
    case 'concurrency-conflict':
      return {
        title: 'Someone else changed this account',
        detail:
          'It was updated while this was open, and nothing has been changed. Close this and reopen it to work from the current values.',
      };
    case 'duplicate-user-email':
      return {
        title: 'That email address is already in use',
        detail:
          'Every account needs its own email address. Check whether the person already has an account, including an archived one.',
      };
    case 'last-administrator':
      return {
        title: 'This is the last administrator',
        detail:
          'At least one enabled administrator must remain, or nobody could administer the system. Give another account the Administrator role first.',
      };
    case 'cannot-archive-self':
      return {
        title: 'You cannot archive your own account',
        detail: 'Ask another administrator to do it.',
      };
    case 'cannot-disable-self':
      return {
        title: 'You cannot disable your own account',
        detail: 'Ask another administrator to do it.',
      };
    case 'user-archived':
      return {
        title: 'This account is archived',
        detail: 'Restore it before making any other change.',
      };
    case 'invalid-role':
      return {
        title: 'Unknown role',
        detail: error.message,
      };
    case 'invalid-user-profile':
      return {
        title: 'Missing details',
        detail: 'An email address and a display name are both required.',
      };
    case 'identity-operation-failed':
      // Carries ASP.NET Identity's own messages — password policy failures land
      // here, and those are worth showing verbatim.
      return {
        title: 'The account could not be saved',
        detail: error.allFieldErrors().join(' ') || error.message,
      };
    default:
      return { title: 'Could not save', detail: describeError(error) };
  }
}
