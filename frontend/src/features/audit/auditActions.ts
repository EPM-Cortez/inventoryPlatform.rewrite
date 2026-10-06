/**
 * Human labels for the audit vocabulary.
 *
 * The *values* come from `GET /audit/vocabulary`, which reports what is actually
 * in the trail — so the filters never offer a value with no rows behind it, and
 * a new kind of event shows up without a frontend change. This file supplies the
 * labels and the grouping only.
 *
 * Anything unlabelled falls back to the raw string rather than being hidden. An
 * audit trail that quietly drops entries it does not understand is worse than an
 * ugly one.
 */

export interface AuditActionGroup {
  label: string;
  actions: { value: string; label: string }[];
}

/**
 * Canonical action names, past tense throughout, dot-separated with snake_case
 * inside each segment.
 *
 * The pre-canonicalisation spellings are listed underneath. The API still
 * accepts them, so a bookmarked filter URL keeps working — and if one is in the
 * URL it must still render as a name rather than as a raw string.
 */
const ACTION_LABELS: Record<string, string> = {
  'authentication.login': 'Signed in',
  'authentication.logout': 'Signed out',

  'user.created': 'Account created',
  'user.updated': 'Account edited',
  'user.roles.changed': 'Roles changed',
  'user.disabled': 'Sign-in disabled',
  'user.archived': 'Account archived',
  'user.restored': 'Account restored',
  'user.password.changed': 'Password changed by owner',
  'user.password.reset': 'Password reset by admin',
  'administrator.bootstrap': 'First administrator created',

  'user.mfa.enrollment.started': 'Enrollment started',
  'user.mfa.enrollment.confirmed': 'Enrollment confirmed',
  'user.mfa.recovery_codes.regenerated': 'Recovery codes replaced',
  'user.mfa.reset': 'Reset by admin',

  'assets.import.validated': 'Asset import checked',
  'assets.import.committed': 'Asset import committed',
  'assets.import.commit_rejected': 'Asset import rejected',
  'assets.exported': 'Assets exported',
  'reference_data.import.validated': 'Reference import checked',
  'reference_data.import.committed': 'Reference import committed',
  'reference_data.import.commit_rejected': 'Reference import rejected',
  'reference_data.exported': 'Reference data exported',
  'audit.exported': 'Audit trail exported',

  'agent.enrolled': 'Agent enrolled',
  'agent.revoked': 'Agent revoked',
  'agent.asset.linked': 'Agent linked to asset',
  'agent.asset.auto_linked': 'Agent auto-linked to asset',
  'agent.asset.created_and_linked': 'Asset created and linked',
  'agent.asset.unlinked': 'Agent unlinked from asset',
  'agent.asset.match_conflict': 'Match conflict found',
  'agent.match.dismissed': 'Dismissed from review',
  'agent.match.review_restored': 'Restored to review',
  'agent.enrollment_profile.created': 'Enrollment token issued',
  'agent.enrollment_profile.revoked': 'Enrollment token revoked',
  'agent.key_recovery.approved': 'Key recovery authorised',
  'agent.key_recovery.revoked': 'Key recovery revoked',
  'agent.key_recovered': 'Machine re-keyed itself',

  'stocktake.started': 'Stocktake started',
  'stocktake.asset.observed': 'Asset seen',
  'stocktake.completed': 'Stocktake completed',
  'stocktake.cancelled': 'Stocktake cancelled',

  'assets.bulk.previewed': 'Bulk change previewed',
  'assets.bulk.committed': 'Bulk change applied',
  'assignment.expected_return.updated': 'Return date changed',

  'leaver.created': 'Leaver case opened',
  'leaver.updated': 'Leaver case edited',
  'leaver.completed': 'Leaver case completed',
  'leaver.cancelled': 'Leaver case cancelled',
  'leaver.rehired': 'Leaver rehired',

  'attachment.uploaded': 'File attached',
  'attachment.downloaded': 'File downloaded',
  'attachment.archived': 'File archived',
  'attachment.scanned': 'File scanned',

  'asset_bundle.created': 'Bundle created',
  'asset_bundle.updated': 'Bundle edited',
  'asset_bundle.archived': 'Bundle archived',
  'asset_bundle.restored': 'Bundle restored',

  'qr.labels.printed': 'Labels printed',
  'saved_search.created': 'Search saved',
  'saved_search.updated': 'Saved search edited',
  'saved_search.default.set': 'Default search set',
  'saved_search.archived': 'Saved search archived',

  'school.settings.created': 'Settings created',
  'school.settings.updated': 'Settings updated',
  'asset_tag_policy.updated': 'Asset tag policy changed',
  'identity.role_presets.reconciled': 'Role presets reconciled',
  'legacy.reconciliation.dry_run': 'Reconciliation dry run',
  'legacy.reconciliation.signed': 'Reconciliation signed off',

  // Pre-canonicalisation spellings. Still accepted by the API's filters, so a
  // saved link keeps working and its entries keep their names.
  'stocktake.asset-observed': 'Asset seen',
  'saved-search.create': 'Search saved',
  'saved-search.update': 'Saved search edited',
  'saved-search.archive': 'Saved search archived',
  'leaver.create': 'Leaver case opened',
  'leaver.update': 'Leaver case edited',
  'leaver.complete': 'Leaver case completed',
  'leaver.cancel': 'Leaver case cancelled',
  'attachment.upload': 'File attached',
  'attachment.download': 'File downloaded',
  'attachment.archive': 'File archived',
  'attachment.scan': 'File scanned',
  'assignment.expected-return.update': 'Return date changed',
};

/**
 * Dropdown groups, matched against the action by longest prefix.
 *
 * Order here is the order on screen. Anything that matches nothing lands in
 * "Other", so a brand-new server action is still selectable before anyone gets
 * round to naming it.
 */
const ACTION_GROUPS: { prefix: string; label: string }[] = [
  { prefix: 'authentication.', label: 'Sign-in' },
  { prefix: 'administrator.', label: 'Accounts' },
  { prefix: 'user.mfa.', label: 'Two-step verification' },
  { prefix: 'user.', label: 'Accounts' },
  { prefix: 'assets.import.', label: 'Import and export' },
  { prefix: 'reference_data.', label: 'Import and export' },
  { prefix: 'assets.exported', label: 'Import and export' },
  { prefix: 'audit.exported', label: 'Import and export' },
  { prefix: 'agent.', label: 'Devices' },
  { prefix: 'stocktake.', label: 'Stocktakes' },
  { prefix: 'assets.bulk.', label: 'Bulk changes and custody' },
  { prefix: 'assignment.', label: 'Bulk changes and custody' },
  { prefix: 'leaver.', label: 'Bulk changes and custody' },
  { prefix: 'attachment.', label: 'Attachments and bundles' },
  { prefix: 'asset_bundle.', label: 'Attachments and bundles' },
  { prefix: 'qr.', label: 'Labels and saved searches' },
  { prefix: 'saved_search.', label: 'Labels and saved searches' },
  { prefix: 'saved-search.', label: 'Labels and saved searches' },
  { prefix: 'school.', label: 'Settings and cutover' },
  { prefix: 'asset_tag_policy.', label: 'Settings and cutover' },
  { prefix: 'identity.', label: 'Settings and cutover' },
  { prefix: 'legacy.', label: 'Settings and cutover' },
];

const OTHER_GROUP = 'Other';

/** Groups the server's action list for the filter dropdown. */
export function groupAuditActions(actions: readonly string[]): AuditActionGroup[] {
  const byLabel = new Map<string, { value: string; label: string }[]>();
  const order: string[] = [];

  for (const action of actions) {
    // Longest prefix wins, so `user.mfa.` beats `user.`.
    const match = ACTION_GROUPS.filter((group) => action.startsWith(group.prefix)).sort(
      (a, b) => b.prefix.length - a.prefix.length,
    )[0];
    const groupLabel = match?.label ?? OTHER_GROUP;
    if (!byLabel.has(groupLabel)) {
      byLabel.set(groupLabel, []);
      order.push(groupLabel);
    }
    byLabel.get(groupLabel)!.push({ value: action, label: describeAuditAction(action) });
  }

  const declared = ACTION_GROUPS.map((group) => group.label);
  return order
    .sort((a, b) => {
      // Declared order first, "Other" last, anything else in between.
      const ai = a === OTHER_GROUP ? Infinity : declared.indexOf(a);
      const bi = b === OTHER_GROUP ? Infinity : declared.indexOf(b);
      return ai - bi;
    })
    .map((label) => ({
      label,
      actions: byLabel.get(label)!.sort((a, b) => a.label.localeCompare(b.label)),
    }));
}

export function describeAuditAction(action: string): string {
  return ACTION_LABELS[action] ?? action;
}

/** True when the raw value has no curated label, so the raw string is on show. */
export function isKnownAuditAction(action: string): boolean {
  return action in ACTION_LABELS;
}

export type OutcomeTone = 'success' | 'warning' | 'danger' | 'neutral';

export function outcomeTone(outcome: string): OutcomeTone {
  // `success` was the pre-canonicalisation spelling and survives on old rows.
  if (outcome === 'succeeded' || outcome === 'success') return 'success';
  if (outcome === 'failed') return 'danger';
  if (outcome === 'rejected' || outcome === 'validation_failed') return 'warning';
  return 'neutral';
}

export function describeOutcome(outcome: string): string {
  switch (outcome) {
    case 'succeeded':
    case 'success':
      return 'Succeeded';
    case 'failed':
      return 'Failed';
    case 'rejected':
      return 'Rejected';
    case 'validation_failed':
      return 'Validation failed';
    default:
      return outcome;
  }
}

const TARGET_TYPE_LABELS: Record<string, string> = {
  asset: 'Asset',
  user: 'Account',
  audit: 'Audit trail',
  school_settings: 'School settings',
  managed_agent: 'Device agent',
  agent_enrollment_profile: 'Enrollment token',
  asset_import_job: 'Asset import',
  reference_data_import_job: 'Reference import',
  asset_label_batch: 'Label batch',
  asset_bulk_change_job: 'Bulk change',
  asset_assignment: 'Custody record',
  asset_attachment: 'Attachment',
  asset_bundle: 'Bundle',
  asset_tag_policy: 'Asset tag policy',
  leaver_case: 'Leaver case',
  stocktake_session: 'Stocktake',
  saved_asset_search: 'Saved search',
  role_presets: 'Role presets',
  people: 'People',
  departments: 'Departments',
  locations: 'Locations',

  // Pre-canonicalisation spellings, kept so old rows and old links still read
  // as names rather than as raw identifiers.
  assetImportJob: 'Asset import',
  referenceDataImportJob: 'Reference import',
  'asset-label-batch': 'Label batch',
  'role-presets': 'Role presets',
};

export function describeTargetType(targetType: string): string {
  return TARGET_TYPE_LABELS[targetType] ?? targetType;
}

/** Builds the target-type options from the server's list. */
export function targetTypeOptions(
  targetTypes: readonly string[],
): { value: string; label: string }[] {
  return targetTypes
    .map((value) => ({ value, label: describeTargetType(value) }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

/** Builds the outcome options from the server's list. */
export function outcomeOptions(
  outcomes: readonly string[],
): { value: string; label: string }[] {
  return outcomes.map((value) => ({ value, label: describeOutcome(value) }));
}
