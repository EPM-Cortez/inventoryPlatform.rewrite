using Inventory.Application.BulkChanges;
using Inventory.Application.Bundles;
using Inventory.Contracts.V1;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;

namespace Inventory.Api.Mapping;

public static class AssetOperationsContractMapping
{
    public static AssetBulkChangeOperationKind ToDomain(
        this AssetBulkChangeOperationContract operation) =>
        operation switch
        {
            AssetBulkChangeOperationContract.SetLocation =>
                AssetBulkChangeOperationKind.SetLocation,
            AssetBulkChangeOperationContract.SetCategory =>
                AssetBulkChangeOperationKind.SetCategory,
            AssetBulkChangeOperationContract.SetCondition =>
                AssetBulkChangeOperationKind.SetCondition,
            AssetBulkChangeOperationContract.SetStatus =>
                AssetBulkChangeOperationKind.SetStatus,
            AssetBulkChangeOperationContract.AddNote =>
                AssetBulkChangeOperationKind.AddNote,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                null),
        };

    public static AssetBulkChangeOperationContract ToContract(
        this AssetBulkChangeOperationKind operation) =>
        operation switch
        {
            AssetBulkChangeOperationKind.SetLocation =>
                AssetBulkChangeOperationContract.SetLocation,
            AssetBulkChangeOperationKind.SetCategory =>
                AssetBulkChangeOperationContract.SetCategory,
            AssetBulkChangeOperationKind.SetCondition =>
                AssetBulkChangeOperationContract.SetCondition,
            AssetBulkChangeOperationKind.SetStatus =>
                AssetBulkChangeOperationContract.SetStatus,
            AssetBulkChangeOperationKind.AddNote =>
                AssetBulkChangeOperationContract.AddNote,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                null),
        };

    public static AssetBulkChangeJobStatusContract ToContract(
        this AssetBulkChangeJobStatus status) =>
        status switch
        {
            AssetBulkChangeJobStatus.Previewed =>
                AssetBulkChangeJobStatusContract.Previewed,
            AssetBulkChangeJobStatus.Committed =>
                AssetBulkChangeJobStatusContract.Committed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static AssetBundleMemberRole ToDomain(
        this AssetBundleMemberRoleContract role) =>
        role switch
        {
            AssetBundleMemberRoleContract.Laptop =>
                AssetBundleMemberRole.Laptop,
            AssetBundleMemberRoleContract.Charger =>
                AssetBundleMemberRole.Charger,
            AssetBundleMemberRoleContract.Dock =>
                AssetBundleMemberRole.Dock,
            AssetBundleMemberRoleContract.Case =>
                AssetBundleMemberRole.Case,
            AssetBundleMemberRoleContract.Other =>
                AssetBundleMemberRole.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public static AssetBundleMemberRoleContract ToContract(
        this AssetBundleMemberRole role) =>
        role switch
        {
            AssetBundleMemberRole.Laptop =>
                AssetBundleMemberRoleContract.Laptop,
            AssetBundleMemberRole.Charger =>
                AssetBundleMemberRoleContract.Charger,
            AssetBundleMemberRole.Dock =>
                AssetBundleMemberRoleContract.Dock,
            AssetBundleMemberRole.Case =>
                AssetBundleMemberRoleContract.Case,
            AssetBundleMemberRole.Other =>
                AssetBundleMemberRoleContract.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public static AssetBulkChangePreviewResponse ToContract(
        this AssetBulkChangePreviewView preview) =>
        new(
            preview.JobId,
            preview.Status.ToContract(),
            preview.Operation.ToContract(),
            preview.Reason,
            preview.AssetCount,
            preview.Items
                .Select(
                    item => new AssetBulkChangePreviewItemResponse(
                        item.AssetId,
                        item.AssetTag,
                        item.Version,
                        item.Field,
                        item.From,
                        item.To,
                        item.IsNoOp))
                .ToArray(),
            preview.Fingerprint,
            preview.CreatedAtUtc,
            preview.ExpiresAtUtc,
            preview.CommittedAtUtc,
            preview.Version);

    public static AssetBulkChangeCommitResponse ToContract(
        this AssetBulkChangeCommitView commit) =>
        new(
            commit.JobId,
            commit.Status.ToContract(),
            commit.AssetCount,
            commit.CommittedAtUtc,
            commit.Assets
                .Select(
                    asset => new AssetBulkChangeCommitItemResponse(
                        asset.AssetId,
                        asset.AssetTag,
                        asset.Version))
                .ToArray(),
            commit.Version);

    public static AssetBundleResponse ToContract(this AssetBundleView bundle) =>
        new(
            bundle.Id,
            bundle.Code,
            bundle.Name,
            bundle.Description,
            bundle.Members
                .Select(
                    member => new AssetBundleMemberResponse(
                        member.MemberId,
                        member.AssetId,
                        member.AssetTag,
                        member.Role.ToContract(),
                        member.OtherRole,
                        member.AddedAtUtc))
                .ToArray(),
            bundle.CreatedAtUtc,
            bundle.UpdatedAtUtc,
            bundle.ArchivedAtUtc,
            bundle.ArchivedAtUtc.HasValue,
            bundle.Version);

    public static AssetBundleListItemResponse ToContract(
        this AssetBundleListItem bundle) =>
        new(
            bundle.Id,
            bundle.Code,
            bundle.Name,
            bundle.Description,
            bundle.MemberCount,
            bundle.IsArchived,
            bundle.UpdatedAtUtc,
            bundle.Version);
}
