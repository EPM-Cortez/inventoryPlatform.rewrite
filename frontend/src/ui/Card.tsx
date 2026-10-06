import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

export function Card({
  className,
  children,
  ...props
}: { className?: string; children: ReactNode } & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        'rounded-lg border border-border bg-surface shadow-sm',
        className,
      )}
      {...props}
    >
      {children}
    </div>
  );
}

export function CardHeader({
  title,
  description,
  actions,
  className,
}: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        'flex items-start justify-between gap-4 border-b border-border px-4 py-3',
        className,
      )}
    >
      <div className="min-w-0">
        <h2 className="text-lg font-semibold tracking-[-0.01em] text-text">{title}</h2>
        {description && <p className="mt-0.5 text-sm text-text-muted">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  );
}

export function CardBody({
  className,
  children,
  ...props
}: { className?: string; children: ReactNode } & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('p-4', className)} {...props}>
      {children}
    </div>
  );
}

export function CardFooter({
  className,
  children,
}: {
  className?: string;
  children: ReactNode;
}) {
  return (
    <div
      className={cn(
        'flex items-center justify-end gap-2 border-t border-border bg-surface-sunken/60 px-4 py-3',
        className,
      )}
    >
      {children}
    </div>
  );
}

/**
 * Label/value pair used throughout detail pages. Values that are missing render
 * an em dash rather than collapsing, so the layout stays predictable.
 */
export function DataItem({
  label,
  children,
  className,
  mono,
}: {
  label: ReactNode;
  children?: ReactNode;
  className?: string;
  mono?: boolean;
}) {
  const isEmpty = children === null || children === undefined || children === '';
  return (
    <div className={cn('min-w-0', className)}>
      <dt className="text-xs font-medium uppercase tracking-wide text-text-subtle">
        {label}
      </dt>
      <dd
        className={cn(
          'mt-1 break-words text-base text-text',
          mono && 'font-mono text-sm',
          isEmpty && 'text-text-subtle',
        )}
      >
        {isEmpty ? '—' : children}
      </dd>
    </div>
  );
}
