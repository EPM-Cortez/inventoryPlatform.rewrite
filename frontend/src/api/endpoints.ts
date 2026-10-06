import { api, invalidateCsrfToken } from './client';
import type {
  AgentEnrollmentProfile,
  AgentInstallerMetadata,
  AgentKeyRecoveryAuthorization,
  AgentSearchParams,
  CreateAgentEnrollmentProfileRequest,
  CreateAgentKeyRecoveryRequest,
  CreateAndLinkManagedAgentAssetRequest,
  CreateAndLinkManagedAgentAssetResponse,
  CreatedAgentEnrollmentProfile,
  CreatedAgentKeyRecovery,
  DismissManagedAgentRequest,
  RevokeAgentKeyRecoveryRequest,
  LinkManagedAgentRequest,
  ManagedAgentDetail,
  ManagedAgentFleetSummary,
  ManagedAgentListItem,
  RestoreManagedAgentReviewRequest,
  RevokeManagedAgentRequest,
  UnlinkManagedAgentRequest,
} from './agentTypes';
import type {
  ArchiveAttachmentRequest,
  AssetAttachment,
  AssetBundle,
  AssetBundleListItem,
  AttachmentCategory,
  BulkChangeCommitResult,
  BulkChangePreview,
  BundleSearchParams,
  ChangeLeaverCaseStatusRequest,
  CommitBulkChangeRequest,
  CreateAssetBundleRequest,
  CreateLeaverCaseRequest,
  LeaverCase,
  LeaverCaseSummary,
  LeaverCustodyAsset,
  LeaverSearchParams,
  PreviewBulkChangeRequest,
  UpdateAssetBundleRequest,
  UpdateLeaverCaseRequest,
} from './operationsTypes';
import type {
  CancelStocktakeRequest,
  RecordStocktakeObservationRequest,
  StartStocktakeRequest,
  StocktakeActionRequest,
  StocktakeItem,
  StocktakeItemFilter,
  StocktakeObservation,
  StocktakeSearchParams,
  StocktakeSession,
  StocktakeSessionListItem,
} from './stocktakeTypes';
import type {
  CustodyException,
  DashboardExceptionAsset,
  DashboardExceptionCode,
  DashboardSummary,
  InventoryDistribution,
  InventoryDistributionParams,
} from './dashboardTypes';
import type {
  AuditLogEntry,
  AuditFilterVocabulary,
  AuditSearchParams,
  BeginMfaEnrollmentRequest,
  ConfirmMfaEnrollmentRequest,
  LegacyCutoverPackage,
  LegacyReconciliation,
  MfaEnrollment,
  MfaRecoveryCodes,
  MfaRecoveryLoginRequest,
  MfaStatus,
  OperationalStatus,
  RegenerateMfaRecoveryCodesRequest,
  SchoolSettings,
  SignOffLegacyReconciliationRequest,
  UpdateSchoolSettingsRequest,
} from './adminTypes';
import type {
  AddAssetNoteRequest,
  AssignAssetRequest,
  Asset,
  AssetAssignment,
  AssetEvent,
  AssetListItem,
  AssetImportCommitResult,
  AssetImportJob,
  AssetImportMode,
  AssetSearchParams,
  AssignmentWorkflowResult,
  Category,
  ChangePasswordRequest,
  CreateAssetRequest,
  CreateCategoryRequest,
  CreateDepartmentRequest,
  CreateLocationRequest,
  CreatePersonRequest,
  CreateUserRequest,
  CreateWarrantyRequest,
  CsvFile,
  CurrentUser,
  Department,
  DisposeAssetRequest,
  Guid,
  HolderAsset,
  LabelDocument,
  LabelStockInfo,
  Location,
  LoginRequest,
  MfaLoginRequest,
  OverdueLoan,
  OverdueLoanSearchParams,
  PagedResponse,
  Person,
  PersonType,
  PrintAssetLabelRequest,
  PrintLabelBatchRequest,
  QrIdentifier,
  QrImageFormat,
  QrResolution,
  ResetUserPasswordRequest,
  RetireAssetRequest,
  ReturnAssetRequest,
  ReferenceDataImportCommitResult,
  ReferenceDataImportJob,
  ReferenceDataImportMode,
  ReferenceDataImportResource,
  RolePreset,
  SavedAssetSearch,
  ChangeSavedAssetSearchRequest,
  CreateSavedAssetSearchRequest,
  UpdateSavedAssetSearchRequest,
  SetUserRolesRequest,
  AssetTagPolicy,
  UpdateAssetRequest,
  UpdateAssetTagPolicyRequest,
  UpdateExpectedReturnRequest,
  UpdateCategoryRequest,
  UpdateDepartmentRequest,
  UpdateLocationRequest,
  UpdatePersonRequest,
  UpdateUserRequest,
  UpdateWarrantyRequest,
  User,
  Warranty,
} from './types';

/**
 * One function per implemented endpoint in `docs/api/permissions.md`. Nothing here
 * is speculative: if a function does not exist, the backend does not implement it.
 */

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------

export const authApi = {
  /**
   * The session probe. A 401 here is the normal answer for a signed-out visitor,
   * so it must not trip the global handler — the session provider renders the
   * sign-in screen itself.
   */
  me: (signal?: AbortSignal) =>
    api.get<CurrentUser>('/auth/me', { signal, skipUnauthenticatedHandler: true }),

  async login(request: LoginRequest): Promise<void> {
    await api.post<void>('/auth/login', {
      body: request,
      skipUnauthenticatedHandler: true,
    });
    // The identity changed, so the antiforgery pair issued to the anonymous
    // caller is no longer valid.
    invalidateCsrfToken();
  },

  async completeMfa(request: MfaLoginRequest): Promise<void> {
    await api.post<void>('/auth/login/mfa', {
      body: request,
      skipUnauthenticatedHandler: true,
    });
    invalidateCsrfToken();
  },

  async logout(): Promise<void> {
    await api.post<void>('/auth/logout');
    invalidateCsrfToken();
  },

  async changePassword(request: ChangePasswordRequest): Promise<void> {
    await api.post<void>('/auth/change-password', { body: request });
    // The security stamp rotates on a password change, which invalidates the pair.
    invalidateCsrfToken();
  },
};

// ---------------------------------------------------------------------------
// Assets
// ---------------------------------------------------------------------------

export const assetsApi = {
  list: (params: AssetSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<AssetListItem>>('/assets', { query: { ...params }, signal }),

  get: (assetId: Guid, signal?: AbortSignal) =>
    api.get<Asset>(`/assets/${assetId}`, { signal }),

  getByTag: (assetTag: string, signal?: AbortSignal) =>
    api.get<Asset>(`/assets/by-tag/${encodeURIComponent(assetTag)}`, { signal }),

  create: (body: CreateAssetRequest) => api.post<Asset>('/assets', { body }),

  update: (assetId: Guid, body: UpdateAssetRequest) =>
    api.put<Asset>(`/assets/${assetId}`, { body }),

  retire: (assetId: Guid, body: RetireAssetRequest) =>
    api.post<Asset>(`/assets/${assetId}/retire`, { body }),

  /** Terminal: a disposed asset can never be restored. */
  dispose: (assetId: Guid, body: DisposeAssetRequest) =>
    api.post<Asset>(`/assets/${assetId}/dispose`, { body }),

  /** Only valid from `retired`; the domain rejects a disposed asset. */
  restore: (assetId: Guid, version: Guid) =>
    api.post<Asset>(`/assets/${assetId}/restore`, { body: { version } }),
};

// ---------------------------------------------------------------------------
// Asset history (append-only)
// ---------------------------------------------------------------------------

export const historyApi = {
  list: (assetId: Guid, page: number, pageSize: number, signal?: AbortSignal) =>
    api.get<PagedResponse<AssetEvent>>(`/assets/${assetId}/history`, {
      query: { page, pageSize },
      signal,
    }),

  addNote: (assetId: Guid, body: AddAssetNoteRequest) =>
    api.post<AssetEvent>(`/assets/${assetId}/history/notes`, { body }),
};

// ---------------------------------------------------------------------------
// Assignments (custody)
// ---------------------------------------------------------------------------

export const assignmentsApi = {
  list: (assetId: Guid, page: number, pageSize: number, signal?: AbortSignal) =>
    api.get<PagedResponse<AssetAssignment>>(`/assets/${assetId}/assignments`, {
      query: { page, pageSize },
      signal,
    }),

  /**
   * Assigning an already-assigned asset is an atomic transfer, not an error. The
   * caller must pass the newest `assetVersion` or the API returns 409.
   */
  assign: (assetId: Guid, body: AssignAssetRequest) =>
    api.post<AssignmentWorkflowResult>(`/assets/${assetId}/assignments`, { body }),

  return: (assetId: Guid, assignmentId: Guid, body: ReturnAssetRequest) =>
    api.post<AssignmentWorkflowResult>(
      `/assets/${assetId}/assignments/${assignmentId}/return`,
      { body },
    ),

  /**
   * Sets or clears when a loan is due back. Versioned on the assignment, not the
   * asset — changing a due date is not a change to the equipment.
   */
  updateExpectedReturn: (
    assetId: Guid,
    assignmentId: Guid,
    body: UpdateExpectedReturnRequest,
  ) =>
    api.put<AssetAssignment>(
      `/assets/${assetId}/assignments/${assignmentId}/expected-return`,
      { body },
    ),

  /** Loans past their due date, newest first. Judged in the school time zone. */
  overdue: (params: OverdueLoanSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<OverdueLoan>>('/assignments/overdue', {
      query: { ...params },
      signal,
    }),
};

// ---------------------------------------------------------------------------
// People and departments
// ---------------------------------------------------------------------------

export interface PersonSearchParams {
  q?: string;
  type?: PersonType;
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

export const peopleApi = {
  list: (params: PersonSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<Person>>('/people', { query: { ...params }, signal }),

  get: (personId: Guid, signal?: AbortSignal) =>
    api.get<Person>(`/people/${personId}`, { signal }),

  create: (body: CreatePersonRequest) => api.post<Person>('/people', { body }),

  /** Setting `isActive: false` additionally requires `people.manage`. */
  update: (personId: Guid, body: UpdatePersonRequest) =>
    api.put<Person>(`/people/${personId}`, { body }),

  assets: (
    personId: Guid,
    options: { includeHistory?: boolean; page?: number; pageSize?: number },
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<HolderAsset>>(`/people/${personId}/assets`, {
      query: { ...options },
      signal,
    }),
};

export interface DepartmentSearchParams {
  q?: string;
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

export const departmentsApi = {
  list: (params: DepartmentSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<Department>>('/departments', { query: { ...params }, signal }),

  get: (departmentId: Guid, signal?: AbortSignal) =>
    api.get<Department>(`/departments/${departmentId}`, { signal }),

  create: (body: CreateDepartmentRequest) => api.post<Department>('/departments', { body }),

  update: (departmentId: Guid, body: UpdateDepartmentRequest) =>
    api.put<Department>(`/departments/${departmentId}`, { body }),

  assets: (
    departmentId: Guid,
    options: { includeHistory?: boolean; page?: number; pageSize?: number },
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<HolderAsset>>(`/departments/${departmentId}/assets`, {
      query: { ...options },
      signal,
    }),
};

// ---------------------------------------------------------------------------
// Warranties
// ---------------------------------------------------------------------------

export const warrantiesApi = {
  listForAsset: (assetId: Guid, includeArchived: boolean, signal?: AbortSignal) =>
    api.get<Warranty[]>(`/assets/${assetId}/warranties`, {
      query: { includeArchived },
      signal,
    }),

  add: (assetId: Guid, body: CreateWarrantyRequest) =>
    api.post<Warranty>(`/assets/${assetId}/warranties`, { body }),

  update: (warrantyId: Guid, body: UpdateWarrantyRequest) =>
    api.put<Warranty>(`/warranties/${warrantyId}`, { body }),

  archive: (warrantyId: Guid, version: Guid) =>
    api.post<Warranty>(`/warranties/${warrantyId}/archive`, { body: { version } }),

  expiring: (
    options: { withinDays?: number; page?: number; pageSize?: number },
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<Warranty>>('/warranties/expiring', {
      query: { ...options },
      signal,
    }),
};

// ---------------------------------------------------------------------------
// QR identifiers
// ---------------------------------------------------------------------------

export const qrApi = {
  issue: (assetId: Guid) => api.post<QrIdentifier>(`/assets/${assetId}/qr`),

  rotate: (assetId: Guid) => api.post<QrIdentifier>(`/assets/${assetId}/qr/rotate`),

  /** Returns the label image itself. `svg` is preferred for print fidelity. */
  async image(assetId: Guid, format: QrImageFormat, signal?: AbortSignal): Promise<Blob> {
    const response = await api.raw(`/assets/${assetId}/qr`, {
      query: { format },
      accept: format === 'png' ? 'image/png' : 'image/svg+xml',
      signal,
    });
    return response.blob();
  },

  /** Resolves a scanned token. Rate limited per IP at 120/minute. */
  resolve: (token: string, signal?: AbortSignal) =>
    api.get<QrResolution>(`/qr/${encodeURIComponent(token)}`, { signal }),
};

// ---------------------------------------------------------------------------
// CSV import and export
// ---------------------------------------------------------------------------

/**
 * The filename the server chose, from `Content-Disposition`.
 *
 * Handles both `filename=` and the RFC 5987 `filename*=UTF-8''` form, so a name
 * with non-ASCII in it survives the round trip.
 */
function fileNameFromDisposition(response: Response): string | null {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  return match?.[1] ? decodeURIComponent(match[1]) : null;
}

/** Reads a CSV download and the row count the server reports beside it. */
async function readCsvFile(response: Response, fallbackFileName: string): Promise<CsvFile> {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);

  return {
    blob: await response.blob(),
    fileName: match?.[1] ? decodeURIComponent(match[1]) : fallbackFileName,
    rowCount: Number(response.headers.get('X-Inventory-Row-Count') ?? 0),
  };
}

export const exportsApi = {
  /**
   * Exports the register as CSV using the *same* filter parameters as the asset
   * search, so "export what I am looking at" is exactly what happens.
   */
  async assetsCsv(params: AssetSearchParams, signal?: AbortSignal): Promise<CsvFile> {
    // Paging is meaningless for an export — the server streams every match.
    const { page: _page, pageSize: _pageSize, ...filters } = params;
    const response = await api.raw('/exports/assets.csv', {
      query: { ...filters },
      accept: 'text/csv',
      signal,
    });
    return readCsvFile(response, 'assets.csv');
  },
};

export const referenceExportsApi = {
  /** Bulk person PII — behind its own `exports.people` permission. */
  peopleCsv: async (signal?: AbortSignal): Promise<CsvFile> =>
    readCsvFile(
      await api.raw('/exports/people.csv', { accept: 'text/csv', signal }),
      'people.csv',
    ),

  departmentsCsv: async (signal?: AbortSignal): Promise<CsvFile> =>
    readCsvFile(
      await api.raw('/exports/departments.csv', { accept: 'text/csv', signal }),
      'departments.csv',
    ),

  locationsCsv: async (signal?: AbortSignal): Promise<CsvFile> =>
    readCsvFile(
      await api.raw('/exports/locations.csv', { accept: 'text/csv', signal }),
      'locations.csv',
    ),
};

/**
 * Reference-data imports. Validation is per resource, but everything after it —
 * fetching, committing, downloading errors — is shared under
 * `/imports/reference-data/{id}`.
 */
export const referenceImportsApi = {
  validate: (resource: ReferenceDataImportResource, file: File, mode: ReferenceDataImportMode) => {
    const form = new FormData();
    form.append('file', file);
    form.append('mode', mode);
    return api.post<ReferenceDataImportJob>(`/imports/${resource}/validate`, {
      body: form,
    });
  },

  get: (importJobId: Guid, signal?: AbortSignal) =>
    api.get<ReferenceDataImportJob>(`/imports/reference-data/${importJobId}`, { signal }),

  commit: (importJobId: Guid, version: Guid) =>
    api.post<ReferenceDataImportCommitResult>(
      `/imports/reference-data/${importJobId}/commit`,
      { body: { version } },
    ),

  errorsCsv: async (importJobId: Guid, signal?: AbortSignal): Promise<CsvFile> =>
    readCsvFile(
      await api.raw(`/imports/reference-data/${importJobId}/errors.csv`, {
        accept: 'text/csv',
        signal,
      }),
      `import-${importJobId}-errors.csv`,
    ),
};

export const importsApi = {
  /**
   * Dry run. Parses and validates the file and stores the outcome as a job;
   * nothing in the register changes until `commit`.
   */
  validate: (file: File, mode: AssetImportMode) => {
    const form = new FormData();
    form.append('file', file);
    form.append('mode', mode);
    return api.post<AssetImportJob>('/imports/assets/validate', { body: form });
  },

  get: (importJobId: Guid, signal?: AbortSignal) =>
    api.get<AssetImportJob>(`/imports/${importJobId}`, { signal }),

  /** Version-checked, so the same validated job cannot be applied twice. */
  commit: (importJobId: Guid, version: Guid) =>
    api.post<AssetImportCommitResult>(`/imports/${importJobId}/commit`, {
      body: { version },
    }),

  async errorsCsv(importJobId: Guid, signal?: AbortSignal): Promise<CsvFile> {
    const response = await api.raw(`/imports/${importJobId}/errors.csv`, {
      accept: 'text/csv',
      signal,
    });
    return readCsvFile(response, `import-${importJobId}-errors.csv`);
  },
};

// ---------------------------------------------------------------------------
// Label printing
// ---------------------------------------------------------------------------

/**
 * Reads a PDF response plus the counts the server reports alongside it.
 *
 * `X-Label-Count` and `X-Label-Page-Count` are the only way to know how many
 * labels and sheets came back without parsing the PDF, and the filename comes
 * from `Content-Disposition` so downloads keep the server's naming.
 */
async function readLabelDocument(
  response: Response,
  fallbackFileName: string,
): Promise<LabelDocument> {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);

  return {
    blob: await response.blob(),
    fileName: match?.[1] ? decodeURIComponent(match[1]) : fallbackFileName,
    labelCount: Number(response.headers.get('X-Label-Count') ?? 0),
    pageCount: Number(response.headers.get('X-Label-Page-Count') ?? 0),
  };
}

export const labelsApi = {
  /** The stocks the server can lay out, with their physical dimensions. */
  stocks: (signal?: AbortSignal) =>
    api.get<LabelStockInfo[]>('/labels/stocks', { signal }),

  /** One asset, `copies` times. */
  async printAsset(
    assetId: Guid,
    body: PrintAssetLabelRequest,
    signal?: AbortSignal,
  ): Promise<LabelDocument> {
    const response = await api.raw(
      `/assets/${assetId}/label`,
      { body, accept: 'application/pdf', signal },
      'POST',
    );
    return readLabelDocument(response, `asset-${assetId}-labels.pdf`);
  },

  /** Many assets in one document. Total quantity is capped at 240 server-side. */
  async printBatch(
    body: PrintLabelBatchRequest,
    signal?: AbortSignal,
  ): Promise<LabelDocument> {
    const response = await api.raw(
      '/labels/batch',
      { body, accept: 'application/pdf', signal },
      'POST',
    );
    return readLabelDocument(response, 'asset-labels.pdf');
  },
};

// ---------------------------------------------------------------------------
// Multi-factor authentication (self-service)
// ---------------------------------------------------------------------------

export const mfaApi = {
  status: (signal?: AbortSignal) => api.get<MfaStatus>('/auth/mfa', { signal }),

  /** Signing in with a recovery code instead of the authenticator. */
  async completeRecovery(request: MfaRecoveryLoginRequest): Promise<void> {
    await api.post<void>('/auth/login/mfa/recovery', {
      body: request,
      skipUnauthenticatedHandler: true,
    });
    invalidateCsrfToken();
  },

  /**
   * Returns the TOTP secret. Nothing is enabled until `confirmEnrollment`.
   *
   * The cached antiforgery token is dropped afterwards because this call can
   * re-issue the pair; keeping the old one would cost the confirm step a
   * rejected request and a replay before it succeeded.
   */
  async beginEnrollment(body: BeginMfaEnrollmentRequest): Promise<MfaEnrollment> {
    const enrollment = await api.post<MfaEnrollment>('/auth/mfa/enrollment', { body });
    invalidateCsrfToken();
    return enrollment;
  },

  /** Proves the authenticator works, then turns MFA on and issues recovery codes. */
  async confirmEnrollment(body: ConfirmMfaEnrollmentRequest): Promise<MfaRecoveryCodes> {
    const codes = await api.post<MfaRecoveryCodes>('/auth/mfa/enrollment/confirm', {
      body,
    });
    // Enabling a second factor changes the identity the pair is bound to.
    invalidateCsrfToken();
    return codes;
  },

  /** Replaces every remaining recovery code. The old ones stop working. */
  regenerateRecoveryCodes: (body: RegenerateMfaRecoveryCodesRequest) =>
    api.post<MfaRecoveryCodes>('/auth/mfa/recovery-codes', { body }),
};

// ---------------------------------------------------------------------------
// Audit
// ---------------------------------------------------------------------------

export const auditApi = {
  list: (params: AuditSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<AuditLogEntry>>('/audit', { query: { ...params }, signal }),

  /**
   * The action, target-type and outcome values actually present in the trail.
   *
   * Derived from the data rather than from a fixed catalogue, so it lists only
   * what has really happened here — which is what a filter should offer, since
   * filtering to a value with no rows is never useful. Labels still come from
   * `features/audit/auditActions.ts`; this supplies the values.
   */
  vocabulary: (signal?: AbortSignal) =>
    api.get<AuditFilterVocabulary>('/audit/vocabulary', { signal }),

  /**
   * Separately permissioned from reading. The export deliberately omits the
   * structured details column and neutralises spreadsheet formulas.
   */
  async exportCsv(params: AuditSearchParams, signal?: AbortSignal): Promise<CsvFile> {
    const { page: _page, pageSize: _pageSize, ...filters } = params;
    const response = await api.raw('/audit/export', {
      query: { ...filters },
      accept: 'text/csv',
      signal,
    });
    return readCsvFile(response, 'audit.csv');
  },
};

// ---------------------------------------------------------------------------
// School settings and operations
// ---------------------------------------------------------------------------

export const settingsApi = {
  school: (signal?: AbortSignal) =>
    api.get<SchoolSettings>('/settings/school', { signal }),

  /** How the next generated asset tag will be composed. */
  assetTagPolicy: (signal?: AbortSignal) =>
    api.get<AssetTagPolicy>('/settings/asset-tags', { signal }),

  updateAssetTagPolicy: (body: UpdateAssetTagPolicyRequest) =>
    api.put<AssetTagPolicy>('/settings/asset-tags', { body }),

  updateSchool: (body: UpdateSchoolSettingsRequest) =>
    api.put<SchoolSettings>('/settings/school', { body }),
};

export const operationsApi = {
  status: (signal?: AbortSignal) =>
    api.get<OperationalStatus>('/operations/status', { signal }),
};

// ---------------------------------------------------------------------------
// Legacy reconciliation
// ---------------------------------------------------------------------------

export const reconciliationApi = {
  createDryRun: (file: File, mode: AssetImportMode) => {
    const form = new FormData();
    form.append('file', file);
    form.append('mode', mode);
    return api.post<LegacyReconciliation>('/legacy-reconciliation/dry-runs', {
      body: form,
    });
  },

  get: (importJobId: Guid, signal?: AbortSignal) =>
    api.get<LegacyReconciliation>(`/legacy-reconciliation/${importJobId}`, { signal }),

  signOff: (importJobId: Guid, body: SignOffLegacyReconciliationRequest) =>
    api.post<LegacyReconciliation>(`/legacy-reconciliation/${importJobId}/sign-off`, {
      body,
    }),

  cutoverPackage: (importJobId: Guid, signal?: AbortSignal) =>
    api.get<LegacyCutoverPackage>(
      `/legacy-reconciliation/${importJobId}/cutover-package`,
      { signal },
    ),
};

// ---------------------------------------------------------------------------
// Managed agents
//
// Everything here is read-only evidence collected from machines, except the
// explicit trust operations (link, revoke, enrollment profiles). The agent's own
// ingress routes (`/agent/enroll`, `/agent/check-ins`) are signed machine-to-machine
// calls with no cookie or CSRF, and are deliberately absent from this client.
// ---------------------------------------------------------------------------

export const agentsApi = {
  list: (params: AgentSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<ManagedAgentListItem>>('/agents', {
      query: { ...params },
      signal,
    }),

  get: (agentId: Guid, signal?: AbortSignal) =>
    api.get<ManagedAgentDetail>(`/agents/${agentId}`, { signal }),

  /** The agent and latest snapshot for an asset, or 404 when none is linked. */
  forAsset: (assetId: Guid, signal?: AbortSignal) =>
    api.get<ManagedAgentDetail>(`/assets/${assetId}/device-inventory`, { signal }),

  /** Counts for the whole fleet, unaffected by the current filters. */
  summary: (signal?: AbortSignal) =>
    api.get<ManagedAgentFleetSummary>('/agents/summary', { signal }),

  link: (agentId: Guid, body: LinkManagedAgentRequest) =>
    api.post<ManagedAgentListItem>(`/agents/${agentId}/link`, { body }),

  /** Breaks the tie to an asset. The agent stays enrolled and keeps reporting. */
  unlink: (agentId: Guid, body: UnlinkManagedAgentRequest) =>
    api.post<ManagedAgentListItem>(`/agents/${agentId}/unlink`, { body }),

  /**
   * Takes a machine out of the review queue without linking it — for a device
   * that genuinely has no place on the register. Reversible, unlike revoke.
   */
  dismiss: (agentId: Guid, body: DismissManagedAgentRequest) =>
    api.post<ManagedAgentListItem>(`/agents/${agentId}/dismiss`, { body }),

  /** Puts a dismissed machine back into the queue. */
  restoreReview: (agentId: Guid, body: RestoreManagedAgentReviewRequest) =>
    api.post<ManagedAgentListItem>(`/agents/${agentId}/restore-review`, { body }),

  /**
   * Creates the asset and links it in one transaction — if either half fails,
   * neither is applied, so an unmatched machine can never leave a stray asset.
   */
  createAndLinkAsset: (agentId: Guid, body: CreateAndLinkManagedAgentAssetRequest) =>
    api.post<CreateAndLinkManagedAgentAssetResponse>(
      `/agents/${agentId}/create-and-link-asset`,
      { body },
    ),

  revoke: (agentId: Guid, body: RevokeManagedAgentRequest) =>
    api.post<ManagedAgentListItem>(`/agents/${agentId}/revoke`, { body }),

  installer: (signal?: AbortSignal) =>
    api.get<AgentInstallerMetadata>('/agents/installer', { signal }),

  /**
   * Key-recovery authorisations for one machine.
   *
   * `includeClosed` brings in the consumed, expired and revoked ones. The
   * response never carries the token, its hash, or a key thumbprint.
   */
  keyRecovery: (
    agentId: Guid,
    params: { includeClosed?: boolean; page?: number; pageSize?: number },
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<AgentKeyRecoveryAuthorization>>(
      `/agents/${agentId}/key-recovery`,
      { query: { ...params }, signal },
    ),

  /** Returns the recovery token exactly once. It is never retrievable again. */
  createKeyRecovery: (agentId: Guid, body: CreateAgentKeyRecoveryRequest) =>
    api.post<CreatedAgentKeyRecovery>(`/agents/${agentId}/key-recovery`, { body }),

  revokeKeyRecovery: (
    agentId: Guid,
    authorizationId: Guid,
    body: RevokeAgentKeyRecoveryRequest,
  ) =>
    api.post<AgentKeyRecoveryAuthorization>(
      `/agents/${agentId}/key-recovery/${authorizationId}/revoke`,
      { body },
    ),

  /** The MSI itself. Authenticated, so it cannot be fetched by a bare link. */
  async downloadInstaller(signal?: AbortSignal): Promise<{ blob: Blob; fileName: string }> {
    const response = await api.raw('/agents/installer/download', {
      accept: 'application/x-msi',
      signal,
    });
    return {
      blob: await response.blob(),
      fileName: fileNameFromDisposition(response) ?? 'inventory-agent.msi',
    };
  },

  enrollmentProfiles: (params: AgentSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<AgentEnrollmentProfile>>('/agents/enrollment-profiles', {
      query: { ...params },
      signal,
    }),

  /** Returns the enrollment token exactly once. It is never retrievable again. */
  createEnrollmentProfile: (body: CreateAgentEnrollmentProfileRequest) =>
    api.post<CreatedAgentEnrollmentProfile>('/agents/enrollment-profiles', { body }),

  revokeEnrollmentProfile: (profileId: Guid, version: Guid) =>
    api.post<AgentEnrollmentProfile>(
      `/agents/enrollment-profiles/${profileId}/revoke`,
      { body: { version } },
    ),
};

// ---------------------------------------------------------------------------
// Reference data
// ---------------------------------------------------------------------------

/**
 * Reference lists are paged like every other collection. They are small enough
 * that the pickers ask for one large page rather than paginating a dropdown —
 * see `REFERENCE_PAGE_SIZE` in `hooks.ts`.
 */
export interface ReferenceSearchParams {
  q?: string;
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export const categoriesApi = {
  list: (params: ReferenceSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<Category>>('/categories', { query: { ...params }, signal }),

  get: (categoryId: Guid, signal?: AbortSignal) =>
    api.get<Category>(`/categories/${categoryId}`, { signal }),

  create: (body: CreateCategoryRequest) => api.post<Category>('/categories', { body }),

  update: (categoryId: Guid, body: UpdateCategoryRequest) =>
    api.put<Category>(`/categories/${categoryId}`, { body }),
};

export const locationsApi = {
  list: (params: ReferenceSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<Location>>('/locations', { query: { ...params }, signal }),

  get: (locationId: Guid, signal?: AbortSignal) =>
    api.get<Location>(`/locations/${locationId}`, { signal }),

  create: (body: CreateLocationRequest) => api.post<Location>('/locations', { body }),

  update: (locationId: Guid, body: UpdateLocationRequest) =>
    api.put<Location>(`/locations/${locationId}`, { body }),
};

// ---------------------------------------------------------------------------
// Users
// ---------------------------------------------------------------------------

/**
 * Every write here is version-checked, and the server returns `409` rather than
 * overwriting. Disable and archive answer `204`, so the caller has no new version
 * afterwards and must refetch before its next write.
 */
export const usersApi = {
  /**
   * Accounts, archived ones included, ordered by email.
   *
   * Paged since the accounts milestone — it returned a bare array before, and
   * the difference is invisible until `.map` is called on the response object.
   * `pageSize` is capped at 200 server-side; the UI asks for the maximum and
   * filters client-side, because an installation with more than 200 sign-in
   * accounts is not the situation this app is built for.
   */
  list: (signal?: AbortSignal) =>
    api.get<PagedResponse<User>>('/users', {
      query: { pageSize: 200 },
      signal,
    }),

  get: (userId: Guid, signal?: AbortSignal) =>
    api.get<User>(`/users/${userId}`, { signal }),

  /** The assignable role presets and the permissions each one grants. */
  rolePresets: (signal?: AbortSignal) =>
    api.get<RolePreset[]>('/users/role-presets', { signal }),

  create: (body: CreateUserRequest) => api.post<User>('/users', { body }),

  /** Profile, enabled state and roles in one write. */
  update: (userId: Guid, body: UpdateUserRequest) =>
    api.put<User>(`/users/${userId}`, { body }),

  setRoles: (userId: Guid, body: SetUserRolesRequest) =>
    api.put<User>(`/users/${userId}/roles`, { body }),

  disable: (userId: Guid, version: Guid) =>
    api.post<void>(`/users/${userId}/disable`, { body: { version } }),

  /**
   * A history-preserving archive, not a hard delete: login is disabled, existing
   * sessions are invalidated, and the account stays as the author of its past
   * audit and asset events.
   *
   * Note the version travels as a **query parameter** here — DELETE carries no
   * body in this API, unlike every other versioned write.
   */
  archive: (userId: Guid, version: Guid) =>
    api.delete<void>(`/users/${userId}`, { query: { version } }),

  restore: (userId: Guid, version: Guid) =>
    api.post<User>(`/users/${userId}/restore`, { body: { version } }),

  resetPassword: (userId: Guid, body: ResetUserPasswordRequest) =>
    api.post<void>(`/users/${userId}/reset-password`, { body }),

  /**
   * Clears the account's authenticator and recovery codes so it can enrol again.
   * Used when someone loses their phone and their codes.
   */
  resetMfa: (userId: Guid, version: Guid) =>
    api.post<User>(`/users/${userId}/reset-mfa`, { body: { version } }),
};

// ---------------------------------------------------------------------------
// Saved asset searches (per-user)
// ---------------------------------------------------------------------------

/**
 * Always scoped to the signed-in user — there is no shared or team search, and
 * no endpoint takes a user id. Everything here reuses `assets.read`, so any role
 * that can see the register can keep its own searches.
 */
export const savedSearchesApi = {
  list: (page = 1, pageSize = 100, signal?: AbortSignal) =>
    api.get<PagedResponse<SavedAssetSearch>>('/me/saved-asset-searches', {
      query: { page, pageSize },
      signal,
    }),

  /**
   * The user's default, or `null` when they have not set one — the API answers
   * `204` rather than `404`, because having no default is not an error.
   */
  async default(signal?: AbortSignal): Promise<SavedAssetSearch | null> {
    const response = await api.raw('/me/saved-asset-searches/default', { signal });
    return response.status === 204 ? null : ((await response.json()) as SavedAssetSearch);
  },

  create: (body: CreateSavedAssetSearchRequest) =>
    api.post<SavedAssetSearch>('/me/saved-asset-searches', { body }),

  update: (savedSearchId: Guid, body: UpdateSavedAssetSearchRequest) =>
    api.put<SavedAssetSearch>(`/me/saved-asset-searches/${savedSearchId}`, { body }),

  /** Promotes one search to the default, demoting whichever held it before. */
  setDefault: (savedSearchId: Guid, body: ChangeSavedAssetSearchRequest) =>
    api.post<SavedAssetSearch>(`/me/saved-asset-searches/${savedSearchId}/default`, {
      body,
    }),

  /** Answers `204`. There is no hard delete. */
  archive: (savedSearchId: Guid, body: ChangeSavedAssetSearchRequest) =>
    api.post<void>(`/me/saved-asset-searches/${savedSearchId}/archive`, { body }),
};

// ---------------------------------------------------------------------------
// Dashboard
// ---------------------------------------------------------------------------

/**
 * Server-computed figures for the whole register.
 *
 * Everything here is counted across all assets, not derived from a page of
 * results — which is exactly why it exists. The UI must not recompute any of it.
 */
export const dashboardApi = {
  summary: (categoryLimit: number, signal?: AbortSignal) =>
    api.get<DashboardSummary>('/dashboard/summary', {
      query: { categoryLimit },
      signal,
    }),

  /** Stock by category and location, including a bucket for "no location". */
  inventoryByLocation: (params: InventoryDistributionParams, signal?: AbortSignal) =>
    api.get<InventoryDistribution>('/dashboard/inventory-by-location', {
      query: { ...params },
      signal,
    }),

  /** The assets behind one exception count. 400s on an unknown code. */
  exception: (
    exceptionCode: DashboardExceptionCode,
    page: number,
    pageSize: number,
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<DashboardExceptionAsset>>(
      `/dashboard/exceptions/${exceptionCode}`,
      { query: { page, pageSize }, signal },
    ),
};

// ---------------------------------------------------------------------------
// Stocktakes
// ---------------------------------------------------------------------------

/**
 * Recording an observation writes it against the *session*, never against the
 * asset. Nothing here changes an asset's location, condition or status.
 */
export const stocktakesApi = {
  list: (params: StocktakeSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<StocktakeSessionListItem>>('/stocktakes', {
      query: { ...params },
      signal,
    }),

  get: (sessionId: Guid, signal?: AbortSignal) =>
    api.get<StocktakeSession>(`/stocktakes/${sessionId}`, { signal }),

  items: (
    sessionId: Guid,
    filter: StocktakeItemFilter,
    page: number,
    pageSize: number,
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<StocktakeItem>>(`/stocktakes/${sessionId}/items`, {
      query: { filter, page, pageSize },
      signal,
    }),

  /** `409 stocktake_duplicate_active` when one is already open for the scope. */
  start: (body: StartStocktakeRequest) =>
    api.post<StocktakeSession>('/stocktakes', { body }),

  observe: (sessionId: Guid, body: RecordStocktakeObservationRequest) =>
    api.post<StocktakeObservation>(`/stocktakes/${sessionId}/observations`, { body }),

  complete: (sessionId: Guid, body: StocktakeActionRequest) =>
    api.post<StocktakeSession>(`/stocktakes/${sessionId}/complete`, { body }),

  cancel: (sessionId: Guid, body: CancelStocktakeRequest) =>
    api.post<StocktakeSession>(`/stocktakes/${sessionId}/cancel`, { body }),
};

// ---------------------------------------------------------------------------
// Bulk asset changes
// ---------------------------------------------------------------------------

/**
 * Two steps on purpose. The preview shows exactly what would change and returns
 * a fingerprint; the commit quotes it back. The whole batch fails if any asset
 * has moved on — there is no partial application to reason about afterwards.
 */
export const bulkChangesApi = {
  preview: (body: PreviewBulkChangeRequest) =>
    api.post<BulkChangePreview>('/assets/bulk-changes/preview', { body }),

  commit: (jobId: Guid, body: CommitBulkChangeRequest) =>
    api.post<BulkChangeCommitResult>(`/assets/bulk-changes/${jobId}/commit`, { body }),
};

// ---------------------------------------------------------------------------
// Leaver cases
// ---------------------------------------------------------------------------

export const leaversApi = {
  list: (params: LeaverSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<LeaverCaseSummary>>('/leavers', { query: { ...params }, signal }),

  get: (leaverCaseId: Guid, signal?: AbortSignal) =>
    api.get<LeaverCase>(`/leavers/${leaverCaseId}`, { signal }),

  /** The full custody list, for cases where the embedded 100 is not enough. */
  assets: (leaverCaseId: Guid, page: number, pageSize: number, signal?: AbortSignal) =>
    api.get<PagedResponse<LeaverCustodyAsset>>(`/leavers/${leaverCaseId}/assets`, {
      query: { page, pageSize },
      signal,
    }),

  /** People who still hold equipment and arguably should not. */
  exceptions: (page: number, pageSize: number, signal?: AbortSignal) =>
    api.get<PagedResponse<CustodyException>>('/leavers/exceptions', {
      query: { page, pageSize },
      signal,
    }),

  /** Opening a case returns nothing — it records the leaving and gathers custody. */
  create: (body: CreateLeaverCaseRequest) => api.post<LeaverCase>('/leavers', { body }),

  update: (leaverCaseId: Guid, body: UpdateLeaverCaseRequest) =>
    api.put<LeaverCase>(`/leavers/${leaverCaseId}`, { body }),

  /** `409 leaver_has_open_custody` until everything is returned. */
  complete: (leaverCaseId: Guid, body: ChangeLeaverCaseStatusRequest) =>
    api.post<LeaverCase>(`/leavers/${leaverCaseId}/complete`, { body }),

  cancel: (leaverCaseId: Guid, body: ChangeLeaverCaseStatusRequest) =>
    api.post<LeaverCase>(`/leavers/${leaverCaseId}/cancel`, { body }),

  rehire: (leaverCaseId: Guid, body: ChangeLeaverCaseStatusRequest) =>
    api.post<LeaverCase>(`/leavers/${leaverCaseId}/rehire`, { body }),
};

// ---------------------------------------------------------------------------
// Asset attachments
// ---------------------------------------------------------------------------

export const attachmentsApi = {
  list: (
    assetId: Guid,
    includeArchived: boolean,
    page: number,
    pageSize: number,
    signal?: AbortSignal,
  ) =>
    api.get<PagedResponse<AssetAttachment>>(`/assets/${assetId}/attachments`, {
      query: { includeArchived, page, pageSize },
      signal,
    }),

  /**
   * Multipart. The client must not set `Content-Type` — the browser has to add
   * the boundary itself, and `api` passes `FormData` through untouched.
   */
  upload: (assetId: Guid, category: AttachmentCategory, file: File) => {
    const form = new FormData();
    form.append('category', category);
    form.append('file', file);
    return api.post<AssetAttachment>(`/assets/${assetId}/attachments`, { body: form });
  },

  async download(
    assetId: Guid,
    attachmentId: Guid,
    signal?: AbortSignal,
  ): Promise<{ blob: Blob; fileName: string }> {
    const response = await api.raw(
      `/assets/${assetId}/attachments/${attachmentId}/download`,
      { signal },
    );
    return {
      blob: await response.blob(),
      fileName: fileNameFromDisposition(response) ?? 'attachment',
    };
  },

  /** Archives rather than deletes; the file stays retrievable by an auditor. */
  archive: (assetId: Guid, attachmentId: Guid, body: ArchiveAttachmentRequest) =>
    api.post<AssetAttachment>(
      `/assets/${assetId}/attachments/${attachmentId}/archive`,
      { body },
    ),
};

// ---------------------------------------------------------------------------
// Asset bundles
// ---------------------------------------------------------------------------

export const bundlesApi = {
  list: (params: BundleSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<AssetBundleListItem>>('/asset-bundles', {
      query: { ...params },
      signal,
    }),

  get: (bundleId: Guid, signal?: AbortSignal) =>
    api.get<AssetBundle>(`/asset-bundles/${bundleId}`, { signal }),

  create: (body: CreateAssetBundleRequest) =>
    api.post<AssetBundle>('/asset-bundles', { body }),

  /** Replaces the member list wholesale — send every member, not a delta. */
  update: (bundleId: Guid, body: UpdateAssetBundleRequest) =>
    api.put<AssetBundle>(`/asset-bundles/${bundleId}`, { body }),

  archive: (bundleId: Guid, version: Guid) =>
    api.post<AssetBundle>(`/asset-bundles/${bundleId}/archive`, { body: { version } }),

  restore: (bundleId: Guid, version: Guid) =>
    api.post<AssetBundle>(`/asset-bundles/${bundleId}/restore`, { body: { version } }),
};
