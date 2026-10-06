# Repository guidance

## Purpose

The frontend for the single-school inventory API in `../backend`. Build
only against endpoints that backend implements. Do not add trusts, schools,
licences, loans, purchase orders, disposals, SSO, alerts or clusters — the old
frontend in the parent directory has those; this one deliberately does not.

## Source of truth

`../backend` is authoritative, in this order:

1. `src/Inventory.Contracts/V1/*.cs` — request and response shapes
2. `docs/api/permissions.md` — the endpoint-to-permission table
3. `docs/api/conventions.md` — paging, sorting, errors, concurrency
4. `src/Inventory.Domain/**` — rules that produce `400`s the UI should prevent

If code here disagrees with those, they win. Do not invent endpoints or fields.

## Commands

```bash
npm run typecheck
npm run lint
npm test
npm run build
```

All four must pass before a change is done.

## Non-negotiables

- **Same origin.** The API sets `__Host-` cookies with `SameSite=Strict`. CORS
  cannot work. Dev uses the Vite proxy; production serves `dist/` from the API's
  host. Never "fix" an auth problem by adding CORS.
- **`src/api/types.ts` mirrors `Inventory.Contracts.V1` by hand.** Update it when
  the backend changes, then fix the type errors. Never widen a type to silence one.
- **Thread `version` through every mutation.** Send the last-seen version; on
  `409` tell the user to reload and reapply. Never retry with the same version.
- **Permission checks are visibility only.** The API enforces them for real.
- **Two session flags are lockouts, not reminders.** `mustChangePassword` and
  `mfaEnrollmentRequired` each make `PermissionAuthorization` return *without*
  granting anything, so every request 403s — while `/auth/me` keeps returning a
  fully populated `permissions` array. `SessionProvider` folds both into `can()`
  centrally; never rebuild a permission check from `user.permissions` directly.
- **Blank optional strings go over the wire as `null`, not `""`.**
- **`pageSize` is capped at 200 by the server.**
- Asset sorting accepts only `assetTag`, `updatedAtUtc` and `status`, with an
  optional `-` prefix. Anything else is a `400`.
- **Accounts are versioned.** Archive puts the version in the **query string**
  (`DELETE /users/{id}?version=…`); every other account write puts it in the body.
- **Never hard-code assignable roles.** Read `GET /users/role-presets`.
  `src/auth/permissions.ts` supplies labels only.
- Accounts (`/accounts`) are sign-ins; people (`/people`) are custody records.
  Keep the distinction visible in copy — users conflate them constantly.
- **Check whether a list endpoint is paged before using it.** Categories and
  locations returned bare arrays and later became `PagedResponse`; the `.map`
  crash that caused took down every page with a picker. When the backend
  changes, re-read the controller signature, not just the contract record.
- Disposal is terminal. Never offer restore on a disposed asset.
- **CSV `schemaVersion` differs by resource**: assets are on 2, reference data
  (people, departments, locations) is on 1. Getting it wrong fails every row with
  an opaque message, so the wizard states the expected value.
- Reference-data imports share one wizard. Per-resource differences belong in
  `features/imports/referenceResources.ts`, not in branching inside the page.
- **An asset import can create categories and locations as a side effect.** Any
  UI showing an import preview must say so before the commit button — a typo in
  the file becomes a real category. Counts come from the job; codes come from the
  capped preview, so never present them as a complete list.
- Imported dates may be inferred from partial values. Mark inferred ones and
  leave exact ones unmarked, so the badge means something.
- **Imported custody may be a guess.** A `fuzzyPerson` resolution decides who is
  accountable for a device, so it must be visibly distinguishable from an exact
  match and checkable per row. Never render it as if it were certain.
- An unresolvable owner silently becomes an unassigned `inStock` asset instead of
  an error. Always report the count — a quietly dropped owner is worse than a
  loud failure.
- **Agent data is read-only evidence.** Never build an edit affordance over a
  snapshot field, and never show one without its collection date — stale evidence
  presented as current is actively misleading.
- Agent-reported booleans are tri-state. `null` means the agent could not read it,
  which is not the same as `false`.
- **Never recompute agent connectivity on the client.** `connectivityStatus` is
  the server's judgement against each agent's own expected check-in interval. A
  fixed client-side threshold over `lastCheckInAtUtc` disagrees with the fleet
  summary counts rendered directly above the rows.
- **Filtering to revoked agents needs `trustStatus=revoked` *and*
  `includeRevoked=true`.** The status filter alone returns an empty list with no
  error, which reads as "none revoked" rather than "you asked wrong".
- **Create-and-link carries the *agent's* version, not an asset's.** It is what
  makes the create atomic: a machine that changed underneath rejects the whole
  request instead of leaving an orphaned asset. Same for unlink, dismiss and
  restore-review, which all take a required reason too.
- Revoking is the only irreversible agent action. Unlink, dismiss and
  restore-review are ordinary reversible work — do not dress them up with the
  type-the-hostname confirmation, or it stops meaning anything on revoke.
- Do not call `Date.now()` during render — it is impure and the hooks lint rejects
  it. Use `useAgeInDays`, or capture the instant once with `useState(() => Date.now())`
  and pass it down, so every row judges against the same moment.
- **Never send a secret to an image service to be drawn.** The MFA
  `authenticatorUri` contains the TOTP secret; it is encoded locally by
  `src/lib/qr.ts`. The same goes for anything else that would put a credential in
  a URL belonging to someone else.
- QR codes render dark-on-white with a four-module quiet zone in **both** themes.
  Scanners assume a light field, so this is the one place the theme tokens are
  the wrong answer.
- **A secret shown once is shown once.** MFA recovery codes, agent enrollment
  tokens and agent key-recovery tokens are stored only as hashes. Any surface
  that displays one must offer copy and download, must pass `dismissible={false}`
  so neither Escape nor a backdrop click can lose it, and must say plainly that
  closing loses it. `Dialog.test.tsx` guards that behaviour — do not weaken it.
- **Agent key recovery is a credential, not a record edit.** It lets one machine
  replace its signing key without re-enrolling, so it sits behind its own
  `agents.recover` permission rather than `agents.manage`, defaults to a short
  window, and defaults to requiring a TPM whenever the current key is TPM-held —
  a recovery must never be a route to a weaker key than the machine already has.
  Only a `pending` authorisation can be revoked; the other three states are
  history.
- **Deactivating a category or location locks the assets in it.** The server
  re-validates both on every asset write, so an asset in an inactive one cannot be
  saved until it is moved. Warn before, not after.
- **Audit filter values come from `GET /audit/vocabulary`, never from a list
  mirrored here.** It reports what is actually in the trail, so the dropdowns
  cannot offer a value with no rows behind it, and a new kind of event becomes
  filterable the first time it happens. `features/audit/auditActions.ts` supplies
  labels and grouping only. Honour `truncated`.
- **Audit outcomes and action names were canonicalised in `6d95974`, and the old
  spellings still filter.** `succeeded` is the only success value written now,
  but rows saying `success` are immutable and stay forever, so both must read as
  a success. Keep the pre-canonicalisation aliases (`stocktake.asset-observed`,
  `saved-search.create`, `assetImportJob`, …) in the label maps — a bookmarked
  URL still resolves server-side, and it must not degrade into raw identifiers.
- **Sign-off on a reconciliation is immutable and single-shot.** Confirm before
  sending, and never present acknowledging a blocking issue as making a failed
  import committable — the server explicitly does not.
- **School settings have no version until they are first saved.** Send `null` while
  `isPersisted` is false; sending a version returns `school_settings_not_created`.
- Changing the QR base URL invalidates every printed label. Treat it as
  irreversible in copy, because the labels are physically on the equipment.
- Operational counters are cumulative since process start, not all-time. Always
  show them next to uptime or they read as a quiet period.

## Adding a screen

1. Add the endpoint function to `src/api/endpoints.ts`.
2. Add a key to `src/api/queryKeys.ts` and a hook to `src/api/hooks.ts`, with
   invalidation for every cache the mutation can affect.
3. Add the route to `src/app/routes.tsx` wrapped in `RequirePermission`.
4. Add the nav entry with its permission to `src/app/navigation.ts`.
5. Build the page from `src/ui` primitives — features never restyle primitives.

## Component conventions

- **Dialogs and drawers are mounted only while open.** The parent renders
  `{open && <Thing onClose={...} />}`. State then initialises fresh on every open
  with no reset effect. Do not add an `open` prop and a resetting `useEffect`.
- **Do not call `setState` inside an effect.** Derive the value during render, or
  clamp it, or handle it in the event that caused the change. ESLint enforces this.
- **Never pass a function `className` to anything wrapped in `asChild`.** Radix
  merges cloned props by string concatenation, so `NavLink`'s
  `className={({ isActive }) => …}` arrives as `String(fn)`. It fails silently and
  half-works — the class names inside the source text still parse as classes,
  while every one next to a quote or brace is dropped. It cost the collapsed
  sidebar its centring and its active state. Match with `useMatch` and pass a
  string.
- **Colour is never the only signal.** Status pills carry a label; the dashboard's
  numerals stay in text tokens and let a labelled icon carry the state.
- Use `Field` for every form control so labels and errors are wired to ARIA.
- Use `NativeSelect` for short fixed lists, `Combobox` when the list is long or
  needs search.

## Verifying a change

A green **incremental** typecheck is not evidence. `tsc -b` caches, and has twice
reported clean on code that failed at runtime. Every change needs all three:

```bash
npx tsc -b --force --noEmit
npm run build
```

…plus a browser smoke test of the page you changed. Load it, use the thing you
altered, and check the console is clean.

Do not append code with a bash heredoc — one silently failed to write and the
cached typecheck did not notice. Write a patch script to the scratchpad and run
it, or use Edit.

## Motion

- **Use the tokens.** `--duration-instant|fast|base|slow` and
  `--ease-enter|exit|spring`. Do not write a bare `200ms` or a one-off
  cubic-bezier; if a value is missing, add it to `@theme`.
- **Animate `transform` and `opacity` only.** Anything touching width, height,
  top or left reflows the page every frame.
- **Every overlay needs an exit.** An enter-only animation makes a panel vanish
  on the frame it is dismissed, which is the single thing that most reads as
  unfinished.
- **Dialogs and drawers close through `useDeferredClose`.** Mounting only while
  open would otherwise unmount the Radix root before it can mark the content
  `data-state="closed"`, and no exit animation would ever run. The `EXIT_MS`
  constant in each primitive must match its CSS duration.
- **Never animate `transform` on an element centred with a transform.** A running
  animation replaces the whole property, so the centring is dropped mid-flight.
  `Dialog` centres with the `translate` property for exactly this reason.
- Motion is opt-out under `prefers-reduced-motion`. Mark the rare exception
  `data-motion="essential"` — currently only the spinner and the skeleton sweep,
  where stopping the motion would read as a hung page rather than a calm one.

## Styling

Tailwind 4, configured in CSS at `src/styles/theme.css`. Components reference
**roles** (`bg-surface`, `text-text-muted`, `border-border`), never raw colours,
so both themes stay correct by construction. Both themes define every role — if
you add one, add it to both.

`data-theme` on `<html>` is the source of truth, set before first paint by an
inline script in `index.html`.

## Current boundary

Every endpoint the backend implements has a frontend surface: assets, custody,
warranties, reference data, labels, agents, import/export, MFA self-service and
recovery sign-in, audit browsing, school settings, operational status, legacy
reconciliation, and agent enrollment tokens.

That means the next change is more likely to be a backend addition than a gap.
Add its contract to `src/api` first, then follow *Adding a screen*. Still no
placeholder UI for anything unimplemented.
