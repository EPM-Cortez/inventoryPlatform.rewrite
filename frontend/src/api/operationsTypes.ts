import type {
  AssetCondition,
  AssetStatus,
  Guid,
  IsoDate,
  IsoInstant,
} from './types';

/**
 * Bulk changes, leaver cases, attachments and bundles — hand-mirrored from
 * `Inventory.Contracts.V1`.
 */

// ---------------------------------------------------------------------------
// Bulk changes
// ---------------------------------------------------------------------------

export const BULK_OPERATIONS = [
  'setLocation',
  'setCategory',
  'setCondition',
  'setStatus',
  'addNote',
] as const;
export type BulkOperationKind = (typeof BULK_OPERATIONS)[number];

export type BulkJobStatus = 'previewed' | 'committed';

/**
 * Each asset is selected *with the version it was read at*.
 *
 * The whole batch is rejected if any one of them has moved on since, which is
 * the point: a partial bulk change is far worse than none, because nobody can
 * tell afterwards which half applied.
 */
export interface BulkChangeSelection {
  assetId: Guid;
  version: Guid;
}

export interface BulkChangeOperation {
  kind: BulkOperationKind;
  locationId?: Guid | null;
  /** For `setLocation`: removes the location instead of setting one. */
  clearLocation?: boolean;
  categoryId?: Guid | null;
  condition?: AssetCondition | null;
  status?: AssetStatus | null;
  note?: string | null;
}

export interface PreviewBulkChangeRequest {
  assets: BulkChangeSelection[];
  operation: BulkChangeOperation;
  reason: string;
}

/** What one asset would become. `isNoOp` means it already has the target value. */
export interface BulkChangePreviewItem {
  assetId: Guid;
  assetTag: string;
  version: Guid;
  field: string;
  from: string | null;
  to: string | null;
  isNoOp: boolean;
}

/**
 * A previewed batch, valid until `expiresAtUtc`.
 *
 * `fingerprint` must be quoted back on commit — it is how the server proves the
 * batch being applied is the one that was shown, not a different plan produced
 * in the meantime.
 */
export interface BulkChangePreview {
  jobId: Guid;
  status: BulkJobStatus;
  operation: BulkChangeOperation;
  reason: string;
  assetCount: number;
  items: BulkChangePreviewItem[];
  fingerprint: string;
  createdAtUtc: IsoInstant;
  expiresAtUtc: IsoInstant;
  committedAtUtc: IsoInstant | null;
  version: Guid;
}

export interface CommitBulkChangeRequest {
  fingerprint: string;
  version: Guid;
}

export interface BulkChangeCommitItem {
  assetId: Guid;
  assetTag: string;
  /** The new version, since every committed asset has moved on. */
  version: Guid;
}

export interface BulkChangeCommitResult {
  jobId: Guid;
  status: BulkJobStatus;
  assetCount: number;
  committedAtUtc: IsoInstant;
  assets: BulkChangeCommitItem[];
  version: Guid;
}

// ---------------------------------------------------------------------------
// Leaver cases
// ---------------------------------------------------------------------------

export const LEAVER_STATUSES = ['open', 'completed', 'cancelled', 'rehired'] as const;
export type LeaverStatus = (typeof LEAVER_STATUSES)[number];

/** One item the leaver still holds. Carries both versions so it can be returned. */
export interface LeaverCustodyAsset {
  assetId: Guid;
  assetTag: string;
  deviceName: string | null;
  assignmentId: Guid;
  assignedAtUtc: IsoInstant;
  expectedReturnOn: IsoDate | null;
  isOverdue: boolean;
  assignmentVersion: Guid;
  assetVersion: Guid;
}

export interface LeaverCaseSummary {
  id: Guid;
  personId: Guid;
  personExternalId: string | null;
  personDisplayName: string;
  personIsActive: boolean;
  leavingOn: IsoDate;
  status: LeaverStatus;
  currentAssetCount: number;
  overdueAssetCount: number;
  version: Guid;
}

/**
 * A leaver case.
 *
 * Opening one **returns nothing** — it records that someone is going and gathers
 * what they hold. Completion is blocked until custody is cleared, so the case is
 * a checklist rather than an action.
 */
export interface LeaverCase {
  id: Guid;
  personId: Guid;
  personExternalId: string | null;
  personDisplayName: string;
  personIsActive: boolean;
  leavingOn: IsoDate;
  notes: string | null;
  status: LeaverStatus;
  /** Embedded, capped at 100 — see `currentAssetsTruncated`. */
  currentAssets: LeaverCustodyAsset[];
  currentAssetCount: number;
  currentAssetsTruncated: boolean;
  createdAtUtc: IsoInstant;
  createdByUserId: Guid;
  createdByDisplayName: string;
  completedAtUtc: IsoInstant | null;
  completedByUserId: Guid | null;
  completedByDisplayName: string | null;
  cancelledAtUtc: IsoInstant | null;
  cancelledByUserId: Guid | null;
  cancelledByDisplayName: string | null;
  version: Guid;
}

export interface CreateLeaverCaseRequest {
  personId: Guid;
  leavingOn: IsoDate;
  notes?: string | null;
}

export interface UpdateLeaverCaseRequest {
  leavingOn: IsoDate;
  notes?: string | null;
  version: Guid;
}

export interface ChangeLeaverCaseStatusRequest {
  version: Guid;
}

export interface LeaverSearchParams {
  q?: string;
  status?: LeaverStatus;
  page?: number;
  pageSize?: number;
}

// ---------------------------------------------------------------------------
// Attachments
// ---------------------------------------------------------------------------

export const ATTACHMENT_CATEGORIES = [
  'invoice',
  'warrantyDocument',
  'disposalCertificate',
  'photo',
  'other',
] as const;
export type AttachmentCategory = (typeof ATTACHMENT_CATEGORIES)[number];

/** PDF, JPEG and PNG only, up to 10 MiB. Enforced by the server. */
export const ATTACHMENT_MAX_BYTES = 10 * 1024 * 1024;
export const ATTACHMENT_ACCEPT = 'application/pdf,image/jpeg,image/png';

export interface AssetAttachment {
  id: Guid;
  assetId: Guid;
  category: AttachmentCategory;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  /** Lets a download be checked against what was stored. */
  sha256: string;
  createdAtUtc: IsoInstant;
  createdByUserId: Guid;
  createdByDisplayName: string;
  /** Archived rather than deleted — there is no hard delete. */
  archivedAtUtc: IsoInstant | null;
  archivedByUserId: Guid | null;
  archivedByDisplayName: string | null;
  version: Guid;
}

export interface ArchiveAttachmentRequest {
  version: Guid;
}

// ---------------------------------------------------------------------------
// Bundles
// ---------------------------------------------------------------------------

export const BUNDLE_ROLES = ['laptop', 'charger', 'dock', 'case', 'other'] as const;
export type BundleRole = (typeof BUNDLE_ROLES)[number];

export interface BundleMember {
  memberId: Guid;
  assetId: Guid;
  assetTag: string;
  role: BundleRole;
  /** Free text, only meaningful when `role` is `other`. */
  otherRole: string | null;
  addedAtUtc: IsoInstant;
}

export interface BundleMemberRequest {
  assetId: Guid;
  role: BundleRole;
  otherRole?: string | null;
}

export interface AssetBundleListItem {
  id: Guid;
  code: string;
  name: string;
  description: string | null;
  memberCount: number;
  isArchived: boolean;
  updatedAtUtc: IsoInstant;
  version: Guid;
}

/**
 * A kit that travels together — laptop, charger, dock.
 *
 * Members stay independent assets: a bundle groups them for issuing and
 * checking, it does not merge them into one record.
 */
export interface AssetBundle {
  id: Guid;
  code: string;
  name: string;
  description: string | null;
  members: BundleMember[];
  createdAtUtc: IsoInstant;
  updatedAtUtc: IsoInstant;
  archivedAtUtc: IsoInstant | null;
  isArchived: boolean;
  version: Guid;
}

export interface CreateAssetBundleRequest {
  code: string;
  name: string;
  description?: string | null;
  members: BundleMemberRequest[];
}

/** The member list is **replaced**, not merged — send the complete set. */
export interface UpdateAssetBundleRequest extends CreateAssetBundleRequest {
  version: Guid;
}

export interface BundleSearchParams {
  includeArchived?: boolean;
  page?: number;
  pageSize?: number;
}
