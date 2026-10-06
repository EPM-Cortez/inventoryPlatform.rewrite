import { Lock } from 'lucide-react';
import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';

import type { PermissionKey } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { EmptyState } from '@/ui/EmptyState';
import { Spinner } from '@/ui/Spinner';

/** Full-viewport splash while the session probe is in flight. */
export function SessionSplash() {
  return (
    <div className="flex h-dvh items-center justify-center bg-canvas">
      <div className="flex flex-col items-center gap-3 text-text-subtle">
        <Spinner className="size-6" />
        <p className="text-sm">Checking your session…</p>
      </div>
    </div>
  );
}

/**
 * Gates the authenticated app.
 *
 * Also enforces the API's two lockout flags. `PermissionAuthorization` checks
 * `mustChangePassword` first and `mfaEnrollmentRequired` second, and in either
 * state it returns without granting *any* permission — so an account in one of
 * them can sign in and then do nothing at all. The redirects below mirror that
 * order and give the user the one screen that can get them out. The backend
 * rejects their other requests regardless; this is about a clear path, not
 * security.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status, mustChangePassword, mfaEnrollmentRequired } = useSession();
  const location = useLocation();

  if (status === 'loading') return <SessionSplash />;

  if (status === 'anonymous') {
    // Remember where they were headed so sign-in can return them there.
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  if (mustChangePassword && location.pathname !== '/change-password') {
    return <Navigate to="/change-password" replace />;
  }

  if (mfaEnrollmentRequired && location.pathname !== '/security') {
    return <Navigate to="/security" replace />;
  }

  return <>{children}</>;
}

/** Redirects an already-signed-in user away from the sign-in screens. */
export function RequireAnonymous({ children }: { children: ReactNode }) {
  const { status, mustChangePassword, mfaEnrollmentRequired } = useSession();

  if (status === 'loading') return <SessionSplash />;
  if (status === 'authenticated') {
    const destination = mustChangePassword
      ? '/change-password'
      : mfaEnrollmentRequired
        ? '/security'
        : '/';
    return <Navigate to={destination} replace />;
  }
  return <>{children}</>;
}

/**
 * Hides a route from users without the permission.
 *
 * This is presentation only. The endpoint behind the route enforces the same
 * permission, and that check is the one that matters.
 */
export function RequirePermission({
  permission,
  children,
}: {
  permission: PermissionKey;
  children: ReactNode;
}) {
  const { can } = useSession();

  if (!can(permission)) {
    return (
      <EmptyState
        icon={<Lock />}
        title="You do not have access to this"
        description={
          <>
            This page needs the <code className="font-mono text-xs">{permission}</code>{' '}
            permission, which your role does not include. Ask an administrator if you
            need it.
          </>
        }
      />
    );
  }

  return <>{children}</>;
}
