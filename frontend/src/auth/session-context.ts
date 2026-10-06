import { createContext, useContext } from 'react';

import type { CurrentUser } from '@/api/types';
import type { PermissionKey } from './permissions';

export type SessionStatus = 'loading' | 'anonymous' | 'authenticated';

export interface SessionValue {
  status: SessionStatus;
  user: CurrentUser | null;
  /** True when the API told us the user must set a new password before working. */
  mustChangePassword: boolean;
  /**
   * True when the account's role requires two-step verification and it has not
   * enrolled. The API refuses every permission check while this holds, so it is
   * a lockout rather than a reminder.
   */
  mfaEnrollmentRequired: boolean;
  /**
   * Visibility check only. The API enforces the same permission server-side.
   *
   * Returns false for everything while the account is locked out by
   * `mustChangePassword` or `mfaEnrollmentRequired`, matching what the server
   * will actually allow — the permission list stays populated in those states,
   * so trusting it alone would show a full app that answers `403` to every
   * action.
   */
  can: (permission: PermissionKey) => boolean;
  canAny: (...permissions: PermissionKey[]) => boolean;
  canAll: (...permissions: PermissionKey[]) => boolean;
  hasRole: (role: string) => boolean;
  /** Re-reads `/auth/me`, e.g. after a role change or password change. */
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}

export const SessionContext = createContext<SessionValue | null>(null);

export function useSession(): SessionValue {
  const value = useContext(SessionContext);
  if (!value) {
    throw new Error('useSession must be used inside <SessionProvider>.');
  }
  return value;
}

/** Convenience for the common `can(...)` call in render. */
export function useCan(permission: PermissionKey): boolean {
  return useSession().can(permission);
}
