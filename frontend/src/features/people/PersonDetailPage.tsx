import { Mail, Pencil, UserMinus } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router';

import { usePerson, usePersonAssets, usePersonHeldCount } from '@/api/hooks';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { PERSON_TYPE_LABELS } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Avatar } from '@/ui/Avatar';
import { Button } from '@/ui/Button';
import { CopyButton } from '@/ui/CopyButton';
import { ErrorState } from '@/ui/EmptyState';
import { Breadcrumbs } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';
import { StatusDot } from '@/ui/StatusDot';
import { SummaryFact, SummaryPanel } from '@/ui/SummaryPanel';

import { CreateLeaverCaseDialog } from '../leavers/CreateLeaverCaseDialog';
import {
  HeldNowFact,
  HolderAssetsPanel,
  HOLDER_ASSETS_PAGE_SIZE,
  useHolderAssetsState,
} from './HolderAssetsPanel';
import { PersonFormDialog } from './PersonFormDialog';

export default function PersonDetailPage() {
  const { personId } = useParams<{ personId: string }>();
  const { can } = useSession();
  const [editOpen, setEditOpen] = useState(false);
  const [leavingOpen, setLeavingOpen] = useState(false);
  const holderState = useHolderAssetsState();
  const canSeeAssets = can(Permission.AssetsRead);

  const person = usePerson(personId);
  const assets = usePersonAssets(
    personId,
    holderState.includeHistory,
    holderState.page,
    HOLDER_ASSETS_PAGE_SIZE,
  );
  const heldCount = usePersonHeldCount(personId, canSeeAssets);

  if (person.isLoading) return <LoadingBlock label="Loading person…" />;
  if (person.isError) {
    return <ErrorState error={person.error} onRetry={() => void person.refetch()} />;
  }
  if (!person.data) return null;

  const record = person.data;
  const canOpenLeaver = can(Permission.LeaversManage) && record.isActive;

  return (
    <div className="flex flex-col gap-5">
      <div className="-mb-2">
        <Breadcrumbs items={[{ label: 'People', to: '/people' }, { label: record.displayName }]} />
      </div>

      <SummaryPanel
        label="Person summary"
        media={<Avatar name={record.displayName} size="xl" />}
        title={record.displayName}
        subtitle={
          <>
            {PERSON_TYPE_LABELS[record.type]}
            <span className="text-text-subtle"> · custody record, not a sign-in</span>
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
          <>
            {canOpenLeaver && (
              <Button iconLeft={<UserMinus />} onClick={() => setLeavingOpen(true)}>
                Mark as leaving
              </Button>
            )}
            {can(Permission.PeopleOperate) && (
              <Button iconLeft={<Pencil />} onClick={() => setEditOpen(true)}>
                Edit
              </Button>
            )}
          </>
        }
        facts={
          <>
            <SummaryFact label="Type">{PERSON_TYPE_LABELS[record.type]}</SummaryFact>
            <SummaryFact label="Email">
              {record.email && (
                <a
                  href={`mailto:${record.email}`}
                  className="inline-flex min-w-0 items-center gap-1.5 rounded-xs hover:text-accent hover:underline"
                >
                  <Mail className="size-3.5 shrink-0 text-text-subtle" aria-hidden />
                  <span className="truncate">{record.email}</span>
                </a>
              )}
            </SummaryFact>
            <SummaryFact label="External ID">
              {record.externalId && (
                <span className="inline-flex items-center gap-1">
                  <span className="font-mono text-sm">{record.externalId}</span>
                  <CopyButton value={record.externalId} label="Copy external ID" />
                </span>
              )}
            </SummaryFact>
            {canSeeAssets && <HeldNowFact query={heldCount} />}
          </>
        }
      />

      {!record.isActive && (
        <Alert tone="warning" title="This person is archived">
          Their custody history is kept, but they cannot be given new assets.
        </Alert>
      )}

      {canSeeAssets ? (
        <HolderAssetsPanel
          query={assets}
          includeHistory={holderState.includeHistory}
          onIncludeHistoryChange={holderState.setIncludeHistory}
          page={holderState.page}
          onPageChange={holderState.setPage}
          holderLabel={record.displayName}
        />
      ) : (
        <Alert tone="info">
          Seeing which assets someone holds needs the assets.read permission.
        </Alert>
      )}

      {editOpen && <PersonFormDialog onClose={() => setEditOpen(false)} person={record} />}
      {leavingOpen && (
        <CreateLeaverCaseDialog person={record} onClose={() => setLeavingOpen(false)} />
      )}
    </div>
  );
}
