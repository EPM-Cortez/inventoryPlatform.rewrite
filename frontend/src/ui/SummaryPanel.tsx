import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

/**
 * The header of a record page: a picture or avatar, the name, a line saying
 * what it is, its status, the actions, and a row of the few facts people open
 * the page to find. One panel rather than a stack of small cards, so each page
 * opens on a single summary of the record.
 */
export function SummaryPanel({
  label,
  media,
  title,
  titleAccessory,
  subtitle,
  status,
  actions,
  facts,
  children,
  as: Heading = 'h1',
  className,
}: {
  /** Accessible name for the region, e.g. "Asset summary". */
  label: string;
  media?: ReactNode;
  title: ReactNode;
  /** Sits on the title line, e.g. a copy button. */
  titleAccessory?: ReactNode;
  subtitle?: ReactNode;
  /** `StatusDot`s, laid out in a row under the subtitle. */
  status?: ReactNode;
  actions?: ReactNode;
  /** `SummaryFact`s. */
  facts?: ReactNode;
  /** Anything that belongs under the facts, such as notes. */
  children?: ReactNode;
  /** `h2` when the panel sits inside a page that already has its `h1`. */
  as?: 'h1' | 'h2';
  className?: string;
}) {
  return (
    <section
      aria-label={label}
      className={cn('overflow-hidden rounded-xl border border-border bg-surface shadow-sm', className)}
    >
      <div className="flex flex-col gap-5 p-4 sm:flex-row sm:p-5">
        {media && <div className="shrink-0 self-start">{media}</div>}

        <div className="flex min-w-0 flex-1 flex-col gap-4">
          <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-3">
            <div className="min-w-0">
              <Heading
                className={cn(
                  'flex flex-wrap items-center gap-1.5 font-semibold text-text',
                  Heading === 'h1'
                    ? 'text-3xl tracking-[-0.02em]'
                    : 'text-2xl tracking-[-0.01em]',
                )}
              >
                <span className="min-w-0 break-words">{title}</span>
                {titleAccessory}
              </Heading>
              {subtitle && <p className="mt-0.5 text-base text-text-muted">{subtitle}</p>}
              {status && (
                <div className="mt-2.5 flex flex-wrap items-center gap-x-4 gap-y-1.5">
                  {status}
                </div>
              )}
            </div>
            {actions && (
              <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>
            )}
          </div>

          {facts && (
            <dl className="grid grid-cols-2 gap-x-6 gap-y-4 border-t border-border pt-4 xl:grid-cols-4">
              {facts}
            </dl>
          )}

          {children}
        </div>
      </div>
    </section>
  );
}

/**
 * One fact in a summary row. A missing value says so in the faded text colour,
 * so an empty fact reads as "not recorded" rather than as a broken layout.
 */
export function SummaryFact({
  label,
  children,
  secondary,
  empty = 'Not recorded',
}: {
  label: string;
  children?: ReactNode;
  secondary?: ReactNode;
  /** Wording for a missing value. */
  empty?: string;
}) {
  const isEmpty = children === null || children === undefined || children === '';
  return (
    <div className="min-w-0">
      <dt className="text-sm text-text-muted">{label}</dt>
      <dd className={cn('mt-1 min-w-0 text-base', isEmpty ? 'text-text-subtle' : 'text-text')}>
        {isEmpty ? empty : children}
      </dd>
      {secondary && (
        <dd className="mt-0.5 truncate text-xs text-text-subtle">{secondary}</dd>
      )}
    </div>
  );
}
