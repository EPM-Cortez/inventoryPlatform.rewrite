# QR identifiers

Status: Implemented in milestone 3.

## Workflow

1. `POST /api/v1/assets/{assetId}/qr` issues the first active identifier with
   `qr.generate`. Reissuing while one is active returns `409`.
2. `GET /api/v1/assets/{assetId}/qr` renders SVG by default; `?format=png` renders
   PNG. Images are generated on demand and returned with `Cache-Control: no-store`.
3. A client decodes the configured HTTPS payload and authenticates before calling
   `GET /api/v1/qr/{token}` with `assets.read`.
4. `POST /api/v1/assets/{assetId}/qr/rotate` requires `qr.rotate`, revokes the old
   token, creates the next version, and appends `QrRotated` in one transaction.
5. `POST /api/v1/assets/{assetId}/label` and `POST /api/v1/labels/batch` require
   `qr.print` and return print-ready PDF labels. See
   [QR label printing](qr-label-printing.md).

The issue/rotate metadata response includes the payload URL so an authorised label
workflow can print or verify it. Normal asset and export records expose only the safe
QR image endpoint, never the token.

## Privacy and persistence

- Tokens contain 32 random bytes (256 bits) encoded as base64url. They are not derived
  from asset, serial, person, or database identifiers.
- SQLite stores a SHA-256 token hash for lookup and a data-protection-protected token
  for later image rendering. Plaintext tokens are not stored.
- Token hash, `(AssetId, TokenVersion)`, and the single active identifier are enforced
  by unique indexes.
- Unknown and revoked tokens return the same non-sensitive `404`. The response redacts
  the token-bearing path as `/api/v1/qr/{token}`.
- Resolution is authenticated, permission checked, rate limited, and returns a limited
  asset summary without serial, holder, purchase, or QR-secret fields.
- QR identifiers are revoked, never deleted or reused. Issue and rotation events contain
  identifier IDs and versions only.

`School:QrBaseUrl` is the configuration fallback. A persisted `SchoolProfile`, when
present, overrides it. Production must preserve the data-protection key ring alongside
the SQLite backup.

Camera interaction remains future frontend or scanner-client work. Physical label
layout is backend-generated and does not require a frontend.
