import { Building2, Pencil } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router';

import { useDepartment, useDepartmentAssets, useDepartmentHeldCount } from '@/api/hooks';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { Alert } from '@/ui/Alert';
import { Avatar } from '@/ui/Avatar';
import { Button } from '@/ui/Button';
import { CopyButton } from '@/ui/CopyButton';
import { ErrorState } from '@/ui/EmptyState';
import { Breadcrumbs } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { StatusDot } from '@/ui/StatusDot';
import { SummaryFact, SummaryPanel } from '@/ui/SummaryPanel';

import {
  HeldNowFact,
  HolderAssetsPanel,
  HOLDER_ASSETS_PAGE_SIZE,
  useHolderAssetsState,
} from '../people/HolderAssetsPanel';
import { DepartmentFormDialog } from './DepartmentFormDialog';

export default function DepartmentDetailPage() {
  const { departmentId } = useParams<{ departmentId: string }>();
  const { can } = useSession();
  const [editOpen, setEditOpen] = useState(false);
  const holderState = useHolderAssetsState();
  const canSeeAssets = can(Permission.AssetsRead);

  const department = useDepartment(departmentId);
  const assets = useDepartmentAssets(
    departmentId,
    holderState.includeHistory,
    holderState.page,
    HOLDER_ASSETS_PAGE_SIZE,
  );
  const heldCount = useDepartmentHeldCount(departmentId, canSeeAssets);

  if (department.isLoading) return <LoadingBlock label="Loading department…" />;
  if (department.isError) {
    return <ErrorState error={department.error} onRetry={() => void department.refetch()} />;
  }
  if (!department.data) return null;

  const record = department.data;

  return (
    <div className="flex flex-col gap-5">
      <div className="-mb-2">
        <Breadcrumbs
          items={[{ label: 'Departments', to: '/departments' }, { label: record.name }]}
        />
      </div>

      <SummaryPanel
        label="Department summary"
        media={
          <Avatar name={record.name} size="xl">
            <Building2 />
          </Avatar>
        }
        title={record.name}
        subtitle={
          <>
            Department
            <span className="text-text-subtle"> · holds shared equipment</span>
          </>
        }
        status={
          record.isActive ? (
            <StatusDot tone="success" muted>
              Active
            </StatusDot>
          ) : (
            <StatusDot tone="neutral" muted>
              Archived
            </StatusDot>
          )
        }
        actions={
          can(Permission.PeopleManage) && (
            <Button iconLeft={<Pencil />} onClick={() => setEditOpen(true)}>
              Edit
            </Button>
          )
        }
        facts={
          <>
            <SummaryFact label="Code">
              <span className="inline-flex items-center gap-1">
                <span className="font-mono text-sm">{record.code}</span>
                <CopyButton value={record.code} label="Copy department code" />
              </span>
            </SummaryFact>
            {canSeeAssets && <HeldNowFact query={heldCount} />}
          </>
        }
      />

      {!record.isActive && (
        <Alert tone="warning" title="This department is archived">
          Its custody history is kept, but it cannot be given new assets.
        </Alert>
      )}

      {canSeeAssets ? (
        <HolderAssetsPanel
          query={assets}
          includeHistory={holderState.includeHistory}
          onIncludeHistoryChange={holderState.setIncludeHistory}
          page={holderState.page}
          onPageChange={holderState.setPage}
          holderLabel={record.name}
        />
      ) : (
        <Alert tone="info">
          Seeing which assets a department holds needs the assets.read permission.
        </Alert>
      )}

      {editOpen && (
        <DepartmentFormDialog onClose={() => setEditOpen(false)} department={record} />
      )}
    </div>
  );
}
