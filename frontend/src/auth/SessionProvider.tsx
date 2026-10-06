import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, type ReactNode } from 'react';

import { setUnauthenticatedHandler } from '@/api/client';
import { authApi } from '@/api/endpoints';
import { ApiError } from '@/api/problem';
import type { CurrentUser } from '@/api/types';

import type { PermissionKey } from './permissions';
import { SessionContext, type SessionValue } from './session-context';

export const sessionQueryKey = ['session'] as const;

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();

  const { data, status, refetch } = useQuery<CurrentUser | null>({
    queryKey: sessionQueryKey,
    queryFn: async ({ signal }) => {
      try {
        return await authApi.me(signal);
      } catch (error) {
        // Signed out is a normal answer, not a failure. Anything else is real.
        if (error instanceof ApiError && error.isUnauthenticated) return null;
        throw error;
      }
    },
    staleTime: 5 * 60_000,
    retry: false,
    refetchOnWindowFocus: true,
  });

  // A 401 from any other endpoint means the cookie expired or the account was
  // disabled mid-session. Drop the cached identity and every cached record with
  // it: the next user to sign in must not see the previous user's data.
  useEffect(() => {
    setUnauthenticatedHandler(() => {
      queryClient.setQueryData(sessionQueryKey, null);
      void queryClient.invalidateQueries();
    });
    return () => setUnauthenticatedHandler(null);
  }, [queryClient]);

  const user = data ?? null;

  const permissions = useMemo(() => new Set(user?.permissions ?? []), [user]);
  const roles = useMemo(() => new Set(user?.roles ?? []), [user]);

  const refresh = useCallback(async () => {
    await refetch();
  }, [refetch]);

  const signOut = useCallback(async () => {
    try {
      await authApi.logout();
    } finally {
      queryClient.setQueryData(sessionQueryKey, null);
      queryClient.clear();
    }
  }, [queryClient]);

  const value = useMemo<SessionValue>(() => {
    // The server returns the full permission list even while it is refusing to
    // honour any of it, so both lockout flags have to be folded in here. Doing it
    // once, centrally, is what keeps every `can()` call site honest.
    const lockedOut =
      (user?.mustChangePassword ?? false) || (user?.mfaEnrollmentRequired ?? false);
    const can = (permission: PermissionKey) =>
      !lockedOut && permissions.has(permission);
    return {
      status: status === 'pending' ? 'loading' : user ? 'authenticated' : 'anonymous',
      user,
      mustChangePassword: user?.mustChangePassword ?? false,
      mfaEnrollmentRequired: user?.mfaEnrollmentRequired ?? false,
      can,
      canAny: (...list: PermissionKey[]) => list.some(can),
      canAll: (...list: PermissionKey[]) => list.every(can),
      hasRole: (role: string) => roles.has(role),
      refresh,
      signOut,
    };
  }, [permissions, refresh, roles, signOut, status, user]);

  return <SessionContext value={value}>{children}</SessionContext>;
}
