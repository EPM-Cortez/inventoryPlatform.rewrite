import { History, UserRound } from 'lucide-react';

import type { Asset } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { SegmentedControl } from '@/ui/SegmentedControl';

import { AssetCustodyTab } from './AssetCustodyTab';
import { AssetHistoryTab } from './AssetHistoryTab';

export type ActivityView = 'timeline' | 'custody';

/**
 * Everything that has happened to the asset, in one place. The timeline is the
 * full event record; the custody log is the same story told as loans, with due
 * dates and return conditions that the timeline only mentions in passing.
 *
 * Each half keeps its own permission, so the switch only appears when both can
 * be shown.
 */
export function AssetActivityTab({
  asset,
  view,
  onViewChange,
  onAssign,
}: {
  asset: Asset;
  view: ActivityView;
  onViewChange: (view: ActivityView) => void;
  onAssign: () => void;
}) {
  const { can } = useSession();
  const canTimeline = can(Permission.HistoryRead);
  const canCustody = can(Permission.AssignmentsRead);

  const effective: ActivityView = !canTimeline ? 'custody' : !canCustody ? 'timeline' : view;

  return (
    <div className="flex flex-col gap-4">
      {canTimeline && canCustody && (
        <SegmentedControl
          label="Activity view"
          hideLabel
          size="sm"
          value={effective}
          onChange={onViewChange}
          options={[
            {
              value: 'timeline',
              label: (
                <>
                  <History aria-hidden />
                  Timeline
                </>
              ),
            },
            {
              value: 'custody',
              label: (
                <>
                  <UserRound aria-hidden />
                  Custody log
                </>
              ),
            },
          ]}
        />
      )}

      {effective === 'timeline' ? (
        <AssetHistoryTab assetId={asset.id} />
      ) : (
        <AssetCustodyTab asset={asset} onAssign={onAssign} />
      )}
    </div>
  );
}
