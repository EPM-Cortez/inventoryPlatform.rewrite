import { Pencil } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';

import type { Asset } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { formatDate, formatDateTime, formatMoney } from '@/lib/format';
import { Button } from '@/ui/Button';
import { StatusDot } from '@/ui/StatusDot';

import { AssetLabelCard } from './AssetLabelCard';

/**
 * The curated record as one spec sheet: sections divided by rules inside a
 * single panel, rather than a card per group. Free text sits last because it is
 * the only part that can run long. The QR label sits beside it.
 */
export function AssetDetailsTab({ asset, editPath }: { asset: Asset; editPath?: string }) {
  const { can } = useSession();

  return (
    // The label sits beside the sheet only when there is room for both of the
    // sheet's columns; on anything narrower it drops below.
    <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_20rem]">
      <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-sm">
        <Section title="Identity">
          <Spec label="Asset tag" mono>
            {asset.assetTag}
          </Spec>
          <Spec label="Device name" mono>
            {asset.deviceName}
          </Spec>
          <Spec label="Category">
            {asset.category.name}{' '}
            <span className="text-text-subtle">({asset.category.code})</span>
          </Spec>
          <Spec label="Manufacturer">{asset.manufacturer}</Spec>
          <Spec label="Model">{asset.model}</Spec>
          <Spec label="Serial number" mono>
            {asset.serialNumber}
          </Spec>
          {asset.reportedSerialNumber && (
            <Spec label="Reported serial (unverified)" mono>
              {asset.reportedSerialNumber}
            </Spec>
          )}
          <Spec label="Ownership">{asset.ownership}</Spec>
          <Spec label="Legacy ID" mono>
            {asset.legacyId}
          </Spec>
        </Section>

        <Section title="Hardware and software">
          <Spec label="Operating system">
            {[asset.operatingSystem, asset.operatingSystemVersion].filter(Boolean).join(' ') ||
              null}
          </Spec>
          <Spec label="Windows 11 ready">
            <Windows11Badge value={asset.windows11Compliant} />
          </Spec>
          <Spec label="LAN MAC" mono>
            {asset.lanMacAddress}
          </Spec>
          <Spec label="BIOS GUID" mono>
            {asset.biosGuid}
          </Spec>
        </Section>

        <Section title="Purchase">
          <Spec label="Cost">{formatMoney(asset.purchaseCost, asset.currency)}</Spec>
          <Spec label="Purchased">{formatDate(asset.purchaseDate)}</Spec>
          <Spec label="Supplier">{asset.supplier}</Spec>
        </Section>

        <section className="border-t border-border px-5 py-4">
          <div className="flex items-center justify-between gap-3">
            <h3 className="text-sm font-semibold text-text">Description and notes</h3>
            {editPath && (
              <Button size="xs" variant="ghost" asChild>
                <Link to={`${editPath}#notes`}>
                  <Pencil className="mr-1 size-3.5" aria-hidden />
                  Edit
                </Link>
              </Button>
            )}
          </div>
          <div className="mt-3 grid gap-5 md:grid-cols-2">
            <TextBlock label="Description" value={asset.description} />
            <TextBlock label="Notes" value={asset.notes} />
          </div>
        </section>

        <p className="border-t border-border bg-surface-sunken/50 px-5 py-2.5 text-xs text-text-subtle">
          Created {formatDateTime(asset.createdAtUtc)} · Last updated{' '}
          {formatDateTime(asset.updatedAtUtc)}
        </p>
      </div>

      {can(Permission.QrGenerate) && <AssetLabelCard asset={asset} />}
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-t border-border px-5 py-4 first:border-t-0">
      <h3 className="text-sm font-semibold text-text">{title}</h3>
      <dl className="mt-2 grid gap-x-10 md:grid-cols-2">{children}</dl>
    </section>
  );
}

/** Label beside value, on a hairline, so a long sheet reads like a spec table. */
function Spec({
  label,
  children,
  mono,
}: {
  label: string;
  children?: ReactNode;
  mono?: boolean;
}) {
  const empty = children === null || children === undefined || children === '';
  return (
    <div className="flex min-w-0 items-baseline justify-between gap-4 border-b border-border/60 py-2">
      <dt className="shrink-0 text-sm text-text-muted">{label}</dt>
      <dd
        className={cn(
          'min-w-0 break-words text-right text-base',
          empty ? 'text-text-subtle' : 'text-text',
          mono && !empty && 'font-mono text-sm',
        )}
      >
        {empty ? '—' : children}
      </dd>
    </div>
  );
}

function TextBlock({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="min-w-0">
      <p className="text-sm text-text-muted">{label}</p>
      {value ? (
        <p className="mt-1 whitespace-pre-wrap break-words text-base text-text">{value}</p>
      ) : (
        <p className="mt-1 text-base text-text-subtle">None recorded.</p>
      )}
    </div>
  );
}

/**
 * Tri-state, so "not assessed yet" is visibly different from "no".
 * Colour is never the only signal — each state carries its own word.
 */
function Windows11Badge({ value }: { value: boolean | null }) {
  if (value === null) {
    return (
      <StatusDot tone="neutral" muted>
        Not assessed
      </StatusDot>
    );
  }
  return value ? (
    <StatusDot tone="success">Ready</StatusDot>
  ) : (
    <StatusDot tone="warning">Not ready</StatusDot>
  );
}
