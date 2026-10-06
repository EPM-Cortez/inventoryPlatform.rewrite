import type { ReactNode } from 'react';
import { Link } from 'react-router';

import type { AuditLogEntry } from '@/api/adminTypes';
import { formatDateTime, formatRelative } from '@/lib/format';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { CopyButton } from '@/ui/CopyButton';
import { Drawer } from '@/ui/Drawer';

import {
  describeAuditAction,
  describeOutcome,
  describeTargetType,
  outcomeTone,
} from './auditActions';

export interface AuditEntryDrawerProps {
  entry: AuditLogEntry;
  onClose: () => void;
}

/**
 * Everything the server recorded about one entry.
 *
 * `detailsJson` is deliberately shown raw (pretty-printed) rather than rendered
 * per action: its shape varies by action and inventing a friendly view per shape
 * would eventually misrepresent one. The evidence is what matters here.
 */
export function AuditEntryDrawer({ entry, onClose }: AuditEntryDrawerProps) {
  const details = prettyJson(entry.detailsJson);
  const targetHref = auditTargetHref(entry);

  return (
    <Drawer
      open
      onOpenChange={(open) => !open && onClose()}
      title={describeAuditAction(entry.action)}
      description={`${formatDateTime(entry.occurredAtUtc)} · ${formatRelative(entry.occurredAtUtc)}`}
      width="md"
      footer={
        <Button variant="primary" onClick={onClose}>
          Close
        </Button>
      }
    >
      <div className="flex flex-col gap-5">
        <dl className="grid grid-cols-[auto_1fr] items-start gap-x-4 gap-y-2.5 text-sm">
          <Row label="Outcome">
            <Badge tone={outcomeTone(entry.outcome)} dot>
              {describeOutcome(entry.outcome)}
            </Badge>
          </Row>

          <Row label="Action">
            <span className="font-mono text-xs text-text-muted">{entry.action}</span>
          </Row>

          <Row label="Who">
            {entry.actorDisplayName ? (
              <span className="text-text">{entry.actorDisplayName}</span>
            ) : (
              // Agent check-ins and the bootstrap account have no signed-in user.
              <span className="text-text-subtle">No signed-in user</span>
            )}
          </Row>

          <Row label="Target">
            <div className="flex min-w-0 flex-col gap-0.5">
              <span className="text-text">{describeTargetType(entry.targetType)}</span>
              {entry.targetId &&
                (targetHref ? (
                  <Link
                    to={targetHref}
                    className="truncate rounded-xs font-mono text-xs text-accent hover:underline"
                  >
                    {entry.targetId}
                  </Link>
                ) : (
                  <span className="truncate font-mono text-xs text-text-subtle">
                    {entry.targetId}
                  </span>
                ))}
            </div>
          </Row>

          {entry.ipAddress && (
            <Row label="From">
              <span className="font-mono text-xs text-text-muted">{entry.ipAddress}</span>
            </Row>
          )}

          {entry.client && (
            <Row label="Client">
              <span className="break-all text-xs text-text-muted">{entry.client}</span>
            </Row>
          )}

          {entry.correlationId && (
            <Row label="Correlation">
              <span className="flex items-center gap-1.5">
                <span className="font-mono text-xs text-text-muted">
                  {entry.correlationId}
                </span>
                <CopyButton value={entry.correlationId} label="Copy correlation id" />
              </span>
            </Row>
          )}
        </dl>

        <div>
          <div className="mb-1.5 flex items-center justify-between">
            <h3 className="text-sm font-medium text-text">Recorded details</h3>
            {entry.detailsJson && (
              <CopyButton value={entry.detailsJson} label="Copy details" />
            )}
          </div>
          {details ? (
            <pre className="max-h-96 overflow-auto rounded-md border border-border bg-surface-sunken p-3 font-mono text-xs leading-relaxed text-text">
              {details}
            </pre>
          ) : (
            <p className="rounded-md border border-border bg-surface-sunken p-3 text-sm text-text-subtle">
              This action recorded no extra detail.
            </p>
          )}
        </div>
      </div>
    </Drawer>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="whitespace-nowrap pt-0.5 text-text-subtle">{label}</dt>
      <dd className="min-w-0">{children}</dd>
    </>
  );
}

/**
 * Reformats the stored JSON for reading. Anything that will not parse is shown
 * exactly as stored — the point of an audit record is that it is not edited.
 */
function prettyJson(raw: string | null): string | null {
  if (!raw) return null;
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

/** Only target types with a page worth opening get a link. */
function auditTargetHref(entry: AuditLogEntry): string | null {
  if (!entry.targetId) return null;
  switch (entry.targetType) {
    case 'asset':
      return `/assets/${entry.targetId}`;
    case 'managed_agent':
      return `/devices/${entry.targetId}`;
    default:
      return null;
  }
}
