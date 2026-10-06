import type { AssetCondition, AssetStatus, WarrantyState } from '@/api/types';
import {
  ASSET_CONDITION_LABELS,
  ASSET_STATUS_LABELS,
  WARRANTY_STATE_LABELS,
} from '@/lib/format';
import { Badge, type BadgeTone } from './Badge';
import { StatusDot } from './StatusDot';

/**
 * Domain vocabulary -> visual tone, in one place.
 *
 * Colour is never the only signal: every pill also carries its label, and the
 * dot shape gives a second cue for colour-vision deficiency.
 */

export const ASSET_STATUS_TONES: Record<AssetStatus, BadgeTone> = {
  inStock: 'success',
  reserved: 'info',
  assigned: 'accent',
  repair: 'warning',
  lost: 'danger',
  stolen: 'danger',
  retired: 'neutral',
  disposed: 'neutral',
};

export const ASSET_CONDITION_TONES: Record<AssetCondition, BadgeTone> = {
  new: 'success',
  good: 'success',
  fair: 'warning',
  damaged: 'danger',
  unserviceable: 'danger',
};

export const WARRANTY_STATE_TONES: Record<WarrantyState, BadgeTone> = {
  notRecorded: 'neutral',
  future: 'info',
  active: 'success',
  expiringSoon: 'warning',
  expired: 'danger',
};

export function AssetStatusPill({ status }: { status: AssetStatus }) {
  return (
    <Badge tone={ASSET_STATUS_TONES[status]} dot>
      {ASSET_STATUS_LABELS[status]}
    </Badge>
  );
}

export function AssetConditionPill({ condition }: { condition: AssetCondition }) {
  return <Badge tone={ASSET_CONDITION_TONES[condition]}>{ASSET_CONDITION_LABELS[condition]}</Badge>;
}

/** The dot-and-word forms, for table rows and the asset header. */
export function AssetStatusDot({ status }: { status: AssetStatus }) {
  return <StatusDot tone={ASSET_STATUS_TONES[status]}>{ASSET_STATUS_LABELS[status]}</StatusDot>;
}

export function AssetConditionDot({ condition }: { condition: AssetCondition }) {
  return (
    <StatusDot tone={ASSET_CONDITION_TONES[condition]} muted>
      {ASSET_CONDITION_LABELS[condition]}
    </StatusDot>
  );
}

/** Expired coverage is the one state worth shouting about, so its word turns red too. */
export function WarrantyStateDot({ state }: { state: WarrantyState }) {
  return (
    <StatusDot
      tone={WARRANTY_STATE_TONES[state]}
      muted={state !== 'expired'}
      className={state === 'expired' ? 'font-medium text-danger' : undefined}
    >
      {WARRANTY_STATE_LABELS[state]}
    </StatusDot>
  );
}

export function WarrantyStatePill({
  state,
  className,
}: {
  state: WarrantyState;
  className?: string;
}) {
  return (
    <Badge tone={WARRANTY_STATE_TONES[state]} className={className}>
      {WARRANTY_STATE_LABELS[state]}
    </Badge>
  );
}
