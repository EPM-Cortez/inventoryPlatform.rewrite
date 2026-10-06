import { CalendarClock, MapPin } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';

import { useAssetAssignments } from '@/api/hooks';
import type { Asset } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import {
  formatDate,
  formatDateTime,
  formatDaysRemaining,
  formatMoney,
} from '@/lib/format';
import { Avatar } from '@/ui/Avatar';
import { CopyButton } from '@/ui/CopyButton';
import { AssetConditionDot, AssetStatusDot, WarrantyStateDot } from '@/ui/StatusPill';
import { SummaryFact, SummaryPanel } from '@/ui/SummaryPanel';

import { holderPath } from './AssetCells';
import { distinctDeviceName, makeAndModel } from './assetText';
import { DeviceIllustration } from './DeviceIllustration';
import { deviceKindFor } from './deviceKind';

/** Same page and size as the custody log, so both read one cached request. */
const CUSTODY_PAGE_SIZE = 25;

/**
 * The top of an asset: what it is, a picture of it, and the four facts people
 * open it to find — who has it, where it is, whether it is covered, and when it
 * was bought.
 */
export function AssetHero({ asset, actions }: { asset: Asset; actions: ReactNode }) {
  const product = makeAndModel(asset);
  const deviceName = distinctDeviceName(asset);

  return (
    <SummaryPanel
      label="Asset summary"
      media={
        <div className="flex w-36 items-center justify-center rounded-xl bg-device-backdrop px-3 py-2 sm:w-44 lg:w-52">
          <DeviceIllustration kind={deviceKindFor(asset)} />
        </div>
      }
      title={<span className="break-all tabular-nums">{asset.assetTag}</span>}
      titleAccessory={<CopyButton value={asset.assetTag} label="Copy asset tag" />}
      subtitle={
        <>
          {product ?? asset.category.name}
          {product && <span className="text-text-subtle"> · {asset.category.name}</span>}
          {deviceName && (
            <span className="text-text-subtle">
              {' · '}
              <span className="font-mono text-sm">{deviceName}</span>
            </span>
          )}
        </>
      }
      status={
        <>
          <AssetStatusDot status={asset.status} />
          <AssetConditionDot condition={asset.condition} />
        </>
      }
      actions={actions}
      facts={
        <>
          <HolderFact asset={asset} />
          <SummaryFact label="Location">
            {asset.location ? (
              <Link
                to={`/assets?locationId=${asset.location.id}`}
                className="inline-flex min-w-0 items-center gap-1.5 rounded-xs text-text hover:text-accent hover:underline"
                title="Other assets in this location"
              >
                <MapPin className="size-3.5 shrink-0 text-text-subtle" aria-hidden />
                <span className="truncate">{asset.location.name}</span>
              </Link>
            ) : null}
          </SummaryFact>
          <WarrantyFact asset={asset} />
          <SummaryFact
            label="Purchased"
            secondary={
              asset.purchaseCost !== null
                ? formatMoney(asset.purchaseCost, asset.currency)
                : asset.supplier
            }
          >
            {asset.purchaseDate ? formatDate(asset.purchaseDate) : null}
          </SummaryFact>
        </>
      }
    />
  );
}

function HolderFact({ asset }: { asset: Asset }) {
  const { can } = useSession();
  const current = asset.currentAssignment;
  // `currentAssignment` carries no due date, so the open loan is read from the
  // custody log — the same request the Activity tab makes.
  const log = useAssetAssignments(
    asset.id,
    1,
    CUSTODY_PAGE_SIZE,
    can(Permission.AssignmentsRead) && current !== null,
  );
  const openLoan = current
    ? log.data?.items.find((assignment) => assignment.id === current.assignmentId)
    : undefined;

  if (!current) {
    const offRegister = asset.status === 'retired' || asset.status === 'disposed';
    return (
      <SummaryFact label="Held by" empty={offRegister ? 'Off the register' : 'Not assigned'} />
    );
  }

  return (
    <SummaryFact
      label="Held by"
      secondary={
        openLoan?.isOverdue && openLoan.expectedReturnOn ? (
          <span className="inline-flex items-center gap-1 font-medium text-danger">
            <CalendarClock className="size-3" aria-hidden />
            Overdue since {formatDate(openLoan.expectedReturnOn)}
          </span>
        ) : openLoan?.expectedReturnOn ? (
          `Due back ${formatDate(openLoan.expectedReturnOn)}`
        ) : (
          `Since ${formatDateTime(current.assignedAtUtc)}`
        )
      }
    >
      <span className="flex min-w-0 items-center gap-2">
        <Avatar name={current.target.displayName} size="sm" />
        <Link
          to={holderPath(current.target)}
          className="truncate rounded-xs text-text hover:text-accent hover:underline"
        >
          {current.target.displayName}
        </Link>
      </span>
    </SummaryFact>
  );
}

function WarrantyFact({ asset }: { asset: Asset }) {
  const summary = asset.warrantySummary;
  if (summary.state === 'notRecorded') return <SummaryFact label="Warranty" />;

  return (
    <SummaryFact
      label="Warranty"
      secondary={[
        formatDaysRemaining(summary.daysRemaining) ??
          (summary.endsOn ? `ends ${formatDate(summary.endsOn)}` : null),
        summary.provider,
      ]
        .filter(Boolean)
        .join(' · ')}
    >
      <WarrantyStateDot state={summary.state} />
    </SummaryFact>
  );
}
