import { describe, expect, it } from 'vitest';

import type { AgentDeviceSnapshot, ManagedAgentListItem } from '@/api/agentTypes';

import {
  buildCreateAndLinkRequest,
  primaryMacAddress,
  seedFromAgent,
  type CreateAssetFromAgentForm,
} from './agentAssetSeed';

const agent: ManagedAgentListItem = {
  id: 'a1000000-0000-0000-0000-000000000001',
  enrollmentProfileId: 'e1000000-0000-0000-0000-000000000001',
  enrollmentProfileName: 'Rollout 2026',
  deviceId: 'device-abc',
  hostname: 'SCH-LAP-0042',
  serialNumber: '5CD1234XYZ',
  biosUuid: 'b1000000-0000-0000-0000-000000000001',
  manufacturer: 'Dell Inc.',
  model: 'Latitude 5440',
  keyProtection: 'tpm',
  status: 'active',
  matchState: 'unmatched',
  matchReason: null,
  connectivityStatus: 'online',
  linkedAssetId: null,
  linkedAssetTag: null,
  linkedAssetDeviceName: null,
  enrolledAtUtc: '2026-07-01T09:00:00Z',
  lastCheckInAtUtc: '2026-07-27T08:00:00Z',
  lastCollectedAtUtc: '2026-07-27T07:55:00Z',
  lastSeenIpAddress: '10.20.0.55',
  nextExpectedCheckInAtUtc: '2026-07-27T09:00:00Z',
  overdueAtUtc: '2026-07-27T11:00:00Z',
  lastAgentVersion: '1.4.0',
  lastSequence: 812,
  revokedAtUtc: null,
  revokedReason: null,
  version: 'f1000000-0000-0000-0000-000000000001',
};

const snapshot: AgentDeviceSnapshot = {
  snapshotId: 's1000000-0000-0000-0000-000000000001',
  collectedAtUtc: '2026-07-27T07:55:00Z',
  agentVersion: '1.4.0',
  hostname: 'SCH-LAP-0042',
  domain: 'school.internal',
  hardware: {
    manufacturer: 'Dell Inc.',
    model: 'Latitude 5440',
    serialNumber: '5CD1234XYZ',
    biosUuid: 'b1000000-0000-0000-0000-000000000001',
    biosVersion: '1.21.0',
    processor: 'Intel Core i5-1335U',
    processorCores: 10,
    logicalProcessors: 12,
    memoryBytes: 17179869184,
  },
  operatingSystem: {
    name: 'Windows 11 Pro',
    version: '10.0.26100',
    build: '26100.1742',
    architecture: 'x64',
    installedAtUtc: '2026-01-14T10:00:00Z',
    lastBootAtUtc: '2026-07-26T07:30:00Z',
  },
  security: {
    secureBootEnabled: true,
    tpmPresent: true,
    tpmEnabled: true,
    tpmVersion: '2.0',
    systemDriveProtected: true,
    antivirusProducts: 'Microsoft Defender',
  },
  networkAdapters: [
    // A Hyper-V adapter: has a MAC, but no routable address.
    { name: 'vEthernet (Default Switch)', type: 'Ethernet', macAddress: '00-15-5D-01-02-03', ipv4Addresses: [] },
    { name: 'Wi-Fi', type: 'Wireless80211', macAddress: 'AA-BB-CC-DD-EE-FF', ipv4Addresses: ['10.20.0.55'] },
  ],
  storageVolumes: [],
  installedSoftware: [],
};

const form: CreateAssetFromAgentForm = {
  generateAssetTag: true,
  assetTag: '',
  categoryId: 'c1000000-0000-0000-0000-000000000001',
  locationId: 'l1000000-0000-0000-0000-000000000001',
  condition: 'good',
  deviceName: 'SCH-LAP-0042',
  manufacturer: 'Dell Inc.',
  model: 'Latitude 5440',
  serialNumber: '5CD1234XYZ',
  reason: '',
};

describe('primaryMacAddress', () => {
  it('skips a virtual adapter with no routable address', () => {
    expect(primaryMacAddress(snapshot)).toBe('AA-BB-CC-DD-EE-FF');
  });

  it('is null when the machine has never checked in', () => {
    expect(primaryMacAddress(null)).toBeNull();
    expect(primaryMacAddress(undefined)).toBeNull();
  });

  it('is null when no adapter has both a MAC and an address', () => {
    const noneUsable: AgentDeviceSnapshot = {
      ...snapshot,
      networkAdapters: [
        { name: 'Loopback', type: null, macAddress: null, ipv4Addresses: ['127.0.0.1'] },
      ],
    };
    expect(primaryMacAddress(noneUsable)).toBeNull();
  });
});

describe('seedFromAgent', () => {
  it('takes what the machine reports', () => {
    expect(seedFromAgent(agent)).toEqual({
      deviceName: 'SCH-LAP-0042',
      manufacturer: 'Dell Inc.',
      model: 'Latitude 5440',
      serialNumber: '5CD1234XYZ',
    });
  });

  it('turns an unreported field into an empty control, not the string "null"', () => {
    const bare = { ...agent, manufacturer: null, model: null, serialNumber: null };
    expect(seedFromAgent(bare)).toEqual({
      deviceName: 'SCH-LAP-0042',
      manufacturer: '',
      model: '',
      serialNumber: '',
    });
  });
});

describe('buildCreateAndLinkRequest', () => {
  it('asks the server for a tag, and never sends both sources', () => {
    const body = buildCreateAndLinkRequest(form, agent, snapshot);
    expect(body).toMatchObject({ generateAssetTag: true });
    expect(body).not.toHaveProperty('assetTag');
  });

  it('sends a typed tag, and never sends the generate flag alongside it', () => {
    const body = buildCreateAndLinkRequest(
      { ...form, generateAssetTag: false, assetTag: '  SCH-000123  ' },
      agent,
      snapshot,
    );
    expect(body).toMatchObject({ assetTag: 'SCH-000123' });
    expect(body).not.toHaveProperty('generateAssetTag');
  });

  it('carries the agent version so a changed machine rejects the whole thing', () => {
    expect(buildCreateAndLinkRequest(form, agent, snapshot).version).toBe(agent.version);
  });

  it('seeds the machine identifiers from the snapshot', () => {
    expect(buildCreateAndLinkRequest(form, agent, snapshot)).toMatchObject({
      biosGuid: agent.biosUuid,
      lanMacAddress: 'AA-BB-CC-DD-EE-FF',
      operatingSystem: 'Windows 11 Pro',
      operatingSystemVersion: '10.0.26100',
    });
  });

  it('leaves the snapshot fields null when the machine has never checked in', () => {
    expect(buildCreateAndLinkRequest(form, agent, null)).toMatchObject({
      lanMacAddress: null,
      operatingSystem: null,
      operatingSystemVersion: null,
    });
  });

  it('sends blank optional strings as null rather than ""', () => {
    const blank = buildCreateAndLinkRequest(
      { ...form, deviceName: '   ', manufacturer: '', model: '  ', serialNumber: '', reason: '  ' },
      { ...agent, biosUuid: '' },
      snapshot,
    );
    expect(blank).toMatchObject({
      deviceName: null,
      manufacturer: null,
      model: null,
      serialNumber: null,
      biosGuid: null,
      reason: null,
    });
  });

  it('creates the asset in stock — assignment is a separate decision', () => {
    expect(buildCreateAndLinkRequest(form, agent, snapshot).status).toBe('inStock');
  });

  it('passes a chosen location through, and null when none was picked', () => {
    expect(buildCreateAndLinkRequest(form, agent, snapshot).locationId).toBe(
      form.locationId,
    );
    expect(
      buildCreateAndLinkRequest({ ...form, locationId: null }, agent, snapshot).locationId,
    ).toBeNull();
  });
});
