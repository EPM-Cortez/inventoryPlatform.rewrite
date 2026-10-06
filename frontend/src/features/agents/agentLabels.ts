import type {
  ManagedAgentConnectivity,
  ManagedAgentMatchState,
  ManagedAgentStatus,
} from '@/api/agentTypes';

export const MATCH_STATE_LABELS: Record<ManagedAgentMatchState, string> = {
  matched: 'Linked',
  unmatched: 'Not linked',
  conflict: 'Conflict',
  dismissed: 'Dismissed',
};

export const MATCH_STATE_TONES: Record<
  ManagedAgentMatchState,
  'success' | 'warning' | 'danger' | 'neutral'
> = {
  matched: 'success',
  unmatched: 'warning',
  // A conflict is worse than merely unmatched: the server found more than one
  // candidate, so someone has to choose rather than just get round to it.
  conflict: 'danger',
  dismissed: 'neutral',
};

export const MATCH_STATE_HINTS: Record<ManagedAgentMatchState, string> = {
  matched: 'Tied to an asset on the register.',
  unmatched: 'Reporting in, but nothing on the register corresponds to it yet.',
  conflict:
    'Its serial or BIOS UUID pointed at more than one asset, or at one already linked to another machine. Someone has to decide.',
  dismissed:
    'Someone looked and decided it does not need an asset. Still enrolled and still reporting.',
};

export const CONNECTIVITY_LABELS: Record<ManagedAgentConnectivity, string> = {
  online: 'Online',
  stale: 'Late',
  offline: 'Offline',
  neverCheckedIn: 'Never reported',
};

export const CONNECTIVITY_TONES: Record<
  ManagedAgentConnectivity,
  'success' | 'warning' | 'danger' | 'neutral'
> = {
  online: 'success',
  stale: 'warning',
  offline: 'danger',
  neverCheckedIn: 'neutral',
};

export const AGENT_STATUS_LABELS: Record<ManagedAgentStatus, string> = {
  active: 'Active',
  revoked: 'Revoked',
};

/**
 * The fleet summary tiles, in the order worth acting on.
 *
 * Each maps to the filter that shows the machines behind it, so a count is
 * always one click from the list it describes. `total` has no filter — it is the
 * whole fleet.
 */
export const FLEET_TILES: {
  key:
    | 'total'
    | 'unmatched'
    | 'conflict'
    | 'offline'
    | 'stale'
    | 'neverCheckedIn'
    | 'dismissed'
    | 'revoked';
  label: string;
  filter: { matchState?: string; connectivity?: string; trustStatus?: string } | null;
  tone?: 'warning' | 'danger' | 'neutral';
}[] = [
  { key: 'total', label: 'Enrolled', filter: null },
  {
    key: 'unmatched',
    label: 'Not linked',
    filter: { matchState: 'unmatched' },
    tone: 'warning',
  },
  {
    key: 'conflict',
    label: 'Conflicts',
    filter: { matchState: 'conflict' },
    tone: 'danger',
  },
  {
    key: 'offline',
    label: 'Offline',
    filter: { connectivity: 'offline' },
    tone: 'danger',
  },
  { key: 'stale', label: 'Late', filter: { connectivity: 'stale' }, tone: 'warning' },
  {
    key: 'neverCheckedIn',
    label: 'Never reported',
    filter: { connectivity: 'neverCheckedIn' },
    tone: 'neutral',
  },
  {
    key: 'dismissed',
    label: 'Dismissed',
    filter: { matchState: 'dismissed' },
    tone: 'neutral',
  },
  {
    key: 'revoked',
    label: 'Revoked',
    filter: { trustStatus: 'revoked' },
    tone: 'neutral',
  },
];
