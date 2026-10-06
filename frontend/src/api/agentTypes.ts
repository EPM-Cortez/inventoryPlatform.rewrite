import type { Asset, CreateAssetRequest, Guid, IsoDate, IsoInstant } from './types';

/**
 * Hand-mirrored from `Inventory.Contracts.V1.AgentContracts`.
 *
 * These describe what the Windows agent observed on a machine, not what someone
 * typed into the register. Everything here is **evidence**: it is collected
 * automatically, it can be stale, and it is never edited from the UI. Treat it as
 * read-only throughout.
 */

/**
 * The API serialises these as camelCase **strings** and rejects integers
 * (`JsonStringEnumConverter(camelCase, allowIntegerValues: false)`), in request
 * bodies and query strings alike. The OpenAPI document publishes the string
 * values, so these unions can be checked against it directly.
 */

/** A key held in the TPM cannot be exported; a software key can. */
export type AgentKeyProtection = 'tpm' | 'software';

export type ManagedAgentStatus = 'active' | 'revoked';

/**
 * Where a machine sits in the triage queue.
 *
 * - `matched` — tied to an asset.
 * - `unmatched` — nothing on the register corresponds to it yet.
 * - `conflict` — its serial or BIOS UUID pointed at more than one asset, or at
 *   an asset that is already linked to a different machine. Needs a decision.
 * - `dismissed` — someone has looked and decided it does not need an asset.
 *   Still enrolled and still reporting; just out of the queue.
 */
export type ManagedAgentMatchState = 'unmatched' | 'matched' | 'conflict' | 'dismissed';

/**
 * How recently the machine reported, judged by the server against the interval
 * it is expected to check in on — not by the client against a fixed threshold.
 */
export type ManagedAgentConnectivity = 'online' | 'stale' | 'offline' | 'neverCheckedIn';

export interface AgentHardwareSnapshot {
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  biosUuid: string | null;
  biosVersion: string | null;
  processor: string | null;
  processorCores: number | null;
  logicalProcessors: number | null;
  memoryBytes: number | null;
}

export interface AgentOperatingSystemSnapshot {
  name: string | null;
  version: string | null;
  build: string | null;
  architecture: string | null;
  installedAtUtc: IsoInstant | null;
  lastBootAtUtc: IsoInstant | null;
}

export interface AgentSecuritySnapshot {
  secureBootEnabled: boolean | null;
  tpmPresent: boolean | null;
  tpmEnabled: boolean | null;
  tpmVersion: string | null;
  /** BitLocker or equivalent on the system drive. */
  systemDriveProtected: boolean | null;
  /** Free text from the machine — often several products, comma separated. */
  antivirusProducts: string | null;
}

export interface AgentNetworkAdapter {
  name: string;
  type: string | null;
  macAddress: string | null;
  ipv4Addresses: string[];
}

export interface AgentStorageVolume {
  name: string;
  label: string | null;
  fileSystem: string | null;
  totalBytes: number;
  freeBytes: number;
}

export interface AgentInstalledSoftware {
  name: string;
  version: string | null;
  publisher: string | null;
  architecture: string | null;
  installedOn: IsoDate | null;
}

/** One complete observation of a machine at a point in time. */
export interface AgentDeviceSnapshot {
  snapshotId: Guid;
  collectedAtUtc: IsoInstant;
  agentVersion: string;
  hostname: string;
  domain: string | null;
  hardware: AgentHardwareSnapshot;
  operatingSystem: AgentOperatingSystemSnapshot;
  security: AgentSecuritySnapshot;
  networkAdapters: AgentNetworkAdapter[];
  storageVolumes: AgentStorageVolume[];
  installedSoftware: AgentInstalledSoftware[];
}

export interface ManagedAgentListItem {
  id: Guid;
  /** The enrolment token this machine used. */
  enrollmentProfileId: Guid;
  enrollmentProfileName: string;
  deviceId: string;
  hostname: string;
  serialNumber: string | null;
  biosUuid: string | null;
  manufacturer: string | null;
  model: string | null;
  keyProtection: AgentKeyProtection;
  status: ManagedAgentStatus;
  matchState: ManagedAgentMatchState;
  /** Why the server reached that match state. Free text, shown as-is. */
  matchReason: string | null;
  connectivityStatus: ManagedAgentConnectivity;
  linkedAssetId: Guid | null;
  linkedAssetTag: string | null;
  linkedAssetDeviceName: string | null;
  enrolledAtUtc: IsoInstant;
  /** When the agent last spoke to the server. */
  lastCheckInAtUtc: IsoInstant | null;
  /** When the machine last *gathered* its snapshot, which can be earlier. */
  lastCollectedAtUtc: IsoInstant | null;
  /**
   * Observed by the server, so it sits outside the signed snapshot — a machine
   * cannot assert its own source address.
   */
  lastSeenIpAddress: string | null;
  nextExpectedCheckInAtUtc: IsoInstant | null;
  /** Past this, the server counts the machine as overdue rather than merely late. */
  overdueAtUtc: IsoInstant | null;
  lastAgentVersion: string | null;
  lastSequence: number;
  revokedAtUtc: IsoInstant | null;
  revokedReason: string | null;
  version: Guid;
}

/** Counts behind the fleet view. Every field is a filterable slice of `total`. */
export interface ManagedAgentFleetSummary {
  total: number;
  active: number;
  revoked: number;
  matched: number;
  unmatched: number;
  conflict: number;
  dismissed: number;
  online: number;
  stale: number;
  offline: number;
  neverCheckedIn: number;
}

/**
 * The installer the agent is deployed from.
 *
 * `sha256` and `signer` are the reason this endpoint exists: whoever installs
 * this needs to be able to check that what they downloaded is what the server
 * intended to serve.
 */
export interface AgentInstallerMetadata {
  fileName: string;
  version: string;
  sizeBytes: number;
  sha256: string;
  signed: boolean;
  signer: string | null;
  downloadUrl: string;
  /** Shell command to install it unattended, with placeholders for the token. */
  installCommandTemplate: string;
}

/** Every triage action is versioned and records why it was taken. */
export interface UnlinkManagedAgentRequest {
  reason: string;
  version: Guid;
}

export interface DismissManagedAgentRequest {
  reason: string;
  version: Guid;
}

export interface RestoreManagedAgentReviewRequest {
  reason: string;
  version: Guid;
}

/**
 * Creates an asset and links this agent to it in one transaction.
 *
 * The body is a normal asset create plus the agent's version, so the whole thing
 * is rejected if the agent changed underneath — no asset is left behind
 * unlinked. The fields are the *user's* to set: the UI seeds them from the
 * snapshot, but nothing is copied without someone seeing it first, which is the
 * distinction between this and the agent writing to the register itself.
 */
export type CreateAndLinkManagedAgentAssetRequest = CreateAssetRequest & {
  reason?: string | null;
  version: Guid;
};

export interface CreateAndLinkManagedAgentAssetResponse {
  asset: Asset;
  agent: ManagedAgentListItem;
}

export interface ManagedAgentDetail {
  agent: ManagedAgentListItem;
  /** Absent until the machine has checked in at least once. */
  latestSnapshot: AgentDeviceSnapshot | null;
}

export interface AgentEnrollmentProfile {
  id: Guid;
  name: string;
  expiresAtUtc: IsoInstant;
  maxEnrollments: number;
  enrolledCount: number;
  requireTpm: boolean;
  allowedHostnameSuffix: string | null;
  createdAtUtc: IsoInstant;
  createdByUserId: Guid;
  revokedAtUtc: IsoInstant | null;
  version: Guid;
}

export interface CreateAgentEnrollmentProfileRequest {
  name: string;
  expiresAtUtc: IsoInstant;
  maxEnrollments: number;
  requireTpm: boolean;
  allowedHostnameSuffix?: string | null;
}

/**
 * The token is returned **once**, on creation, and never again — it is the
 * secret an agent presents to enrol. The UI must make that plain.
 */
export interface CreatedAgentEnrollmentProfile {
  profile: AgentEnrollmentProfile;
  enrollmentToken: string;
}

// ---------------------------------------------------------------------------
// Key recovery
// ---------------------------------------------------------------------------

/**
 * An authorisation lets one machine replace its signing key without enrolling
 * again — for a motherboard swap or a TPM clear, where the hardware is the same
 * asset but can no longer prove it is.
 *
 * - `pending` — issued, unused, not yet expired.
 * - `consumed` — the machine used it. It is spent; a second use is refused.
 * - `expired` — the window closed with it unused.
 * - `revoked` — withdrawn by hand before it could be used.
 *
 * Only `pending` can be acted on. The other three are history.
 */
export type AgentKeyRecoveryStatus = 'pending' | 'expired' | 'consumed' | 'revoked';

/**
 * The list never carries the token, its hash, or a key thumbprint — by design on
 * the server. What is auditable is *that* someone authorised a re-key and why,
 * not the material that made it work.
 */
export interface AgentKeyRecoveryAuthorization {
  authorizationId: Guid;
  agentId: Guid;
  status: AgentKeyRecoveryStatus;
  /** Required at creation, so this is never blank. */
  reason: string;
  createdAtUtc: IsoInstant;
  createdByUserId: Guid;
  createdByDisplayName: string;
  expiresAtUtc: IsoInstant;
  /** When true the replacement key must also be TPM-held, as the original was. */
  requireTpm: boolean;
  consumedAtUtc: IsoInstant | null;
  revokedAtUtc: IsoInstant | null;
  revokedByUserId: Guid | null;
  revokedReason: string | null;
  version: Guid;
}

export interface CreateAgentKeyRecoveryRequest {
  reason: string;
  /** Server default if omitted. Shorter is safer — it is a live credential. */
  expiresAtUtc?: IsoInstant;
  requireTpm?: boolean;
  version?: Guid;
}

/**
 * `recoveryToken` is returned **once** and stored only as a hash. Any surface
 * that shows it must offer copy and download, must not close on a backdrop
 * click, and must say plainly that closing loses it.
 */
export interface CreatedAgentKeyRecovery {
  authorizationId: Guid;
  agentId: Guid;
  expiresAtUtc: IsoInstant;
  requireTpm: boolean;
  recoveryToken: string;
  version: Guid;
}

export interface RevokeAgentKeyRecoveryRequest {
  reason: string;
  version: Guid;
}

export interface LinkManagedAgentRequest {
  assetId: Guid;
  reason?: string | null;
  version: Guid;
}

export interface RevokeManagedAgentRequest {
  reason: string;
  version: Guid;
}

export interface AgentSearchParams {
  /** Free text over hostname, serial, model and the like. Server-side. */
  q?: string;
  includeRevoked?: boolean;
  /** Named `trustStatus` on the wire to avoid clashing with connectivity. */
  trustStatus?: ManagedAgentStatus;
  matchState?: ManagedAgentMatchState;
  connectivity?: ManagedAgentConnectivity;
  page?: number;
  pageSize?: number;
}
