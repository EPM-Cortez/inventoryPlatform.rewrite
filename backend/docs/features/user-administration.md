# User administration

Login users are separate from people and departments that hold assets. There is no
public registration. An authenticated user with `users.manage` can list role presets,
create accounts, edit email/display name/enabled state/roles, reset passwords, disable
accounts, and archive or restore accounts.

## Initial administrator

Apply migrations, then use `scripts/seed-admin.ps1` or the non-interactive
`--seed-admin` command with secret-backed `BootstrapAdmin:*` configuration. The command
is idempotent for the same administrator and refuses to introduce a different
administrator once one is enabled. A default password is never stored in source.

## Role presets

- `Administrator` has every named permission.
- `InventoryManager` manages inventory operations but not login users or settings.
- `Technician` performs normal asset, custody, warranty, history-note, and QR work.
- `ReadOnlyAuditor` has controlled read, export, and audit access.

Normal API startup reconciles these code-managed permission claims before accepting
requests. `--seed-roles` performs the same idempotent operation as a deployment
preflight and migrates memberships from the former `InventoryOperator` name to
`Technician`. A real repair and its audit row commit atomically; an audit failure
rolls back the claim repair. Because role claims are refreshed on every request,
already-issued cookies receive added permissions and lose removed permissions without
waiting for cookie expiry.

## Administrator MFA

The shipped MFA policy requires `Administrator` accounts to enrol before business
permissions are honoured. An authenticated but unenrolled administrator can access
only its account/password/MFA endpoints:

1. `POST /api/v1/auth/mfa/enrollment` verifies the current password and returns a
   shared key/authenticator URI with `no-store`;
2. `POST /api/v1/auth/mfa/enrollment/confirm` verifies TOTP and returns recovery codes
   once;
3. subsequent login returns an MFA challenge completed through
   `/api/v1/auth/login/mfa` or the one-time
   `/api/v1/auth/login/mfa/recovery` path.

Recovery codes can be regenerated only with both password and current TOTP.
`POST /api/v1/users/{id}/reset-mfa` is an audited `users.manage` action, rotates the
security stamp, invalidates sessions, and forces a required-role account through
enrolment again. Authenticator keys and Identity recovery tokens are data-protected
before persistence; codes and shared keys are never included in audit details.

## Lifecycle guarantees

- Account responses include an application-managed `version`; stale edits, role
  changes, resets, disables, archives, and restores return `409`.
- Deletion is a soft archive. It sets `ArchivedAtUtc`, disables login, invalidates
  sessions, and keeps role assignments and historical foreign keys.
- Restore re-enables the account and invalidates any pre-archive session material.
- The current administrator cannot disable or archive itself.
- The final enabled administrator cannot be disabled or lose that role.
- Password values and reset tokens are never written to audit details.
- Create, edit, role, password reset, disable, archive, and restore operations write
  append-only audit records.
- Identity mutations and their audit row share one SQLite transaction. Forced
  audit-insert failures prove that login sessions, passwords, account creation, role
  changes, and logout state do not partially succeed.
- Security stamps are checked on every request, so a role/permission change
  immediately removes old-cookie access rather than waiting for cookie expiry.

The service is deliberately configured for one write-capable SQLite API instance.
Identity administration mutations are serialized inside that process so two concurrent
requests cannot both remove the final administrator.
