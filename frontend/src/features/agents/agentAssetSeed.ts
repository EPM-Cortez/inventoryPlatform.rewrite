import type {
  AgentDeviceSnapshot,
  CreateAndLinkManagedAgentAssetRequest,
  ManagedAgentListItem,
} from '@/api/agentTypes';
import type { AssetCondition, AssetFields } from '@/api/types';

/**
 * The machine's primary MAC address.
 *
 * The first adapter that has both a MAC and an IPv4 address — virtual adapters
 * from Hyper-V, VPN clients and Docker have a MAC but usually no routable IPv4,
 * so this skips most of them without needing to know their names.
 */
export function primaryMacAddress(
  snapshot: AgentDeviceSnapshot | null | undefined,
): string | null {
  const adapter = snapshot?.networkAdapters.find(
    (candidate) => candidate.macAddress && candidate.ipv4Addresses.length > 0,
  );
  return adapter?.macAddress ?? null;
}

/** What the operator sees prefilled, before they change any of it. */
export interface AgentAssetSeed {
  deviceName: string;
  manufacturer: string;
  model: string;
  serialNumber: string;
}

export function seedFromAgent(agent: ManagedAgentListItem): AgentAssetSeed {
  return {
    deviceName: agent.hostname,
    manufacturer: agent.manufacturer ?? '',
    model: agent.model ?? '',
    serialNumber: agent.serialNumber ?? '',
  };
}

export interface CreateAssetFromAgentForm extends AgentAssetSeed {
  generateAssetTag: boolean;
  assetTag: string;
  categoryId: string;
  locationId: string | null;
  condition: AssetCondition;
  reason: string;
}

/**
 * Builds the create-and-link request.
 *
 * Two things it is responsible for. Blank optional strings must go over the wire
 * as `null`, not `""` — the server treats an empty string as a value and will
 * happily store one. And the asset tag must come from exactly one place: the
 * union in `AssetTagSource` rejects both a tag and `generateAssetTag`, and
 * rejects neither, so the two shapes are built whole rather than patched.
 *
 * The machine identifiers — BIOS UUID, MAC, OS — are seeded from the snapshot
 * without being shown as fields. They are how the server recognises the machine
 * on its next check-in, and nobody would sensibly retype them.
 */
export function buildCreateAndLinkRequest(
  form: CreateAssetFromAgentForm,
  agent: ManagedAgentListItem,
  snapshot: AgentDeviceSnapshot | null | undefined,
): CreateAndLinkManagedAgentAssetRequest {
  const fields: AssetFields = {
    deviceName: form.deviceName.trim() || null,
    categoryId: form.categoryId,
    manufacturer: form.manufacturer.trim() || null,
    model: form.model.trim() || null,
    serialNumber: form.serialNumber.trim() || null,
    biosGuid: agent.biosUuid || null,
    lanMacAddress: primaryMacAddress(snapshot),
    operatingSystem: snapshot?.operatingSystem.name ?? null,
    operatingSystemVersion: snapshot?.operatingSystem.version ?? null,
    locationId: form.locationId,
    // A machine that is reporting in is in service, but it is nobody's yet —
    // `assigned` belongs to the assignment workflow.
    status: 'inStock',
    condition: form.condition,
  };

  const common = { reason: form.reason.trim() || null, version: agent.version };

  return form.generateAssetTag
    ? { ...fields, generateAssetTag: true, ...common }
    : { ...fields, assetTag: form.assetTag.trim(), ...common };
}
