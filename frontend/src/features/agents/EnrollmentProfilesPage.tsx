import { KeyRound, MoreHorizontal, Plus, ShieldOff } from 'lucide-react';
import { useMemo, useState } from 'react';

import type { AgentEnrollmentProfile } from '@/api/agentTypes';
import {
  useAgentEnrollmentProfiles,
  useRevokeAgentEnrollmentProfile,
} from '@/api/hooks';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime, formatNumber, formatRelative } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Menu, MenuContent, MenuItem, MenuTrigger } from '@/ui/Menu';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { Tooltip } from '@/ui/Tooltip';
import { useToast } from '@/ui/Toast';

import { CreateEnrollmentProfileDialog } from './CreateEnrollmentProfileDialog';

const DEFAULT_PAGE_SIZE = 25;

/**
 * Tokens that let a machine enrol itself as a managed agent.
 *
 * A live token is a credential: anything holding it can register a machine into
 * the register. The list is built around answering "which of these are still
 * usable, and by how many more machines".
 */
export default function EnrollmentProfilesPage() {
  const url = useUrlState();
  const toast = useToast();
  const { can } = useSession();

  // Captured once so every row judges expiry against the same instant, and so
  // rendering stays pure.
  const [now] = useState(() => Date.now());
  const [creating, setCreating] = useState(false);
  const [revoking, setRevoking] = useState<AgentEnrollmentProfile | null>(null);

  const params = useMemo(
    () => ({
      includeRevoked: url.getBoolean('includeRevoked') ?? false,
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
    }),
    [url],
  );

  const profiles = useAgentEnrollmentProfiles(params);
  const revoke = useRevokeAgentEnrollmentProfile();
  const canManage = can(Permission.AgentsManage);

  const rows = profiles.data?.items ?? [];

  const columns: Column<AgentEnrollmentProfile>[] = [
    {
      id: 'name',
      header: 'Token',
      cell: (profile) => (
        <div className="min-w-0">
          <p className="truncate font-medium text-text">{profile.name}</p>
          <p className="truncate text-2xs text-text-subtle">
            Created {formatRelative(profile.createdAtUtc)}
          </p>
        </div>
      ),
    },
    {
      id: 'state',
      header: 'State',
      width: 'w-36',
      cell: (profile) => <ProfileState profile={profile} now={now} />,
    },
    {
      id: 'used',
      header: 'Machines enrolled',
      align: 'right',
      width: 'w-40',
      cell: (profile) => (
        <span className="text-sm text-text-muted">
          {formatNumber(profile.enrolledCount)} of {formatNumber(profile.maxEnrollments)}
        </span>
      ),
    },
    {
      id: 'rules',
      header: 'Restrictions',
      hideBelow: 'lg',
      cell: (profile) => (
        <div className="flex flex-wrap gap-1">
          {profile.requireTpm ? (
            <Tooltip content="The machine must hold its signing key in a TPM, so the key cannot be copied off it.">
              <span className="inline-block">
                <Badge tone="success">TPM required</Badge>
              </span>
            </Tooltip>
          ) : (
            <Tooltip content="A machine without a TPM may enrol and keep its key in software.">
              <span className="inline-block">
                <Badge tone="warning">Software keys allowed</Badge>
              </span>
            </Tooltip>
          )}
          {profile.allowedHostnameSuffix && (
            <Badge tone="neutral">
              <span className="font-mono">*{profile.allowedHostnameSuffix}</span>
            </Badge>
          )}
        </div>
      ),
    },
    {
      id: 'expires',
      header: 'Expires',
      hideBelow: 'md',
      align: 'right',
      width: 'w-44',
      cell: (profile) => (
        <div className="min-w-0">
          <p className="truncate text-sm text-text-muted">
            {formatRelative(profile.expiresAtUtc)}
          </p>
          <p className="truncate text-2xs text-text-subtle">
            {formatDateTime(profile.expiresAtUtc)}
          </p>
        </div>
      ),
    },
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-14',
      cell: (profile) =>
        canManage && !profile.revokedAtUtc ? (
          <Menu>
            <MenuTrigger asChild>
              <IconButton label={`Actions for ${profile.name}`} size="sm">
                <MoreHorizontal />
              </IconButton>
            </MenuTrigger>
            <MenuContent>
              <MenuItem destructive onSelect={() => setRevoking(profile)}>
                <ShieldOff />
                Revoke token
              </MenuItem>
            </MenuContent>
          </Menu>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Enrollment tokens"
        description="What a new machine presents to register itself as a managed device."
        actions={
          canManage && (
            <Button variant="primary" iconLeft={<Plus />} onClick={() => setCreating(true)}>
              New token
            </Button>
          )
        }
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <Checkbox
            checked={params.includeRevoked}
            onCheckedChange={(checked) => url.set({ includeRevoked: checked, page: 1 })}
            label="Include revoked and expired"
          />
        </div>

        {profiles.isError ? (
          <ErrorState error={profiles.error} onRetry={() => void profiles.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(profile) => profile.id}
              loading={profiles.isLoading}
              rowClassName={(profile) =>
                profile.revokedAtUtc || isExpired(profile, now) ? 'opacity-60' : undefined
              }
              empty={
                <EmptyState
                  icon={<KeyRound />}
                  title="No enrollment tokens"
                  description="Create one before installing the agent on a machine. Each token can be limited to a number of machines and an expiry."
                  action={
                    canManage && (
                      <Button
                        variant="primary"
                        iconLeft={<Plus />}
                        onClick={() => setCreating(true)}
                      >
                        New token
                      </Button>
                    )
                  }
                />
              }
            />

            {(profiles.data?.totalCount ?? 0) > 0 && (
              <Pagination
                page={profiles.data?.page ?? 1}
                pageSize={profiles.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={profiles.data?.totalCount ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="tokens"
              />
            )}
          </>
        )}
      </Card>

      {creating && <CreateEnrollmentProfileDialog onClose={() => setCreating(false)} />}

      {revoking && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setRevoking(null)}
          title="Revoke this token?"
          description={
            <>
              <strong>{revoking.name}</strong> stops working immediately and no further
              machine can enrol with it. The{' '}
              {formatNumber(revoking.enrolledCount)}{' '}
              {revoking.enrolledCount === 1 ? 'machine' : 'machines'} already enrolled
              carry on reporting — revoke those separately if that is what you want.
            </>
          }
          confirmLabel="Revoke token"
          destructive
          loading={revoke.isPending}
          onConfirm={() =>
            revoke.mutate(
              { profileId: revoking.id, version: revoking.version },
              {
                onSuccess: () => {
                  toast.success('Token revoked', revoking.name);
                  setRevoking(null);
                },
                onError: (error) => {
                  toast.fromError(error, 'Could not revoke');
                  setRevoking(null);
                },
              },
            )
          }
        />
      )}
    </div>
  );
}

function ProfileState({
  profile,
  now,
}: {
  profile: AgentEnrollmentProfile;
  now: number;
}) {
  if (profile.revokedAtUtc) {
    return (
      <Tooltip content={`Revoked ${formatDateTime(profile.revokedAtUtc)}`}>
        <span className="inline-block">
          <Badge tone="danger" dot>
            Revoked
          </Badge>
        </span>
      </Tooltip>
    );
  }
  if (isExpired(profile, now)) {
    return (
      <Badge tone="neutral" dot>
        Expired
      </Badge>
    );
  }
  if (profile.enrolledCount >= profile.maxEnrollments) {
    return (
      <Tooltip content="Every enrollment this token allows has been used.">
        <span className="inline-block">
          <Badge tone="neutral" dot>
            Used up
          </Badge>
        </span>
      </Tooltip>
    );
  }
  return (
    <Badge tone="success" dot>
      Usable
    </Badge>
  );
}

function isExpired(profile: AgentEnrollmentProfile, now: number): boolean {
  return new Date(profile.expiresAtUtc).getTime() <= now;
}
