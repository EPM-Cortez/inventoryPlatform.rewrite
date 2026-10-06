import type { AgentSearchParams } from './agentTypes';
import type { InventoryDistributionParams } from './dashboardTypes';
import type { StocktakeSearchParams } from './stocktakeTypes';
import type { BundleSearchParams, LeaverSearchParams } from './operationsTypes';
import type { AuditSearchParams } from './adminTypes';
import type { AssetSearchParams, Guid, OverdueLoanSearchParams } from './types';
import type { DepartmentSearchParams, PersonSearchParams } from './endpoints';

/**
 * Every cache key in one place, arranged so a prefix invalidates everything
 * beneath it. `queryClient.invalidateQueries({ queryKey: keys.assets.all })`
 * clears every asset list and detail without touching people or reference data.
 */
export const keys = {
  session: ['session'] as const,

  assets: {
    all: ['assets'] as const,
    lists: () => [...keys.assets.all, 'list'] as const,
    list: (params: AssetSearchParams) => [...keys.assets.lists(), params] as const,
    details: () => [...keys.assets.all, 'detail'] as const,
    detail: (assetId: Guid) => [...keys.assets.details(), assetId] as const,
    byTag: (assetTag: string) => [...keys.assets.all, 'byTag', assetTag] as const,
    history: (assetId: Guid, page: number, pageSize: number) =>
      [...keys.assets.detail(assetId), 'history', page, pageSize] as const,
    assignments: (assetId: Guid, page: number, pageSize: number) =>
      [...keys.assets.detail(assetId), 'assignments', page, pageSize] as const,
    warranties: (assetId: Guid, includeArchived: boolean) =>
      [...keys.assets.detail(assetId), 'warranties', includeArchived] as const,
    qr: (assetId: Guid) => [...keys.assets.detail(assetId), 'qr'] as const,
  },

  people: {
    all: ['people'] as const,
    lists: () => [...keys.people.all, 'list'] as const,
    list: (params: PersonSearchParams) => [...keys.people.lists(), params] as const,
    detail: (personId: Guid) => [...keys.people.all, 'detail', personId] as const,
    assets: (personId: Guid, includeHistory: boolean, page: number) =>
      [...keys.people.all, 'detail', personId, 'assets', includeHistory, page] as const,
    /** Kept apart from `assets`, whose key carries no page size. */
    heldCount: (personId: Guid) =>
      [...keys.people.all, 'detail', personId, 'heldCount'] as const,
  },

  departments: {
    all: ['departments'] as const,
    lists: () => [...keys.departments.all, 'list'] as const,
    list: (params: DepartmentSearchParams) => [...keys.departments.lists(), params] as const,
    detail: (departmentId: Guid) =>
      [...keys.departments.all, 'detail', departmentId] as const,
    assets: (departmentId: Guid, includeHistory: boolean, page: number) =>
      [
        ...keys.departments.all,
        'detail',
        departmentId,
        'assets',
        includeHistory,
        page,
      ] as const,
    /** Kept apart from `assets`, whose key carries no page size. */
    heldCount: (departmentId: Guid) =>
      [...keys.departments.all, 'detail', departmentId, 'heldCount'] as const,
  },

  assignments: {
    all: ['assignments'] as const,
    overdue: (params: OverdueLoanSearchParams) =>
      [...keys.assignments.all, 'overdue', params] as const,
  },

  warranties: {
    all: ['warranties'] as const,
    expiring: (withinDays: number | undefined, page: number, pageSize: number) =>
      [...keys.warranties.all, 'expiring', withinDays, page, pageSize] as const,
  },

  categories: {
    all: ['categories'] as const,
    list: (includeInactive: boolean) => [...keys.categories.all, includeInactive] as const,
  },

  locations: {
    all: ['locations'] as const,
    list: (includeInactive: boolean) => [...keys.locations.all, includeInactive] as const,
  },

  users: {
    all: ['users'] as const,
    list: () => [...keys.users.all, 'list'] as const,
    detail: (userId: Guid) => [...keys.users.all, 'detail', userId] as const,
    // Presets are compiled into the server; they only change on deployment.
    rolePresets: () => [...keys.users.all, 'rolePresets'] as const,
  },

  leavers: {
    all: ['leavers'] as const,
    list: (params: LeaverSearchParams) => [...keys.leavers.all, 'list', params] as const,
    detail: (leaverCaseId: Guid) => [...keys.leavers.all, 'detail', leaverCaseId] as const,
    assets: (leaverCaseId: Guid, page: number) =>
      [...keys.leavers.detail(leaverCaseId), 'assets', page] as const,
    exceptions: (page: number, pageSize: number) =>
      [...keys.leavers.all, 'exceptions', page, pageSize] as const,
  },

  attachments: {
    /** Nested under the asset so it clears with the rest of that asset. */
    list: (assetId: Guid, includeArchived: boolean, page: number) =>
      [...keys.assets.detail(assetId), 'attachments', includeArchived, page] as const,
  },

  bundles: {
    all: ['bundles'] as const,
    list: (params: BundleSearchParams) => [...keys.bundles.all, 'list', params] as const,
    detail: (bundleId: Guid) => [...keys.bundles.all, 'detail', bundleId] as const,
  },

  stocktakes: {
    all: ['stocktakes'] as const,
    list: (params: StocktakeSearchParams) =>
      [...keys.stocktakes.all, 'list', params] as const,
    detail: (sessionId: Guid) => [...keys.stocktakes.all, 'detail', sessionId] as const,
    items: (sessionId: Guid, filter: string, page: number, pageSize: number) =>
      [...keys.stocktakes.detail(sessionId), 'items', filter, page, pageSize] as const,
  },

  dashboard: {
    all: ['dashboard'] as const,
    summary: (categoryLimit: number) =>
      [...keys.dashboard.all, 'summary', categoryLimit] as const,
    byLocation: (params: InventoryDistributionParams) =>
      [...keys.dashboard.all, 'byLocation', params] as const,
    locationTotals: () => [...keys.dashboard.all, 'locationTotals'] as const,
    exception: (code: string, page: number, pageSize: number) =>
      [...keys.dashboard.all, 'exception', code, page, pageSize] as const,
  },

  savedSearches: {
    all: ['savedSearches'] as const,
    list: () => [...keys.savedSearches.all, 'list'] as const,
    default: () => [...keys.savedSearches.all, 'default'] as const,
  },

  qr: {
    resolve: (token: string) => ['qr', 'resolve', token] as const,
  },

  mfa: {
    status: () => ['mfa', 'status'] as const,
  },

  audit: {
    all: ['audit'] as const,
    list: (params: AuditSearchParams) => [...keys.audit.all, 'list', params] as const,
    vocabulary: () => [...keys.audit.all, 'vocabulary'] as const,
  },

  settings: {
    school: () => ['settings', 'school'] as const,
    assetTags: () => ['settings', 'assetTags'] as const,
  },

  operations: {
    status: () => ['operations', 'status'] as const,
  },

  reconciliation: {
    all: ['reconciliation'] as const,
    detail: (importJobId: Guid) =>
      [...keys.reconciliation.all, 'detail', importJobId] as const,
    cutover: (importJobId: Guid) =>
      [...keys.reconciliation.all, 'cutover', importJobId] as const,
  },

  agents: {
    all: ['agents'] as const,
    list: (params: AgentSearchParams) => [...keys.agents.all, 'list', params] as const,
    detail: (agentId: Guid) => [...keys.agents.all, 'detail', agentId] as const,
    /** Keyed by asset so it invalidates with the rest of that asset's data. */
    forAsset: (assetId: Guid) => [...keys.assets.detail(assetId), 'deviceInventory'] as const,
    enrollmentProfiles: (params: AgentSearchParams) =>
      [...keys.agents.all, 'enrollmentProfiles', params] as const,
    summary: () => [...keys.agents.all, 'summary'] as const,
    /** Compiled into the server build; only changes on deployment. */
    installer: () => [...keys.agents.all, 'installer'] as const,
    keyRecovery: (agentId: Guid, includeClosed: boolean) =>
      [...keys.agents.detail(agentId), 'keyRecovery', includeClosed] as const,
  },

  imports: {
    all: ['imports'] as const,
    detail: (importJobId: Guid) => [...keys.imports.all, 'detail', importJobId] as const,
  },

  labels: {
    all: ['labels'] as const,
    // Compiled into the server; only changes on deployment.
    stocks: () => [...keys.labels.all, 'stocks'] as const,
  },
} as const;
