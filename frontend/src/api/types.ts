/**
 * Hand-mirrored from `Inventory.Contracts.V1` in ../backend.
 *
 * These types are the contract boundary. If the backend changes a shape, change it
 * here first and let the type errors show you every call site. Do not widen a type
 * to silence an error — the API is the source of truth.
 *
 * Serialisation rules the API guarantees (docs/api/conventions.md):
 *   - property names are camel-case
 *   - enums are camel-case strings, never integers
 *   - `Guid`           -> string
 *   - `DateTimeOffset` -> ISO 8601 UTC string
 *   - `DateOnly`       -> 'YYYY-MM-DD'
 *   - money is decimal in the contract, integer minor units in the database
 */

/** ISO 8601 instant, e.g. `2026-07-25T09:14:22.117Z`. */
export type IsoInstant = string;
/** Calendar date with no time or zone, e.g. `2026-07-25`. */
export type IsoDate = string;
export type Guid = string;

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export const ASSET_STATUSES = [
  'inStock',
  'reserved',
  'assigned',
  'repair',
  'lost',
  'stolen',
  'retired',
  'disposed',
] as const;
export type AssetStatus = (typeof ASSET_STATUSES)[number];

export const ASSET_CONDITIONS = [
  'new',
  'good',
  'fair',
  'damaged',
  'unserviceable',
] as const;
export type AssetCondition = (typeof ASSET_CONDITIONS)[number];

export const WARRANTY_STATES = [
  'notRecorded',
  'future',
  'active',
  'expiringSoon',
  'expired',
] as const;
export type WarrantyState = (typeof WARRANTY_STATES)[number];

export const PERSON_TYPES = ['staff', 'pupil', 'contractor', 'other'] as const;
export type PersonType = (typeof PERSON_TYPES)[number];

export type AssignmentTargetType = 'person' | 'department';

export type QrImageFormat = 'svg' | 'png';

// ---------------------------------------------------------------------------
// Common
// ---------------------------------------------------------------------------

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface VersionRequest {
  version: Guid;
}

/** Server-enforced ceiling from `docs/api/conventions.md`. */
export const MAX_PAGE_SIZE = 200;

// ---------------------------------------------------------------------------
// Reference data
// ---------------------------------------------------------------------------

export interface ReferenceSummary {
  id: Guid;
  code: string;
  name: string;
}

export interface LocationSummary {
  id: Guid;
  code: string;
  name: string;
}

export interface Category {
  id: Guid;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  version: Guid;
}

export interface CreateCategoryRequest {
  code: string;
  name: string;
  description?: string | null;
}

export interface UpdateCategoryRequest extends CreateCategoryRequest {
  isActive: boolean;
  version: Guid;
}

export interface Location {
  id: Guid;
  code: string;
  name: string;
  type: string | null;
  parentLocationId: Guid | null;
  isActive: boolean;
  version: Guid;
}

export interface CreateLocationRequest {
  code: string;
  name: string;
  type?: string | null;
  parentLocationId?: Guid | null;
}

export interface UpdateLocationRequest extends CreateLocationRequest {
  isActive: boolean;
  version: Guid;
}

// ---------------------------------------------------------------------------
// Assets
// ---------------------------------------------------------------------------

export interface WarrantySummary {
  state: WarrantyState;
  provider: string | null;
  endsOn: IsoDate | null;
  daysRemaining: number | null;
}

export interface AssignmentTarget {
  type: AssignmentTargetType;
  id: Guid;
  displayName: string;
  reference: string | null;
}

export interface CurrentAssignment {
  assignmentId: Guid;
  target: AssignmentTarget;
  assignedAtUtc: IsoInstant;
  version: Guid;
}

export interface AssetListItem {
  id: Guid;
  assetTag: string;
  /** Hostname or asset-management name, distinct from the printed tag. */
  deviceName: string | null;
  category: ReferenceSummary;
  description: string | null;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  /** Wired NIC address, at most 17 characters (`AA:BB:CC:DD:EE:FF`). */
  lanMacAddress: string | null;
  /** SMBIOS system UUID. A guid, not free text. */
  biosGuid: Guid | null;
  ownership: string | null;
  status: AssetStatus;
  condition: AssetCondition;
  location: LocationSummary | null;
  currentAssignment: CurrentAssignment | null;
  warrantySummary: WarrantySummary;
  operatingSystem: string | null;
  operatingSystemVersion: string | null;
  /** Tri-state: unknown when the estate has not been assessed. */
  windows11Compliant: boolean | null;
  qrCodeUrl: string;
  updatedAtUtc: IsoInstant;
  version: Guid;
}

export interface Asset {
  id: Guid;
  assetTag: string;
  deviceName: string | null;
  category: ReferenceSummary;
  description: string | null;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  /** Source value from an import that could not be verified as unique. */
  reportedSerialNumber: string | null;
  lanMacAddress: string | null;
  biosGuid: Guid | null;
  ownership: string | null;
  status: AssetStatus;
  condition: AssetCondition;
  location: LocationSummary | null;
  currentAssignment: CurrentAssignment | null;
  warrantySummary: WarrantySummary;
  operatingSystem: string | null;
  operatingSystemVersion: string | null;
  windows11Compliant: boolean | null;
  qrCodeUrl: string;
  purchaseDate: IsoDate | null;
  purchaseCost: number | null;
  currency: string | null;
  supplier: string | null;
  notes: string | null;
  legacyId: string | null;
  createdAtUtc: IsoInstant;
  updatedAtUtc: IsoInstant;
  retiredAtUtc: IsoInstant | null;
  retirementReason: string | null;
  /** Disposal is terminal — a disposed asset cannot be restored. */
  disposedAtUtc: IsoInstant | null;
  disposalReason: string | null;
  version: Guid;
}

/**
 * Everything about an asset except where its tag comes from.
 *
 * Split out because create can have the server issue a tag and update cannot —
 * an existing asset is never silently renamed.
 */
export interface AssetFields {
  deviceName?: string | null;
  categoryId: Guid;
  description?: string | null;
  manufacturer?: string | null;
  model?: string | null;
  serialNumber?: string | null;
  lanMacAddress?: string | null;
  biosGuid?: Guid | null;
  ownership?: string | null;
  status: AssetStatus;
  condition: AssetCondition;
  locationId?: Guid | null;
  purchaseDate?: IsoDate | null;
  purchaseCost?: number | null;
  currency?: string | null;
  supplier?: string | null;
  operatingSystem?: string | null;
  operatingSystemVersion?: string | null;
  windows11Compliant?: boolean | null;
  notes?: string | null;
  legacyId?: string | null;
}

/**
 * The tag must come from exactly one place.
 *
 * The server rejects both a supplied tag *and* `generateAssetTag: true`, and
 * rejects neither, with `asset_tag_source_invalid`. Modelling it as a union means
 * an impossible request does not typecheck, rather than failing at runtime.
 */
export type AssetTagSource =
  | { assetTag: string; generateAssetTag?: false }
  | { assetTag?: null; generateAssetTag: true };

export type CreateAssetRequest = AssetFields & AssetTagSource;

/** Updates always carry the existing tag; there is no generating on an edit. */
export interface UpdateAssetRequest extends AssetFields {
  assetTag: string;
  version: Guid;
}

export interface RetireAssetRequest {
  version: Guid;
  reason: string;
}

/**
 * Disposal is the end of an asset's life: unlike retirement it cannot be
 * restored, so the UI must treat it as irreversible.
 */
export interface DisposeAssetRequest {
  version: Guid;
  reason: string;
}

export interface AddAssetNoteRequest {
  note: string;
}

/** Append-only history entry. `details` is an open object shaped by `type`. */
export interface AssetEvent {
  id: Guid;
  type: string;
  occurredAtUtc: IsoInstant;
  actorUserId: Guid;
  actorDisplayName: string;
  source: string;
  summary: string;
  details: Record<string, unknown>;
  correlationId: string | null;
}

/**
 * Server-supported sort keys for `GET /api/v1/assets`. A `-` prefix reverses.
 * Anything else is rejected with 400, so the UI must not invent its own.
 */
export const ASSET_SORTS = [
  'assetTag',
  '-assetTag',
  'updatedAtUtc',
  '-updatedAtUtc',
  'status',
  '-status',
] as const;
export type AssetSort = (typeof ASSET_SORTS)[number];

export interface AssetSearchParams {
  q?: string;
  assetTag?: string;
  serialNumber?: string;
  categoryId?: Guid;
  status?: AssetStatus;
  condition?: AssetCondition;
  locationId?: Guid;
  departmentId?: Guid;
  assignedPersonId?: Guid;
  assigned?: boolean;
  warrantyState?: WarrantyState;
  warrantyEndsBefore?: IsoDate;
  createdFrom?: IsoInstant;
  createdTo?: IsoInstant;
  legacyId?: string;
  page?: number;
  pageSize?: number;
  sort?: AssetSort;
}

// ---------------------------------------------------------------------------
// Assignments (custody)
// ---------------------------------------------------------------------------

export interface AssetAssignment {
  id: Guid;
  assetId: Guid;
  target: AssignmentTarget;
  assignedAtUtc: IsoInstant;
  assignedByUserId: Guid;
  assignedByDisplayName: string;
  assignedCondition: AssetCondition;
  assignedLocation: LocationSummary | null;
  assignmentNotes: string | null;
  returnedAtUtc: IsoInstant | null;
  returnedByUserId: Guid | null;
  returnedByDisplayName: string | null;
  returnCondition: AssetCondition | null;
  returnLocation: LocationSummary | null;
  returnNotes: string | null;
  /**
   * When the holder is due to give it back. A school-local calendar date, not an
   * instant — "due back on the 12th" does not shift with a time zone.
   */
  expectedReturnOn: IsoDate | null;
  /** Server-computed against the school time zone, so it agrees with the list. */
  isOverdue: boolean;
  version: Guid;
}

/**
 * Assign/transfer/return all return the asset's *new* state alongside the
 * assignment, because the operation mutates both in one transaction. Always feed
 * `assetVersion` back into the cache — the previously held version is now stale.
 */
export interface AssignmentWorkflowResult {
  assignment: AssetAssignment;
  assetStatus: AssetStatus;
  assetCondition: AssetCondition;
  assetLocation: LocationSummary | null;
  assetVersion: Guid;
}

/** Supply exactly one of `personId` or `departmentId`. */
export interface AssignAssetRequest {
  personId?: Guid | null;
  departmentId?: Guid | null;
  assetVersion: Guid;
  notes?: string | null;
  /** Optional. Set it to make the loan temporary and trackable as overdue. */
  expectedReturnOn?: IsoDate | null;
}

/** Versioned on the *assignment*, not the asset. Null clears the due date. */
export interface UpdateExpectedReturnRequest {
  expectedReturnOn: IsoDate | null;
  assignmentVersion: Guid;
}

/** A loan past its due date. Carries both versions so it can be acted on inline. */
export interface OverdueLoan {
  assetId: Guid;
  assetTag: string;
  deviceName: string | null;
  assignmentId: Guid;
  target: AssignmentTarget;
  assignedAtUtc: IsoInstant;
  expectedReturnOn: IsoDate;
  daysOverdue: number;
  assignmentVersion: Guid;
  assetVersion: Guid;
}

export interface OverdueLoanSearchParams {
  personId?: Guid;
  departmentId?: Guid;
  page?: number;
  pageSize?: number;
}

/**
 * How new asset tags are issued.
 *
 * Changing the policy affects only tags issued from now on — nothing already on
 * the register is renamed, and the labels stuck to equipment stay valid.
 */
/**
 * A stored asset filter.
 *
 * Deliberately not `AssetSearchParams`: the API names the free-text field
 * `query` here and `q` on the list endpoint, and a saved search has no page.
 * `savedSearchFilter.ts` owns the translation.
 */
export interface SavedAssetFilter {
  query?: string | null;
  assetTag?: string | null;
  serialNumber?: string | null;
  categoryId?: Guid | null;
  status?: AssetStatus | null;
  condition?: AssetCondition | null;
  locationId?: Guid | null;
  departmentId?: Guid | null;
  assignedPersonId?: Guid | null;
  assigned?: boolean | null;
  warrantyState?: WarrantyState | null;
  warrantyEndsBefore?: IsoDate | null;
  createdFrom?: IsoInstant | null;
  createdTo?: IsoInstant | null;
  legacyId?: string | null;
  pageSize?: number;
  sort?: AssetSort;
}

/** The only schema version the server accepts today. */
export const SAVED_SEARCH_SCHEMA_VERSION = 1;

export interface SavedAssetSearch {
  id: Guid;
  name: string;
  schemaVersion: number;
  filter: SavedAssetFilter;
  /** Applied automatically when the assets page is opened with no filters. */
  isDefault: boolean;
  createdAtUtc: IsoInstant;
  updatedAtUtc: IsoInstant;
  version: Guid;
}

export interface CreateSavedAssetSearchRequest {
  name: string;
  schemaVersion: number;
  filter: SavedAssetFilter;
}

export interface UpdateSavedAssetSearchRequest extends CreateSavedAssetSearchRequest {
  version: Guid;
}

/** Setting a default and archiving both only need the version. */
export interface ChangeSavedAssetSearchRequest {
  version: Guid;
}

export interface AssetTagPolicy {
  prefix: string;
  /** Digits the number is padded to, 2 to 12. */
  numericWidth: number;
  nextNumber: number;
  /** What the next generated tag will actually look like. */
  nextAssetTag: string;
  version: Guid;
}

export interface UpdateAssetTagPolicyRequest {
  prefix: string;
  numericWidth: number;
  nextNumber: number;
  version: Guid;
}

export interface ReturnAssetRequest {
  assetVersion: Guid;
  assignmentVersion: Guid;
  condition?: AssetCondition | null;
  locationId?: Guid | null;
  notes?: string | null;
}

export interface HolderAsset {
  assetId: Guid;
  assetTag: string;
  status: AssetStatus;
  condition: AssetCondition;
  location: LocationSummary | null;
  assetVersion: Guid;
  assignment: AssetAssignment;
}

// ---------------------------------------------------------------------------
// People and departments
// ---------------------------------------------------------------------------

export interface Person {
  id: Guid;
  externalId: string | null;
  displayName: string;
  type: PersonType;
  email: string | null;
  isActive: boolean;
  version: Guid;
}

export interface CreatePersonRequest {
  externalId?: string | null;
  displayName: string;
  type: PersonType;
  email?: string | null;
}

export interface UpdatePersonRequest extends CreatePersonRequest {
  isActive: boolean;
  version: Guid;
}

export interface Department {
  id: Guid;
  code: string;
  name: string;
  isActive: boolean;
  version: Guid;
}

export interface CreateDepartmentRequest {
  code: string;
  name: string;
}

export interface UpdateDepartmentRequest extends CreateDepartmentRequest {
  isActive: boolean;
  version: Guid;
}

// ---------------------------------------------------------------------------
// Warranties
// ---------------------------------------------------------------------------

export interface Warranty {
  id: Guid;
  assetId: Guid;
  provider: string;
  coverageType: string;
  startDate: IsoDate | null;
  endDate: IsoDate | null;
  reference: string | null;
  notes: string | null;
  isPrimary: boolean;
  state: WarrantyState;
  daysRemaining: number | null;
  createdAtUtc: IsoInstant;
  updatedAtUtc: IsoInstant;
  archivedAtUtc: IsoInstant | null;
  version: Guid;
}

export interface CreateWarrantyRequest {
  provider: string;
  coverageType: string;
  startDate?: IsoDate | null;
  endDate?: IsoDate | null;
  reference?: string | null;
  notes?: string | null;
  isPrimary: boolean;
}

export interface UpdateWarrantyRequest extends CreateWarrantyRequest {
  version: Guid;
}

// ---------------------------------------------------------------------------
// QR identifiers
// ---------------------------------------------------------------------------

/**
 * `payloadUrl` is the value encoded in the label. Treat it as a secret-ish
 * capability URL: keep it inside the label workflow, never in logs or exports.
 */
export interface QrIdentifier {
  id: Guid;
  assetId: Guid;
  tokenVersion: number;
  issuedAtUtc: IsoInstant;
  payloadUrl: string;
  imageUrl: string;
}

/** Deliberately narrow: no serial, purchase, holder or token fields. */
export interface QrResolution {
  id: Guid;
  assetTag: string;
  category: ReferenceSummary;
  status: AssetStatus;
  condition: AssetCondition;
  location: LocationSummary | null;
  warrantySummary: WarrantySummary;
}

// ---------------------------------------------------------------------------
// CSV import and export
// ---------------------------------------------------------------------------

/** `createOnly` rejects rows whose asset tag already exists; upsert updates them. */
export const ASSET_IMPORT_MODES = ['createOnly', 'upsertByAssetTag'] as const;
export type AssetImportMode = (typeof ASSET_IMPORT_MODES)[number];

export type AssetImportStatus = 'validationFailed' | 'readyToCommit' | 'committed';

export type AssetImportAction = 'create' | 'update';

/**
 * How a row's custody target was resolved.
 *
 * - `none` — the row named nobody.
 * - `exactPerson` — the external ID matched exactly.
 * - `fuzzyPerson` — **a guess**. Accepted only when exactly one active person is
 *   within a single edit, both values are at least six characters, and the first
 *   two characters are unchanged. `Schappel` resolves to `schappell`; `Tgreen`
 *   is never allowed to become `Jgreen`.
 * - `department` — resolved to an active department instead.
 * - `unassigned` — the row named someone, but they were missing, inactive or
 *   ambiguous. The asset still imports, forced to `inStock` with no custody
 *   record. It is a silent loss of the intended owner, so the UI must say so.
 */
export const ASSET_IMPORT_ASSIGNMENT_RESOLUTIONS = [
  'none',
  'exactPerson',
  'fuzzyPerson',
  'department',
  'unassigned',
] as const;
export type AssetImportAssignmentResolution =
  (typeof ASSET_IMPORT_ASSIGNMENT_RESOLUTIONS)[number];

export type AssetImportSerialResolution =
  | 'missing'
  | 'verified'
  | 'reportedDuplicate'
  | 'ignoredPlaceholder';

export interface AssetImportError {
  rowNumber: number;
  column: string;
  code: string;
  message: string;
  value: string | null;
}

export interface AssetImportPreviewRow {
  rowNumber: number;
  action: AssetImportAction;
  assetTag: string;
  /**
   * True when the row had no asset tag and the server minted one, e.g.
   * `STR-AUTO-D99F9EAF8AF3927E-0002`.
   *
   * The value is derived from the file content and the source row, so the same
   * file always yields the same tags — which is what makes a later
   * `upsertByAssetTag` re-import of the corrected file line up with what this
   * import created.
   */
  assetTagGenerated: boolean;
  /** The category text exactly as it appeared in the file. */
  category: string;
  /** The code it resolved to — an existing category, or one about to be made. */
  resolvedCategoryCode: string;
  /**
   * True when no category matched and committing will create one. An import can
   * therefore add reference data as a side effect, which the UI must say plainly
   * before the user commits.
   */
  categoryWillBeCreated: boolean;
  status: AssetStatus;
  condition: AssetCondition;
  /** The location text exactly as it appeared in the file. */
  locationCode: string | null;
  resolvedLocationCode: string | null;
  locationWillBeCreated: boolean;
  /** The person or department text exactly as it appeared in the file. */
  assignmentSource: string | null;
  assignmentResolution: AssetImportAssignmentResolution;
  /** Who it resolved to, or null when nothing was matched. */
  assignedTo: string | null;
  /**
   * Dates after normalisation. The parser accepts year-only, `MM/yyyy`,
   * `MM.yyyy` and legacy Excel serial numbers, so what lands in the register can
   * differ from what the file literally said.
   */
  purchaseDate: IsoDate | null;
  warrantyStartDate: IsoDate | null;
  warrantyEndDate: IsoDate | null;
  /**
   * Field names whose date was inferred rather than read exactly — a year-only
   * value becomes 1 January for start dates and 31 December for end dates, which
   * is a guess worth showing the user.
   */
  inferredDateFields: string[];
  sourceSerialNumber: string | null;
  serialResolution: AssetImportSerialResolution;
}

/**
 * A validated-but-not-yet-applied import. Validation is a dry run: nothing
 * changes until `commit`, and commit re-checks the snapshot and takes the
 * job's `version` so two people cannot apply the same file twice.
 */
export interface AssetImportJob {
  id: Guid;
  schemaVersion: number;
  originalFileName: string;
  fileHash: string;
  mode: AssetImportMode;
  status: AssetImportStatus;
  totalRows: number;
  validRows: number;
  errorRows: number;
  createRows: number;
  updateRows: number;
  /** How many rows arrived with no asset tag and had one generated. */
  generatedAssetTagRows: number;
  /**
   * Reference data this import will create as a side effect. Committing an asset
   * import can therefore add categories and locations nobody explicitly asked
   * for, so these counts are shown before the commit button, not after.
   */
  plannedCategoryCreateCount: number;
  plannedLocationCreateCount: number;
  /** Rows where at least one date was inferred rather than read exactly. */
  inferredDateRows: number;
  /** Rows whose owner was matched by approximate spelling — guesses to check. */
  fuzzyPersonMatchRows: number;
  /**
   * Rows that named an owner who could not be resolved. They import as
   * unassigned `inStock` rather than failing, so the intended owner is lost
   * unless someone notices.
   */
  unassignedImportRows: number;
  /** Source serials retained separately because they are duplicated. */
  reportedDuplicateSerialRows: number;
  createdAtUtc: IsoInstant;
  createdByUserId: Guid;
  createdByDisplayName: string;
  committedAtUtc: IsoInstant | null;
  committedByUserId: Guid | null;
  committedByDisplayName: string | null;
  committedCreateRows: number;
  committedUpdateRows: number;
  committedUnchangedRows: number;
  preview: AssetImportPreviewRow[];
  errors: AssetImportError[];
  /** The server caps how much of each list it returns. */
  previewTruncated: boolean;
  errorsTruncated: boolean;
  version: Guid;
}

export interface AssetImportCommitResult {
  importJobId: Guid;
  status: AssetImportStatus;
  createdRows: number;
  updatedRows: number;
  unchangedRows: number;
  /** Reference data created alongside the assets, in the same transaction. */
  createdCategoryCount: number;
  createdLocationCount: number;
  committedAtUtc: IsoInstant;
  version: Guid;
}

// ---------------------------------------------------------------------------
// Reference data import (people, departments, locations)
//
// Structurally the same validate -> preview -> commit pipeline as assets, but
// keyed differently: the natural key is a person's external ID or a department
// or location code, so the upsert mode is `upsertByKey` rather than by tag.
// ---------------------------------------------------------------------------

export const REFERENCE_IMPORT_RESOURCES = ['people', 'departments', 'locations'] as const;
export type ReferenceDataImportResource = (typeof REFERENCE_IMPORT_RESOURCES)[number];

export const REFERENCE_IMPORT_MODES = ['createOnly', 'upsertByKey'] as const;
export type ReferenceDataImportMode = (typeof REFERENCE_IMPORT_MODES)[number];

export type ReferenceDataImportStatus = AssetImportStatus;
export type ReferenceDataImportAction = AssetImportAction;

export interface ReferenceDataImportError {
  rowNumber: number;
  column: string;
  code: string;
  message: string;
  value: string | null;
}

export interface ReferenceDataImportPreviewRow {
  rowNumber: number;
  action: ReferenceDataImportAction;
  /** External ID for people; code for departments and locations. */
  key: string;
  /**
   * True when the row had no key and one was derived — a staff external ID from
   * the email local-part, or a slug from the name (`Sixth Form` -> `SIXTH-FORM`).
   */
  keyGenerated: boolean;
  name: string;
  /** Locations only. */
  type: string | null;
  /** Locations only: the parent's code. */
  parentKey: string | null;
  isActive: boolean;
}

export interface ReferenceDataImportJob {
  id: Guid;
  resource: ReferenceDataImportResource;
  schemaVersion: number;
  originalFileName: string;
  fileHash: string;
  mode: ReferenceDataImportMode;
  status: ReferenceDataImportStatus;
  totalRows: number;
  validRows: number;
  errorRows: number;
  createRows: number;
  updateRows: number;
  generatedKeyRows: number;
  createdAtUtc: IsoInstant;
  createdByUserId: Guid;
  createdByDisplayName: string;
  committedAtUtc: IsoInstant | null;
  committedByUserId: Guid | null;
  committedByDisplayName: string | null;
  committedCreateRows: number;
  committedUpdateRows: number;
  committedUnchangedRows: number;
  preview: ReferenceDataImportPreviewRow[];
  errors: ReferenceDataImportError[];
  previewTruncated: boolean;
  errorsTruncated: boolean;
  version: Guid;
}

export interface ReferenceDataImportCommitResult {
  importJobId: Guid;
  resource: ReferenceDataImportResource;
  status: ReferenceDataImportStatus;
  createdRows: number;
  updatedRows: number;
  unchangedRows: number;
  committedAtUtc: IsoInstant;
  version: Guid;
}

/** Server defaults from `ImportExportSettings`; both are configurable. */
export const IMPORT_MAX_BYTES = 5 * 1024 * 1024;
export const IMPORT_MAX_ROWS = 500;

/** A downloaded CSV plus the row count the server reports in a header. */
export interface CsvFile {
  blob: Blob;
  fileName: string;
  rowCount: number;
}

// ---------------------------------------------------------------------------
// Label printing
// ---------------------------------------------------------------------------

/**
 * Mirrors `LabelStockContract`.
 *
 * A camel-case string on the wire. The OpenAPI document used to type every enum
 * as a bare `integer`; since `6d95974` it publishes the string values, and an
 * integer is rejected in bodies and query strings alike.
 */
export const LABEL_STOCKS = ['brotherDk11209', 'averyL7159'] as const;
export type LabelStock = (typeof LABEL_STOCKS)[number];

/** `continuousRoll` prints one label at a time; `a4Sheet` is a grid per page. */
export type LabelPageType = 'continuousRoll' | 'a4Sheet';

export interface LabelStockInfo {
  stock: LabelStock;
  displayName: string;
  pageType: LabelPageType;
  labelWidthMillimetres: number;
  labelHeightMillimetres: number;
  labelsPerPage: number;
  columns: number;
  rows: number;
  /** False for roll stock, where `startPosition` must be 1. */
  supportsStartPosition: boolean;
}

/** Server cap across the whole request, summed over every item's quantity. */
export const MAX_LABELS_PER_REQUEST = 240;
/** Per-item quantity bounds. */
export const MAX_LABEL_QUANTITY = 100;

export interface PrintAssetLabelRequest {
  stock: LabelStock;
  copies: number;
  startPosition: number;
  /** Issues a QR identifier first if the asset has none, rather than failing. */
  issueMissingQr: boolean;
}

export interface LabelBatchItem {
  assetId: Guid;
  quantity: number;
}

export interface PrintLabelBatchRequest {
  stock: LabelStock;
  startPosition: number;
  items: LabelBatchItem[];
  issueMissingQr: boolean;
}

/** A generated PDF plus the counts the server reports in response headers. */
export interface LabelDocument {
  blob: Blob;
  fileName: string;
  labelCount: number;
  pageCount: number;
}

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------

export interface CsrfTokenResponse {
  token: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface MfaLoginRequest {
  code: string;
  rememberMe: boolean;
  rememberMachine: boolean;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface CurrentUser {
  id: Guid;
  displayName: string;
  roles: string[];
  permissions: string[];
  mustChangePassword: boolean;
  /**
   * The account holds a role that requires two-step verification and has not
   * enrolled yet.
   *
   * This is a lockout, not a nag. `PermissionAuthorization` returns without
   * succeeding while it is true, so **every** permission check fails — even
   * though `permissions` below is still fully populated. Never drive `can()`
   * from that list without accounting for this flag, or the UI will offer a
   * working app that answers `403` to everything.
   */
  mfaEnrollmentRequired: boolean;
}

export interface User {
  id: Guid;
  email: string;
  displayName: string;
  isEnabled: boolean;
  mustChangePassword: boolean;
  mfaEnabled: boolean;
  roles: string[];
  /** Set once the account has been archived. Archived accounts cannot sign in. */
  archivedAtUtc: IsoInstant | null;
  version: Guid;
}

export interface CreateUserRequest {
  email: string;
  displayName: string;
  temporaryPassword: string;
  roles: string[];
}

/** Full account edit: profile, enabled state and roles in one versioned write. */
export interface UpdateUserRequest {
  email: string;
  displayName: string;
  isEnabled: boolean;
  roles: string[];
  version: Guid;
}

export interface SetUserRolesRequest {
  roles: string[];
  version: Guid;
}

export interface ResetUserPasswordRequest {
  newTemporaryPassword: string;
  version: Guid;
}

/**
 * A role and the permissions it grants, straight from `RolePermissionPresets`.
 * The server owns this list — never hard-code the assignable roles.
 */
export interface RolePreset {
  name: string;
  permissions: string[];
}
