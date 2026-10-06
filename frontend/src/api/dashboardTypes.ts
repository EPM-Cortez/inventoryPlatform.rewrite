import type {
  AssetCondition,
  AssetStatus,
  Guid,
  IsoDate,
  IsoInstant,
  LocationSummary,
  PagedResponse,
  ReferenceSummary,
} from './types';

/**
 * Dashboard and exception contracts, hand-mirrored from
 * `Inventory.Contracts.V1`.
 *
 * These counts are computed by the server across the whole register, not derived
 * from a page of results — which is the point of them. Never recompute a figure
 * here from a list the UI happens to be holding: the two will disagree the
 * moment the list is filtered or paged.
 */

/**
 * The exception codes the server recognises, from `DashboardExceptionCodes`.
 *
 * The drill-down endpoint 400s on anything else, so this is a closed set rather
 * than free text.
 */
export const DASHBOARD_EXCEPTION_CODES = [
  'missingLocation',
  'missingSerialNumber',
  'repair',
  'lostOrStolen',
  'assignedWithoutCustody',
  'inactivePersonCustody',
  'missingWarranty',
  'expiredWarranty',
] as const;
export type DashboardExceptionCode = (typeof DASHBOARD_EXCEPTION_CODES)[number];

export interface DashboardValueCount<T> {
  value: T;
  count: number;
}

export interface DashboardCategoryCount {
  categoryId: Guid;
  categoryCode: string;
  categoryName: string;
  count: number;
}

export interface DashboardExceptionCount {
  /** One of `DASHBOARD_EXCEPTION_CODES`; the server owns the label. */
  code: string;
  label: string;
  count: number;
}

export interface DashboardSummary {
  totalAssets: number;
  /** Excludes retired and disposed. */
  activeAssets: number;
  statusCounts: DashboardValueCount<AssetStatus>[];
  conditionCounts: DashboardValueCount<AssetCondition>[];
  categoryCounts: DashboardCategoryCount[];
  /** True when `categoryLimit` cut the list short, so it is not the whole estate. */
  categoryCountsTruncated: boolean;
  exceptionCounts: DashboardExceptionCount[];
}

/** One asset behind an exception count. */
export interface DashboardExceptionAsset {
  assetId: Guid;
  assetTag: string;
  deviceName: string | null;
  category: ReferenceSummary;
  status: AssetStatus;
  condition: AssetCondition;
  location: LocationSummary | null;
  updatedAtUtc: IsoInstant;
}

/**
 * Stock of one category in one location.
 *
 * `locationId` is null for assets with no location recorded — that row is the
 * "nowhere" bucket, and it is usually the interesting one.
 */
export interface InventoryLocationCount {
  categoryId: Guid;
  categoryCode: string;
  categoryName: string;
  locationId: Guid | null;
  locationCode: string | null;
  locationName: string | null;
  quantity: number;
  inStockQuantity: number;
  assignedQuantity: number;
  /** Repair, lost, stolen, reserved — anything neither in stock nor assigned. */
  otherQuantity: number;
}

export interface InventoryDistribution {
  groups: PagedResponse<InventoryLocationCount>;
  /** Total assets matching the filter, before paging the groups. */
  matchingAssetCount: number;
}

export interface InventoryDistributionParams {
  categoryId?: Guid;
  query?: string;
  /** Retired and disposed are excluded unless this is set. */
  includeTerminalAssets?: boolean;
  page?: number;
  pageSize?: number;
}

/** A person who still holds equipment they arguably should not. */
export interface CustodyException {
  personId: Guid;
  personExternalId: string | null;
  personDisplayName: string;
  personIsActive: boolean;
  leaverCaseId: Guid | null;
  leavingOn: IsoDate | null;
  currentAssetCount: number;
  overdueAssetCount: number;
}
