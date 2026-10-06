# ADR 0002: Model exactly one school

- Status: Accepted
- Date: 2026-07-24

## Decision

Store one `SchoolProfile`. Do not place `TenantId`, `SchoolId`, or trust scope fields on
ordinary domain rows.

## Rationale

The immediate product is explicitly for one school. Dormant multi-tenancy would
increase authorization and migration risk and recreate the complexity being removed.

## Consequence

Trust support would be a new product and architecture decision with an explicit data
migration. An architecture test protects the current boundary.
