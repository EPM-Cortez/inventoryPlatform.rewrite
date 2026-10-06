import type { AssetImportMode, AssetImportStatus, Guid, IsoInstant } from './types';

/**
 * Contracts for the administrative surfaces: MFA, audit, school settings,
 * operational status and legacy reconciliation.
 *
 * Hand-mirrored from `Inventory.Contracts.V1`, same rules as `types.ts`.
 */

// ---------------------------------------------------------------------------
// Multi-factor authentication
// ---------------------------------------------------------------------------

export interface MfaStatus {
  enabled: boolean;
  /** Policy says this account must enrol before it can keep working. */
  enrollmentRequired: boolean;
  recoveryCodesRemaining: number;
}

/**
 * Returned when enrollment begins.
 *
 * `sharedKey` is the TOTP secret in a form a person can type; `authenticatorUri`
 * is the same secret as an `otpauth://` URL for a QR code. Both are the *same
 * credential* — showing them is showing a secret, so this is never persisted or
 * logged and the UI must not leave it lying around after confirmation.
 */
export interface MfaEnrollment {
  sharedKey: string;
  authenticatorUri: string;
}

/** Shown once. The server keeps only hashes, so they cannot be re-displayed. */
export interface MfaRecoveryCodes {
  recoveryCodes: string[];
  recoveryCodesRemaining: number;
}

export interface MfaRecoveryLoginRequest {
  recoveryCode: string;
  rememberMe: boolean;
}

export interface BeginMfaEnrollmentRequest {
  currentPassword: string;
}

export interface ConfirmMfaEnrollmentRequest {
  code: string;
}

export interface RegenerateMfaRecoveryCodesRequest {
  currentPassword: string;
  code: string;
}

// ---------------------------------------------------------------------------
// Audit
// ---------------------------------------------------------------------------

export interface AuditLogEntry {
  id: Guid;
  /** Absent for machine or anonymous actions. */
  actorUserId: Guid | null;
  actorDisplayName: string | null;
  action: string;
  targetType: string;
  targetId: string | null;
  occurredAtUtc: IsoInstant;
  outcome: string;
  /** Raw JSON string; shape varies by action. */
  detailsJson: string | null;
  ipAddress: string | null;
  client: string | null;
  correlationId: string | null;
}

/**
 * The distinct values present in the audit trail, for the filter dropdowns.
 *
 * Server-derived, so it grows as new kinds of thing happen and never offers a
 * value with no rows behind it. Filters still accept the pre-canonicalisation
 * spellings, so an old bookmarked URL keeps working even though the old value
 * no longer appears here.
 */
export interface AuditFilterVocabulary {
  actions: string[];
  targetTypes: string[];
  outcomes: string[];
  /** The server capped the list. Say so rather than implying it is exhaustive. */
  truncated: boolean;
}

export interface AuditSearchParams {
  action?: string;
  targetType?: string;
  targetId?: string;
  actorUserId?: Guid;
  outcome?: string;
  from?: IsoInstant;
  to?: IsoInstant;
  page?: number;
  pageSize?: number;
}

// ---------------------------------------------------------------------------
// School settings
// ---------------------------------------------------------------------------

export interface SchoolSettings {
  name: string;
  code: string;
  timeZone: string;
  /** Base of every QR payload. Changing it invalidates printed labels. */
  qrBaseUrl: string;
  warrantyWarningDays: number;
  /** False while the app is still running on configuration defaults. */
  isPersisted: boolean;
  version: Guid | null;
}

export interface UpdateSchoolSettingsRequest {
  name: string;
  code: string;
  timeZone: string;
  qrBaseUrl: string;
  warrantyWarningDays: number;
  /** Null on the first save, when nothing has been persisted yet. */
  version: Guid | null;
}

// ---------------------------------------------------------------------------
// Operational status
// ---------------------------------------------------------------------------

export interface OperationalMetrics {
  startedAtUtc: IsoInstant;
  uptimeSeconds: number;
  failedLogins: number;
  failedMfaChallenges: number;
  importRejections: number;
  qrFailures: number;
  sqliteContentionEvents: number;
  readinessFailures: number;
}

export interface OperationalStorage {
  configured: boolean;
  availableFreeSpaceBytes: number | null;
  latestBackupAtUtc: IsoInstant | null;
  latestBackupAgeHours: number | null;
}

/** Severity is server-authored free text; treat unknown values as informational. */
export interface OperationalAlert {
  severity: string;
  code: string;
  message: string;
}

export interface OperationalStatus {
  metrics: OperationalMetrics;
  storage: OperationalStorage;
  alerts: OperationalAlert[];
}

// ---------------------------------------------------------------------------
// Legacy reconciliation
//
// A dry run compares a legacy export against the current register and produces a
// fingerprinted report. Signing it off records an authenticated decision — it
// never bypasses import validation.
// ---------------------------------------------------------------------------

export type LegacyReconciliationDecision = 'approved' | 'rejected';

export interface LegacyReconciliationCounts {
  sourceRows: number;
  validRows: number;
  errorRows: number;
  plannedCreates: number;
  plannedUpdates: number;
  targetAssetsBefore: number;
  expectedTargetAssetsAfter: number;
}

export interface LegacyReconciliationMappingSummary {
  generatedAssetTags: number;
  plannedCategoryCreates: number;
  plannedLocationCreates: number;
  inferredDateRows: number;
  fuzzyPersonMatches: number;
  unassignedImports: number;
}

export interface LegacyReconciliationIssue {
  /** Stable 64-char hex identity, used when acknowledging an exception. */
  issueKey: string;
  rowNumber: number;
  column: string;
  code: string;
  classification: string;
  severity: string;
  message: string;
  value: string | null;
}

export interface LegacyFieldComparison {
  field: string;
  source: string | null;
  target: string | null;
  matches: boolean;
}

export interface LegacyReconciliationSample {
  rowNumber: number;
  action: 'create' | 'update';
  assetTag: string;
  targetExists: boolean;
  outcome: string;
  comparisons: LegacyFieldComparison[];
}

export interface LegacyReconciliationReport {
  formatVersion: number;
  importJobId: Guid;
  importJobVersion: Guid;
  sourceFileName: string;
  sourceFileHash: string;
  mode: AssetImportMode;
  importStatus: AssetImportStatus;
  createdAtUtc: IsoInstant;
  counts: LegacyReconciliationCounts;
  mappings: LegacyReconciliationMappingSummary;
  issues: LegacyReconciliationIssue[];
  duplicateIssueCount: number;
  unmappedReferenceIssueCount: number;
  samples: LegacyReconciliationSample[];
  samplesTruncated: boolean;
  /**
   * Hash of the report contents. Sign-off must quote it, so a report that
   * changed after it was read cannot be signed by mistake.
   */
  reportFingerprint: string;
}

export interface LegacySignedIssueAcknowledgement {
  issueKey: string;
  rowNumber: number;
  column: string;
  code: string;
  rationale: string;
}

export interface LegacyReconciliationSignOff {
  id: Guid;
  decision: LegacyReconciliationDecision;
  reportFingerprint: string;
  signedExceptions: LegacySignedIssueAcknowledgement[];
  notes: string;
  signedAtUtc: IsoInstant;
  signedByUserId: Guid;
  signedByDisplayName: string;
}

export interface LegacyReconciliation {
  report: LegacyReconciliationReport;
  signOff: LegacyReconciliationSignOff | null;
  cutoverReady: boolean;
}

export interface LegacyExceptionAcknowledgementRequest {
  issueKey: string;
  rationale: string;
}

export interface SignOffLegacyReconciliationRequest {
  decision: LegacyReconciliationDecision;
  reportFingerprint: string;
  notes: string;
  exceptions: LegacyExceptionAcknowledgementRequest[];
}

export interface LegacyCutoverPackage {
  formatVersion: number;
  importJobId: Guid;
  importJobVersion: Guid;
  sourceFileName: string;
  sourceFileHash: string;
  reportFingerprint: string;
  packageFingerprint: string;
  mode: AssetImportMode;
  importStatus: AssetImportStatus;
  counts: LegacyReconciliationCounts;
  signOff: LegacyReconciliationSignOff | null;
  cutoverReady: boolean;
  blockingReasons: string[];
  cutoverProcedure: string[];
  rollbackProcedure: string[];
}
