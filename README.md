# Inventory Platform rewrite

This repository contains the single-school inventory rewrite:

- [`backend/`](backend/README.md): ASP.NET Core API, domain, Windows endpoint agent,
  migrations, tests, Docker deployment and installer tooling.
- [`frontend/`](frontend/README.md): React/Vite client for the rewrite API.

Each project has its own setup and operational instructions. Run the backend and
frontend checks in their respective directories; the repository CI does both.
The frontend development server proxies API requests to the backend. A successful
source build does not by itself establish a production HTTPS hosting topology.

No live credentials, signing keys, database files, built MSI, or local environment
files belong in Git. Copy each project's `.env.example` and provision real secrets
through the deployment environment.

See the [production release gate](backend/docs/operations/production-readiness.md)
before any rollout. Hardware-trusted agent enrollment remains blocked until a
trusted TPM attestation issuer is configured and piloted.
