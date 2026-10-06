import { ArrowRight, UserRoundX } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { useCustodyExceptions } from '@/api/hooks';
import { formatDate, formatNumber } from '@/lib/format';
import { Avatar } from '@/ui/Avatar';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { StatusDot } from '@/ui/StatusDot';

const PREVIEW_COUNT = 5;

/**
 * People holding equipment who arguably should not be.
 *
 * The server decides what counts — an inactive person still holding kit, or one
 * with a leaver case whose date has passed. This is the prompt to open a case,
 * so it sits above the case list rather than on a page of its own.
 */
export function CustodyExceptionsCard() {
  const [expanded, setExpanded] = useState(false);
  const exceptions = useCustodyExceptions(1, expanded ? 50 : PREVIEW_COUNT);

  const rows = exceptions.data?.items ?? [];
  const total = exceptions.data?.totalCount ?? 0;

  // Nothing to say when there is nothing wrong — this is a prompt, not a metric.
  if (exceptions.isLoading || total === 0) return null;

  return (
    <Card className="border-warning-border bg-warning-soft/20">
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <UserRoundX className="size-4 text-warning" aria-hidden />
            {formatNumber(total)} {total === 1 ? 'person is' : 'people are'} holding
            equipment they should not be
          </span>
        }
        description="Inactive people, or leavers whose last day has passed."
      />
      <CardBody className="p-0">
        <ul className="divide-y divide-border/70">
          {rows.map((exception) => (
            <li
              key={exception.personId}
              className="flex flex-wrap items-center justify-between gap-3 px-4 py-2.5"
            >
              <div className="flex min-w-0 items-center gap-3">
                <Avatar name={exception.personDisplayName} size="sm" />
                <div className="min-w-0">
                  <Link
                    to={`/people/${exception.personId}`}
                    className="block truncate rounded-xs font-medium text-text hover:text-accent hover:underline"
                  >
                    {exception.personDisplayName}
                  </Link>
                  <p className="truncate text-xs text-text-muted">
                    {exception.personExternalId ? (
                      <span className="font-mono">{exception.personExternalId}</span>
                    ) : (
                      <span className="text-text-subtle">No staff number</span>
                    )}
                    {!exception.personIsActive && (
                      <span className="text-text-subtle"> · record archived</span>
                    )}
                    {exception.leavingOn && (
                      <span className="text-text-subtle">
                        {' · '}left {formatDate(exception.leavingOn)}
                      </span>
                    )}
                  </p>
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <StatusDot tone={exception.overdueAssetCount > 0 ? 'danger' : 'warning'}>
                  {formatNumber(exception.currentAssetCount)} held
                </StatusDot>
                {exception.leaverCaseId ? (
                  <Button size="sm" asChild>
                    <Link to={`/leavers/${exception.leaverCaseId}`}>
                      Open case <ArrowRight className="ml-1 size-3" />
                    </Link>
                  </Button>
                ) : (
                  <span className="w-24 text-right text-xs text-text-subtle">No case yet</span>
                )}
              </div>
            </li>
          ))}
        </ul>

        {total > rows.length && (
          <div className="border-t border-border px-4 py-2">
            <button
              type="button"
              onClick={() => setExpanded(true)}
              className="rounded-xs text-sm font-medium text-accent hover:underline"
            >
              Show all {formatNumber(total)}
            </button>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
