import {
  AlertTriangle,
  ArrowRight,
  Boxes,
  CircleCheck,
  ShieldAlert,
  UserRoundCheck,
  Wrench,
  type LucideIcon,
} from 'lucide-react';
import { Link } from 'react-router';

import { useAssets, useDashboardSummary, useExpiringWarranties } from '@/api/hooks';
import type { AssetStatus } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { formatDate, formatDaysRemaining, formatNumber, formatRelative } from '@/lib/format';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { EmptyState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { Skeleton } from '@/ui/Skeleton';
import { AssetStatusPill, WarrantyStatePill } from '@/ui/StatusPill';

import { HolderLink } from '../assets/AssetCells';
import { ExceptionsCard } from './ExceptionsCard';


export default function DashboardPage() {
  const { user, can } = useSession();
  const canReadAssets = can(Permission.AssetsRead);
  const canReadWarranties = can(Permission.WarrantiesRead);

  // One request for every figure on this page. These are counted across the
  // whole register by the server, so they do not drift with the assets page
  // filters the way a `pageSize: 1` search once did.
  const summary = useDashboardSummary(canReadAssets);
  const statusCount = (status: AssetStatus) =>
    summary.data?.statusCounts.find((entry) => entry.value === status)?.count ?? 0;

  const recent = useAssets(
    { page: 1, pageSize: 8, sort: '-updatedAtUtc' },
    canReadAssets,
  );
  const missing = useAssets({ page: 1, pageSize: 5, status: 'lost' }, canReadAssets);
  const expiring = useExpiringWarranties(60, 1, 6, canReadWarranties);

  const firstName = user?.displayName.split(/[\s.]+/)[0] ?? '';

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={firstName ? `Hello, ${firstName}` : 'Dashboard'}
        description="Where the register stands today, and what needs a look."
      />

      {canReadAssets && (
        <section
          aria-label="Register summary"
          className="stagger-children grid gap-3 sm:grid-cols-2 xl:grid-cols-4"
        >
          <StatTile
            label="Assets on the register"
            value={summary.data?.totalAssets}
            loading={summary.isLoading}
            icon={Boxes}
            to="/assets"
          />
          <StatTile
            label="Currently assigned"
            value={statusCount('assigned')}
            loading={summary.isLoading}
            icon={UserRoundCheck}
            to="/assets?assigned=true"
          />
          <StatTile
            label="Available in stock"
            value={statusCount('inStock')}
            loading={summary.isLoading}
            icon={CircleCheck}
            tone="success"
            to="/assets?status=inStock"
          />
          <StatTile
            label="In repair"
            value={statusCount('repair')}
            loading={summary.isLoading}
            icon={Wrench}
            // Only flag a state when there is something in it.
            tone={statusCount('repair') > 0 ? 'warning' : undefined}
            to="/assets?status=repair"
          />
        </section>
      )}

      {canReadAssets && (
        <ExceptionsCard
          exceptions={summary.data?.exceptionCounts ?? []}
          loading={summary.isLoading}
        />
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        {canReadWarranties && (
          <Card>
            <CardHeader
              title="Warranty coverage ending"
              description="Within the next 60 days."
              actions={
                <Button size="sm" variant="link" asChild>
                  <Link to="/warranties">
                    View all <ArrowRight className="ml-1 size-3.5" />
                  </Link>
                </Button>
              }
            />
            <CardBody className="p-0">
              {expiring.isLoading ? (
                <RowSkeletons />
              ) : (expiring.data?.items.length ?? 0) === 0 ? (
                <EmptyState
                  icon={<CircleCheck />}
                  title="Nothing expiring soon"
                  description="No recorded coverage ends in the next 60 days."
                  className="py-10"
                />
              ) : (
                <ul className="divide-y divide-border">
                  {expiring.data?.items.map((warranty) => (
                    <li key={warranty.id}>
                      <Link
                        to={`/assets/${warranty.assetId}?tab=warranties`}
                        className="flex items-center justify-between gap-3 px-4 py-2.5 transition-colors hover:bg-surface-hover"
                      >
                        <div className="min-w-0">
                          <p className="truncate text-base font-medium text-text">
                            {warranty.provider}
                          </p>
                          <p className="truncate text-xs text-text-subtle">
                            {warranty.coverageType} · ends {formatDate(warranty.endDate)}
                            {warranty.daysRemaining !== null &&
                              ` · ${formatDaysRemaining(warranty.daysRemaining)}`}
                          </p>
                        </div>
                        <WarrantyStatePill state={warranty.state} />
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        )}

        {canReadAssets && (
          <Card>
            <CardHeader
              title="Recently updated"
              description="The last changes to the register."
            />
            <CardBody className="p-0">
              {recent.isLoading ? (
                <RowSkeletons />
              ) : (recent.data?.items.length ?? 0) === 0 ? (
                <EmptyState
                  icon={<Boxes />}
                  title="Nothing on the register yet"
                  description="Assets you add will show up here."
                  className="py-10"
                />
              ) : (
                <ul className="divide-y divide-border">
                  {recent.data?.items.map((asset) => (
                    <li key={asset.id}>
                      <Link
                        to={`/assets/${asset.id}`}
                        className="flex items-center justify-between gap-3 px-4 py-2.5 transition-colors hover:bg-surface-hover"
                      >
                        <div className="min-w-0">
                          <p className="truncate font-mono text-sm font-medium tracking-tight text-text">
                            {asset.assetTag}
                          </p>
                          <p className="truncate text-xs text-text-subtle">
                            {[asset.manufacturer, asset.model].filter(Boolean).join(' ') ||
                              asset.category.name}{' '}
                            · {formatRelative(asset.updatedAtUtc)}
                          </p>
                        </div>
                        <div className="flex shrink-0 items-center gap-2">
                          <span className="hidden text-sm text-text-muted sm:block">
                            <HolderLink assignment={asset.currentAssignment} />
                          </span>
                          <AssetStatusPill status={asset.status} />
                        </div>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        )}
      </div>

      {canReadAssets && (missing.data?.totalCount ?? 0) > 0 && (
        <Card>
          <CardHeader
            title={
              <span className="flex items-center gap-2">
                <ShieldAlert className="size-4 text-danger" aria-hidden />
                Reported lost
              </span>
            }
            description="Items marked lost that have not been resolved."
            actions={
              <Button size="sm" variant="link" asChild>
                <Link to="/assets?status=lost">
                  View all <ArrowRight className="ml-1 size-3.5" />
                </Link>
              </Button>
            }
          />
          <CardBody className="p-0">
            <ul className="divide-y divide-border">
              {missing.data?.items.map((asset) => (
                <li key={asset.id}>
                  <Link
                    to={`/assets/${asset.id}`}
                    className="flex items-center justify-between gap-3 px-4 py-2.5 transition-colors hover:bg-surface-hover"
                  >
                    <div className="min-w-0">
                      <p className="truncate font-mono text-sm font-medium tracking-tight text-text">
                        {asset.assetTag}
                      </p>
                      <p className="truncate text-xs text-text-subtle">
                        {[asset.manufacturer, asset.model].filter(Boolean).join(' ') ||
                          asset.category.name}
                      </p>
                    </div>
                    <AssetStatusPill status={asset.status} />
                  </Link>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      )}

      {!canReadAssets && (
        <EmptyState
          icon={<AlertTriangle />}
          title="Nothing to show yet"
          description="Your role does not include access to the asset register. Ask an administrator if you need it."
        />
      )}
    </div>
  );
}

/**
 * A number, its label, and at most one state cue.
 *
 * The numeral itself always wears the normal text colour — a tile that turns red
 * makes the figure harder to read and says nothing a labelled icon does not. Tone
 * colours the small icon only, and the icon is always accompanied by its label.
 */
function StatTile({
  label,
  value,
  loading,
  icon: Icon,
  tone,
  to,
}: {
  label: string;
  value: number | undefined;
  loading: boolean;
  icon: LucideIcon;
  tone?: 'success' | 'warning';
  to: string;
}) {
  const toneClass =
    tone === 'success'
      ? 'bg-success-soft text-success'
      : tone === 'warning'
        ? 'bg-warning-soft text-warning'
        : 'bg-surface-sunken text-text-subtle';

  return (
    <Link
      to={to}
      className="group lift rounded-lg border border-border bg-surface p-4 shadow-sm hover:border-border-strong hover:bg-surface-hover"
    >
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm font-medium text-text-muted">{label}</p>
        <span
          className={cn(
            'flex size-7 shrink-0 items-center justify-center rounded-md',
            toneClass,
          )}
          aria-hidden="true"
        >
          <Icon className="size-4 transition-transform duration-(--duration-base) ease-(--ease-spring) group-hover:scale-110" />
        </span>
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-8 w-20" />
      ) : (
        <p className="mt-1 text-3xl font-semibold tabular-nums tracking-[-0.02em] text-text">
          {value === undefined ? '—' : formatNumber(value)}
        </p>
      )}
    </Link>
  );
}

function RowSkeletons() {
  return (
    <div className="flex flex-col gap-3 p-4">
      {Array.from({ length: 5 }, (_, index) => (
        <div key={index} className="flex items-center justify-between gap-3">
          <div className="flex-1 space-y-1.5">
            <Skeleton className="h-4 w-1/3" />
            <Skeleton className="h-3 w-1/2" />
          </div>
          <Skeleton className="h-5 w-20 rounded-full" />
        </div>
      ))}
    </div>
  );
}
