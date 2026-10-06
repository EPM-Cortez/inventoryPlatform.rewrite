import { AlertTriangle, Inbox, Lock, SearchX } from 'lucide-react';
import type { ReactNode } from 'react';

import { ApiError } from '@/api/problem';
import { describeError } from '@/api/problem';
import { cn } from '@/lib/cn';

export interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}

export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: EmptyStateProps) {
  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center gap-3 px-6 py-14 text-center',
        className,
      )}
    >
      <div className="flex size-11 items-center justify-center rounded-full bg-surface-sunken text-text-subtle [&_svg]:size-5">
        {icon ?? <Inbox />}
      </div>
      <div className="max-w-sm space-y-1">
        <p className="text-lg font-medium text-text">{title}</p>
        {description && <p className="text-sm text-text-muted">{description}</p>}
      </div>
      {action && <div className="mt-1">{action}</div>}
    </div>
  );
}

/** Empty state for a filtered list that matched nothing. */
export function NoResults({ onClear }: { onClear?: () => void }) {
  return (
    <EmptyState
      icon={<SearchX />}
      title="No matches"
      description="No records match the current filters. Try widening or clearing them."
      action={
        onClear && (
          <button
            type="button"
            onClick={onClear}
            className="text-sm font-medium text-accent hover:underline"
          >
            Clear all filters
          </button>
        )
      }
    />
  );
}

/**
 * Renders a failed query. Distinguishes "you cannot see this" from "this broke",
 * because the recovery action is completely different.
 */
export function ErrorState({
  error,
  onRetry,
  className,
}: {
  error: unknown;
  onRetry?: () => void;
  className?: string;
}) {
  const forbidden = error instanceof ApiError && error.isForbidden;
  const traceId = error instanceof ApiError ? error.traceId : undefined;

  return (
    <EmptyState
      className={className}
      icon={forbidden ? <Lock /> : <AlertTriangle />}
      title={forbidden ? 'You do not have access' : 'Could not load this'}
      description={
        <>
          {forbidden
            ? 'Your role does not include permission for this data. Ask an administrator if you need it.'
            : describeError(error)}
          {traceId && (
            <span className="mt-2 block font-mono text-2xs text-text-subtle">
              Trace {traceId}
            </span>
          )}
        </>
      }
      action={
        !forbidden &&
        onRetry && (
          <button
            type="button"
            onClick={onRetry}
            className="text-sm font-medium text-accent hover:underline"
          >
            Try again
          </button>
        )
      }
    />
  );
}
