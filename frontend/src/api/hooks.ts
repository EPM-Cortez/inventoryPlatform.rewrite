import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryOptions,
} from '@tanstack/react-query';
import { useCallback } from 'react';

import type {
  AgentSearchParams,
  CreateAgentEnrollmentProfileRequest,
  CreateAgentKeyRecoveryRequest,
  CreateAndLinkManagedAgentAssetRequest,
  DismissManagedAgentRequest,
  LinkManagedAgentRequest,
  RestoreManagedAgentReviewRequest,
  RevokeAgentKeyRecoveryRequest,
  RevokeManagedAgentRequest,
  UnlinkManagedAgentRequest,
} from './agentTypes';
import type {
  AuditSearchParams,
  BeginMfaEnrollmentRequest,
  ConfirmMfaEnrollmentRequest,
  RegenerateMfaRecoveryCodesRequest,
  SignOffLegacyReconciliationRequest,
  UpdateSchoolSettingsRequest,
} from './adminTypes';
import type {
  AssetBundle,
  AttachmentCategory,
  BundleSearchParams,
  ChangeLeaverCaseStatusRequest,
  CommitBulkChangeRequest,
  CreateAssetBundleRequest,
  CreateLeaverCaseRequest,
  LeaverCase,
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
  StocktakeItemFilter,
  StocktakeSearchParams,
} from './stocktakeTypes';
import type {
  DashboardExceptionCode,
  InventoryDistributionParams,
} from './dashboardTypes';
import {
  agentsApi,
  assetsApi,
  attachmentsApi,
  bulkChangesApi,
  bundlesApi,
  dashboardApi,
  leaversApi,
  auditApi,
  assignmentsApi,
  categoriesApi,
  departmentsApi,
  exportsApi,
  historyApi,
  importsApi,
  labelsApi,
  locationsApi,
  mfaApi,
  operationsApi,
  peopleApi,
  qrApi,
  referenceExportsApi,
  reconciliationApi,
  referenceImportsApi,
  savedSearchesApi,
  settingsApi,
  stocktakesApi,
  usersApi,
  warrantiesApi,
  type DepartmentSearchParams,
  type PersonSearchParams,
} from './endpoints';
import { keys } from './queryKeys';
import type {
  AddAssetNoteRequest,
  AssignAssetRequest,
  Asset,
  AssetImportMode,
  AssetSearchParams,
  CreateAssetRequest,
  CreateCategoryRequest,
  CreateDepartmentRequest,
  CreateLocationRequest,
  CreatePersonRequest,
  CreateUserRequest,
  CreateWarrantyRequest,
  DisposeAssetRequest,
  Guid,
  PrintAssetLabelRequest,
  PrintLabelBatchRequest,
  ResetUserPasswordRequest,
  ReferenceDataImportMode,
  ReferenceDataImportResource,
  RetireAssetRequest,
  OverdueLoanSearchParams,
  CreateSavedAssetSearchRequest,
  UpdateSavedAssetSearchRequest,
  ReturnAssetRequest,
  SetUserRolesRequest,
  UpdateAssetRequest,
  UpdateAssetTagPolicyRequest,
  UpdateCategoryRequest,
  UpdateExpectedReturnRequest,
  UpdateDepartmentRequest,
  UpdateLocationRequest,
  UpdatePersonRequest,
  UpdateUserRequest,
  UpdateWarrantyRequest,
} from './types';

/**
 * Query and mutation hooks.
 *
 * Two rules keep optimistic concurrency workable:
 *
 * 1. Any mutation that returns the updated aggregate writes it straight into the
 *    detail cache. The `version` the user's next edit sends is then the one the
 *    server just issued, instead of the one they loaded minutes ago.
 * 2. Any mutation that can change an asset's derived state (status, condition,
 *    location, holder) invalidates the *lists* too, because those carry their own
 *    copies of the same fields.
 */

// ---------------------------------------------------------------------------
// Assets
// ---------------------------------------------------------------------------

export function useAssets(params: AssetSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.assets.list(params),
    queryFn: ({ signal }) => assetsApi.list(params, signal),
    enabled,
    // Keeps the previous page on screen while the next one loads, so paging and
    // filtering do not flash an empty table.
    placeholderData: (previous) => previous,
  });
}

export function useAsset(assetId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.assets.detail(assetId ?? ''),
    queryFn: ({ signal }) => assetsApi.get(assetId!, signal),
    enabled: Boolean(assetId) && enabled,
  });
}

export function useAssetByTag(assetTag: string | undefined) {
  return useQuery({
    queryKey: keys.assets.byTag(assetTag ?? ''),
    queryFn: ({ signal }) => assetsApi.getByTag(assetTag!, signal),
    enabled: Boolean(assetTag),
    retry: false,
  });
}

/** Shared cache write for every endpoint that returns a whole `Asset`. */
function useAssetWriter() {
  const queryClient = useQueryClient();
  return (asset: Asset) => {
    queryClient.setQueryData(keys.assets.detail(asset.id), asset);
    void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
  };
}

export function useCreateAsset() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAssetRequest) => assetsApi.create(body),
    onSuccess: (asset) => {
      queryClient.setQueryData(keys.assets.detail(asset.id), asset);
      void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
    },
  });
}

export function useUpdateAsset(assetId: Guid) {
  const writeAsset = useAssetWriter();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateAssetRequest) => assetsApi.update(assetId, body),
    onSuccess: (asset) => {
      writeAsset(asset);
      // An edit is recorded as an AssetEvent in the same transaction.
      void queryClient.invalidateQueries({
        queryKey: [...keys.assets.detail(assetId), 'history'],
      });
    },
  });
}

export function useRetireAsset(assetId: Guid) {
  const writeAsset = useAssetWriter();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RetireAssetRequest) => assetsApi.retire(assetId, body),
    onSuccess: (asset) => {
      writeAsset(asset);
      // Retirement closes any open custody, so holder views change too.
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
      void queryClient.invalidateQueries({ queryKey: keys.people.all });
      void queryClient.invalidateQueries({ queryKey: keys.departments.all });
    },
  });
}

export function useDisposeAsset(assetId: Guid) {
  const writeAsset = useAssetWriter();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: DisposeAssetRequest) => assetsApi.dispose(assetId, body),
    onSuccess: (asset) => {
      writeAsset(asset);
      // Disposal closes any open custody, so holder views change too.
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
      void queryClient.invalidateQueries({ queryKey: keys.people.all });
      void queryClient.invalidateQueries({ queryKey: keys.departments.all });
    },
  });
}

export function useRestoreAsset(assetId: Guid) {
  const writeAsset = useAssetWriter();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (version: Guid) => assetsApi.restore(assetId, version),
    onSuccess: (asset) => {
      writeAsset(asset);
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
    },
  });
}

// ---------------------------------------------------------------------------
// Asset history
// ---------------------------------------------------------------------------

export function useAssetHistory(assetId: Guid, page: number, pageSize: number) {
  return useQuery({
    queryKey: keys.assets.history(assetId, page, pageSize),
    queryFn: ({ signal }) => historyApi.list(assetId, page, pageSize, signal),
    placeholderData: (previous) => previous,
  });
}

export function useAddAssetNote(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AddAssetNoteRequest) => historyApi.addNote(assetId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...keys.assets.detail(assetId), 'history'],
      });
    },
  });
}

// ---------------------------------------------------------------------------
// Assignments (custody)
// ---------------------------------------------------------------------------

export function useAssetAssignments(
  assetId: Guid,
  page: number,
  pageSize: number,
  enabled = true,
) {
  return useQuery({
    queryKey: keys.assets.assignments(assetId, page, pageSize),
    queryFn: ({ signal }) => assignmentsApi.list(assetId, page, pageSize, signal),
    placeholderData: (previous) => previous,
    enabled,
  });
}

/**
 * Assign and return both mutate the asset and the assignment atomically, so
 * everything touching either — the asset itself, its history, and both holders'
 * inventories — has to be refetched. Refetching the asset detail is what supplies
 * the caller's next `assetVersion`.
 */
function useCustodyInvalidation(assetId: Guid) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
    void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
    void queryClient.invalidateQueries({ queryKey: keys.people.all });
    void queryClient.invalidateQueries({ queryKey: keys.departments.all });
  };
}

export function useAssignAsset(assetId: Guid) {
  const invalidate = useCustodyInvalidation(assetId);
  return useMutation({
    mutationFn: (body: AssignAssetRequest) => assignmentsApi.assign(assetId, body),
    onSuccess: invalidate,
  });
}

export function useReturnAsset(assetId: Guid) {
  const invalidate = useCustodyInvalidation(assetId);
  return useMutation({
    mutationFn: ({
      assignmentId,
      body,
    }: {
      assignmentId: Guid;
      body: ReturnAssetRequest;
    }) => assignmentsApi.return(assetId, assignmentId, body),
    onSuccess: invalidate,
  });
}

// ---------------------------------------------------------------------------
// Warranties
// ---------------------------------------------------------------------------

export function useAssetWarranties(assetId: Guid, includeArchived: boolean, enabled = true) {
  return useQuery({
    queryKey: keys.assets.warranties(assetId, includeArchived),
    queryFn: ({ signal }) => warrantiesApi.listForAsset(assetId, includeArchived, signal),
    enabled,
  });
}

/** Coverage changes alter the asset's warranty summary, so the asset is refreshed too. */
function useWarrantyInvalidation(assetId: Guid | undefined) {
  const queryClient = useQueryClient();
  return () => {
    if (assetId) {
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
    }
    void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
    void queryClient.invalidateQueries({ queryKey: keys.warranties.all });
  };
}

export function useAddWarranty(assetId: Guid) {
  const invalidate = useWarrantyInvalidation(assetId);
  return useMutation({
    mutationFn: (body: CreateWarrantyRequest) => warrantiesApi.add(assetId, body),
    onSuccess: invalidate,
  });
}

export function useUpdateWarranty(assetId: Guid) {
  const invalidate = useWarrantyInvalidation(assetId);
  return useMutation({
    mutationFn: ({ warrantyId, body }: { warrantyId: Guid; body: UpdateWarrantyRequest }) =>
      warrantiesApi.update(warrantyId, body),
    onSuccess: invalidate,
  });
}

export function useArchiveWarranty(assetId: Guid) {
  const invalidate = useWarrantyInvalidation(assetId);
  return useMutation({
    mutationFn: ({ warrantyId, version }: { warrantyId: Guid; version: Guid }) =>
      warrantiesApi.archive(warrantyId, version),
    onSuccess: invalidate,
  });
}

export function useExpiringWarranties(
  withinDays: number | undefined,
  page: number,
  pageSize: number,
  enabled = true,
) {
  return useQuery({
    queryKey: keys.warranties.expiring(withinDays, page, pageSize),
    queryFn: ({ signal }) =>
      warrantiesApi.expiring({ withinDays, page, pageSize }, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

// ---------------------------------------------------------------------------
// QR
// ---------------------------------------------------------------------------

export function useIssueQr(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => qrApi.issue(assetId),
    onSuccess: (identifier) => {
      queryClient.setQueryData(keys.assets.qr(assetId), identifier);
    },
  });
}

export function useRotateQr(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => qrApi.rotate(assetId),
    onSuccess: (identifier) => {
      queryClient.setQueryData(keys.assets.qr(assetId), identifier);
    },
  });
}

export function useResolveQr(token: string | undefined) {
  return useQuery({
    queryKey: keys.qr.resolve(token ?? ''),
    queryFn: ({ signal }) => qrApi.resolve(token!, signal),
    enabled: Boolean(token),
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// Multi-factor authentication
// ---------------------------------------------------------------------------

export function useMfaStatus(enabled = true) {
  return useQuery({
    queryKey: keys.mfa.status(),
    queryFn: ({ signal }) => mfaApi.status(signal),
    enabled,
  });
}

/**
 * Enrollment is two calls. Beginning it returns a secret but changes nothing;
 * only confirming with a working code turns MFA on. Both invalidate the session,
 * because `mustChangePassword`-style flags and MFA state live on `/auth/me`.
 */
function useMfaInvalidation() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.mfa.status() });
    void queryClient.invalidateQueries({ queryKey: keys.session });
  };
}

export function useBeginMfaEnrollment() {
  return useMutation({
    mutationFn: (body: BeginMfaEnrollmentRequest) => mfaApi.beginEnrollment(body),
  });
}

export function useConfirmMfaEnrollment() {
  const invalidate = useMfaInvalidation();
  return useMutation({
    mutationFn: (body: ConfirmMfaEnrollmentRequest) => mfaApi.confirmEnrollment(body),
    onSuccess: invalidate,
  });
}

export function useRegenerateRecoveryCodes() {
  const invalidate = useMfaInvalidation();
  return useMutation({
    mutationFn: (body: RegenerateMfaRecoveryCodesRequest) =>
      mfaApi.regenerateRecoveryCodes(body),
    onSuccess: invalidate,
  });
}

export function useResetUserMfa() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, version }: { userId: Guid; version: Guid }) =>
      usersApi.resetMfa(userId, version),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.users.all });
    },
  });
}

// ---------------------------------------------------------------------------
// Audit
// ---------------------------------------------------------------------------

export function useAuditLog(params: AuditSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.audit.list(params),
    queryFn: ({ signal }) => auditApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

/**
 * The filter values present in the trail.
 *
 * Rarely changes within a session and only ever grows, so it is cached hard —
 * refetching it on every filter change would be a request per keystroke for a
 * list that is the same all day.
 */
export function useAuditVocabulary(enabled = true) {
  return useQuery({
    queryKey: keys.audit.vocabulary(),
    queryFn: ({ signal }) => auditApi.vocabulary(signal),
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}

export function useExportAudit() {
  return useMutation({
    mutationFn: (params: AuditSearchParams) => auditApi.exportCsv(params),
  });
}

// ---------------------------------------------------------------------------
// School settings and operations
// ---------------------------------------------------------------------------

export function useSchoolSettings(enabled = true) {
  return useQuery({
    queryKey: keys.settings.school(),
    queryFn: ({ signal }) => settingsApi.school(signal),
    enabled,
  });
}

export function useUpdateSchoolSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateSchoolSettingsRequest) => settingsApi.updateSchool(body),
    onSuccess: (settings) => {
      queryClient.setQueryData(keys.settings.school(), settings);
      // The warranty warning threshold changes which assets count as expiring.
      void queryClient.invalidateQueries({ queryKey: keys.warranties.all });
      void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
    },
  });
}

export function useAssetTagPolicy(enabled = true) {
  return useQuery({
    queryKey: keys.settings.assetTags(),
    queryFn: ({ signal }) => settingsApi.assetTagPolicy(signal),
    enabled,
  });
}

export function useUpdateAssetTagPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateAssetTagPolicyRequest) =>
      settingsApi.updateAssetTagPolicy(body),
    onSuccess: (policy) => {
      queryClient.setQueryData(keys.settings.assetTags(), policy);
    },
  });
}

export function useOperationalStatus(enabled = true) {
  return useQuery({
    queryKey: keys.operations.status(),
    queryFn: ({ signal }) => operationsApi.status(signal),
    enabled,
    // Live health: worth refreshing while someone is watching it.
    refetchInterval: 30_000,
    staleTime: 0,
  });
}

// ---------------------------------------------------------------------------
// Legacy reconciliation
// ---------------------------------------------------------------------------

export function useCreateReconciliationDryRun() {
  return useMutation({
    mutationFn: ({ file, mode }: { file: File; mode: AssetImportMode }) =>
      reconciliationApi.createDryRun(file, mode),
  });
}

export function useReconciliation(importJobId: Guid | undefined) {
  return useQuery({
    queryKey: keys.reconciliation.detail(importJobId ?? ''),
    queryFn: ({ signal }) => reconciliationApi.get(importJobId!, signal),
    enabled: Boolean(importJobId),
  });
}

export function useSignOffReconciliation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      importJobId,
      body,
    }: {
      importJobId: Guid;
      body: SignOffLegacyReconciliationRequest;
    }) => reconciliationApi.signOff(importJobId, body),
    onSuccess: (_result, variables) => {
      void queryClient.invalidateQueries({
        queryKey: keys.reconciliation.detail(variables.importJobId),
      });
      void queryClient.invalidateQueries({
        queryKey: keys.reconciliation.cutover(variables.importJobId),
      });
    },
  });
}

export function useCutoverPackage(importJobId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.reconciliation.cutover(importJobId ?? ''),
    queryFn: ({ signal }) => reconciliationApi.cutoverPackage(importJobId!, signal),
    enabled: Boolean(importJobId) && enabled,
  });
}

// ---------------------------------------------------------------------------
// Managed agents
// ---------------------------------------------------------------------------

export function useAgents(params: AgentSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.agents.list(params),
    queryFn: ({ signal }) => agentsApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useAgent(agentId: Guid | undefined) {
  return useQuery({
    queryKey: keys.agents.detail(agentId ?? ''),
    queryFn: ({ signal }) => agentsApi.get(agentId!, signal),
    enabled: Boolean(agentId),
  });
}

/**
 * Device inventory for one asset.
 *
 * A 404 is the normal answer for an asset with no agent, so it must not retry —
 * most assets in a school are not managed machines.
 */
export function useAssetDeviceInventory(assetId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.agents.forAsset(assetId ?? ''),
    queryFn: ({ signal }) => agentsApi.forAsset(assetId!, signal),
    enabled: Boolean(assetId) && enabled,
    retry: false,
  });
}

/** Linking changes which asset the evidence belongs to, so both caches drop. */
function useAgentInvalidation() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.agents.all });
    void queryClient.invalidateQueries({ queryKey: keys.assets.all });
  };
}

export function useLinkAgent() {
  const invalidate = useAgentInvalidation();
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: Guid; body: LinkManagedAgentRequest }) =>
      agentsApi.link(agentId, body),
    onSuccess: invalidate,
  });
}

export function useRevokeAgent() {
  const invalidate = useAgentInvalidation();
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: Guid; body: RevokeManagedAgentRequest }) =>
      agentsApi.revoke(agentId, body),
    onSuccess: invalidate,
  });
}

export function useAgentFleetSummary(enabled = true) {
  return useQuery({
    queryKey: keys.agents.summary(),
    queryFn: ({ signal }) => agentsApi.summary(signal),
    enabled,
  });
}

export function useAgentInstaller(enabled = true) {
  return useQuery({
    queryKey: keys.agents.installer(),
    queryFn: ({ signal }) => agentsApi.installer(signal),
    enabled,
    // Built into the server image, so it only changes on deployment.
    staleTime: Infinity,
  });
}

export function useAgentKeyRecovery(
  agentId: Guid | undefined,
  includeClosed: boolean,
  enabled = true,
) {
  return useQuery({
    queryKey: keys.agents.keyRecovery(agentId ?? '', includeClosed),
    queryFn: ({ signal }) =>
      agentsApi.keyRecovery(agentId!, { includeClosed, pageSize: 50 }, signal),
    enabled: Boolean(agentId) && enabled,
  });
}

/**
 * Invalidates both the open and the closed list.
 *
 * Creating or revoking moves an authorisation between them, so refreshing only
 * the one on screen leaves the other stale — and the toggle then shows a state
 * that has not existed for several minutes.
 */
function useKeyRecoveryInvalidation(agentId: Guid) {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: keys.agents.detail(agentId) });
  }, [agentId, queryClient]);
}

export function useCreateAgentKeyRecovery(agentId: Guid) {
  const invalidate = useKeyRecoveryInvalidation(agentId);
  return useMutation({
    mutationFn: (body: CreateAgentKeyRecoveryRequest) =>
      agentsApi.createKeyRecovery(agentId, body),
    onSuccess: invalidate,
  });
}

export function useRevokeAgentKeyRecovery(agentId: Guid) {
  const invalidate = useKeyRecoveryInvalidation(agentId);
  return useMutation({
    mutationFn: ({
      authorizationId,
      body,
    }: {
      authorizationId: Guid;
      body: RevokeAgentKeyRecoveryRequest;
    }) => agentsApi.revokeKeyRecovery(agentId, authorizationId, body),
    onSuccess: invalidate,
  });
}

export function useUnlinkAgent() {
  const invalidate = useAgentInvalidation();
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: Guid; body: UnlinkManagedAgentRequest }) =>
      agentsApi.unlink(agentId, body),
    onSuccess: invalidate,
  });
}

export function useDismissAgent() {
  const invalidate = useAgentInvalidation();
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: Guid; body: DismissManagedAgentRequest }) =>
      agentsApi.dismiss(agentId, body),
    onSuccess: invalidate,
  });
}

export function useRestoreAgentReview() {
  const invalidate = useAgentInvalidation();
  return useMutation({
    mutationFn: ({
      agentId,
      body,
    }: {
      agentId: Guid;
      body: RestoreManagedAgentReviewRequest;
    }) => agentsApi.restoreReview(agentId, body),
    onSuccess: invalidate,
  });
}

/**
 * Creates an asset and links the agent to it in one server-side transaction.
 *
 * Invalidates both sides: a new asset appears in every asset list, and the
 * agent's match state moves out of the review queue.
 */
export function useCreateAndLinkAgentAsset() {
  const queryClient = useQueryClient();
  const invalidate = useAgentInvalidation();
  return useMutation({
    mutationFn: ({
      agentId,
      body,
    }: {
      agentId: Guid;
      body: CreateAndLinkManagedAgentAssetRequest;
    }) => agentsApi.createAndLinkAsset(agentId, body),
    onSuccess: (result) => {
      queryClient.setQueryData(keys.assets.detail(result.asset.id), result.asset);
      void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
      invalidate();
    },
  });
}

export function useAgentEnrollmentProfiles(params: AgentSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.agents.enrollmentProfiles(params),
    queryFn: ({ signal }) => agentsApi.enrollmentProfiles(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useCreateAgentEnrollmentProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAgentEnrollmentProfileRequest) =>
      agentsApi.createEnrollmentProfile(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.agents.all });
    },
  });
}

export function useRevokeAgentEnrollmentProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ profileId, version }: { profileId: Guid; version: Guid }) =>
      agentsApi.revokeEnrollmentProfile(profileId, version),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.agents.all });
    },
  });
}

// ---------------------------------------------------------------------------
// CSV import and export
// ---------------------------------------------------------------------------

/**
 * Export is a mutation only in the sense that it is user-initiated and produces
 * a download — there is nothing to cache, and re-running it should always hit
 * the server for current data.
 */
export function useExportAssets() {
  return useMutation({
    mutationFn: (params: AssetSearchParams) => exportsApi.assetsCsv(params),
  });
}

export function useValidateAssetImport() {
  return useMutation({
    mutationFn: ({ file, mode }: { file: File; mode: AssetImportMode }) =>
      importsApi.validate(file, mode),
  });
}

export function useImportJob(importJobId: Guid | undefined) {
  return useQuery({
    queryKey: keys.imports.detail(importJobId ?? ''),
    queryFn: ({ signal }) => importsApi.get(importJobId!, signal),
    enabled: Boolean(importJobId),
  });
}

/**
 * Applying the import. Everything about the register can change in one commit,
 * so the whole asset cache — and the reference data an import can introduce —
 * is dropped rather than surgically patched.
 */
export function useCommitAssetImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ importJobId, version }: { importJobId: Guid; version: Guid }) =>
      importsApi.commit(importJobId, version),
    onSuccess: (_result, variables) => {
      void queryClient.invalidateQueries({ queryKey: keys.assets.all });
      void queryClient.invalidateQueries({ queryKey: keys.people.all });
      void queryClient.invalidateQueries({ queryKey: keys.departments.all });
      void queryClient.invalidateQueries({ queryKey: keys.categories.all });
      void queryClient.invalidateQueries({ queryKey: keys.locations.all });
      void queryClient.invalidateQueries({
        queryKey: keys.imports.detail(variables.importJobId),
      });
    },
  });
}

// ---------------------------------------------------------------------------
// Reference data import and export
// ---------------------------------------------------------------------------

export function useExportPeople() {
  return useMutation({ mutationFn: () => referenceExportsApi.peopleCsv() });
}

export function useExportDepartments() {
  return useMutation({ mutationFn: () => referenceExportsApi.departmentsCsv() });
}

export function useExportLocations() {
  return useMutation({ mutationFn: () => referenceExportsApi.locationsCsv() });
}

export function useValidateReferenceImport(resource: ReferenceDataImportResource) {
  return useMutation({
    mutationFn: ({ file, mode }: { file: File; mode: ReferenceDataImportMode }) =>
      referenceImportsApi.validate(resource, file, mode),
  });
}

/**
 * Committing reference data can rename or deactivate anything an asset points
 * at, so the asset caches are dropped alongside the resource's own.
 */
export function useCommitReferenceImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ importJobId, version }: { importJobId: Guid; version: Guid }) =>
      referenceImportsApi.commit(importJobId, version),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.people.all });
      void queryClient.invalidateQueries({ queryKey: keys.departments.all });
      void queryClient.invalidateQueries({ queryKey: keys.locations.all });
      void queryClient.invalidateQueries({ queryKey: keys.assets.all });
    },
  });
}

// ---------------------------------------------------------------------------
// Label printing
// ---------------------------------------------------------------------------

export function useLabelStocks(enabled = true) {
  return useQuery({
    queryKey: keys.labels.stocks(),
    queryFn: ({ signal }) => labelsApi.stocks(signal),
    enabled,
    staleTime: Infinity,
    gcTime: Infinity,
  });
}

/**
 * Printing is a mutation, not a query: it is a POST, it writes an audit record,
 * and with `issueMissingQr` it can mint QR identifiers as a side effect. That
 * last part is why the asset caches are invalidated on success — an asset that
 * had no label now has one.
 */
export function usePrintAssetLabel(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: PrintAssetLabelRequest) => labelsApi.printAsset(assetId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.assets.qr(assetId) });
    },
  });
}

export function usePrintLabelBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: PrintLabelBatchRequest) => labelsApi.printBatch(body),
    onSuccess: (_document, variables) => {
      if (!variables.issueMissingQr) return;
      for (const item of variables.items) {
        void queryClient.invalidateQueries({ queryKey: keys.assets.qr(item.assetId) });
      }
    },
  });
}

export function useOverdueLoans(params: OverdueLoanSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.assignments.overdue(params),
    queryFn: ({ signal }) => assignmentsApi.overdue(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

/**
 * Sets or clears when a loan is due back.
 *
 * Invalidates the overdue list as well as the asset: a changed date can move a
 * loan into or out of that list, and leaving it stale would keep showing a loan
 * as late moments after someone extended it.
 */
export function useUpdateExpectedReturn(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      assignmentId,
      body,
    }: {
      assignmentId: Guid;
      body: UpdateExpectedReturnRequest;
    }) => assignmentsApi.updateExpectedReturn(assetId, assignmentId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
      void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
      void queryClient.invalidateQueries({ queryKey: keys.assignments.all });
    },
  });
}

// ---------------------------------------------------------------------------
// People
// ---------------------------------------------------------------------------

export function usePeople(params: PersonSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.people.list(params),
    queryFn: ({ signal }) => peopleApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function usePerson(personId: Guid | undefined) {
  return useQuery({
    queryKey: keys.people.detail(personId ?? ''),
    queryFn: ({ signal }) => peopleApi.get(personId!, signal),
    enabled: Boolean(personId),
  });
}

/**
 * How many assets a person holds right now — one row requested, the total read
 * from the page envelope. Custody writes invalidate `people.all`, so this stays
 * current with assign and return.
 */
export function usePersonHeldCount(personId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.people.heldCount(personId ?? ''),
    queryFn: async ({ signal }) =>
      (
        await peopleApi.assets(
          personId!,
          { includeHistory: false, page: 1, pageSize: 1 },
          signal,
        )
      ).totalCount,
    enabled: Boolean(personId) && enabled,
  });
}

export function usePersonAssets(
  personId: Guid | undefined,
  includeHistory: boolean,
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: keys.people.assets(personId ?? '', includeHistory, page),
    queryFn: ({ signal }) =>
      peopleApi.assets(personId!, { includeHistory, page, pageSize }, signal),
    enabled: Boolean(personId),
    placeholderData: (previous) => previous,
  });
}

export function useCreatePerson() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreatePersonRequest) => peopleApi.create(body),
    onSuccess: (person) => {
      queryClient.setQueryData(keys.people.detail(person.id), person);
      void queryClient.invalidateQueries({ queryKey: keys.people.lists() });
    },
  });
}

export function useUpdatePerson(personId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdatePersonRequest) => peopleApi.update(personId, body),
    onSuccess: (person) => {
      queryClient.setQueryData(keys.people.detail(person.id), person);
      void queryClient.invalidateQueries({ queryKey: keys.people.lists() });
      // The holder's display name is embedded in every asset row they hold.
      void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
    },
  });
}

// ---------------------------------------------------------------------------
// Departments
// ---------------------------------------------------------------------------

export function useDepartments(params: DepartmentSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.departments.list(params),
    queryFn: ({ signal }) => departmentsApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useDepartment(departmentId: Guid | undefined) {
  return useQuery({
    queryKey: keys.departments.detail(departmentId ?? ''),
    queryFn: ({ signal }) => departmentsApi.get(departmentId!, signal),
    enabled: Boolean(departmentId),
  });
}

/**
 * How many assets a department holds right now — one row requested, the total
 * read from the page envelope. Custody writes invalidate `departments.all`, so
 * this stays current with assign and return.
 */
export function useDepartmentHeldCount(departmentId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.departments.heldCount(departmentId ?? ''),
    queryFn: async ({ signal }) =>
      (
        await departmentsApi.assets(
          departmentId!,
          { includeHistory: false, page: 1, pageSize: 1 },
          signal,
        )
      ).totalCount,
    enabled: Boolean(departmentId) && enabled,
  });
}

export function useDepartmentAssets(
  departmentId: Guid | undefined,
  includeHistory: boolean,
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: keys.departments.assets(departmentId ?? '', includeHistory, page),
    queryFn: ({ signal }) =>
      departmentsApi.assets(departmentId!, { includeHistory, page, pageSize }, signal),
    enabled: Boolean(departmentId),
    placeholderData: (previous) => previous,
  });
}

export function useCreateDepartment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDepartmentRequest) => departmentsApi.create(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.departments.all });
    },
  });
}

export function useUpdateDepartment(departmentId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateDepartmentRequest) =>
      departmentsApi.update(departmentId, body),
    onSuccess: (department) => {
      queryClient.setQueryData(keys.departments.detail(department.id), department);
      void queryClient.invalidateQueries({ queryKey: keys.departments.lists() });
      void queryClient.invalidateQueries({ queryKey: keys.assets.lists() });
    },
  });
}

// ---------------------------------------------------------------------------
// Reference data
//
// Categories and locations are small, slow-moving and needed by nearly every
// form. They are cached hard and shared rather than refetched per picker.
// ---------------------------------------------------------------------------

const REFERENCE_QUERY_OPTIONS = {
  staleTime: 10 * 60_000,
  gcTime: 30 * 60_000,
} satisfies Partial<UseQueryOptions>;

/**
 * Reference lists are paged, but they are picker fodder: a dropdown that
 * paginates is worse than one that shows everything. 200 is the server's cap,
 * and the pages that render these surface `totalCount` so a school that
 * genuinely exceeds it is not silently truncated.
 */
const REFERENCE_PAGE_SIZE = 200;

export function useCategories(includeInactive = false, enabled = true) {
  return useQuery({
    queryKey: keys.categories.list(includeInactive),
    queryFn: ({ signal }) =>
      categoriesApi.list({ includeInactive, pageSize: REFERENCE_PAGE_SIZE }, signal),
    enabled,
    ...REFERENCE_QUERY_OPTIONS,
  });
}

export function useCreateCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCategoryRequest) => categoriesApi.create(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.categories.all });
    },
  });
}

export function useUpdateCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ categoryId, ...body }: UpdateCategoryRequest & { categoryId: Guid }) =>
      categoriesApi.update(categoryId, body),
    onSuccess: () => {
      // Assets carry a denormalised category name, so their lists go too.
      void queryClient.invalidateQueries({ queryKey: keys.categories.all });
      void queryClient.invalidateQueries({ queryKey: keys.assets.all });
    },
  });
}

export function useLocations(includeInactive = false, enabled = true) {
  return useQuery({
    queryKey: keys.locations.list(includeInactive),
    queryFn: ({ signal }) =>
      locationsApi.list({ includeInactive, pageSize: REFERENCE_PAGE_SIZE }, signal),
    enabled,
    ...REFERENCE_QUERY_OPTIONS,
  });
}

export function useCreateLocation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateLocationRequest) => locationsApi.create(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.locations.all });
    },
  });
}

export function useUpdateLocation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ locationId, ...body }: UpdateLocationRequest & { locationId: Guid }) =>
      locationsApi.update(locationId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.locations.all });
      void queryClient.invalidateQueries({ queryKey: keys.assets.all });
    },
  });
}

// ---------------------------------------------------------------------------
// Users
// ---------------------------------------------------------------------------

export function useUsers(enabled = true) {
  return useQuery({
    queryKey: keys.users.list(),
    queryFn: ({ signal }) => usersApi.list(signal),
    enabled,
  });
}

export function useRolePresets(enabled = true) {
  return useQuery({
    queryKey: keys.users.rolePresets(),
    queryFn: ({ signal }) => usersApi.rolePresets(signal),
    enabled,
    // Compiled into the server, so they cannot change while the tab is open.
    staleTime: Infinity,
    gcTime: Infinity,
  });
}

/**
 * Every account mutation invalidates the whole `users` tree.
 *
 * Disable and archive answer `204`, so there is no fresh version to write back —
 * a refetch is the only way to get one, and without it the next action on that
 * row would send a stale version and be rejected with `409`.
 *
 * Any change can also alter the *acting* user's own permissions (they may have
 * edited themselves), so the session is re-read too.
 */
function useUserInvalidation() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.users.all });
    void queryClient.invalidateQueries({ queryKey: keys.session });
  };
}

export function useCreateUser() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: (body: CreateUserRequest) => usersApi.create(body),
    onSuccess: invalidate,
  });
}

export function useUpdateUser() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ userId, body }: { userId: Guid; body: UpdateUserRequest }) =>
      usersApi.update(userId, body),
    onSuccess: invalidate,
  });
}

export function useSetUserRoles() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ userId, body }: { userId: Guid; body: SetUserRolesRequest }) =>
      usersApi.setRoles(userId, body),
    onSuccess: invalidate,
  });
}

export function useDisableUser() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ userId, version }: { userId: Guid; version: Guid }) =>
      usersApi.disable(userId, version),
    onSuccess: invalidate,
  });
}

export function useArchiveUser() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ userId, version }: { userId: Guid; version: Guid }) =>
      usersApi.archive(userId, version),
    onSuccess: invalidate,
  });
}

export function useRestoreUser() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ userId, version }: { userId: Guid; version: Guid }) =>
      usersApi.restore(userId, version),
    onSuccess: invalidate,
  });
}

export function useResetUserPassword() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ userId, body }: { userId: Guid; body: ResetUserPasswordRequest }) =>
      usersApi.resetPassword(userId, body),
    // A reset bumps the version and sets `mustChangePassword`.
    onSuccess: invalidate,
  });
}

// ---------------------------------------------------------------------------
// Saved asset searches
// ---------------------------------------------------------------------------

export function useSavedSearches(enabled = true) {
  return useQuery({
    queryKey: keys.savedSearches.list(),
    queryFn: ({ signal }) => savedSearchesApi.list(1, 100, signal),
    enabled,
  });
}

/** Resolves to `null` when the user has not chosen a default. */
export function useDefaultSavedSearch(enabled = true) {
  return useQuery({
    queryKey: keys.savedSearches.default(),
    queryFn: ({ signal }) => savedSearchesApi.default(signal),
    enabled,
  });
}

/** Every write invalidates both the list and the default, since either can move. */
function useSavedSearchInvalidation() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.savedSearches.all });
  };
}

export function useCreateSavedSearch() {
  const invalidate = useSavedSearchInvalidation();
  return useMutation({
    mutationFn: (body: CreateSavedAssetSearchRequest) => savedSearchesApi.create(body),
    onSuccess: invalidate,
  });
}

export function useUpdateSavedSearch() {
  const invalidate = useSavedSearchInvalidation();
  return useMutation({
    mutationFn: ({
      savedSearchId,
      body,
    }: {
      savedSearchId: Guid;
      body: UpdateSavedAssetSearchRequest;
    }) => savedSearchesApi.update(savedSearchId, body),
    onSuccess: invalidate,
  });
}

export function useSetDefaultSavedSearch() {
  const invalidate = useSavedSearchInvalidation();
  return useMutation({
    mutationFn: ({ savedSearchId, version }: { savedSearchId: Guid; version: Guid }) =>
      savedSearchesApi.setDefault(savedSearchId, { version }),
    onSuccess: invalidate,
  });
}

export function useArchiveSavedSearch() {
  const invalidate = useSavedSearchInvalidation();
  return useMutation({
    mutationFn: ({ savedSearchId, version }: { savedSearchId: Guid; version: Guid }) =>
      savedSearchesApi.archive(savedSearchId, { version }),
    onSuccess: invalidate,
  });
}

// ---------------------------------------------------------------------------
// Dashboard
// ---------------------------------------------------------------------------

/** Categories beyond this are folded into the truncation flag by the server. */
const DASHBOARD_CATEGORY_LIMIT = 12;

export function useDashboardSummary(enabled = true) {
  return useQuery({
    queryKey: keys.dashboard.summary(DASHBOARD_CATEGORY_LIMIT),
    queryFn: ({ signal }) => dashboardApi.summary(DASHBOARD_CATEGORY_LIMIT, signal),
    enabled,
  });
}

export function useInventoryByLocation(
  params: InventoryDistributionParams,
  enabled = true,
) {
  return useQuery({
    queryKey: keys.dashboard.byLocation(params),
    queryFn: ({ signal }) => dashboardApi.inventoryByLocation(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

/** Groups per request; the server caps `pageSize` at 200. */
const LOCATION_TOTALS_PAGE_SIZE = 200;
/** Stop after this many pages rather than walk an unexpectedly huge result. */
const LOCATION_TOTALS_MAX_PAGES = 25;

/**
 * Active assets recorded directly against each location, keyed by location id.
 *
 * The distribution endpoint splits each location by category and pages the
 * groups, so this walks every page and sums them. Retired and disposed assets
 * are excluded, matching the endpoint's default. `complete` is false only if the
 * page cap was hit, in which case the counts are a floor, not a total.
 */
export function useLocationAssetTotals(enabled = true) {
  return useQuery({
    queryKey: keys.dashboard.locationTotals(),
    queryFn: async ({ signal }) => {
      const totals = new Map<Guid, number>();
      let page = 1;
      let fetched = 0;
      let totalGroups: number;
      do {
        const response = await dashboardApi.inventoryByLocation(
          { page, pageSize: LOCATION_TOTALS_PAGE_SIZE },
          signal,
        );
        totalGroups = response.groups.totalCount;
        fetched += response.groups.items.length;
        for (const group of response.groups.items) {
          if (!group.locationId) continue;
          totals.set(group.locationId, (totals.get(group.locationId) ?? 0) + group.quantity);
        }
        if (response.groups.items.length === 0) break;
        page += 1;
      } while (fetched < totalGroups && page <= LOCATION_TOTALS_MAX_PAGES);

      return { totals, complete: fetched >= totalGroups };
    },
    enabled,
  });
}

export function useDashboardException(
  code: DashboardExceptionCode | undefined,
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: keys.dashboard.exception(code ?? '', page, pageSize),
    queryFn: ({ signal }) => dashboardApi.exception(code!, page, pageSize, signal),
    enabled: Boolean(code),
    placeholderData: (previous) => previous,
  });
}

// ---------------------------------------------------------------------------
// Stocktakes
// ---------------------------------------------------------------------------

export function useStocktakes(params: StocktakeSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.stocktakes.list(params),
    queryFn: ({ signal }) => stocktakesApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useStocktake(sessionId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.stocktakes.detail(sessionId ?? ''),
    queryFn: ({ signal }) => stocktakesApi.get(sessionId!, signal),
    enabled: Boolean(sessionId) && enabled,
  });
}

export function useStocktakeItems(
  sessionId: Guid | undefined,
  filter: StocktakeItemFilter,
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: keys.stocktakes.items(sessionId ?? '', filter, page, pageSize),
    queryFn: ({ signal }) =>
      stocktakesApi.items(sessionId!, filter, page, pageSize, signal),
    enabled: Boolean(sessionId),
    placeholderData: (previous) => previous,
  });
}

export function useStartStocktake() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: StartStocktakeRequest) => stocktakesApi.start(body),
    onSuccess: (session) => {
      queryClient.setQueryData(keys.stocktakes.detail(session.id), session);
      void queryClient.invalidateQueries({ queryKey: keys.stocktakes.all });
    },
  });
}

/**
 * Records a sighting.
 *
 * The response carries the new session version, which is written straight into
 * the cache — the next scan must quote it or the server rejects the request, and
 * during a walk round a building that is every few seconds.
 */
export function useRecordObservation(sessionId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RecordStocktakeObservationRequest) =>
      stocktakesApi.observe(sessionId, body),
    onSuccess: (result) => {
      queryClient.setQueryData(keys.stocktakes.detail(sessionId), result.session);
      void queryClient.invalidateQueries({
        queryKey: [...keys.stocktakes.detail(sessionId), 'items'],
      });
      void queryClient.invalidateQueries({ queryKey: keys.stocktakes.list({}) });
    },
  });
}

export function useCompleteStocktake(sessionId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: StocktakeActionRequest) =>
      stocktakesApi.complete(sessionId, body),
    onSuccess: (session) => {
      queryClient.setQueryData(keys.stocktakes.detail(sessionId), session);
      void queryClient.invalidateQueries({ queryKey: keys.stocktakes.all });
    },
  });
}

export function useCancelStocktake(sessionId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CancelStocktakeRequest) => stocktakesApi.cancel(sessionId, body),
    onSuccess: (session) => {
      queryClient.setQueryData(keys.stocktakes.detail(sessionId), session);
      void queryClient.invalidateQueries({ queryKey: keys.stocktakes.all });
    },
  });
}

// ---------------------------------------------------------------------------
// Bulk asset changes
// ---------------------------------------------------------------------------

export function usePreviewBulkChange() {
  return useMutation({
    mutationFn: (body: PreviewBulkChangeRequest) => bulkChangesApi.preview(body),
  });
}

/**
 * Applies a previewed batch.
 *
 * Every asset in it now has a new version, so the whole asset cache is dropped
 * rather than patched — reconciling a hundred individual entries is more ways to
 * be wrong than a refetch is expensive.
 */
export function useCommitBulkChange() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ jobId, body }: { jobId: Guid; body: CommitBulkChangeRequest }) =>
      bulkChangesApi.commit(jobId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.assets.all });
      void queryClient.invalidateQueries({ queryKey: keys.dashboard.all });
    },
  });
}

// ---------------------------------------------------------------------------
// Leaver cases
// ---------------------------------------------------------------------------

export function useLeaverCases(params: LeaverSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.leavers.list(params),
    queryFn: ({ signal }) => leaversApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useLeaverCase(leaverCaseId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.leavers.detail(leaverCaseId ?? ''),
    queryFn: ({ signal }) => leaversApi.get(leaverCaseId!, signal),
    enabled: Boolean(leaverCaseId) && enabled,
  });
}

export function useLeaverCaseAssets(
  leaverCaseId: Guid | undefined,
  page: number,
  pageSize: number,
  enabled = true,
) {
  return useQuery({
    queryKey: keys.leavers.assets(leaverCaseId ?? '', page),
    queryFn: ({ signal }) => leaversApi.assets(leaverCaseId!, page, pageSize, signal),
    enabled: Boolean(leaverCaseId) && enabled,
    placeholderData: (previous) => previous,
  });
}

export function useCustodyExceptions(page: number, pageSize: number, enabled = true) {
  return useQuery({
    queryKey: keys.leavers.exceptions(page, pageSize),
    queryFn: ({ signal }) => leaversApi.exceptions(page, pageSize, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

/** Leaver writes touch custody, so people go stale alongside the case. */
function useLeaverInvalidation() {
  const queryClient = useQueryClient();
  return (leaverCase: LeaverCase) => {
    queryClient.setQueryData(keys.leavers.detail(leaverCase.id), leaverCase);
    void queryClient.invalidateQueries({ queryKey: keys.leavers.all });
    void queryClient.invalidateQueries({ queryKey: keys.people.all });
  };
}

export function useCreateLeaverCase() {
  const invalidate = useLeaverInvalidation();
  return useMutation({
    mutationFn: (body: CreateLeaverCaseRequest) => leaversApi.create(body),
    onSuccess: invalidate,
  });
}

export function useUpdateLeaverCase(leaverCaseId: Guid) {
  const invalidate = useLeaverInvalidation();
  return useMutation({
    mutationFn: (body: UpdateLeaverCaseRequest) => leaversApi.update(leaverCaseId, body),
    onSuccess: invalidate,
  });
}

export function useCompleteLeaverCase(leaverCaseId: Guid) {
  const invalidate = useLeaverInvalidation();
  return useMutation({
    mutationFn: (body: ChangeLeaverCaseStatusRequest) =>
      leaversApi.complete(leaverCaseId, body),
    onSuccess: invalidate,
  });
}

export function useCancelLeaverCase(leaverCaseId: Guid) {
  const invalidate = useLeaverInvalidation();
  return useMutation({
    mutationFn: (body: ChangeLeaverCaseStatusRequest) =>
      leaversApi.cancel(leaverCaseId, body),
    onSuccess: invalidate,
  });
}

export function useRehireLeaver(leaverCaseId: Guid) {
  const invalidate = useLeaverInvalidation();
  return useMutation({
    mutationFn: (body: ChangeLeaverCaseStatusRequest) =>
      leaversApi.rehire(leaverCaseId, body),
    onSuccess: invalidate,
  });
}

// ---------------------------------------------------------------------------
// Attachments
// ---------------------------------------------------------------------------

export function useAssetAttachments(
  assetId: Guid,
  includeArchived: boolean,
  page: number,
  pageSize: number,
  enabled = true,
) {
  return useQuery({
    queryKey: keys.attachments.list(assetId, includeArchived, page),
    queryFn: ({ signal }) =>
      attachmentsApi.list(assetId, includeArchived, page, pageSize, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useUploadAttachment(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ category, file }: { category: AttachmentCategory; file: File }) =>
      attachmentsApi.upload(assetId, category, file),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
    },
  });
}

export function useArchiveAttachment(assetId: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ attachmentId, version }: { attachmentId: Guid; version: Guid }) =>
      attachmentsApi.archive(assetId, attachmentId, { version }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.assets.detail(assetId) });
    },
  });
}

// ---------------------------------------------------------------------------
// Bundles
// ---------------------------------------------------------------------------

export function useBundles(params: BundleSearchParams, enabled = true) {
  return useQuery({
    queryKey: keys.bundles.list(params),
    queryFn: ({ signal }) => bundlesApi.list(params, signal),
    enabled,
    placeholderData: (previous) => previous,
  });
}

export function useBundle(bundleId: Guid | undefined, enabled = true) {
  return useQuery({
    queryKey: keys.bundles.detail(bundleId ?? ''),
    queryFn: ({ signal }) => bundlesApi.get(bundleId!, signal),
    enabled: Boolean(bundleId) && enabled,
  });
}

function useBundleWriter() {
  const queryClient = useQueryClient();
  return (bundle: AssetBundle) => {
    queryClient.setQueryData(keys.bundles.detail(bundle.id), bundle);
    void queryClient.invalidateQueries({ queryKey: keys.bundles.all });
  };
}

export function useCreateBundle() {
  const write = useBundleWriter();
  return useMutation({
    mutationFn: (body: CreateAssetBundleRequest) => bundlesApi.create(body),
    onSuccess: write,
  });
}

export function useUpdateBundle(bundleId: Guid) {
  const write = useBundleWriter();
  return useMutation({
    mutationFn: (body: UpdateAssetBundleRequest) => bundlesApi.update(bundleId, body),
    onSuccess: write,
  });
}

export function useArchiveBundle() {
  const write = useBundleWriter();
  return useMutation({
    mutationFn: ({ bundleId, version }: { bundleId: Guid; version: Guid }) =>
      bundlesApi.archive(bundleId, version),
    onSuccess: write,
  });
}

export function useRestoreBundle() {
  const write = useBundleWriter();
  return useMutation({
    mutationFn: ({ bundleId, version }: { bundleId: Guid; version: Guid }) =>
      bundlesApi.restore(bundleId, version),
    onSuccess: write,
  });
}
