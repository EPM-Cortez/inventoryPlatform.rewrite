# Endpoint permission map

Implemented endpoints:

| Endpoint | Decision |
| --- | --- |
| `GET /api/v1/auth/csrf` | Anonymous |
| `POST /api/v1/auth/login` | Anonymous + CSRF + rate limit |
| `POST /api/v1/auth/login/mfa` | Anonymous challenge + CSRF + rate limit |
| `POST /api/v1/auth/login/mfa/recovery` | Anonymous recovery challenge + CSRF + rate limit |
| `POST /api/v1/auth/logout` | Authenticated |
| `GET /api/v1/auth/me` | Authenticated |
| `POST /api/v1/auth/change-password` | Authenticated |
| `GET /api/v1/auth/mfa` | Authenticated |
| `POST /api/v1/auth/mfa/enrollment` | Authenticated; current password required |
| `POST /api/v1/auth/mfa/enrollment/confirm` | Authenticated; authenticator code required |
| `POST /api/v1/auth/mfa/recovery-codes` | Authenticated; current password and authenticator code required |
| `GET /api/v1/assets` | `assets.read` |
| `GET /api/v1/assets/{id}` | `assets.read` |
| `GET /api/v1/assets/by-tag/{tag}` | `assets.read` |
| `POST /api/v1/assets` | `assets.create` |
| `POST /api/v1/assets/bulk-changes/preview` | `assets.bulk` |
| `POST /api/v1/assets/bulk-changes/{jobId}/commit` | `assets.bulk` |
| `PUT /api/v1/assets/{id}` | `assets.update` |
| `POST /api/v1/assets/{id}/retire` | `assets.retire` |
| `POST /api/v1/assets/{id}/restore` | `assets.restore` |
| `POST /api/v1/assets/{id}/dispose` | `assets.retire` |
| `POST /api/v1/assets/{id}/qr` | `qr.generate` |
| `GET /api/v1/assets/{id}/qr` | `qr.generate` |
| `POST /api/v1/assets/{id}/qr/rotate` | `qr.rotate` |
| `POST /api/v1/assets/{id}/label` | `qr.print` |
| `GET /api/v1/labels/stocks` | `qr.print` |
| `POST /api/v1/labels/batch` | `qr.print` |
| `GET /api/v1/qr/{token}` | `assets.read` + per-IP rate limit |
| `GET /api/v1/assets/{id}/warranties` | `warranties.read` |
| `POST /api/v1/assets/{id}/warranties` | `warranties.manage` |
| `PUT /api/v1/warranties/{id}` | `warranties.manage` |
| `POST /api/v1/warranties/{id}/archive` | `warranties.manage` |
| `GET /api/v1/warranties/expiring` | `warranties.read` |
| `GET /api/v1/assets/{id}/history` | `history.read` |
| `POST /api/v1/assets/{id}/history/notes` | `history.note` |
| `GET /api/v1/assets/{id}/assignments` | `assignments.read` |
| `POST /api/v1/assets/{id}/assignments` | `assignments.manage` |
| `PUT /api/v1/assets/{id}/assignments/{assignmentId}/expected-return` | `assignments.manage` |
| `POST /api/v1/assets/{id}/assignments/{assignmentId}/return` | `assignments.manage` |
| `GET /api/v1/assignments/overdue` | `assignments.read` |
| `GET /api/v1/dashboard/summary` | `assets.read` |
| `GET /api/v1/dashboard/inventory-by-location` | `assets.read` |
| `GET /api/v1/dashboard/exceptions/{exceptionCode}` | `assets.read` |
| `GET /api/v1/stocktakes`, `GET /api/v1/stocktakes/{sessionId}` | `stocktakes.read` |
| `GET /api/v1/stocktakes/{sessionId}/items` | `stocktakes.read` |
| `POST /api/v1/stocktakes` | `stocktakes.manage` |
| `POST /api/v1/stocktakes/{sessionId}/observations` | `stocktakes.manage` |
| `POST /api/v1/stocktakes/{sessionId}/complete` | `stocktakes.manage` |
| `POST /api/v1/stocktakes/{sessionId}/cancel` | `stocktakes.manage` |
| `GET`, `PUT /api/v1/settings/asset-tags` | `asset-tags.manage` |
| `GET /api/v1/asset-bundles`, `GET /api/v1/asset-bundles/{bundleId}` | `bundles.read` |
| `POST /api/v1/asset-bundles`, `PUT /api/v1/asset-bundles/{bundleId}` | `bundles.manage` |
| `POST /api/v1/asset-bundles/{bundleId}/archive`, `/restore` | `bundles.manage` |
| `GET /api/v1/leavers`, `GET /api/v1/leavers/{leaverCaseId}` | `leavers.read` |
| `GET /api/v1/leavers/{leaverCaseId}/assets` | `leavers.read` |
| `GET /api/v1/leavers/exceptions` | `leavers.read` |
| `POST /api/v1/leavers`, `PUT /api/v1/leavers/{leaverCaseId}` | `leavers.manage` |
| `POST /api/v1/leavers/{leaverCaseId}/complete`, `/cancel`, `/rehire` | `leavers.manage` |
| `GET /api/v1/assets/{assetId}/attachments` | `attachments.read` |
| `GET /api/v1/assets/{assetId}/attachments/{attachmentId}/download` | `attachments.read` |
| `POST /api/v1/assets/{assetId}/attachments` | `attachments.manage` |
| `POST /api/v1/assets/{assetId}/attachments/{attachmentId}/archive` | `attachments.manage` |
| `GET`, `POST /api/v1/me/saved-asset-searches` | `assets.read`; always scoped to the signed-in user |
| `GET /api/v1/me/saved-asset-searches/default` | `assets.read`; always scoped to the signed-in user |
| `GET`, `PUT /api/v1/me/saved-asset-searches/{savedSearchId}` | `assets.read`; owner-only |
| `POST /api/v1/me/saved-asset-searches/{savedSearchId}/default`, `/archive` | `assets.read`; owner-only |
| `GET /api/v1/people`, `GET /api/v1/people/{id}` | `people.read` |
| `POST /api/v1/people` | `people.operate` |
| `PUT /api/v1/people/{id}` | `people.operate`; setting `isActive=false` additionally requires `people.manage` |
| `GET /api/v1/people/{id}/assets` | `assets.read` |
| `GET /api/v1/departments`, `GET /api/v1/departments/{id}` | `people.read` |
| `POST /api/v1/departments`, `PUT /api/v1/departments/{id}` | `people.manage` |
| `GET /api/v1/departments/{id}/assets` | `assets.read` |
| `GET /api/v1/categories`, `GET /api/v1/categories/{id}` | `assets.read` |
| `POST /api/v1/categories`, `PUT /api/v1/categories/{id}` | `reference.manage` |
| `GET /api/v1/locations`, `GET /api/v1/locations/{id}` | `assets.read` |
| `POST /api/v1/locations`, `PUT /api/v1/locations/{id}` | `reference.manage` |
| `POST /api/v1/imports/assets/validate` | `imports.validate` |
| `GET /api/v1/imports/{id}` | `imports.read` |
| `POST /api/v1/imports/{id}/commit` | `imports.commit` |
| `GET /api/v1/imports/{id}/errors.csv` | `imports.read` |
| `GET /api/v1/exports/assets.csv` | `exports.assets` |
| `POST /api/v1/imports/people/validate` | `imports.validate` |
| `POST /api/v1/imports/departments/validate` | `imports.validate` |
| `POST /api/v1/imports/locations/validate` | `imports.validate` |
| `GET /api/v1/imports/reference-data/{id}` | `imports.read` |
| `POST /api/v1/imports/reference-data/{id}/commit` | `imports.commit` |
| `GET /api/v1/imports/reference-data/{id}/errors.csv` | `imports.read` |
| `GET /api/v1/exports/people.csv` | `exports.people` |
| `GET /api/v1/exports/departments.csv` | `exports.departments` |
| `GET /api/v1/exports/locations.csv` | `exports.locations` |
| `GET /api/v1/users`, `GET /api/v1/users/{id}` | `users.manage` |
| `GET /api/v1/users/role-presets` | `users.manage` |
| `POST /api/v1/users`, `PUT /api/v1/users/{id}` | `users.manage` |
| `PUT /api/v1/users/{id}/roles` | `users.manage` |
| `POST /api/v1/users/{id}/disable` | `users.manage` |
| `DELETE /api/v1/users/{id}` | `users.manage`; archives rather than hard-deletes |
| `POST /api/v1/users/{id}/restore` | `users.manage` |
| `POST /api/v1/users/{id}/reset-password` | `users.manage` |
| `POST /api/v1/users/{id}/reset-mfa` | `users.manage` |
| `GET /api/v1/audit` | `audit.read` |
| `GET /api/v1/audit/vocabulary` | `audit.read` |
| `GET /api/v1/audit/export` | `audit.export` |
| `GET`, `PUT /api/v1/settings/school` | `settings.manage` |
| `GET /api/v1/operations/status` | `settings.manage` |
| `POST /api/v1/legacy-reconciliation/dry-runs` | `legacy.reconcile` |
| `GET /api/v1/legacy-reconciliation/{importJobId}` | `legacy.reconcile` |
| `POST /api/v1/legacy-reconciliation/{importJobId}/sign-off` | `legacy.reconcile` |
| `GET /api/v1/legacy-reconciliation/{importJobId}/cutover-package` | `legacy.reconcile` |
| `GET /api/v1/agents`, `GET /api/v1/agents/summary`, `GET /api/v1/agents/{agentId}` | `agents.read` |
| `GET /api/v1/assets/{assetId}/device-inventory` | `agents.read` |
| `GET /api/v1/agents/enrollment-profiles` | `agents.read` |
| `POST /api/v1/agents/enrollment-profiles` | `agents.manage` |
| `POST /api/v1/agents/enrollment-profiles/{profileId}/revoke` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/link`, `/unlink`, `/dismiss`, `/restore-review` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/create-and-link-asset` | `agents.manage` and `assets.create` |
| `POST /api/v1/agents/{agentId}/revoke` | `agents.manage` |
| `POST /api/v1/agents/{agentId}/key-recovery` | `agents.recover` |
| `GET /api/v1/agents/{agentId}/key-recovery` | `agents.recover` |
| `POST /api/v1/agents/{agentId}/key-recovery/{authorizationId}/revoke` | `agents.recover` |
| `GET /api/v1/agents/installer`, `/installer/download` | `agents.manage` |
| `POST /api/v1/agent/enroll` | Anonymous signed enrollment proof + per-IP rate limit; no cookie/CSRF |
| `POST /api/v1/agent/check-ins` | Anonymous enrolled-device signature + per-IP rate limit; no cookie/CSRF |
| `GET /health/live`, `GET /health/ready` | Anonymous deployment probes; readiness is rate limited |
| `GET /openapi/v1.json` | Anonymous in Development only |

Role claims are generated from `RolePermissionPresets`. Endpoint authorization never
checks UI visibility and never relies only on a role name.

`people.operate` is the Technician's limited people capability: it permits
creating and maintaining person custody records. Archiving a person and all department
administration require `people.manage`, which is held only by managers and administrators.

Dashboard reads deliberately reuse `assets.read`; they expose aggregates and the same
bounded asset summaries already available to a register reader. Stocktake reads have
their own `stocktakes.read` permission, while starting, observing, completing, and
cancelling require `stocktakes.manage`. The shipped presets grant stocktake management
to Administrator, InventoryManager, and Technician; ReadOnlyAuditor receives read-only
stocktake access.

`asset-tags.manage` and `assets.bulk` are limited to Administrator and
InventoryManager in the shipped presets. Managed generation remains an explicit
choice on asset creation and does not replace a supplied tag. Bulk commit requires
the persisted preview's version and fingerprint, rechecks every selected asset
version, and cannot use the permission to bypass assignment, retirement, or disposal
workflows.

`bundles.read`, `leavers.read`, and `attachments.read` are granted to all shipped
roles. Their management counterparts are granted to Administrator and
InventoryManager; Technician can also manage bundles and attachments but has
read-only leaver access. Bundle membership does not grant permission to mutate the
member assets. A leaver case surfaces custody but never returns assets automatically.
Attachment access is separate from ordinary asset read access because downloads can
contain financial or disposal evidence.

Saved searches deliberately reuse `assets.read` because they only persist a validated
asset-list filter. Every query is scoped to the current user, including identifier
lookups; possession of another user's saved-search identifier confers no access.

`qr.print` is granted to Administrator, InventoryManager, and Technician. It is
separate from `qr.generate` so print access remains explicit if custom roles are added
later. Label printing with `issueMissingQr` additionally requires `qr.generate`
because it mints new QR identifiers; a print-only principal receives `403` with
problem code `qr_generate_required`.

`audit.read` and `audit.export` are granted to Administrator and ReadOnlyAuditor.
Export is separately bounded, excludes structured audit details, and neutralises
spreadsheet formulas.

`settings.manage` and `legacy.reconcile` are Administrator-only in the shipped role
presets. A reconciliation exception sign-off records an authenticated decision but
never overrides import validation, snapshot, version, or commit checks.

`agents.read` is granted to all four shipped roles so technicians and auditors can
inspect endpoint evidence without mutating trust. `agents.manage` is limited to
Administrator and InventoryManager and controls enrollment profiles, explicit
asset links, review lifecycle, trusted installer delivery, and revocation. Atomic
asset creation from an agent additionally requires `assets.create`. Machine ingress
does not inherit a browser session:
enrollment proves possession of the submitted non-exportable key and a one-time
profile token; subsequent check-ins prove the enrolled key, freshness, nonce and
monotonic sequence. These two routes are the only unsafe antiforgery exemptions and
are independently rate limited.

`agents.recover` is Administrator-only in the shipped presets. It approves or revokes
a short-lived, single-use replacement-key authorization. Approval immediately revokes
the old device key and atomically releases any linked asset. The recovery token is
returned once and is not persisted in clear text. `agents.manage` alone cannot create,
view, or revoke recovery credentials.

Configured MFA-required roles receive only authentication/MFA account endpoints until
enrolment completes. Changing roles, passwords, enabled/archive state, or MFA rotates
the security stamp; existing cookies are revalidated on every request.

`imports.validate`, `imports.read`, and `imports.commit` are granted to Administrator
and InventoryManager. Those roles also hold `exports.people`, `exports.departments`,
and `exports.locations`; the separate keys keep bulk person PII and each reference
export explicitly grantable. `exports.assets` is additionally granted to
ReadOnlyAuditor. Technician cannot import or export, and ReadOnlyAuditor cannot export
people/reference data. Commit still rechecks the validated snapshot and job version;
permission alone cannot bypass validation. Asset-import commit can create the
categories and locations explicitly shown as planned in its validation preview; both
authorized roles also hold `reference.manage`.
