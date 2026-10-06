import { Building2, UserRound } from 'lucide-react';
import { Link } from 'react-router';

import type {
  AssetListItem,
  AssignmentTarget,
  CurrentAssignment,
  WarrantySummary,
} from '@/api/types';
import { formatDate, formatDaysRemaining } from '@/lib/format';
import { Tooltip } from '@/ui/Tooltip';
import { WarrantyStateDot } from '@/ui/StatusPill';

import { distinctDeviceName, makeAndModel } from './assetText';
import { DeviceGlyph } from './DeviceIllustration';
import { deviceKindFor } from './deviceKind';

/**
 * The first cell of any asset table: what kind of thing it is, its tag, and the
 * make and model under it. Shared so an asset reads the same wherever it is
 * listed — the register, a location, a bundle.
 */
export function AssetIdentity({ asset }: { asset: AssetListItem }) {
  const product = makeAndModel(asset);
  const deviceName = distinctDeviceName(asset);
  return (
    <div className="flex min-w-0 items-center gap-3">
      <DeviceGlyph kind={deviceKindFor(asset)} />
      <div className="min-w-0">
        <Link
          to={`/assets/${asset.id}`}
          // Stops a clickable row's own handler firing a second navigation.
          onClick={(event) => event.stopPropagation()}
          className="block truncate rounded-xs font-medium tabular-nums text-text hover:text-accent hover:underline"
        >
          {asset.assetTag}
        </Link>
        <p className="truncate text-xs text-text-muted">
          {product ?? asset.category.name}
          {product && <span className="text-text-subtle"> · {asset.category.name}</span>}
          {deviceName && <span className="text-text-subtle"> · {deviceName}</span>}
        </p>
      </div>
    </div>
  );
}

/** Route to whichever holder kind an assignment points at. */
export function holderPath(target: AssignmentTarget): string {
  return target.type === 'person'
    ? `/people/${target.id}`
    : `/departments/${target.id}`;
}

export function HolderLink({
  assignment,
  className,
}: {
  assignment: CurrentAssignment | null;
  className?: string;
}) {
  if (!assignment) {
    return <span className="text-text-subtle">Unassigned</span>;
  }

  const { target } = assignment;
  const Icon = target.type === 'person' ? UserRound : Building2;

  return (
    <Link
      to={holderPath(target)}
      onClick={(event) => event.stopPropagation()}
      className={`inline-flex min-w-0 max-w-full items-center gap-1.5 rounded-xs text-text hover:text-accent hover:underline ${className ?? ''}`}
    >
      <Icon className="size-3.5 shrink-0 text-text-subtle" />
      <span className="truncate">{target.displayName}</span>
    </Link>
  );
}

/**
 * Warranty state plus the detail that makes it actionable. `notRecorded` renders
 * as a plain dash so a register with little coverage data does not look alarming.
 */
export function WarrantyCell({ summary }: { summary: WarrantySummary }) {
  if (summary.state === 'notRecorded') {
    return <span className="text-text-subtle">—</span>;
  }

  const detail = [
    summary.provider,
    summary.endsOn ? `Ends ${formatDate(summary.endsOn)}` : null,
    formatDaysRemaining(summary.daysRemaining),
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <Tooltip content={detail}>
      <span className="inline-block">
        <WarrantyStateDot state={summary.state} />
      </span>
    </Tooltip>
  );
}
