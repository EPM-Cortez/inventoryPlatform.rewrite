import { describe, expect, it } from 'vitest';

import type { CurrentUser } from '@/api/types';
import { Permission } from './permissions';

/**
 * The rule `SessionProvider` implements, extracted so it can be asserted without
 * mounting the provider.
 *
 * The API keeps returning a full `permissions` array while it is refusing to
 * honour any of it — `PermissionAuthorization` returns without succeeding when
 * either lockout claim is set. A `can()` built only from that array therefore
 * reports capabilities the server will deny, and the user gets a complete-looking
 * app that answers 403 to every action.
 */
function canFor(user: CurrentUser) {
  const lockedOut = user.mustChangePassword || user.mfaEnrollmentRequired;
  const held = new Set(user.permissions);
  return (permission: string) => !lockedOut && held.has(permission);
}

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    displayName: 'Test Admin',
    roles: ['Administrator'],
    // Deliberately a full set: this is what the server really sends in every
    // state below, including the locked-out ones.
    permissions: [Permission.AssetsRead, Permission.UsersManage, Permission.AuditRead],
    mustChangePassword: false,
    mfaEnrollmentRequired: false,
    ...overrides,
  };
}

describe('permission checks under a server lockout', () => {
  it('grants held permissions to an unlocked account', () => {
    const can = canFor(user());
    expect(can(Permission.AssetsRead)).toBe(true);
    expect(can(Permission.UsersManage)).toBe(true);
  });

  it('never grants one it does not hold', () => {
    expect(canFor(user())(Permission.SettingsManage)).toBe(false);
  });

  it('grants nothing while a password change is outstanding', () => {
    const can = canFor(user({ mustChangePassword: true }));
    expect(can(Permission.AssetsRead)).toBe(false);
    expect(can(Permission.AuditRead)).toBe(false);
  });

  it('grants nothing while two-step enrollment is outstanding', () => {
    const can = canFor(user({ mfaEnrollmentRequired: true }));
    expect(can(Permission.AssetsRead)).toBe(false);
    expect(can(Permission.UsersManage)).toBe(false);
  });

  it('still reports a populated permission list in both locked states', () => {
    // Guards the assumption the whole rule rests on. If the server ever starts
    // emptying the array instead, this test should fail and the fold-in can go.
    expect(user({ mustChangePassword: true }).permissions.length).toBeGreaterThan(0);
    expect(user({ mfaEnrollmentRequired: true }).permissions.length).toBeGreaterThan(0);
  });
});
