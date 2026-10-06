import { History, Plus } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { useAddAssetNote, useAssetHistory } from '@/api/hooks';
import type { AssetEvent } from '@/api/types';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { formatDateTime, formatEventType, initials } from '@/lib/format';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardBody } from '@/ui/Card';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { Textarea } from '@/ui/Field';
import { Pagination } from '@/ui/Pagination';
import { Skeleton } from '@/ui/Skeleton';
import { useToast } from '@/ui/Toast';

const PAGE_SIZE = 25;

export function AssetHistoryTab({ assetId }: { assetId: string }) {
  const [page, setPage] = useState(1);
  const [note, setNote] = useState('');
  const { can } = useSession();
  const toast = useToast();

  const history = useAssetHistory(assetId, page, PAGE_SIZE);
  const addNote = useAddAssetNote(assetId);

  function submitNote(event: FormEvent) {
    event.preventDefault();
    const trimmed = note.trim();
    if (!trimmed) return;

    addNote.mutate(
      { note: trimmed },
      {
        onSuccess: () => {
          setNote('');
          setPage(1);
          toast.success('Note added');
        },
        onError: (error) => toast.fromError(error, 'Could not add the note'),
      },
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        {can(Permission.HistoryNote) && (
          // A comment box at the head of the timeline it adds to: one line until
          // there is something longer to say.
          <form
            onSubmit={submitNote}
            className="flex items-start gap-2 border-b border-border p-3"
          >
            <label htmlFor="asset-note" className="sr-only">
              Add a note
            </label>
            <Textarea
              id="asset-note"
              value={note}
              onChange={(event) => setNote(event.target.value)}
              maxLength={4000}
              rows={note.includes('\n') || note.length > 90 ? 3 : 1}
              className="min-h-9 resize-none py-1.5"
              placeholder="Add a note to this asset’s history — notes are permanent"
            />
            <Button
              type="submit"
              variant="primary"
              iconLeft={<Plus />}
              loading={addNote.isPending}
              disabled={!note.trim()}
            >
              Add note
            </Button>
          </form>
        )}

        <CardBody className="p-0">
          {history.isError ? (
            <ErrorState error={history.error} onRetry={() => void history.refetch()} />
          ) : history.isLoading ? (
            <div className="flex flex-col gap-4 p-4">
              {Array.from({ length: 5 }, (_, index) => (
                <div key={index} className="flex gap-3">
                  <Skeleton className="size-7 shrink-0 rounded-full" />
                  <div className="flex-1 space-y-1.5">
                    <Skeleton className="h-4 w-1/3" />
                    <Skeleton className="h-3 w-1/2" />
                  </div>
                </div>
              ))}
            </div>
          ) : (history.data?.items.length ?? 0) === 0 ? (
            <EmptyState
              icon={<History />}
              title="No history yet"
              description="Events appear here as the asset is edited, assigned, returned and retired."
            />
          ) : (
            <ol className="py-1">
              {history.data?.items.map((event) => (
                <HistoryRow key={event.id} event={event} />
              ))}
            </ol>
          )}
        </CardBody>

        {(history.data?.totalCount ?? 0) > PAGE_SIZE && (
          <Pagination
            page={history.data?.page ?? 1}
            pageSize={history.data?.pageSize ?? PAGE_SIZE}
            totalCount={history.data?.totalCount ?? 0}
            onPageChange={setPage}
            itemLabel="events"
          />
        )}
      </Card>
    </div>
  );
}

function HistoryRow({ event }: { event: AssetEvent }) {
  const [expanded, setExpanded] = useState(false);
  // `details` is an open bag whose shape depends on the event type. Rather than
  // guess at every variant, show the summary and let the user open the raw record.
  const hasDetails = event.details && Object.keys(event.details).length > 0;

  return (
    <li className="group relative flex gap-3 px-4 py-3">
      {/* Connector to the next event, so the list reads as one sequence. */}
      <span
        className="absolute bottom-0 left-[1.875rem] top-11 w-px bg-border group-last:hidden"
        aria-hidden="true"
      />
      <span
        className="relative mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full border border-border bg-surface-sunken text-2xs font-semibold text-text-muted"
        aria-hidden="true"
      >
        {initials(event.actorDisplayName)}
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <Badge tone="neutral">{formatEventType(event.type)}</Badge>
          <span className="text-xs text-text-subtle">
            {formatDateTime(event.occurredAtUtc)} · {event.actorDisplayName}
            {event.source && event.source !== 'Api' ? ` · ${event.source}` : ''}
          </span>
        </div>

        <p className="mt-1 whitespace-pre-wrap text-base text-text">{event.summary}</p>

        {hasDetails && (
          <>
            <button
              type="button"
              onClick={() => setExpanded((open) => !open)}
              className="mt-1 rounded-xs text-xs font-medium text-text-muted hover:text-text hover:underline"
              aria-expanded={expanded}
            >
              {expanded ? 'Hide details' : 'Show details'}
            </button>
            {expanded && (
              <pre className="mt-2 overflow-x-auto rounded-md bg-surface-sunken p-3 font-mono text-2xs leading-relaxed text-text-muted">
                {JSON.stringify(event.details, null, 2)}
              </pre>
            )}
          </>
        )}
      </div>
    </li>
  );
}
