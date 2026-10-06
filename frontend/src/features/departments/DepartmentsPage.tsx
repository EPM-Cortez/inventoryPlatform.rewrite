import { Building2, Pencil, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';

import { useDepartmentHeldCount, useDepartments, useExportDepartments } from '@/api/hooks';
import type { Department } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatNumber } from '@/lib/format';
import { useUrlState } from '@/lib/useUrlState';
import { Avatar } from '@/ui/Avatar';
import { Button, IconButton } from '@/ui/Button';
import { Card } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { EmptyState, ErrorState, NoResults } from '@/ui/EmptyState';
import { FilterButton } from '@/ui/FilterButton';
import { ListToolbar } from '@/ui/ListToolbar';
import { PageHeader } from '@/ui/PageHeader';
import { Pagination } from '@/ui/Pagination';
import { SearchInput } from '@/ui/SearchInput';
import { Skeleton } from '@/ui/Skeleton';
import { StatusDot } from '@/ui/StatusDot';

import { ImportExportActions } from '../imports/ImportExportActions';
import { DepartmentFormDialog } from './DepartmentFormDialog';

/** Smallest server page size; each row also fetches its own held count. */
const DEFAULT_PAGE_SIZE = 25;

/** Archived departments are hidden unless asked for. */
const ARCHIVED_OPTIONS = [{ value: 'true', label: 'Included' }];

export default function DepartmentsPage() {
  const url = useUrlState();
  const navigate = useNavigate();
  const { can } = useSession();
  // `'new'` opens an empty form; a `Department` opens it populated for editing.
  const [form, setForm] = useState<Department | 'new' | null>(null);
  const exportDepartments = useExportDepartments();

  const params = useMemo(
    () => ({
      q: url.get('q'),
      includeInactive: url.getBoolean('includeInactive') ?? false,
      page: url.getNumber('page', 1),
      pageSize: url.getNumber('pageSize', DEFAULT_PAGE_SIZE),
      sort: 'name',
    }),
    [url],
  );

  const departments = useDepartments(params);
  // Department administration is manager-and-above, unlike people.
  const canManage = can(Permission.PeopleManage);
  const canSeeHoldings = can(Permission.AssetsRead);
  const total = departments.data?.totalCount;

  const columns: Column<Department>[] = [
    {
      id: 'name',
      header: 'Department',
      cell: (department) => (
        <div className="flex min-w-0 items-center gap-3">
          <Avatar name={department.name}>
            <Building2 />
          </Avatar>
          <div className="min-w-0">
            <Link
              to={`/departments/${department.id}`}
              onClick={(event) => event.stopPropagation()}
              className="block truncate rounded-xs font-medium text-text hover:text-accent hover:underline"
            >
              {department.name}
            </Link>
            <p className="truncate font-mono text-xs text-text-muted">{department.code}</p>
          </div>
        </div>
      ),
    },
    ...(canSeeHoldings
      ? ([
          {
            id: 'holding',
            header: 'Holding',
            width: 'w-40',
            cell: (department) => <HeldCount departmentId={department.id} />,
          },
        ] satisfies Column<Department>[])
      : []),
    ...(params.includeInactive
      ? ([
          {
            id: 'status',
            header: 'Status',
            width: 'w-32',
            cell: (department) =>
              department.isActive ? (
                <StatusDot tone="success" muted>
                  Active
                </StatusDot>
              ) : (
                <StatusDot tone="neutral" muted>
                  Archived
                </StatusDot>
              ),
          },
        ] satisfies Column<Department>[])
      : []),
    {
      id: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'right',
      width: 'w-14',
      cell: (department) =>
        canManage ? (
          <IconButton
            label={`Edit ${department.name}`}
            size="sm"
            onClick={(event) => {
              event.stopPropagation();
              setForm(department);
            }}
          >
            <Pencil />
          </IconButton>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Departments"
        description="Teams that hold equipment collectively — shared kit that is not issued to one person."
        actions={
          <>
            <ImportExportActions
              importPath="/departments/import"
              exportPermission={Permission.ExportsDepartments}
              exportMutation={exportDepartments}
            />
            {canManage && (
              <Button variant="primary" iconLeft={<Plus />} onClick={() => setForm('new')}>
                Add department
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
              placeholder="Search name or code…"
              aria-label="Search departments"
            />
          }
          count={
            total === undefined
              ? undefined
              : `${formatNumber(total)} ${total === 1 ? 'department' : 'departments'}`
          }
          onClearFilters={url.hasFilters() ? url.clear : undefined}
          filters={
            <FilterButton
              label="Archived"
              emptyLabel="Hidden"
              value={params.includeInactive ? 'true' : null}
              onChange={(value) => url.set({ includeInactive: value === 'true' })}
              options={ARCHIVED_OPTIONS}
            />
          }
        />

        {departments.isError ? (
          <ErrorState error={departments.error} onRetry={() => void departments.refetch()} />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={departments.data?.items ?? []}
              rowKey={(department) => department.id}
              loading={departments.isLoading}
              onRowClick={(department) => void navigate(`/departments/${department.id}`)}
              rowClassName={(department) => (department.isActive ? undefined : 'opacity-60')}
              empty={
                url.hasFilters() ? (
                  <NoResults onClear={url.clear} />
                ) : (
                  <EmptyState
                    icon={<Building2 />}
                    title="No departments yet"
                    description="Add a department so shared equipment can be signed out to a team rather than a person."
                    action={
                      canManage && (
                        <Button
                          variant="primary"
                          iconLeft={<Plus />}
                          onClick={() => setForm('new')}
                        >
                          Add department
                        </Button>
                      )
                    }
                  />
                )
              }
            />

            {(total ?? 0) > 0 && (
              <Pagination
                page={departments.data?.page ?? 1}
                pageSize={departments.data?.pageSize ?? DEFAULT_PAGE_SIZE}
                totalCount={total ?? 0}
                onPageChange={(page) => url.set({ page })}
                onPageSizeChange={(pageSize) => url.set({ pageSize, page: 1 })}
                itemLabel="departments"
              />
            )}
          </>
        )}
      </Card>

      {form && (
        <DepartmentFormDialog
          onClose={() => setForm(null)}
          department={form === 'new' ? undefined : form}
          onCreated={(department) => void navigate(`/departments/${department.id}`)}
        />
      )}
    </div>
  );
}

/** Each row asks for its own count; a zero fades, as zeros do across the app. */
function HeldCount({ departmentId }: { departmentId: string }) {
  const count = useDepartmentHeldCount(departmentId);

  if (count.isLoading) return <Skeleton className="h-4 w-16" />;
  if (count.isError) return <span className="text-text-subtle">—</span>;

  const value = count.data ?? 0;
  return value === 0 ? (
    <span className="text-text-subtle">Nothing</span>
  ) : (
    <span className="text-text">
      <span className="font-medium tabular-nums">{formatNumber(value)}</span>{' '}
      <span className="text-text-muted">{value === 1 ? 'item' : 'items'}</span>
    </span>
  );
}
