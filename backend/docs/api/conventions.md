# API conventions

## Base contract

- Base path: `/api/v1`
- Media type: `application/json`
- JSON names: camel-case
- Enums: camel-case strings. The OpenAPI schema publishes every legal value.
- IDs: Guid strings
- Timestamps: ISO 8601 UTC
- Business dates: `YYYY-MM-DD`

## Authentication and CSRF

The API uses an Identity application cookie. Clients:

1. `GET /api/v1/auth/csrf`
2. retain the returned antiforgery cookie
3. send the returned value as `X-CSRF-TOKEN` on login
4. retain the authentication cookie
5. fetch a new CSRF token after login
6. send the authenticated token on every POST/PUT/PATCH/DELETE

API clients receive status codes, not login redirects.

Integer enum values are rejected consistently in JSON bodies and query strings.
For example, `condition=good` is valid and `condition=1` returns `400`. Generated
clients should use the string values listed in the OpenAPI `enum` array.

## Paging and sorting

Paged responses contain:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 50,
  "totalCount": 0
}
```

`page` starts at 1. `pageSize` must be 1-200. Asset sorting accepts `assetTag`,
`updatedAtUtc`, `status`, and a `-` prefix for descending order.

Asset filters include `warrantyState`, inclusive `warrantyEndsBefore`,
`createdFrom`, and `createdTo`. Warranty state is calculated using the current UTC
date and configured warning threshold.

## Concurrency

Mutable responses expose a Guid `version`. PUT and lifecycle commands must send the
last-seen version. A stale token returns `409` and never overwrites newer data.

## Errors

Errors use RFC Problem Details and include `traceId`. Expected status codes:

| Status | Meaning |
| --- | --- |
| 400 | Invalid model, domain rule, paging, sort, or CSRF token |
| 401 | Missing/expired/disabled authentication |
| 403 | Authenticated but missing the named permission |
| 404 | Resource not found |
| 409 | Duplicate identifier, stale version, or protected admin invariant |
| 429 | Authentication or QR-resolution rate limit |
| 500 | Generic unexpected failure; details stay in structured logs |

## Compatibility

Removing/renaming fields, changing meanings, or making optional inputs required is
breaking. Additive optional fields are normally non-breaking. Do not return EF entities.
