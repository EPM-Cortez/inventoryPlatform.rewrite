# ADR 0003: Identity cookie authentication

- Status: Accepted
- Date: 2026-07-24

## Decision

Use ASP.NET Core Identity local accounts and secure cookies. Disable registration.
Enforce named permission claims populated through four managed role presets.

## Controls

- HTTP-only, Secure, SameSite=Strict cookie
- CSRF cookie/header validation on every unsafe method
- Password length and complexity policy, lockout, and reset tokens
- Security-stamp validation on every request
- Immediate enabled-user lookup and rejection
- Rate-limited login
- Audited user/role/password administrative operations
- `must_change_password` claim blocks permission-protected work until changed

## Future

Entra ID can later map external identities to the same local user and permission model.
It does not change domain authorization.
