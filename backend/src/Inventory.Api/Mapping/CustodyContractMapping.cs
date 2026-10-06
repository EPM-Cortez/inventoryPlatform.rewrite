using Inventory.Application.Assignments;
using Inventory.Application.Attachments;
using Inventory.Application.Common;
using Inventory.Application.Leavers;
using Inventory.Application.SavedSearches;
using Inventory.Contracts.V1;
using Inventory.Domain.Assignments;

namespace Inventory.Api.Mapping;

internal static class CustodyContractMapping
{
    public static OverdueLoanResponse ToContract(this OverdueLoanView loan) =>
        new(
            loan.AssetId,
            loan.AssetTag,
            loan.DeviceName,
            loan.AssignmentId,
            new AssignmentTargetResponse(
                loan.TargetType switch
                {
                    AssignmentTargetType.Person => AssignmentTargetTypeContract.Person,
                    AssignmentTargetType.Department =>
                        AssignmentTargetTypeContract.Department,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(loan),
                        loan.TargetType,
                        null),
                },
                loan.TargetId,
                loan.TargetDisplayName,
                loan.TargetReference),
            loan.AssignedAtUtc,
            loan.ExpectedReturnOn,
            loan.DaysOverdue,
            loan.AssignmentVersion,
            loan.AssetVersion);

    public static LeaverCaseStatusContract ToContract(this LeaverCaseStatusView status) =>
        status switch
        {
            LeaverCaseStatusView.Open => LeaverCaseStatusContract.Open,
            LeaverCaseStatusView.Completed => LeaverCaseStatusContract.Completed,
            LeaverCaseStatusView.Cancelled => LeaverCaseStatusContract.Cancelled,
            LeaverCaseStatusView.Rehired => LeaverCaseStatusContract.Rehired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static LeaverCaseResponse ToContract(this LeaverCaseDetailView item) =>
        new(
            item.Id,
            item.PersonId,
            item.PersonExternalId,
            item.PersonDisplayName,
            item.PersonIsActive,
            item.LeavingOn,
            item.Notes,
            item.Status.ToContract(),
            item.CurrentAssets
                .Select(asset => asset.ToContract())
                .ToArray(),
            item.CurrentAssetCount,
            item.CurrentAssetsTruncated,
            item.CreatedAtUtc,
            item.CreatedByUserId,
            item.CreatedByDisplayName,
            item.CompletedAtUtc,
            item.CompletedByUserId,
            item.CompletedByDisplayName,
            item.CancelledAtUtc,
            item.CancelledByUserId,
            item.CancelledByDisplayName,
            item.Version);

    public static LeaverCustodyAssetResponse ToContract(
        this LeaverCustodyAssetView asset) =>
        new(
            asset.AssetId,
            asset.AssetTag,
            asset.DeviceName,
            asset.AssignmentId,
            asset.AssignedAtUtc,
            asset.ExpectedReturnOn,
            asset.IsOverdue,
            asset.AssignmentVersion,
            asset.AssetVersion);

    public static LeaverCaseSummaryResponse ToContract(this LeaverCaseSummaryView item) =>
        new(
            item.Id,
            item.PersonId,
            item.PersonExternalId,
            item.PersonDisplayName,
            item.PersonIsActive,
            item.LeavingOn,
            item.Status.ToContract(),
            item.CurrentAssetCount,
            item.OverdueAssetCount,
            item.Version);

    public static CustodyExceptionResponse ToContract(this CustodyExceptionView item) =>
        new(
            item.PersonId,
            item.PersonExternalId,
            item.PersonDisplayName,
            item.PersonIsActive,
            item.LeaverCaseId,
            item.LeavingOn,
            item.CurrentAssetCount,
            item.OverdueAssetCount);

    public static AssetAttachmentResponse ToContract(this AssetAttachmentView item) =>
        new(
            item.Id,
            item.AssetId,
            item.Category.ToContract(),
            item.FileName,
            item.ContentType,
            item.SizeBytes,
            item.Sha256,
            item.CreatedAtUtc,
            item.CreatedByUserId,
            item.CreatedByDisplayName,
            item.ArchivedAtUtc,
            item.ArchivedByUserId,
            item.ArchivedByDisplayName,
            item.Version);

    public static AttachmentCategoryContract ToContract(
        this AttachmentCategoryView category) =>
        category switch
        {
            AttachmentCategoryView.Invoice => AttachmentCategoryContract.Invoice,
            AttachmentCategoryView.WarrantyDocument =>
                AttachmentCategoryContract.WarrantyDocument,
            AttachmentCategoryView.DisposalCertificate =>
                AttachmentCategoryContract.DisposalCertificate,
            AttachmentCategoryView.Photo => AttachmentCategoryContract.Photo,
            AttachmentCategoryView.Other => AttachmentCategoryContract.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

    public static AttachmentCategoryView ToApplication(
        this AttachmentCategoryContract category) =>
        category switch
        {
            AttachmentCategoryContract.Invoice => AttachmentCategoryView.Invoice,
            AttachmentCategoryContract.WarrantyDocument =>
                AttachmentCategoryView.WarrantyDocument,
            AttachmentCategoryContract.DisposalCertificate =>
                AttachmentCategoryView.DisposalCertificate,
            AttachmentCategoryContract.Photo => AttachmentCategoryView.Photo,
            AttachmentCategoryContract.Other => AttachmentCategoryView.Other,
            _ => throw new RequestValidationException(
                "invalid_attachment_category",
                "The attachment category is not supported.",
                new Dictionary<string, string[]>
                {
                    ["category"] = ["The attachment category is not supported."],
                }),
        };

    public static SavedAssetSearchResponse ToContract(this SavedAssetSearchView item) =>
        new(
            item.Id,
            item.Name,
            item.SchemaVersion,
            item.Filter.ToContract(),
            item.IsDefault,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            item.Version);

    public static SavedAssetFilterResponse ToContract(this SavedAssetFilter item) =>
        new(
            item.Query,
            item.AssetTag,
            item.SerialNumber,
            item.CategoryId,
            item.Status?.ToContract(),
            item.Condition?.ToContract(),
            item.LocationId,
            item.DepartmentId,
            item.AssignedPersonId,
            item.Assigned,
            item.WarrantyState?.ToContract(),
            item.WarrantyEndsBefore,
            item.CreatedFrom,
            item.CreatedTo,
            item.LegacyId,
            item.PageSize,
            item.Sort);

    public static SavedAssetFilter ToApplication(this SavedAssetFilterRequest item) =>
        new(
            item.Query,
            item.AssetTag,
            item.SerialNumber,
            item.CategoryId,
            item.Status?.ToDomain(),
            item.Condition?.ToDomain(),
            item.LocationId,
            item.DepartmentId,
            item.AssignedPersonId,
            item.Assigned,
            item.WarrantyState?.ToDomain(),
            item.WarrantyEndsBefore,
            item.CreatedFrom,
            item.CreatedTo,
            item.LegacyId,
            item.PageSize,
            item.Sort);
}
