/**
 * Mirrors `Inventory.Application.Security.Permissions`.
 *
 * These strings gate *visibility only*. The API re-checks every one of them on
 * every request, so hiding a button is a courtesy to the user, never a security
 * control. Never treat a permission check here as authorisation.
 */
export const Permission = {
  AssetsRead: 'assets.read',
  AssetsCreate: 'assets.create',
  AssetsUpdate: 'assets.update',
  AssetsRetire: 'assets.retire',
  AssetsRestore: 'assets.restore',
  AssignmentsRead: 'assignments.read',
  AssignmentsManage: 'assignments.manage',
  PeopleRead: 'people.read',
  PeopleOperate: 'people.operate',
  PeopleManage: 'people.manage',
  WarrantiesRead: 'warranties.read',
  WarrantiesManage: 'warranties.manage',
  HistoryRead: 'history.read',
  HistoryNote: 'history.note',
  QrGenerate: 'qr.generate',
  QrRotate: 'qr.rotate',
  /** Separate from `qr.generate` so print access stays explicit for custom roles. */
  QrPrint: 'qr.print',
  ImportsValidate: 'imports.validate',
  ImportsRead: 'imports.read',
  ImportsCommit: 'imports.commit',
  ExportsAssets: 'exports.assets',
  /** Split per resource so bulk person PII stays separately grantable. */
  ExportsPeople: 'exports.people',
  ExportsDepartments: 'exports.departments',
  ExportsLocations: 'exports.locations',
  ReferenceManage: 'reference.manage',
  AuditRead: 'audit.read',
  /** Separate from reading: the export is bounded and omits structured details. */
  AuditExport: 'audit.export',
  /** Administrator-only: legacy migration dry runs and cutover sign-off. */
  LegacyReconcile: 'legacy.reconcile',
  /** Granted to all four roles — inspecting endpoint evidence changes nothing. */
  AgentsRead: 'agents.read',
  /** Administrator and manager only: enrollment tokens, asset links, revocation. */
  AgentsManage: 'agents.manage',
  /**
   * Authorising a machine to re-key itself, held separately from `agents.manage`.
   *
   * It mints a one-time token that lets a machine replace its signing key without
   * re-enrolling, so it is closer to issuing a credential than to managing a
   * record — and is deliberately not implied by being able to link or revoke.
   */
  AgentsRecover: 'agents.recover',
  UsersManage: 'users.manage',
  SettingsManage: 'settings.manage',

  /** Bulk asset changes, and the policy that issues generated asset tags. */
  AssetsBulk: 'assets.bulk',
  AssetTagsManage: 'asset-tags.manage',

  StocktakesRead: 'stocktakes.read',
  StocktakesManage: 'stocktakes.manage',

  BundlesRead: 'bundles.read',
  BundlesManage: 'bundles.manage',

  LeaversRead: 'leavers.read',
  LeaversManage: 'leavers.manage',

  AttachmentsRead: 'attachments.read',
  AttachmentsManage: 'attachments.manage',
} as const;

export type PermissionKey = (typeof Permission)[keyof typeof Permission];

/**
 * Mirrors `RoleNames`. Roles are administrative presets, not enforcement units.
 *
 * The *assignable* list is served by `GET /api/v1/users/role-presets` — read it
 * from there rather than from this object, so a role added on the server appears
 * without a frontend release. What lives here is presentation: friendly names and
 * one-line explanations for the roles we know about.
 */
export const Role = {
  Administrator: 'Administrator',
  InventoryManager: 'InventoryManager',
  Technician: 'Technician',
  ReadOnlyAuditor: 'ReadOnlyAuditor',
} as const;

export type RoleName = (typeof Role)[keyof typeof Role];

/**
 * `InventoryOperator` was renamed to `Technician`. The backend seeder migrates
 * existing assignments on startup, so this should not appear — but a stale cache
 * or a half-applied deployment could still surface it, and showing the raw name
 * with no explanation would be worse than labelling it.
 */
export const LEGACY_ROLE_LABELS: Record<string, string> = {
  InventoryOperator: 'Technician (legacy name)',
};

export const ROLE_LABELS: Record<RoleName, string> = {
  [Role.Administrator]: 'Administrator',
  [Role.InventoryManager]: 'Inventory manager',
  [Role.Technician]: 'Technician',
  [Role.ReadOnlyAuditor]: 'Read-only auditor',
};

export const ROLE_DESCRIPTIONS: Record<RoleName, string> = {
  [Role.Administrator]:
    'Full access, including account administration and system settings.',
  [Role.InventoryManager]:
    'Manages the register, custody, people, departments and reference data.',
  [Role.Technician]:
    'Day-to-day register and custody work. Cannot retire assets or archive people.',
  [Role.ReadOnlyAuditor]: 'Reads the register, custody history and audit trail.',
};

/** Sort order for role lists: broadest access first. */
const ROLE_RANK: Record<string, number> = {
  [Role.Administrator]: 0,
  [Role.InventoryManager]: 1,
  [Role.Technician]: 2,
  [Role.ReadOnlyAuditor]: 3,
};

export function isKnownRole(value: string): value is RoleName {
  return value in ROLE_LABELS;
}

export function describeRole(value: string): string {
  if (isKnownRole(value)) return ROLE_LABELS[value];
  return LEGACY_ROLE_LABELS[value] ?? value;
}

export function describeRolePurpose(value: string): string | undefined {
  return isKnownRole(value) ? ROLE_DESCRIPTIONS[value] : undefined;
}

export function compareRoles(a: string, b: string): number {
  const rankA = ROLE_RANK[a] ?? Number.MAX_SAFE_INTEGER;
  const rankB = ROLE_RANK[b] ?? Number.MAX_SAFE_INTEGER;
  return rankA === rankB ? a.localeCompare(b) : rankA - rankB;
}

/**
 * Human wording for a permission key, e.g. `assets.retire` -> "Retire assets".
 * Used to explain what a role preset actually grants. Unknown keys fall back to
 * the raw key rather than guessing.
 */
export const PERMISSION_LABELS: Record<string, string> = {
  [Permission.AssetsRead]: 'View assets',
  [Permission.AssetsCreate]: 'Add assets',
  [Permission.AssetsUpdate]: 'Edit assets',
  [Permission.AssetsRetire]: 'Retire assets',
  [Permission.AssetsRestore]: 'Restore retired assets',
  [Permission.AssignmentsRead]: 'View custody',
  [Permission.AssignmentsManage]: 'Assign, transfer and return',
  [Permission.PeopleRead]: 'View people and departments',
  [Permission.PeopleOperate]: 'Add and edit people',
  [Permission.PeopleManage]: 'Archive people, manage departments',
  [Permission.WarrantiesRead]: 'View warranties',
  [Permission.WarrantiesManage]: 'Manage warranties',
  [Permission.HistoryRead]: 'View asset history',
  [Permission.HistoryNote]: 'Add history notes',
  [Permission.QrGenerate]: 'Issue QR labels',
  [Permission.QrRotate]: 'Rotate QR identifiers',
  [Permission.QrPrint]: 'Print label sheets',
  [Permission.ImportsValidate]: 'Validate imports',
  [Permission.ImportsRead]: 'View imports',
  [Permission.ImportsCommit]: 'Commit imports',
  [Permission.ExportsAssets]: 'Export assets',
  [Permission.ExportsPeople]: 'Export people',
  [Permission.ExportsDepartments]: 'Export departments',
  [Permission.ExportsLocations]: 'Export locations',
  [Permission.ReferenceManage]: 'Manage categories and locations',
  [Permission.AuditRead]: 'Read the audit trail',
  [Permission.AuditExport]: 'Export the audit trail',
  [Permission.LegacyReconcile]: 'Run migration reconciliation',
  [Permission.AgentsRead]: 'View device inventory',
  [Permission.AgentsManage]: 'Manage device agents',
  [Permission.AgentsRecover]: 'Authorise agent key recovery',
  [Permission.UsersManage]: 'Administer accounts',
  [Permission.SettingsManage]: 'Change system settings',
  [Permission.AssetsBulk]: 'Change many assets at once',
  [Permission.AssetTagsManage]: 'Set how asset tags are issued',
  [Permission.StocktakesRead]: 'View stocktakes',
  [Permission.StocktakesManage]: 'Run stocktakes',
  [Permission.BundlesRead]: 'View asset bundles',
  [Permission.BundlesManage]: 'Manage asset bundles',
  [Permission.LeaversRead]: 'View leaver cases',
  [Permission.LeaversManage]: 'Manage leaver cases',
  [Permission.AttachmentsRead]: 'View asset attachments',
  [Permission.AttachmentsManage]: 'Upload and archive attachments',
};

export function describePermission(key: string): string {
  return PERMISSION_LABELS[key] ?? key;
}
