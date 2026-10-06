import type {
  AssetCondition,
  AssetStatus,
  Guid,
  IsoInstant,
  LocationSummary,
  ReferenceSummary,
} from './types';

/**
 * Stocktake contracts, hand-mirrored from `Inventory.Contracts.V1`.
 *
 * The rule that shapes every screen here: **a scan never changes the asset.**
 * Recording an observation writes it against the stocktake, not against the
 * register — the asset's location, condition and status are untouched. Applying
 * what was found is a separate, deliberate act. Any UI that blurs that would let
 * a walk round a cupboard silently rewrite the register.
 */

export const STOCKTAKE_SCOPES = ['all', 'location', 'category'] as const;
export type StocktakeScope = (typeof STOCKTAKE_SCOPES)[number];

export const STOCKTAKE_STATUSES = ['inProgress', 'completed', 'cancelled'] as const;
export type StocktakeStatus = (typeof STOCKTAKE_STATUSES)[number];

/** The views of a session's item list the server can produce. */
export const STOCKTAKE_ITEM_FILTERS = [
  'all',
  'seen',
  'missing',
  'unexpected',
  'wrongLocation',
  'conditionChanged',
] as const;
export type StocktakeItemFilter = (typeof STOCKTAKE_ITEM_FILTERS)[number];

/** Reference data as the stocktake reports it, including whether it is still active. */
export interface StocktakeReference {
  id: Guid;
  code: string;
  name: string;
  isActive: boolean;
}

/**
 * Where a session has got to.
 *
 * `expected` is what the scope said should be there; the other five are the ways
 * reality differed. They overlap — one asset can be both seen and in the wrong
 * place — so they must never be added together.
 */
export interface StocktakeProgress {
  expected: number;
  seen: number;
  missing: number;
  /** Scanned but not in the scope — it belongs somewhere else. */
  unexpected: number;
  wrongLocation: number;
  conditionChanged: number;
}

export interface StocktakeSessionListItem {
  id: Guid;
  name: string;
  scope: StocktakeScope;
  /** The location or category the scope names, already resolved by the server. */
  scopeDisplayName: string | null;
  status: StocktakeStatus;
  startedAtUtc: IsoInstant;
  startedByDisplayName: string;
  closedAtUtc: IsoInstant | null;
  progress: StocktakeProgress;
  version: Guid;
}

export interface StocktakeSession {
  id: Guid;
  name: string;
  scope: StocktakeScope;
  scopeLocation: StocktakeReference | null;
  scopeCategory: StocktakeReference | null;
  status: StocktakeStatus;
  startedAtUtc: IsoInstant;
  startedByUserId: Guid;
  startedByDisplayName: string;
  completedAtUtc: IsoInstant | null;
  completedByUserId: Guid | null;
  completedByDisplayName: string | null;
  cancelledAtUtc: IsoInstant | null;
  cancelledByUserId: Guid | null;
  cancelledByDisplayName: string | null;
  cancellationReason: string | null;
  progress: StocktakeProgress;
  version: Guid;
}

/**
 * One asset in a session, with what the register expected beside what was found.
 *
 * The `expected*` fields are the register's values and the `observed*` fields are
 * what the scan recorded. They are deliberately kept apart rather than merged —
 * the difference between them is the entire output of a stocktake.
 */
export interface StocktakeItem {
  assetId: Guid;
  assetTag: string;
  deviceName: string | null;
  category: ReferenceSummary;
  /** In the scope the session was started with. */
  isExpected: boolean;
  isSeen: boolean;
  expectedStatus: AssetStatus;
  expectedCondition: AssetCondition;
  expectedLocation: LocationSummary | null;
  observedLocation: LocationSummary | null;
  observedCondition: AssetCondition | null;
  note: string | null;
  observedAtUtc: IsoInstant | null;
  observedByUserId: Guid | null;
  observedByDisplayName: string | null;
  /** Found here but not expected here. */
  isUnexpected: boolean;
  isWrongLocation: boolean;
  isConditionChanged: boolean;
}

export interface StartStocktakeRequest {
  name: string;
  scope: StocktakeScope;
  /** Required for a `location` scope, ignored otherwise. */
  locationId?: Guid | null;
  /** Required for a `category` scope, ignored otherwise. */
  categoryId?: Guid | null;
}

/**
 * Records that an asset was seen.
 *
 * Identify it by `assetId` or by `assetTag` — the tag is what a scanner or a
 * typed entry produces, so the caller does not have to resolve it first.
 * `version` is the *session* version, so two people scanning the same session
 * cannot silently overwrite each other's progress.
 */
export interface RecordStocktakeObservationRequest {
  assetId?: Guid | null;
  assetTag?: string | null;
  observedLocationId: Guid;
  /**
   * Required. Every sighting asserts a condition — there is no "leave it as
   * recorded" option, so the scan screen keeps this set for the run rather than
   * asking on each item.
   */
  observedCondition: AssetCondition;
  note?: string | null;
  version: Guid;
}

export interface StocktakeObservation {
  session: StocktakeSession;
  item: StocktakeItem;
  /** False when the same asset was already recorded with the same values. */
  changed: boolean;
}

export interface StocktakeActionRequest {
  version: Guid;
}

export interface CancelStocktakeRequest {
  version: Guid;
  reason: string;
}

export interface StocktakeSearchParams {
  status?: StocktakeStatus;
  page?: number;
  pageSize?: number;
}
