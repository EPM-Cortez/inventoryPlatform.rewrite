namespace Inventory.Application.Security;

public static class Permissions
{
    public const string ClaimType = "permission";
    public const string MustChangePasswordClaimType = "must_change_password";
    public const string MfaEnrollmentRequiredClaimType = "mfa_enrollment_required";

    public const string AssetsRead = "assets.read";
    public const string AssetsCreate = "assets.create";
    public const string AssetsUpdate = "assets.update";
    public const string AssetsRetire = "assets.retire";
    public const string AssetsRestore = "assets.restore";
    public const string AssignmentsRead = "assignments.read";
    public const string AssignmentsManage = "assignments.manage";
    public const string PeopleRead = "people.read";
    public const string PeopleOperate = "people.operate";
    public const string PeopleManage = "people.manage";
    public const string WarrantiesRead = "warranties.read";
    public const string WarrantiesManage = "warranties.manage";
    public const string HistoryRead = "history.read";
    public const string HistoryNote = "history.note";
    public const string QrGenerate = "qr.generate";
    public const string QrPrint = "qr.print";
    public const string QrRotate = "qr.rotate";
    public const string ImportsValidate = "imports.validate";
    public const string ImportsRead = "imports.read";
    public const string ImportsCommit = "imports.commit";
    public const string ExportsAssets = "exports.assets";
    public const string ExportsPeople = "exports.people";
    public const string ExportsDepartments = "exports.departments";
    public const string ExportsLocations = "exports.locations";
    public const string ReferenceManage = "reference.manage";
    public const string AuditRead = "audit.read";
    public const string AuditExport = "audit.export";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";
    public const string LegacyReconcile = "legacy.reconcile";
    public const string AgentsRead = "agents.read";
    public const string AgentsManage = "agents.manage";
    public const string AgentsRecover = "agents.recover";
    public const string StocktakesRead = "stocktakes.read";
    public const string StocktakesManage = "stocktakes.manage";
    public const string AssetTagsManage = "asset-tags.manage";
    public const string AssetsBulk = "assets.bulk";
    public const string BundlesRead = "bundles.read";
    public const string BundlesManage = "bundles.manage";
    public const string LeaversRead = "leavers.read";
    public const string LeaversManage = "leavers.manage";
    public const string AttachmentsRead = "attachments.read";
    public const string AttachmentsManage = "attachments.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        AssetsRead,
        AssetsCreate,
        AssetsUpdate,
        AssetsRetire,
        AssetsRestore,
        AssignmentsRead,
        AssignmentsManage,
        PeopleRead,
        PeopleOperate,
        PeopleManage,
        WarrantiesRead,
        WarrantiesManage,
        HistoryRead,
        HistoryNote,
        QrGenerate,
        QrPrint,
        QrRotate,
        ImportsValidate,
        ImportsRead,
        ImportsCommit,
        ExportsAssets,
        ExportsPeople,
        ExportsDepartments,
        ExportsLocations,
        ReferenceManage,
        AuditRead,
        AuditExport,
        UsersManage,
        SettingsManage,
        LegacyReconcile,
        AgentsRead,
        AgentsManage,
        AgentsRecover,
        StocktakesRead,
        StocktakesManage,
        AssetTagsManage,
        AssetsBulk,
        BundlesRead,
        BundlesManage,
        LeaversRead,
        LeaversManage,
        AttachmentsRead,
        AttachmentsManage,
    ];
}

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string InventoryManager = "InventoryManager";
    public const string Technician = "Technician";
    public const string ReadOnlyAuditor = "ReadOnlyAuditor";

    public const string LegacyInventoryOperator = "InventoryOperator";

    public static IReadOnlyList<string> All { get; } =
    [
        Administrator,
        InventoryManager,
        Technician,
        ReadOnlyAuditor,
    ];
}

public static class RolePermissionPresets
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> All { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [RoleNames.Administrator] = Permissions.All.ToHashSet(StringComparer.Ordinal),
            [RoleNames.InventoryManager] = new HashSet<string>(
                [
                    Permissions.AssetsRead,
                    Permissions.AssetsCreate,
                    Permissions.AssetsUpdate,
                    Permissions.AssetsRetire,
                    Permissions.AssetsRestore,
                    Permissions.AssignmentsRead,
                    Permissions.AssignmentsManage,
                    Permissions.PeopleRead,
                    Permissions.PeopleOperate,
                    Permissions.PeopleManage,
                    Permissions.WarrantiesRead,
                    Permissions.WarrantiesManage,
                    Permissions.HistoryRead,
                    Permissions.HistoryNote,
                    Permissions.QrGenerate,
                    Permissions.QrPrint,
                    Permissions.QrRotate,
                    Permissions.ImportsValidate,
                    Permissions.ImportsRead,
                    Permissions.ImportsCommit,
                    Permissions.ExportsAssets,
                    Permissions.ExportsPeople,
                    Permissions.ExportsDepartments,
                    Permissions.ExportsLocations,
                    Permissions.ReferenceManage,
                    Permissions.AgentsRead,
                    Permissions.AgentsManage,
                    Permissions.StocktakesRead,
                    Permissions.StocktakesManage,
                    Permissions.AssetTagsManage,
                    Permissions.AssetsBulk,
                    Permissions.BundlesRead,
                    Permissions.BundlesManage,
                    Permissions.LeaversRead,
                    Permissions.LeaversManage,
                    Permissions.AttachmentsRead,
                    Permissions.AttachmentsManage,
                ],
                StringComparer.Ordinal),
            [RoleNames.Technician] = new HashSet<string>(
                [
                    Permissions.AssetsRead,
                    Permissions.AssetsCreate,
                    Permissions.AssetsUpdate,
                    Permissions.AssignmentsRead,
                    Permissions.AssignmentsManage,
                    Permissions.PeopleRead,
                    Permissions.PeopleOperate,
                    Permissions.WarrantiesRead,
                    Permissions.WarrantiesManage,
                    Permissions.HistoryRead,
                    Permissions.HistoryNote,
                    Permissions.QrGenerate,
                    Permissions.QrPrint,
                    Permissions.AgentsRead,
                    Permissions.StocktakesRead,
                    Permissions.StocktakesManage,
                    Permissions.BundlesRead,
                    Permissions.BundlesManage,
                    Permissions.LeaversRead,
                    Permissions.AttachmentsRead,
                    Permissions.AttachmentsManage,
                ],
                StringComparer.Ordinal),
            [RoleNames.ReadOnlyAuditor] = new HashSet<string>(
                [
                    Permissions.AssetsRead,
                    Permissions.AssignmentsRead,
                    Permissions.PeopleRead,
                    Permissions.WarrantiesRead,
                    Permissions.HistoryRead,
                    Permissions.ExportsAssets,
                    Permissions.AuditRead,
                    Permissions.AuditExport,
                    Permissions.AgentsRead,
                    Permissions.StocktakesRead,
                    Permissions.BundlesRead,
                    Permissions.LeaversRead,
                    Permissions.AttachmentsRead,
                ],
                StringComparer.Ordinal),
        };
}
