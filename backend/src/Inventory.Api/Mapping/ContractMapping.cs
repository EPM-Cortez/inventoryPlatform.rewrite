using System.Text.Json;
using Inventory.Application.Assets;
using Inventory.Application.Assignments;
using Inventory.Application.Auditing;
using Inventory.Application.Identity;
using Inventory.Application.DataExchange;
using Inventory.Application.Labels;
using Inventory.Application.People;
using Inventory.Application.Qr;
using Inventory.Application.References;
using Inventory.Application.Warranties;
using Inventory.Contracts.V1;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Importing;
using Inventory.Domain.People;
using Inventory.Domain.Warranties;

namespace Inventory.Api.Mapping;

internal static class ContractMapping
{
    public static ReferenceDataImportMode ToDomain(
        this ReferenceDataImportModeContract mode) =>
        mode switch
        {
            ReferenceDataImportModeContract.CreateOnly =>
                ReferenceDataImportMode.CreateOnly,
            ReferenceDataImportModeContract.UpsertByKey =>
                ReferenceDataImportMode.UpsertByKey,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    public static ReferenceDataImportResourceContract ToContract(
        this ReferenceDataImportResource resource) =>
        resource switch
        {
            ReferenceDataImportResource.People =>
                ReferenceDataImportResourceContract.People,
            ReferenceDataImportResource.Departments =>
                ReferenceDataImportResourceContract.Departments,
            ReferenceDataImportResource.Locations =>
                ReferenceDataImportResourceContract.Locations,
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
        };

    public static ReferenceDataImportModeContract ToContract(
        this ReferenceDataImportMode mode) =>
        mode switch
        {
            ReferenceDataImportMode.CreateOnly =>
                ReferenceDataImportModeContract.CreateOnly,
            ReferenceDataImportMode.UpsertByKey =>
                ReferenceDataImportModeContract.UpsertByKey,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    public static ReferenceDataImportStatusContract ToContract(
        this ReferenceDataImportStatus status) =>
        status switch
        {
            ReferenceDataImportStatus.ValidationFailed =>
                ReferenceDataImportStatusContract.ValidationFailed,
            ReferenceDataImportStatus.ReadyToCommit =>
                ReferenceDataImportStatusContract.ReadyToCommit,
            ReferenceDataImportStatus.Committed =>
                ReferenceDataImportStatusContract.Committed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static ReferenceDataImportActionContract ToContract(
        this ReferenceDataImportAction action) =>
        action switch
        {
            ReferenceDataImportAction.Create =>
                ReferenceDataImportActionContract.Create,
            ReferenceDataImportAction.Update =>
                ReferenceDataImportActionContract.Update,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    public static ReferenceDataImportJobResponse ToContract(
        this ReferenceDataImportJobView job) =>
        new(
            job.Id,
            job.Resource.ToContract(),
            job.SchemaVersion,
            job.OriginalFileName,
            job.FileHash,
            job.Mode.ToContract(),
            job.Status.ToContract(),
            job.TotalRows,
            job.ValidRows,
            job.ErrorRows,
            job.CreateRows,
            job.UpdateRows,
            job.GeneratedKeyRows,
            job.CreatedAtUtc,
            job.CreatedByUserId,
            job.CreatedByDisplayName,
            job.CommittedAtUtc,
            job.CommittedByUserId,
            job.CommittedByDisplayName,
            job.CommittedCreateRows,
            job.CommittedUpdateRows,
            job.CommittedUnchangedRows,
            job.Preview
                .Select(
                    row => new ReferenceDataImportPreviewRowResponse(
                        row.RowNumber,
                        row.Action.ToContract(),
                        row.Key,
                        row.KeyGenerated,
                        row.Name,
                        row.Type,
                        row.ParentKey,
                        row.IsActive))
                .ToArray(),
            job.Errors
                .Select(
                    error => new ReferenceDataImportErrorResponse(
                        error.RowNumber,
                        error.Column,
                        error.Code,
                        error.Message,
                        error.Value))
                .ToArray(),
            job.PreviewTruncated,
            job.ErrorsTruncated,
            job.Version);

    public static ReferenceDataImportCommitResponse ToContract(
        this ReferenceDataImportCommitResult result) =>
        new(
            result.ImportJobId,
            result.Resource.ToContract(),
            result.Status.ToContract(),
            result.CreatedRows,
            result.UpdatedRows,
            result.UnchangedRows,
            result.CommittedAtUtc,
            result.Version);

    public static AssetImportMode ToDomain(this AssetImportModeContract mode) =>
        mode switch
        {
            AssetImportModeContract.CreateOnly => AssetImportMode.CreateOnly,
            AssetImportModeContract.UpsertByAssetTag => AssetImportMode.UpsertByAssetTag,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    public static AssetImportModeContract ToContract(this AssetImportMode mode) =>
        mode switch
        {
            AssetImportMode.CreateOnly => AssetImportModeContract.CreateOnly,
            AssetImportMode.UpsertByAssetTag => AssetImportModeContract.UpsertByAssetTag,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    public static AssetImportStatusContract ToContract(this AssetImportStatus status) =>
        status switch
        {
            AssetImportStatus.ValidationFailed =>
                AssetImportStatusContract.ValidationFailed,
            AssetImportStatus.ReadyToCommit =>
                AssetImportStatusContract.ReadyToCommit,
            AssetImportStatus.Committed => AssetImportStatusContract.Committed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static AssetImportActionContract ToContract(this AssetImportAction action) =>
        action switch
        {
            AssetImportAction.Create => AssetImportActionContract.Create,
            AssetImportAction.Update => AssetImportActionContract.Update,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    public static AssetImportAssignmentResolutionContract ToContract(
        this AssetImportAssignmentResolution resolution) =>
        resolution switch
        {
            AssetImportAssignmentResolution.None =>
                AssetImportAssignmentResolutionContract.None,
            AssetImportAssignmentResolution.ExactPerson =>
                AssetImportAssignmentResolutionContract.ExactPerson,
            AssetImportAssignmentResolution.FuzzyPerson =>
                AssetImportAssignmentResolutionContract.FuzzyPerson,
            AssetImportAssignmentResolution.Department =>
                AssetImportAssignmentResolutionContract.Department,
            AssetImportAssignmentResolution.Unassigned =>
                AssetImportAssignmentResolutionContract.Unassigned,
            _ => throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                null),
        };

    public static AssetImportSerialResolutionContract ToContract(
        this AssetImportSerialResolution resolution) =>
        resolution switch
        {
            AssetImportSerialResolution.Missing =>
                AssetImportSerialResolutionContract.Missing,
            AssetImportSerialResolution.Verified =>
                AssetImportSerialResolutionContract.Verified,
            AssetImportSerialResolution.ReportedDuplicate =>
                AssetImportSerialResolutionContract.ReportedDuplicate,
            AssetImportSerialResolution.IgnoredPlaceholder =>
                AssetImportSerialResolutionContract.IgnoredPlaceholder,
            _ => throw new ArgumentOutOfRangeException(
                nameof(resolution), resolution, null),
        };

    public static AssetImportJobResponse ToContract(this AssetImportJobView job) =>
        new(
            job.Id,
            job.SchemaVersion,
            job.OriginalFileName,
            job.FileHash,
            job.Mode.ToContract(),
            job.Status.ToContract(),
            job.TotalRows,
            job.ValidRows,
            job.ErrorRows,
            job.CreateRows,
            job.UpdateRows,
            job.GeneratedAssetTagRows,
            job.PlannedCategoryCreateCount,
            job.PlannedLocationCreateCount,
            job.InferredDateRows,
            job.FuzzyPersonMatchRows,
            job.UnassignedImportRows,
            job.CreatedAtUtc,
            job.CreatedByUserId,
            job.CreatedByDisplayName,
            job.CommittedAtUtc,
            job.CommittedByUserId,
            job.CommittedByDisplayName,
            job.CommittedCreateRows,
            job.CommittedUpdateRows,
            job.CommittedUnchangedRows,
            job.Preview
                .Select(
                    row => new AssetImportPreviewRowResponse(
                        row.RowNumber,
                        row.Action.ToContract(),
                        row.AssetTag,
                        row.AssetTagGenerated,
                        row.Category,
                        row.ResolvedCategoryCode,
                        row.CategoryWillBeCreated,
                        row.Status.ToContract(),
                        row.Condition.ToContract(),
                        row.LocationCode,
                        row.ResolvedLocationCode,
                        row.LocationWillBeCreated,
                        row.AssignmentSource,
                        row.AssignmentResolution.ToContract(),
                        row.AssignedTo,
                        row.PurchaseDate,
                        row.WarrantyStartDate,
                        row.WarrantyEndDate,
                        row.InferredDateFields,
                        row.SourceSerialNumber,
                        row.SerialResolution.ToContract()))
                .ToArray(),
            job.Errors
                .Select(
                    error => new AssetImportErrorResponse(
                        error.RowNumber,
                        error.Column,
                        error.Code,
                        error.Message,
                        error.Value))
                .ToArray(),
            job.PreviewTruncated,
            job.ErrorsTruncated,
            job.Version,
            job.ReportedDuplicateSerialRows);

    public static AssetImportCommitResponse ToContract(
        this AssetImportCommitResult result) =>
        new(
            result.ImportJobId,
            result.Status.ToContract(),
            result.CreatedRows,
            result.UpdatedRows,
            result.UnchangedRows,
            result.CreatedCategoryCount,
            result.CreatedLocationCount,
            result.CommittedAtUtc,
            result.Version);

    public static LegacyReconciliationDecision ToDomain(
        this LegacyReconciliationDecisionContract decision) =>
        decision switch
        {
            LegacyReconciliationDecisionContract.Approved =>
                LegacyReconciliationDecision.Approved,
            LegacyReconciliationDecisionContract.Rejected =>
                LegacyReconciliationDecision.Rejected,
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision),
                decision,
                null),
        };

    public static LegacyReconciliationDecisionContract ToContract(
        this LegacyReconciliationDecision decision) =>
        decision switch
        {
            LegacyReconciliationDecision.Approved =>
                LegacyReconciliationDecisionContract.Approved,
            LegacyReconciliationDecision.Rejected =>
                LegacyReconciliationDecisionContract.Rejected,
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision),
                decision,
                null),
        };

    public static LegacyReconciliationResponse ToContract(
        this LegacyReconciliationView view) =>
        new(
            view.Report.ToContract(),
            view.SignOff?.ToContract(),
            view.CutoverReady);

    public static LegacyCutoverPackageResponse ToContract(
        this LegacyCutoverPackage package) =>
        new(
            package.FormatVersion,
            package.ImportJobId,
            package.ImportJobVersion,
            package.SourceFileName,
            package.SourceFileHash,
            package.ReportFingerprint,
            package.PackageFingerprint,
            package.Mode.ToContract(),
            package.ImportStatus.ToContract(),
            package.Counts.ToContract(),
            package.SignOff?.ToContract(),
            package.CutoverReady,
            package.BlockingReasons,
            package.CutoverProcedure,
            package.RollbackProcedure);

    private static LegacyReconciliationReportResponse ToContract(
        this LegacyReconciliationReport report) =>
        new(
            report.FormatVersion,
            report.ImportJobId,
            report.ImportJobVersion,
            report.SourceFileName,
            report.SourceFileHash,
            report.Mode.ToContract(),
            report.ImportStatus.ToContract(),
            report.CreatedAtUtc,
            report.Counts.ToContract(),
            new LegacyReconciliationMappingSummaryResponse(
                report.Mappings.GeneratedAssetTags,
                report.Mappings.PlannedCategoryCreates,
                report.Mappings.PlannedLocationCreates,
                report.Mappings.InferredDateRows,
                report.Mappings.FuzzyPersonMatches,
                report.Mappings.UnassignedImports),
            report.Issues
                .Select(
                    issue => new LegacyReconciliationIssueResponse(
                        issue.IssueKey,
                        issue.RowNumber,
                        issue.Column,
                        issue.Code,
                        issue.Classification,
                        issue.Severity,
                        issue.Message,
                        issue.Value))
                .ToArray(),
            report.DuplicateIssueCount,
            report.UnmappedReferenceIssueCount,
            report.Samples
                .Select(
                    sample => new LegacyReconciliationSampleResponse(
                        sample.RowNumber,
                        sample.Action.ToContract(),
                        sample.AssetTag,
                        sample.TargetExists,
                        sample.Outcome,
                        sample.Comparisons
                            .Select(
                                comparison =>
                                    new LegacyFieldComparisonResponse(
                                        comparison.Field,
                                        comparison.Source,
                                        comparison.Target,
                                        comparison.Matches))
                            .ToArray()))
                .ToArray(),
            report.SamplesTruncated,
            report.ReportFingerprint);

    private static LegacyReconciliationCountsResponse ToContract(
        this LegacyReconciliationCounts counts) =>
        new(
            counts.SourceRows,
            counts.ValidRows,
            counts.ErrorRows,
            counts.PlannedCreates,
            counts.PlannedUpdates,
            counts.TargetAssetsBefore,
            counts.ExpectedTargetAssetsAfter);

    private static LegacyReconciliationSignOffResponse ToContract(
        this LegacyReconciliationSignOffView signOff) =>
        new(
            signOff.Id,
            signOff.Decision.ToContract(),
            signOff.ReportFingerprint,
            signOff.SignedExceptions
                .Select(
                    exception =>
                        new LegacySignedIssueAcknowledgementResponse(
                            exception.IssueKey,
                            exception.RowNumber,
                            exception.Column,
                            exception.Code,
                            exception.Rationale))
                .ToArray(),
            signOff.Notes,
            signOff.SignedAtUtc,
            signOff.SignedByUserId,
            signOff.SignedByDisplayName);

    public static AssetStatus ToDomain(this AssetStatusContract status) =>
        status switch
        {
            AssetStatusContract.InStock => AssetStatus.InStock,
            AssetStatusContract.Reserved => AssetStatus.Reserved,
            AssetStatusContract.Assigned => AssetStatus.Assigned,
            AssetStatusContract.Repair => AssetStatus.Repair,
            AssetStatusContract.Lost => AssetStatus.Lost,
            AssetStatusContract.Stolen => AssetStatus.Stolen,
            AssetStatusContract.Retired => AssetStatus.Retired,
            AssetStatusContract.Disposed => AssetStatus.Disposed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static AssetCondition ToDomain(this AssetConditionContract condition) =>
        condition switch
        {
            AssetConditionContract.New => AssetCondition.New,
            AssetConditionContract.Good => AssetCondition.Good,
            AssetConditionContract.Fair => AssetCondition.Fair,
            AssetConditionContract.Damaged => AssetCondition.Damaged,
            AssetConditionContract.Unserviceable => AssetCondition.Unserviceable,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null),
        };

    public static AssetStatusContract ToContract(this AssetStatus status) =>
        status switch
        {
            AssetStatus.InStock => AssetStatusContract.InStock,
            AssetStatus.Reserved => AssetStatusContract.Reserved,
            AssetStatus.Assigned => AssetStatusContract.Assigned,
            AssetStatus.Repair => AssetStatusContract.Repair,
            AssetStatus.Lost => AssetStatusContract.Lost,
            AssetStatus.Stolen => AssetStatusContract.Stolen,
            AssetStatus.Retired => AssetStatusContract.Retired,
            AssetStatus.Disposed => AssetStatusContract.Disposed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static AssetConditionContract ToContract(this AssetCondition condition) =>
        condition switch
        {
            AssetCondition.New => AssetConditionContract.New,
            AssetCondition.Good => AssetConditionContract.Good,
            AssetCondition.Fair => AssetConditionContract.Fair,
            AssetCondition.Damaged => AssetConditionContract.Damaged,
            AssetCondition.Unserviceable => AssetConditionContract.Unserviceable,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null),
        };

    public static AssetResponse ToContract(this AssetView asset) =>
        new(
            asset.Id,
            asset.AssetTag,
            asset.DeviceName,
            new ReferenceSummaryResponse(
                asset.CategoryId,
                asset.CategoryCode,
                asset.CategoryName),
            asset.Description,
            asset.Manufacturer,
            asset.Model,
            asset.SerialNumber,
            asset.LanMacAddress,
            asset.BiosGuid,
            asset.Ownership,
            asset.Status.ToContract(),
            asset.Condition.ToContract(),
            asset.LocationId.HasValue
                ? new LocationSummaryResponse(
                    asset.LocationId.Value,
                    asset.LocationCode ?? string.Empty,
                    asset.LocationName ?? string.Empty)
                : null,
            asset.CurrentAssignment?.ToContract(),
            asset.WarrantySummary.ToContract(),
            asset.OperatingSystem,
            asset.OperatingSystemVersion,
            asset.Windows11Compliant,
            $"/api/v1/assets/{asset.Id}/qr",
            asset.PurchaseDate,
            asset.PurchaseCost,
            asset.Currency,
            asset.Supplier,
            asset.Notes,
            asset.LegacyId,
            asset.CreatedAtUtc,
            asset.UpdatedAtUtc,
            asset.RetiredAtUtc,
            asset.RetirementReason,
            asset.DisposedAtUtc,
            asset.DisposalReason,
            asset.Version,
            asset.ReportedSerialNumber);

    public static AssetListItemResponse ToContract(this AssetListItem asset) =>
        new(
            asset.Id,
            asset.AssetTag,
            asset.DeviceName,
            new ReferenceSummaryResponse(
                asset.CategoryId,
                asset.CategoryCode,
                asset.CategoryName),
            asset.Description,
            asset.Manufacturer,
            asset.Model,
            asset.SerialNumber,
            asset.LanMacAddress,
            asset.BiosGuid,
            asset.Ownership,
            asset.Status.ToContract(),
            asset.Condition.ToContract(),
            asset.LocationId.HasValue
                ? new LocationSummaryResponse(
                    asset.LocationId.Value,
                    asset.LocationCode ?? string.Empty,
                    asset.LocationName ?? string.Empty)
                : null,
            asset.CurrentAssignment?.ToContract(),
            asset.WarrantySummary.ToContract(),
            asset.OperatingSystem,
            asset.OperatingSystemVersion,
            asset.Windows11Compliant,
            $"/api/v1/assets/{asset.Id}/qr",
            asset.UpdatedAtUtc,
            asset.Version);

    public static WarrantyState ToDomain(this WarrantyStateContract state) =>
        state switch
        {
            WarrantyStateContract.NotRecorded => WarrantyState.NotRecorded,
            WarrantyStateContract.Future => WarrantyState.Future,
            WarrantyStateContract.Active => WarrantyState.Active,
            WarrantyStateContract.ExpiringSoon => WarrantyState.ExpiringSoon,
            WarrantyStateContract.Expired => WarrantyState.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    public static WarrantyStateContract ToContract(this WarrantyState state) =>
        state switch
        {
            WarrantyState.NotRecorded => WarrantyStateContract.NotRecorded,
            WarrantyState.Future => WarrantyStateContract.Future,
            WarrantyState.Active => WarrantyStateContract.Active,
            WarrantyState.ExpiringSoon => WarrantyStateContract.ExpiringSoon,
            WarrantyState.Expired => WarrantyStateContract.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    public static WarrantySummaryResponse ToContract(this WarrantySummaryView summary) =>
        new(
            summary.State.ToContract(),
            summary.Provider,
            summary.EndsOn,
            summary.DaysRemaining);

    public static QrIdentifierResponse ToContract(this QrIdentifierView identifier) =>
        new(
            identifier.Id,
            identifier.AssetId,
            identifier.TokenVersion,
            identifier.IssuedAtUtc,
            identifier.PayloadUrl,
            identifier.ImageUrl);

    public static QrResolutionResponse ToContract(this QrResolutionView asset) =>
        new(
            asset.Id,
            asset.AssetTag,
            new ReferenceSummaryResponse(
                asset.CategoryId,
                asset.CategoryCode,
                asset.CategoryName),
            asset.Status.ToContract(),
            asset.Condition.ToContract(),
            asset.LocationId.HasValue
                ? new LocationSummaryResponse(
                    asset.LocationId.Value,
                    asset.LocationCode ?? string.Empty,
                    asset.LocationName ?? string.Empty)
                : null,
            asset.WarrantySummary.ToContract());

    public static LabelStock ToDomain(this LabelStockContract stock) =>
        stock switch
        {
            LabelStockContract.BrotherDk11209 => LabelStock.BrotherDk11209,
            LabelStockContract.AveryL7159 => LabelStock.AveryL7159,
            _ => throw new ArgumentOutOfRangeException(nameof(stock), stock, null),
        };

    public static LabelStockContract ToContract(this LabelStock stock) =>
        stock switch
        {
            LabelStock.BrotherDk11209 => LabelStockContract.BrotherDk11209,
            LabelStock.AveryL7159 => LabelStockContract.AveryL7159,
            _ => throw new ArgumentOutOfRangeException(nameof(stock), stock, null),
        };

    public static LabelStockResponse ToContract(this LabelStockView stock) =>
        new(
            stock.Stock.ToContract(),
            stock.DisplayName,
            stock.PageType,
            stock.LabelWidthMillimetres,
            stock.LabelHeightMillimetres,
            stock.LabelsPerPage,
            stock.Columns,
            stock.Rows,
            stock.SupportsStartPosition);

    public static WarrantyResponse ToContract(this WarrantyView warranty) =>
        new(
            warranty.Id,
            warranty.AssetId,
            warranty.Provider,
            warranty.CoverageType,
            warranty.StartDate,
            warranty.EndDate,
            warranty.Reference,
            warranty.Notes,
            warranty.IsPrimary,
            warranty.State.ToContract(),
            warranty.DaysRemaining,
            warranty.CreatedAtUtc,
            warranty.UpdatedAtUtc,
            warranty.ArchivedAtUtc,
            warranty.Version);

    public static AuditLogResponse ToContract(this AuditLogView audit) =>
        new(
            audit.Id,
            audit.ActorUserId,
            audit.ActorDisplayName,
            audit.Action,
            audit.TargetType,
            audit.TargetId,
            audit.OccurredAtUtc,
            audit.Outcome,
            audit.DetailsJson,
            audit.IpAddress,
            audit.Client,
            audit.CorrelationId);

    public static AssetEventResponse ToContract(this AssetEventView assetEvent)
    {
        object details;
        try
        {
            using var document = JsonDocument.Parse(assetEvent.DetailsJson);
            details = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            details = new { };
        }

        return new AssetEventResponse(
            assetEvent.Id,
            assetEvent.Type.ToString(),
            assetEvent.OccurredAtUtc,
            assetEvent.ActorUserId,
            assetEvent.ActorDisplayName,
            assetEvent.Source.ToString(),
            assetEvent.Summary,
            details,
            assetEvent.CorrelationId);
    }

    public static CategoryResponse ToContract(this CategoryView category) =>
        new(
            category.Id,
            category.Code,
            category.Name,
            category.Description,
            category.IsActive,
            category.Version);

    public static LocationResponse ToContract(this LocationView location) =>
        new(
            location.Id,
            location.Code,
            location.Name,
            location.Type,
            location.ParentLocationId,
            location.IsActive,
            location.Version);

    public static PersonType ToDomain(this PersonTypeContract type) =>
        type switch
        {
            PersonTypeContract.Staff => PersonType.Staff,
            PersonTypeContract.Pupil => PersonType.Pupil,
            PersonTypeContract.Contractor => PersonType.Contractor,
            PersonTypeContract.Other => PersonType.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    public static PersonTypeContract ToContract(this PersonType type) =>
        type switch
        {
            PersonType.Staff => PersonTypeContract.Staff,
            PersonType.Pupil => PersonTypeContract.Pupil,
            PersonType.Contractor => PersonTypeContract.Contractor,
            PersonType.Other => PersonTypeContract.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    public static PersonResponse ToContract(this PersonView person) =>
        new(
            person.Id,
            person.ExternalId,
            person.DisplayName,
            person.Type.ToContract(),
            person.Email,
            person.IsActive,
            person.Version);

    public static DepartmentResponse ToContract(this DepartmentView department) =>
        new(
            department.Id,
            department.Code,
            department.Name,
            department.IsActive,
            department.Version);

    public static AssetAssignmentResponse ToContract(this AssetAssignmentView assignment) =>
        new(
            assignment.Id,
            assignment.AssetId,
            new AssignmentTargetResponse(
                assignment.TargetType.ToContract(),
                assignment.PersonId ?? assignment.DepartmentId
                    ?? throw new InvalidOperationException("Assignment target is missing."),
                assignment.TargetDisplayName,
                assignment.TargetReference),
            assignment.AssignedAtUtc,
            assignment.AssignedByUserId,
            assignment.AssignedByDisplayName,
            assignment.AssignedCondition.ToContract(),
            assignment.AssignedLocationId.HasValue
                ? new LocationSummaryResponse(
                    assignment.AssignedLocationId.Value,
                    assignment.AssignedLocationCode ?? string.Empty,
                    assignment.AssignedLocationName ?? string.Empty)
                : null,
            assignment.AssignmentNotes,
            assignment.ReturnedAtUtc,
            assignment.ReturnedByUserId,
            assignment.ReturnedByDisplayName,
            assignment.ReturnCondition?.ToContract(),
            assignment.ReturnLocationId.HasValue
                ? new LocationSummaryResponse(
                    assignment.ReturnLocationId.Value,
                    assignment.ReturnLocationCode ?? string.Empty,
                    assignment.ReturnLocationName ?? string.Empty)
                : null,
            assignment.ReturnNotes,
            assignment.Version,
            assignment.ExpectedReturnOn,
            assignment.IsOverdue);

    public static AssignmentWorkflowResponse ToContract(
        this AssignmentWorkflowResult result) =>
        new(
            result.Assignment.ToContract(),
            result.AssetStatus.ToContract(),
            result.AssetCondition.ToContract(),
            result.AssetLocationId.HasValue
                ? new LocationSummaryResponse(
                    result.AssetLocationId.Value,
                    result.AssetLocationCode ?? string.Empty,
                    result.AssetLocationName ?? string.Empty)
                : null,
            result.AssetVersion);

    public static HolderAssetResponse ToContract(this HolderAssetView asset) =>
        new(
            asset.AssetId,
            asset.AssetTag,
            asset.Status.ToContract(),
            asset.Condition.ToContract(),
            asset.LocationId.HasValue
                ? new LocationSummaryResponse(
                    asset.LocationId.Value,
                    asset.LocationCode ?? string.Empty,
                    asset.LocationName ?? string.Empty)
                : null,
            asset.AssetVersion,
            asset.Assignment.ToContract());

    private static CurrentAssignmentResponse ToContract(
        this CurrentAssignmentView assignment) =>
        new(
            assignment.AssignmentId,
            new AssignmentTargetResponse(
                assignment.TargetType.ToContract(),
                assignment.PersonId ?? assignment.DepartmentId
                    ?? throw new InvalidOperationException("Assignment target is missing."),
                assignment.TargetDisplayName,
                assignment.TargetReference),
            assignment.AssignedAtUtc,
            assignment.Version,
            assignment.ExpectedReturnOn,
            assignment.IsOverdue);

    private static AssignmentTargetTypeContract ToContract(
        this AssignmentTargetType type) =>
        type switch
        {
            AssignmentTargetType.Person => AssignmentTargetTypeContract.Person,
            AssignmentTargetType.Department => AssignmentTargetTypeContract.Department,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    public static UserResponse ToContract(this UserView user) =>
        new(
            user.Id,
            user.Email,
            user.DisplayName,
            user.IsEnabled,
            user.MustChangePassword,
            user.MfaEnabled,
            user.Roles,
            user.ArchivedAtUtc,
            user.Version);
}
