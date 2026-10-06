import {
  ArchiveRestore,
  KeyRound,
  MoreHorizontal,
  Pencil,
  Plus,
  ShieldOff,
  SmartphoneNfc,
  Trash2,
  UserCog,
} from 'lucide-react';
import { useMemo, useState } from 'react';

import {
  useArchiveUser,
  useDisableUser,
  useResetUserMfa,
  useRestoreUser,
  useUsers,
} from '@/api/hooks';
import type { User } from '@/api/types';
import { compareRoles, describeRole, Role } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime, formatNumber } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Alert } from '@/ui/Alert';
import { Avatar } from '@/ui/Avatar';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { FilterButton } from '@/ui/FilterButton';
import { ListToolbar } from '@/ui/ListToolbar';
import { Menu, MenuContent, MenuItem, MenuSeparator, MenuTrigger } from '@/ui/Menu';
import { PageHeader } from '@/ui/PageHeader';
import { SearchInput } from '@/ui/SearchInput';
import { StatusDot } from '@/ui/StatusDot';
import { Tooltip } from '@/ui/Tooltip';
import { useToast } from '@/ui/Toast';

import { describeAccountError } from './accountErrors';
import { ResetPasswordDialog } from './ResetPasswordDialog';
import { UserFormDialog } from './UserFormDialog';

type PendingAction =
  | { kind: 'disable'; user: User }
  | { kind: 'archive'; user: User }
  | { kind: 'restore'; user: User }
  | { kind: 'resetMfa'; user: User };

export default function AccountsPage() {
  const url = useUrlState();
  const toast = useToast();
  const { user: currentUser } = useSession();

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<User | null>(null);
  const [resetting, setResetting] = useState<User | null>(null);
  const [pending, setPending] = useState<PendingAction | null>(null);

  const users = useUsers();
  const disable = useDisableUser();
  const archive = useArchiveUser();
  const restore = useRestoreUser();
  const resetMfa = useResetUserMfa();

  const query = url.get('q')?.trim().toLowerCase() ?? '';
  const showArchived = url.getBoolean('archived') ?? false;

  // The API returns every account in one unfiltered list, so searching and the
  // archived toggle are both applied here.
  const rows = useMemo(() => {
    const all = users.data?.items ?? [];
    return all.filter((user) => {
      if (!showArchived && user.archivedAtUtc) return false;
      if (!query) return true;
      return (
        user.displayName.toLowerCase().includes(query) ||
        user.email.toLowerCase().includes(query) ||
        user.roles.some((role) => describeRole(role).toLowerCase().includes(query))
      );
    });
  }, [query, showArchived, users.data]);

  const archivedCount = (users.data?.items ?? []).filter((user) => user.archivedAtUtc).length;

  // Mirrors the server's `last_administrator` guard so the UI can warn before the
  // request rather than only explaining the 409 afterwards.
  const enabledAdministrators = (users.data?.items ?? []).filter(
    (user) =>
      user.roles.includes(Role.Administrator) && user.isEnabled && !user.archivedAtUtc,
  );
  const isLastAdministrator = (user: User) =>
    enabledAdministrators.length === 1 && enabledAdministrators[0]?.id === user.id;

  function runPending() {
    if (!pending) return;
    const { kind, user } = pending;
    const onError = (error: unknown) => {
      const problem = describeAccountError(error);
      toast.error(problem.title, problem.detail);
      setPending(null);
    };
    const onSuccess = (message: string) => () => {
      toast.success(message, user.displayName);
      setPending(null);
    };

    if (kind === 'disable') {
      disable.mutate(
        { userId: user.id, version: user.version },
        { onSuccess: onSuccess('Account disabled'), onError },
      );
    } else if (kind === 'archive') {
      archive.mutate(
        { userId: user.id, version: user.version },
        { onSuccess: onSuccess('Account archived'), onError },
      );
    } else if (kind === 'resetMfa') {
      resetMfa.mutate(
        { userId: user.id, version: user.version },
        { onSuccess: onSuccess('Two-step verification reset'), onError },
      );
    } else {
      restore.mutate(
        { userId: user.id, version: user.version },
        { onSuccess: onSuccess('Account restored'), onError },
      );
    }
  }

  const columns: Column<User>[] = [
    {
      id: 'account',
      header: 'Account',
      cell: (user) => (
        <div className="flex min-w-0 items-center gap-3">
          <Avatar name={user.displayName} />
          <div className="min-w-0">
            <p className="truncate font-medium text-text">
              {user.displayName}
              {user.id === currentUser?.id && (
                <span className="font-normal text-text-subtle"> · you</span>
              )}
            </p>
            <p className="truncate text-xs text-text-muted">{user.email}</p>
          </div>
        </div>
      ),
    },
    {
      id: 'roles',
      header: 'Roles',
      cell: (user) =>
        user.roles.length === 0 ? (
          <Tooltip content="This account can sign in but cannot see or do anything.">
            <span className="inline-block">
              <StatusDot tone="warning">No role</StatusDot>
            </span>
          </Tooltip>
        ) : (
          // Plain words rather than a pill per role: the administrator role is
          // the one that matters at a glance, so it alone is in full-strength text.
          <p className="text-sm text-text-muted">
            {[...user.roles].sort(compareRoles).map((role, index) => (
              <span key={role}>
                {index > 0 && ', '}
                <span className={role === Role.Administrator ? 'font-medium text-text' : undefined}>
                  {describeRole(role)}
                </span>
              </span>
            ))}
          </p>
        ),
    },
    {
      id: 'status',
      header: 'Status',
      width: 'w-52',
      cell: (user) => {
        const notes = [
          user.mustChangePassword && !user.archivedAtUtc ? 'Must set password' : null,
          user.mfaEnabled ? 'Two-step on' : null,
        ].filter(Boolean);
        return (
          <div className="min-w-0">
            {user.archivedAtUtc ? (
              <Tooltip content={`Archived ${formatDateTime(user.archivedAtUtc)}`}>
                <span className="inline-block">
                  <StatusDot tone="neutral" muted>
                    Archived
                  </StatusDot>
                </span>
              </Tooltip>
            ) : user.isEnabled ? (
              <StatusDot tone="success">Active</StatusDot>
            ) : (
              <StatusDot tone="warning">Disabled</StatusDot>
            )}
            {notes.length > 0 && (
              <p className="mt-0.5 truncate pl-3.5 text-xs text-text-subtle">
                {notes.join(' · ')}
              </p>
            )}
          </div>
        );
      },
    },
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-14',
      cell: (user) => {
        const isSelf = user.id === currentUser?.id;
        const isArchived = Boolean(user.archivedAtUtc);

        return (
          <Menu>
            <MenuTrigger asChild>
              <IconButton label={`Actions for ${user.displayName}`} size="sm">
                <MoreHorizontal />
              </IconButton>
            </MenuTrigger>
            <MenuContent>
              {isArchived ? (
                // Every other write returns `user_archived` until it is restored.
                <MenuItem onSelect={() => setPending({ kind: 'restore', user })}>
                  <ArchiveRestore />
                  Restore account
                </MenuItem>
              ) : (
                <>
                  <MenuItem onSelect={() => setEditing(user)}>
                    <Pencil />
                    Edit account
                  </MenuItem>
                  <MenuItem onSelect={() => setResetting(user)}>
                    <KeyRound />
                    Reset password
                  </MenuItem>
                  <MenuItem
                    disabled={!user.mfaEnabled}
                    onSelect={() => setPending({ kind: 'resetMfa', user })}
                  >
                    <SmartphoneNfc />
                    {user.mfaEnabled
                      ? 'Reset two-step verification'
                      : 'No two-step verification'}
                  </MenuItem>
                  <MenuSeparator />
                  <MenuItem
                    disabled={isSelf || !user.isEnabled}
                    onSelect={() => setPending({ kind: 'disable', user })}
                  >
                    <ShieldOff />
                    {user.isEnabled ? 'Disable sign-in' : 'Already disabled'}
                  </MenuItem>
                  <MenuItem
                    destructive
                    disabled={isSelf}
                    onSelect={() => setPending({ kind: 'archive', user })}
                  >
                    <Trash2 />
                    Archive account
                  </MenuItem>
                </>
              )}
            </MenuContent>
          </Menu>
        );
      },
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Accounts"
        description="Sign-in accounts and the roles that decide what each one can do."
        actions={
          <Button variant="primary" iconLeft={<Plus />} onClick={() => setCreating(true)}>
            New account
          </Button>
        }
      />

      <Alert tone="info" title="Accounts are not the same as people">
        An account is a sign-in for someone who <em>uses</em> the system. A person under{' '}
        <strong>People</strong> is a custody record for someone who <em>holds</em>{' '}
        equipment. Creating one does not create the other.
      </Alert>

      <Card className="overflow-hidden">
        <ListToolbar
          search={
            <SearchInput
              value={url.get('q') ?? ''}
              onChange={(value) => url.set({ q: value })}
              placeholder="Search name, email or role…"
              aria-label="Search accounts"
            />
          }
          count={
            users.data
              ? `${formatNumber(rows.length)} ${rows.length === 1 ? 'account' : 'accounts'}`
              : undefined
          }
          onClearFilters={query || showArchived ? url.clear : undefined}
          filters={
            <FilterButton
              label="Archived"
              emptyLabel="Hidden"
              value={showArchived ? 'true' : null}
              onChange={(value) => url.set({ archived: value === 'true' })}
              options={[
                {
                  value: 'true',
                  label: archivedCount > 0 ? `Included (${archivedCount})` : 'Included',
                },
              ]}
            />
          }
        />

        {users.isError ? (
          <ErrorState error={users.error} onRetry={() => void users.refetch()} />
        ) : (
          <DataTable
            columns={columns}
            rows={rows}
            rowKey={(user) => user.id}
            loading={users.isLoading}
            skeletonRows={5}
            rowClassName={(user) =>
              user.archivedAtUtc || !user.isEnabled ? 'opacity-60' : undefined
            }
            empty={
              query || showArchived ? (
                <NoResults onClear={url.clear} />
              ) : (
                <EmptyState
                  icon={<UserCog />}
                  title="No accounts"
                  description="Create the sign-in accounts that administrators and technicians will use."
                  action={
                    <Button
                      variant="primary"
                      iconLeft={<Plus />}
                      onClick={() => setCreating(true)}
                    >
                      New account
                    </Button>
                  }
                />
              )
            }
          />
        )}
      </Card>

      {creating && <UserFormDialog onClose={() => setCreating(false)} />}
      {editing && <UserFormDialog onClose={() => setEditing(null)} user={editing} />}
      {resetting && (
        <ResetPasswordDialog onClose={() => setResetting(null)} user={resetting} />
      )}

      {/* Mounted only while there is something to confirm. A permanently mounted
          dialog that flips to open from a menu item loses the race with the
          menu's own closing focus-restore and is dismissed on the same tick. */}
      {pending && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setPending(null)}
          title={PENDING_COPY[pending.kind].title}
          description={describePendingAction(pending, isLastAdministrator)}
          confirmLabel={PENDING_COPY[pending.kind].confirm}
          destructive={pending.kind !== 'restore'}
          loading={
            disable.isPending ||
            archive.isPending ||
            restore.isPending ||
            resetMfa.isPending
          }
          onConfirm={runPending}
        />
      )}
    </div>
  );
}

const PENDING_COPY: Record<PendingAction['kind'], { title: string; confirm: string }> = {
  disable: { title: 'Disable sign-in?', confirm: 'Disable sign-in' },
  archive: { title: 'Archive this account?', confirm: 'Archive account' },
  restore: { title: 'Restore this account?', confirm: 'Restore account' },
  resetMfa: {
    title: 'Reset two-step verification?',
    confirm: 'Reset two-step verification',
  },
};

function describePendingAction(
  pending: PendingAction,
  isLastAdministrator: (user: User) => boolean,
) {
  const { kind, user } = pending;
  const lastAdminWarning = isLastAdministrator(user) ? (
    <p className="mt-2 rounded-md border border-warning-border bg-warning-soft p-2.5 text-warning">
      This is the only enabled administrator. The server will refuse this — give
      another account the Administrator role first.
    </p>
  ) : null;

  if (kind === 'archive') {
    return (
      <>
        <strong>{user.displayName}</strong> will no longer be able to sign in, and
        existing sessions end immediately. Nothing is deleted: the account stays
        recorded as the author of its past asset history and audit entries, and it can
        be restored later.
        {lastAdminWarning}
      </>
    );
  }

  if (kind === 'restore') {
    return (
      <>
        <strong>{user.displayName}</strong> will be able to sign in again with the roles
        it had before. Reset its password afterwards if you are not sure the old one is
        still safe.
      </>
    );
  }

  if (kind === 'resetMfa') {
    return (
      <>
        <strong>{user.displayName}</strong> will sign in with just a password until they
        set up an authenticator again, and their existing recovery codes stop working.
        Do this only once you are sure who you are talking to — a caller claiming to be
        locked out is the usual way this gets abused.
      </>
    );
  }

  return (
    <>
      <strong>{user.displayName}</strong> will be signed out and blocked from signing in,
      but the account stays on this list and keeps its roles. Use this for someone on
      leave; archive instead for someone who has left.
      {lastAdminWarning}
    </>
  );
}
