import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { describe, expect, it, vi } from 'vitest';

import { SessionContext, type SessionValue } from '@/auth/session-context';

import { RequireAnonymous, RequireAuth } from './guards';

function session(overrides: Partial<SessionValue> = {}): SessionValue {
  return {
    status: 'authenticated',
    user: null,
    mustChangePassword: false,
    mfaEnrollmentRequired: false,
    can: () => false,
    canAny: () => false,
    canAll: () => false,
    hasRole: () => false,
    refresh: vi.fn(),
    signOut: vi.fn(),
    ...overrides,
  };
}

function RedirectDestination() {
  const location = useLocation();
  return (
    <>
      <p>Redirected to {location.pathname}</p>
      <p>{JSON.stringify(location.state)}</p>
    </>
  );
}

// Guard the entry route and observe its destination separately. This catches
// an incorrect redirect without allowing a regression to loop indefinitely.
function renderGuard(path: string, value: SessionValue, anonymous = false) {
  const Guard = anonymous ? RequireAnonymous : RequireAuth;
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SessionContext value={value}>
        <Routes>
          <Route
            path={path.split('?')[0]}
            element={<Guard><p>Protected content</p></Guard>}
          />
          <Route path="*" element={<RedirectDestination />} />
        </Routes>
      </SessionContext>
    </MemoryRouter>,
  );
}

describe('RequireAuth', () => {
  it('allows the password form when both password change and MFA enrollment are required', () => {
    renderGuard('/change-password', session({
      mustChangePassword: true,
      mfaEnrollmentRequired: true,
    }));

    expect(screen.getByText('Protected content')).toBeInTheDocument();
    expect(screen.queryByText(/Redirected to/)).not.toBeInTheDocument();
  });

  it.each([
    { path: '/', mustChangePassword: true, mfaEnrollmentRequired: false, destination: '/change-password' },
    { path: '/', mustChangePassword: true, mfaEnrollmentRequired: true, destination: '/change-password' },
    { path: '/security', mustChangePassword: true, mfaEnrollmentRequired: true, destination: '/change-password' },
    { path: '/', mustChangePassword: false, mfaEnrollmentRequired: true, destination: '/security' },
    { path: '/change-password', mustChangePassword: false, mfaEnrollmentRequired: true, destination: '/security' },
  ])('routes $path to $destination with password=$mustChangePassword and MFA=$mfaEnrollmentRequired', async ({
    path, mustChangePassword, mfaEnrollmentRequired, destination,
  }) => {
    renderGuard(path, session({ mustChangePassword, mfaEnrollmentRequired }));

    expect(await screen.findByText(`Redirected to ${destination}`)).toBeInTheDocument();
    expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
  });

  it.each([
    { path: '/', mustChangePassword: false, mfaEnrollmentRequired: false },
    { path: '/change-password', mustChangePassword: true, mfaEnrollmentRequired: false },
    { path: '/security', mustChangePassword: false, mfaEnrollmentRequired: true },
  ])('allows $path with password=$mustChangePassword and MFA=$mfaEnrollmentRequired', ({
    path, mustChangePassword, mfaEnrollmentRequired,
  }) => {
    renderGuard(path, session({ mustChangePassword, mfaEnrollmentRequired }));

    expect(screen.getByText('Protected content')).toBeInTheDocument();
  });

  it('keeps protected content hidden while the session is loading', () => {
    renderGuard('/assets', session({ status: 'loading' }));

    expect(screen.getByText('Checking your session…')).toBeInTheDocument();
    expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
  });

  it('sends signed-out users to login and preserves their destination', async () => {
    renderGuard('/assets?status=inStock', session({ status: 'anonymous' }));

    expect(await screen.findByText('Redirected to /login')).toBeInTheDocument();
    expect(screen.getByText('{"from":"/assets?status=inStock"}')).toBeInTheDocument();
  });
});

describe('RequireAnonymous', () => {
  it('sends a reset account requiring MFA to the password form first', async () => {
    renderGuard('/login', session({
      mustChangePassword: true,
      mfaEnrollmentRequired: true,
    }), true);

    expect(await screen.findByText('Redirected to /change-password')).toBeInTheDocument();
  });

  it('sends an account needing only MFA enrollment to security', async () => {
    renderGuard('/login', session({ mfaEnrollmentRequired: true }), true);

    expect(await screen.findByText('Redirected to /security')).toBeInTheDocument();
  });

  it('allows signed-out users to see the login form', () => {
    renderGuard('/login', session({ status: 'anonymous' }), true);

    expect(screen.getByText('Protected content')).toBeInTheDocument();
  });
});
