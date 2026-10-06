import {
  Archive,
  ArrowLeftRight,
  CornerDownLeft,
  FileText,
  History,
  MonitorSmartphone,
  MoreHorizontal,
  Paperclip,
  Pencil,
  Printer,
  RotateCcw,
  ShieldCheck,
  Trash2,
  UserRoundPlus,
} from 'lucide-react';
import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';

import { useAsset, useAssetAttachments, useAssetWarranties, useRestoreAsset } from '@/api/hooks';
import type { Asset } from '@/api/types';
import { Permission, type PermissionKey } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button, IconButton } from '@/ui/Button';
import { ErrorState } from '@/ui/EmptyState';
import { Menu, MenuContent, MenuItem, MenuSeparator, MenuTrigger } from '@/ui/Menu';
import { Breadcrumbs } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { TabCount, Tabs, TabsContent, TabsList, TabsTrigger } from '@/ui/Tabs';
import { useToast } from '@/ui/Toast';

import { AssetDeviceTab } from '../agents/AssetDeviceTab';
import { PrintLabelDialog } from '../labels/PrintLabelDialog';
import { AssetActivityTab, type ActivityView } from './AssetActivityTab';
import { AssetAttachmentsTab } from './AssetAttachmentsTab';
import { AssetDetailsTab } from './AssetDetailsTab';
import { AssetHero } from './AssetHero';
import { AssetWarrantiesTab } from './AssetWarrantiesTab';
import { AssignDialog } from './AssignDialog';
import { DisposeDialog } from './DisposeDialog';
import { RetireDialog } from './RetireDialog';
import { ReturnDialog } from './ReturnDialog';

type TabId = 'details' | 'activity' | 'warranties' | 'files' | 'device';

interface TabDefinition {
  id: TabId;
  label: string;
  icon: typeof FileText;
  /** Any one of these is enough to show the tab. */
  anyOf?: PermissionKey[];
}

const TABS: TabDefinition[] = [
  { id: 'details', label: 'Details', icon: FileText },
  {
    id: 'activity',
    label: 'Activity',
    icon: History,
    anyOf: [Permission.HistoryRead, Permission.AssignmentsRead],
  },
  {
    id: 'warranties',
    label: 'Warranties',
    icon: ShieldCheck,
    anyOf: [Permission.WarrantiesRead],
  },
  { id: 'files', label: 'Files', icon: Paperclip, anyOf: [Permission.AttachmentsRead] },
  { id: 'device', label: 'Device', icon: MonitorSmartphone, anyOf: [Permission.AgentsRead] },
];

/**
 * Tab names from before custody and history were merged and the QR label moved
 * beside the details. Kept so an old bookmark still lands somewhere sensible.
 */
const LEGACY_TABS: Record<string, { tab: TabId; view?: ActivityView }> = {
  overview: { tab: 'details' },
  history: { tab: 'activity', view: 'timeline' },
  custody: { tab: 'activity', view: 'custody' },
  qr: { tab: 'details' },
};

export default function AssetDetailPage() {
  const { assetId } = useParams<{ assetId: string }>();
  const asset = useAsset(assetId);

  if (asset.isLoading) return <LoadingBlock label="Loading asset…" />;
  if (asset.isError) {
    return <ErrorState error={asset.error} onRetry={() => void asset.refetch()} />;
  }
  if (!asset.data || !assetId) return null;

  // Split so every hook below runs against a loaded asset, in a stable order.
  return <AssetDetail record={asset.data} />;
}

function AssetDetail({ record }: { record: Asset }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const { can } = useSession();
  const toast = useToast();

  const [assignOpen, setAssignOpen] = useState(false);
  const [returnOpen, setReturnOpen] = useState(false);
  const [retireOpen, setRetireOpen] = useState(false);
  const [disposeOpen, setDisposeOpen] = useState(false);
  const [printOpen, setPrintOpen] = useState(false);

  const restore = useRestoreAsset(record.id);

  // Counts for the tab labels. Same arguments as the tabs themselves, so opening
  // a tab reuses the request rather than making a second one.
  const warranties = useAssetWarranties(record.id, false, can(Permission.WarrantiesRead));
  const attachments = useAssetAttachments(
    record.id,
    false,
    1,
    25,
    can(Permission.AttachmentsRead),
  );

  const isRetired = record.status === 'retired';
  // Disposal is terminal: `Restore` only accepts a retired asset.
  const isDisposed = record.status === 'disposed';
  const isOffRegister = isRetired || isDisposed;
  const isAssigned = record.currentAssignment !== null;
  // The domain only permits assignment from `inStock` or `reserved`.
  const canBeAssigned = record.status === 'inStock' || record.status === 'reserved';
  const canEdit = can(Permission.AssetsUpdate) && !isOffRegister;
  const editPath = `/assets/${record.id}/edit`;

  const visibleTabs = TABS.filter((tab) => !tab.anyOf || tab.anyOf.some((key) => can(key)));

  const rawTab = searchParams.get('tab') ?? 'details';
  const legacy = LEGACY_TABS[rawTab];
  const requested = legacy?.tab ?? rawTab;
  const tab: TabId = visibleTabs.some((candidate) => candidate.id === requested)
    ? (requested as TabId)
    : 'details';
  const view: ActivityView =
    legacy?.view ?? (searchParams.get('view') === 'custody' ? 'custody' : 'timeline');

  function navigateTo(nextTab: string, nextView?: ActivityView) {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.set('tab', nextTab);
        if (nextView) next.set('view', nextView);
        else if (nextTab !== 'activity') next.delete('view');
        return next;
      },
      { replace: true },
    );
  }

  const counts: Partial<Record<TabId, number | undefined>> = {
    warranties: warranties.data?.length,
    files: attachments.data?.totalCount,
  };

  const custodyActions =
    can(Permission.AssignmentsManage) && !isOffRegister ? (
      isAssigned ? (
        <>
          <Button iconLeft={<CornerDownLeft />} onClick={() => setReturnOpen(true)}>
            Return
          </Button>
          <Button iconLeft={<ArrowLeftRight />} onClick={() => setAssignOpen(true)}>
            Transfer
          </Button>
        </>
      ) : (
        <Button
          variant="primary"
          iconLeft={<UserRoundPlus />}
          onClick={() => setAssignOpen(true)}
          disabled={!canBeAssigned}
          title={
            canBeAssigned ? undefined : 'Only assets that are in stock or reserved can be assigned.'
          }
        >
          Assign
        </Button>
      )
    ) : null;

  return (
    <div className="flex flex-col gap-5">
      {/* The summary panel below is the page header; only the trail sits above it. */}
      <div className="-mb-2">
        <Breadcrumbs items={[{ label: 'Assets', to: '/assets' }, { label: record.assetTag }]} />
      </div>

      <AssetHero
        asset={record}
        actions={
          <>
            {custodyActions}
            {canEdit && (
              <Button asChild>
                <Link to={editPath}>
                  <Pencil className="mr-1.5 size-4" aria-hidden />
                  Edit
                </Link>
              </Button>
            )}
            <Menu>
              <MenuTrigger asChild>
                <IconButton label="More actions" variant="secondary">
                  <MoreHorizontal />
                </IconButton>
              </MenuTrigger>
              <MenuContent>
                {can(Permission.QrPrint) && (
                  <MenuItem onSelect={() => setPrintOpen(true)}>
                    <Printer />
                    Print label
                  </MenuItem>
                )}
                {can(Permission.AssetsRetire) && !isOffRegister && (
                  <>
                    <MenuSeparator />
                    <MenuItem destructive onSelect={() => setRetireOpen(true)}>
                      <Archive />
                      Retire asset
                    </MenuItem>
                  </>
                )}
                {/* Disposal is allowed from any state except already-disposed,
                    including from retired — that is the normal path. */}
                {can(Permission.AssetsRetire) && !isDisposed && (
                  <MenuItem destructive onSelect={() => setDisposeOpen(true)}>
                    <Trash2 />
                    Dispose of asset
                  </MenuItem>
                )}
                {can(Permission.AssetsRestore) && isRetired && (
                  <>
                    <MenuSeparator />
                    <MenuItem
                      onSelect={() =>
                        restore.mutate(record.version, {
                          onSuccess: () => toast.success('Asset restored to stock'),
                          onError: (error) => toast.fromError(error, 'Could not restore'),
                        })
                      }
                    >
                      <RotateCcw />
                      Restore to stock
                    </MenuItem>
                  </>
                )}
              </MenuContent>
            </Menu>
          </>
        }
      />

      {isDisposed ? (
        <Alert tone="danger" title="This asset has been disposed of">
          Disposed {formatDateTime(record.disposedAtUtc)}
          {record.disposalReason ? ` — ${record.disposalReason}` : ''}. Disposal is
          permanent; the record is kept for audit but cannot be returned to the register.
        </Alert>
      ) : isRetired ? (
        <Alert tone="warning" title="This asset is off the active register">
          Retired {formatDateTime(record.retiredAtUtc)}
          {record.retirementReason ? ` — ${record.retirementReason}` : ''}.
          {can(Permission.AssetsRestore) && ' Restore it to put it back into stock.'}
        </Alert>
      ) : null}

      <Tabs value={tab} onValueChange={(next) => navigateTo(next)} className="min-w-0">
        <TabsList>
          {visibleTabs.map(({ id, label, icon: Icon }) => (
            <TabsTrigger key={id} value={id} className="flex items-center gap-1.5">
              <Icon className="size-4 shrink-0" aria-hidden />
              {label}
              <TabCount value={counts[id]} />
            </TabsTrigger>
          ))}
        </TabsList>

        <TabsContent value="details">
          <AssetDetailsTab asset={record} editPath={canEdit ? editPath : undefined} />
        </TabsContent>

        {visibleTabs.some((candidate) => candidate.id === 'activity') && (
          <TabsContent value="activity">
            <AssetActivityTab
              asset={record}
              view={view}
              onViewChange={(nextView) => navigateTo('activity', nextView)}
              onAssign={() => setAssignOpen(true)}
            />
          </TabsContent>
        )}

        {can(Permission.WarrantiesRead) && (
          <TabsContent value="warranties">
            <AssetWarrantiesTab assetId={record.id} />
          </TabsContent>
        )}

        {can(Permission.AttachmentsRead) && (
          <TabsContent value="files">
            <AssetAttachmentsTab asset={record} />
          </TabsContent>
        )}

        {can(Permission.AgentsRead) && (
          <TabsContent value="device">
            <AssetDeviceTab asset={record} />
          </TabsContent>
        )}
      </Tabs>

      {/* Each panel is mounted only while it is open, so it always opens from the
          asset's current state and version rather than a stale snapshot. */}
      {assignOpen && <AssignDialog onClose={() => setAssignOpen(false)} asset={record} />}
      {returnOpen && record.currentAssignment && (
        <ReturnDialog
          onClose={() => setReturnOpen(false)}
          asset={record}
          assignment={record.currentAssignment}
        />
      )}
      {retireOpen && <RetireDialog onClose={() => setRetireOpen(false)} asset={record} />}
      {disposeOpen && (
        <DisposeDialog onClose={() => setDisposeOpen(false)} asset={record} />
      )}
      {printOpen && (
        <PrintLabelDialog
          onClose={() => setPrintOpen(false)}
          assetId={record.id}
          assetTag={record.assetTag}
        />
      )}
    </div>
  );
}
