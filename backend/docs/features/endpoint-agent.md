# Secure Windows endpoint agent

## Delivery boundary

The endpoint-agent rewrite is a backend and Windows-service vertical slice. It adds:

- a self-contained .NET 10 Windows Worker Service;
- Agent-reported TPM-backed, non-exportable ECDSA P-256 device keys by default;
- explicitly permitted non-exportable Windows software keys only as a fallback;
- short-lived, usage-capped and revocable deployment enrollment profiles;
- signed enrollment and check-in requests with timestamps, nonces, SHA-256 body
  hashes and monotonic per-device sequences;
- a DPAPI-protected, atomically replaced offline state/queue;
- typed, bounded hardware, OS, security, network, storage and installed-software
  telemetry;
- exact serial/BIOS asset matching plus an audited administrative link action;
- separate `agents.read`, `agents.manage`, and Administrator-only
  `agents.recover` permissions; and
- a major-upgrade MSI suitable for computer deployment through GPO or SCCM.

There is no agent-management frontend in this increment. The generated API contract
is the integration surface.

The current signed protocol proves possession of the reported key, not hardware
origin. No trusted TPM attestation issuer is configured. `RequireTpm` profile and
recovery creation therefore fail closed with `agent_tpm_attestation_unavailable`;
existing TPM-reported agents expose `hardwareAttestationVerified: false`. A
hardware-only rollout needs a new cryptographic attestation protocol and issuer
before those policies can be enabled.

## Trust and protocol

Enrollment is deliberately separate from an ordinary user session:

1. An administrator creates an enrollment profile with an expiry, maximum device
   count, TPM requirement and optional hostname suffix.
2. The API returns the profile token once. Only its SHA-256 hash is stored.
3. The agent creates a non-exportable machine key, reports whether it is TPM or
   software protected, and signs a canonical enrollment request.
4. The API verifies the profile, key, signature, timestamp, hostname policy and usage
   cap, then atomically reserves the hashed nonce in one SQLite transaction with the
   enrollment audit row.
5. Every later check-in signs the agent/device identifiers, snapshot identifier,
   monotonic sequence, current timestamp, fresh nonce and snapshot SHA-256.

Lost or replaced device keys do not require database editing. An Administrator with
`agents.recover` creates a short-lived, single-use recovery authorization for the
specific managed-agent ID and last-seen concurrency version. Approval immediately
revokes the old key and releases any asset link. The clear recovery token is returned
once with `Cache-Control: no-store`; only its SHA-256 hash is retained. The replacement
   agent uses that token through the existing signed enrollment route, must present a
different ECDSA key, and must satisfy the authorization's TPM policy. Successful
replacement consumes the authorization atomically, preserves the monotonic sequence,
records key-replacement provenance, and leaves the asset relationship in conflict
review for an explicit relink. The old key fails from the moment approval commits.

Signed machine endpoints are anonymous to cookie authentication and explicitly exempt
from browser antiforgery. They are independently rate limited. An architecture test
rejects any unsafe antiforgery exemption that is not both anonymous and rate limited.

The agent queues unsigned typed snapshots. It assigns and persists a sequence before
delivery, then creates a fresh timestamp, nonce and signature for every retry. It
never stores an already-signed request that will expire while offline. Accepted
snapshot identifiers are idempotent only when their sequence and body hash match;
reuse with different content is a conflict. An identical signed request nonce is
rejected; a legitimate retry generates a new timestamp, nonce and signature and can
receive the idempotent result. Nonce rows retain only SHA-256 hashes and are removed
after twice the accepted request window. Enrollment and every accepted check-in,
including an idempotent duplicate, return the server's current
`checkInIntervalSeconds`. This lets a changed server cadence reach already-enrolled
devices on their next successful contact instead of remaining frozen at enrollment.

## Collected inventory

The current collector reports:

- manufacturer, model, serial, BIOS UUID/version, processor/core counts and memory;
- Windows caption/version/build/architecture, install time and last boot;
- Secure Boot, TPM presence/version, system-volume protection and antivirus names;
- non-loopback network adapter names/types/MAC/IPv4 addresses;
- ready fixed-volume filesystem/capacity/free space; and
- machine-wide uninstall-registry software name/version/publisher/architecture/date.

The collector does not use `Win32_Product`, trigger MSI consistency checks, inspect
user documents, collect browser history, or report the last logged-on user. Values are
length limited and collections are capped before transmission. A complete snapshot is
limited to 1 MiB.

## Inventory projection

Every accepted check-in is retained as an append-only receipt for replay/idempotency
and transport provenance. A SHA-256 content hash excludes volatile `snapshotId` and
`collectedAtUtc` envelope values. If the substantive inventory is unchanged, the
receipt is a compact heartbeat that advances sequence, last-seen, freshness and
provenance without duplicating the full JSON payload. The newest detailed payload is
always retained and is projected with the newest receipt's collection envelope.

SQLite storage is bounded per agent by a recent-receipt count and detailed-payload byte
budget. Controlled retention removes only older receipts/payloads after the accepted
current receipt and newest detailed payload exist in the same transaction. It never
rewrites telemetry. Persisted per-agent short and long ingestion windows accept the
agent's maximum ordered offline backlog, then return `429` with `Retry-After` for
abusive volume; exact idempotent retries do not consume additional capacity. The latest
snapshot is available from:

```text
GET /api/v1/assets/{assetId}/device-inventory
```

The response is `ManagedAgentDetailResponse`: its `agent.lastSeenIpAddress` is the
server-observed source address from the latest accepted check-in. It deliberately
sits on the managed-agent envelope rather than inside the signed machine snapshot,
because it is transport provenance observed by the API, not data claimed by the
endpoint.

Telemetry remains separate provenance and does not silently overwrite manually
curated asset fields. On a check-in, an unlinked agent is automatically linked only
when its non-empty serial number or parseable BIOS UUID resolves to exactly one
unlinked asset. Multiple candidates place the agent into the explicit `conflict`
review state. Retired and disposed assets are never auto-linked, and a manual attempt
returns `409 agent_asset_terminal`. Conversely, retiring or disposing a linked asset
returns `409 asset_has_linked_agent` and tells the operator to unlink it first.
Application preflights provide the stable API errors; SQLite triggers enforce both
directions again at commit time so a concurrent terminal transition or link cannot
violate the invariant. Revocation atomically releases an existing link; legacy
revoked/link rows can still be explicitly unlinked. Administrators can link/relink,
unlink, dismiss a reviewed row, or
restore it to review; every state change is versioned and audited. Dismissal is an
operational review state, not key revocation, and it prevents later automatic linking.

The fleet list supports server-side `q`, `trustStatus`, `matchState`, and
`connectivity` filters. Trust (`active`/`revoked`) is deliberately separate from
check-in freshness (`online`/`stale`/`offline`/`neverCheckedIn`). Freshness is computed
from server time, the advertised check-in interval, a grace period, and the configured
offline threshold. The response also supplies the next expected check-in and overdue
time so clients do not invent incompatible thresholds.

Creating a missing asset from an agent uses one
`POST /api/v1/agents/{agentId}/create-and-link-asset` transaction. The request must
explicitly provide the curated asset fields. Asset, creation event, agent link, and
audit record either all commit or all roll back; observed telemetry is never silently
copied into curated fields.

## Administrative API

| Endpoint | Permission |
| --- | --- |
| `GET /api/v1/agents` | `agents.read` |
| `GET /api/v1/agents/summary` | `agents.read` |
| `GET /api/v1/agents/{agentId}` | `agents.read` |
| `GET /api/v1/assets/{assetId}/device-inventory` | `agents.read` |
| `GET /api/v1/agents/enrollment-profiles` | `agents.read` |
| `POST /api/v1/agents/enrollment-profiles` | `agents.manage` |
| `POST /api/v1/agents/enrollment-profiles/{profileId}/revoke` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/link` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/unlink` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/dismiss` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/restore-review` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/create-and-link-asset` | `agents.manage` and `assets.create` |
| `POST /api/v1/agents/{agentId}/revoke` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/key-recovery` | `agents.recover` |
| `GET /api/v1/agents/{agentId}/key-recovery` | `agents.recover` |
| `POST /api/v1/agents/{agentId}/key-recovery/{authorizationId}/revoke` | `agents.recover` |
| `GET /api/v1/agents/installer` | `agents.manage` |
| `GET /api/v1/agents/installer/download` | `agents.manage` |
| `POST /api/v1/agent/enroll` | Signed machine enrollment + rate limit |
| `POST /api/v1/agent/check-ins` | Signed enrolled key + rate limit |

Administrator and InventoryManager receive read/manage. Technician and
ReadOnlyAuditor receive read only. Recovery is deliberately Administrator-only;
granting ordinary `agents.manage` does not permit key replacement.

The recovery list is paged and returns only pending, unexpired authorizations unless
`includeClosed=true` is supplied. It exposes the authorization ID, status, reason,
approver, expiry, consumption/revocation metadata, and concurrency version. It never
returns the one-time recovery token, token hash, or replacement-key thumbprint.

Installer endpoints are optional and fail closed unless absolute MSI and manifest
paths are configured. Every response recalculates and constant-time compares the
SHA-256 hash before returning metadata or a streaming file. Production additionally
requires a manifest signing claim and a trusted Windows Authenticode signature. The
installation template advertises the hidden `ENROLLMENT_TOKEN` bootstrap property but
never substitutes a real token. The MSI immediately DPAPI-protects a supplied direct
token and the agent deletes it after successful enrollment. The token-file path remains
the preferred mass-deployment workflow because direct command lines can be observed in
process inspection and shell history.

## Local protection

The service uses the unique `NT SERVICE\InventoryEndpointAgent` virtual account. The
MSI applies an exact ProgramData ACL for that service SID, Administrators and SYSTEM.
Persistent state and queued snapshots are additionally authenticated and encrypted
with Windows DPAPI, are written with write-through semantics, and are replaced
atomically. Corrupt or unauthentic state fails closed instead of silently
re-enrolling. Direct enrollment-token bootstrap data uses a separate DPAPI purpose and
is removed after protected device enrollment state is saved.

The private signing key remains in Windows CNG with export disabled. The Microsoft
Platform Crypto Provider is attempted first. Software-provider fallback requires both
an MSI/registry opt-in and a server enrollment profile that does not require TPM.

## Production gates

Repository build and integration tests do not replace an estate pilot. Before broad
deployment:

1. Authenticode-sign the EXE and MSI with the organisation's code-signing certificate.
2. Install/uninstall/upgrade on representative Windows 10/11 TPM hardware.
3. Validate the service account can create and reopen its TPM key after reboot.
4. Exercise the real proxy, TLS trust chain, filtering and rate limits.
5. Pilot both GPO assignment and SCCM supersedence/detection.
6. Verify sampled serial/BIOS matches and manually resolve ambiguous assets.
7. Revoke the rollout enrollment profile and remove its staged token immediately.

See [agent-deployment.md](../operations/agent-deployment.md).
