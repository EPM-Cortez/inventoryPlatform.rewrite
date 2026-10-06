import { Mail, MoreHorizontal, Pencil, Plus, UserMinus, UsersRound } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { useExportPeople, usePeople } from '@/api/hooks';
import { PERSON_TYPES, type Person } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatNumber, PERSON_TYPE_LABELS } from '@/lib/format';
import { asEnum, useUrlState } from '@/lib/useUrlState';
import { Avatar } from '@/ui/Avatar';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { FilterButton } from '@/ui/FilterButton';
import { ListToolbar } from '@/ui/ListToolbar';
import { Menu, MenuContent, MenuItem, MenuTrigger } from '@/ui/Menu';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { SearchInput } from '@/ui/SearchInput';
import { StatusDot } from '@/ui/StatusDot';

import { ImportExportActions } from '../imports/ImportExportActions';
import { CreateLeaverCaseDialog } from '../leavers/CreateLeaverCaseDialog';
import { PersonFormDialog } from './PersonFormDialog';

const DEFAULT_PAGE_SIZE = 50;

const TYPE_OPTIONS = PERSON_TYPES.map((type) => ({
  value: type,
  label: PERSON_TYPE_LABELS[type],
}));

/** Archived people are hidden unless asked for. */
const ARCHIVED_OPTIONS = [{ value: 'true', label: 'Included' }];

export default function PeoplePage() {
  const url = useUrlState();
  const navigate = useNavigate();
  const { can } = useSession();
  // `'new'` opens an empty form; a `Person` opens it populated for editing.
  const [form, setForm] = useState<Person | 'new' | null>(null);
  const [leaving, setLeaving] = useState<Person | null>(null);
  const exportPeople = useExportPeople();

  const params = useMemo(
    () => ({
      q: url.get('q'),
      type: asEnum(url.get('type'), PERSON_TYPES),
      includeInactive: url.getBoolean('includeInactive') ?? false,
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
      sort: 'displayName',
    }),
    [url],
  );

  const people = usePeople(params);
  const canEdit = can(Permission.PeopleOperate);
  const canOpenLeaver = can(Permission.LeaversManage);
  const total = people.data?.totalCount;

  const columns: Column<Person>[] = [
    {
      id: 'name',
      header: 'Person',
      cell: (person) => (
        <div className="flex min-w-0 items-center gap-3">
          <Avatar name={person.displayName} />
          <div className="min-w-0">
            <Link
              to={`/people/${person.id}`}
              onClick={(event) => event.stopPropagation()}
              className="block truncate rounded-xs font-medium text-text hover:text-accent hover:underline"
            >
              {person.displayName}
            </Link>
            <p className="truncate text-xs text-text-muted">
              {person.email ?? <span className="text-text-subtle">No email recorded</span>}
            </p>
          </div>
        </div>
      ),
    },
    {
      id: 'type',
      header: 'Type',
      width: 'w-32',
      hideBelow: 'sm',
      cell: (person) => (
        <span className="text-text-muted">{PERSON_TYPE_LABELS[person.type]}</span>
      ),
    },
    {
      id: 'externalId',
      header: 'External ID',
      hideBelow: 'md',
      width: 'w-44',
      cell: (person) =>
        person.externalId ? (
          <span className="block truncate font-mono text-xs text-text-muted">
            {person.externalId}
          </span>
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    // Every row would read "Active" otherwise; the column earns its place only
    // when archived people are in the list too.
    ...(params.includeInactive
      ? ([
          {
            id: 'status',
            header: 'Status',
            width: 'w-32',
            cell: (person) =>
              person.isActive ? (
                <StatusDot tone="success" muted>
                  Active
                </StatusDot>
              ) : (
                <StatusDot tone="neutral" muted>
                  Archived
                </StatusDot>
              ),
          },
        ] satisfies Column<Person>[])
      : []),
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-24',
      cell: (person) => (
        // The row itself navigates, so clicks here must not bubble up to it.
        <div className="flex justify-end gap-1" onClick={(event) => event.stopPropagation()}>
          {canEdit && (
            <IconButton
              label={`Edit ${person.displayName}`}
              size="sm"
              onClick={() => setForm(person)}
            >
              <Pencil />
            </IconButton>
          )}
          {(person.email || (canOpenLeaver && person.isActive)) && (
            <Menu>
              <MenuTrigger asChild>
                <IconButton label={`More for ${person.displayName}`} size="sm">
                  <MoreHorizontal />
                </IconButton>
              </MenuTrigger>
              <MenuContent align="end">
                {person.email && (
                  <MenuItem
                    onSelect={() => {
                      window.location.href = `mailto:${person.email}`;
                    }}
                  >
                    <Mail />
                    Email {person.displayName.split(' ')[0]}
                  </MenuItem>
                )}
                {canOpenLeaver && person.isActive && (
                  <MenuItem onSelect={() => setLeaving(person)}>
                    <UserMinus />
                    Mark as leaving
                  </MenuItem>
                )}
              </MenuContent>
            </Menu>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="People"
        description="Staff, pupils and contractors who can hold equipment. These are custody records, not sign-in accounts."
        actions={
          <>
            <ImportExportActions
              importPath="/people/import"
              exportPermission={Permission.ExportsPeople}
              exportMutation={exportPeople}
            />
            {canEdit && (
              <Button variant="primary" iconLeft={<Plus />} onClick={() => setForm('new')}>
                Add person
              </Button>
            )}
          </>
        }
      />

      <Card className="overflow-hidden">
        <ListToolbar
          search={
            <SearchInput
              value={params.q ?? ''}
              onChange={(value) => url.set({ q: value })}
              placeholder="Search name, external ID or email…"
              aria-label="Search people"
            />
          }
          count={
            total === undefined
              ? undefined
              : `${formatNumber(total)} ${total === 1 ? 'person' : 'people'}`
          }
          onClearFilters={url.hasFilters() ? url.clear : undefined}
          filters={
            <>
              <FilterButton
                label="Type"
                value={params.type ?? null}
                onChange={(value) => url.set({ type: value ?? undefined })}
                options={TYPE_OPTIONS}
              />
              <FilterButton
                label="Archived"
                emptyLabel="Hidden"
                value={params.includeInactive ? 'true' : null}
                onChange={(value) => url.set({ includeInactive: value === 'true' })}
                options={ARCHIVED_OPTIONS}
              />
            </>
          }
        />

        {people.isError ? (
          <ErrorState error={people.error} onRetry={() => void people.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={people.data?.items ?? []}
              rowKey={(person) => person.id}
              loading={people.isLoading}
              onRowClick={(person) => void navigate(`/people/${person.id}`)}
              rowClassName={(person) => (person.isActive ? undefined : 'opacity-60')}
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<UsersRound />}
                    title="No people yet"
                    description="Add the staff, pupils and contractors who will hold equipment."
                    action={
                      canEdit && (
                        <Button
                          variant="primary"
                          iconLeft={<Plus />}
                          onClick={() => setForm('new')}
                        >
                          Add person
                        </Button>
                      )
                    }
                  />
                )
              }
            />

            {(total ?? 0) > 0 && (
              <Pagination
                page={people.data?.page ?? 1}
                pageSize={people.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={total ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="people"
              />
            )}
          </>
        )}
      </Card>

      {form && (
        <PersonFormDialog
          onClose={() => setForm(null)}
          person={form === 'new' ? undefined : form}
          onCreated={(person) => void navigate(`/people/${person.id}`)}
        />
      )}
      {leaving && (
        <CreateLeaverCaseDialog person={leaving} onClose={() => setLeaving(null)} />
      )}
    </div>
  );
}
