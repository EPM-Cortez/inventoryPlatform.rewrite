# Adding an endpoint

1. Confirm the capability belongs to the current scope and document its domain rule.
2. Add request/response types to `Inventory.Contracts/V1`; never expose EF entities.
3. Add or extend an application use case and port.
4. Implement persistence in Infrastructure with one explicit unit-of-work boundary.
5. Add a named, versioned controller route and stable operation name.
6. Mark the endpoint `[AllowAnonymous]` only when deliberately public; otherwise require
   the exact permission policy.
7. Map validation, missing data, and concurrency to the documented `ProblemDetails`
   statuses.
8. Add contract examples, permission-map entries, and relevant feature/operations docs.
9. Add domain/application tests and API happy/error-path integration tests.
10. Regenerate `docs/api/openapi-v1.json` and run all quality gates.

Unsafe controller actions are protected globally by antiforgery validation. Do not
disable it for convenience.
