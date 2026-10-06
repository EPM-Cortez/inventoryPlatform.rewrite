# ADR 0001: Modular monolith

- Status: Accepted
- Date: 2026-07-24

## Context

The product serves one school and requires one transactional inventory record. Separate
services would add deployments, distributed consistency, and failure modes without a
product need.

## Decision

Use one ASP.NET Core service and one relational database with Domain, Application,
Infrastructure, Contracts, and Api project boundaries.

## Consequences

Cross-module commands can be atomic. Operations remain simple. Project dependency and
controller rules must be tested so the monolith does not become an unstructured layer.
