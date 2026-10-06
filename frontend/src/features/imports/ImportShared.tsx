import {
  AlertTriangle,
  BookOpen,
  Check,
  CheckCircle2,
  Download,
  FileSpreadsheet,
  CloudUpload,
  X,
} from 'lucide-react';
import { useState, type ChangeEvent, type DragEvent, type ReactNode } from 'react';

import { IMPORT_MAX_BYTES, IMPORT_MAX_ROWS } from '@/api/types';
import { cn } from '@/lib/cn';
import { downloadBlob } from '@/lib/download';
import { formatNumber } from '@/lib/format';
import { Button, IconButton } from '@/ui/Button';
import { Card, CardBody, CardFooter, CardHeader } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { useToast } from '@/ui/Toast';

/**
 * Pieces shared by the asset and reference-data import wizards. Both follow the
 * same validate -> preview -> commit shape; only the preview columns and the
 * wording differ, so everything else lives here once.
 */

/** A small CSV rounding to "0 KB" looks like a failed read. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${formatNumber(bytes)} bytes`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export type ImportStage = 'setup' | 'review' | 'done';

const STAGES: { id: ImportStage; label: string }[] = [
  { id: 'setup', label: 'Choose a file' },
  { id: 'review', label: 'Check and review' },
  { id: 'done', label: 'Applied' },
];

/**
 * Where the user is in validate -> preview -> commit. Mostly reassurance: it
 * makes plain that choosing and checking a file changes nothing yet.
 */
export function ImportSteps({ stage, blocked }: { stage: ImportStage; blocked?: boolean }) {
  const current = STAGES.findIndex((candidate) => candidate.id === stage);

  return (
    <ol className="flex flex-wrap items-center gap-x-2 gap-y-2" aria-label="Import progress">
      {STAGES.map((step, index) => {
        const complete = index < current || stage === 'done';
        const active = index === current && stage !== 'done';
        const failed = active && blocked;
        return (
          <li key={step.id} className="flex items-center gap-2">
            {index > 0 && (
              <span
                className={cn('h-px w-6 sm:w-10', complete || active ? 'bg-accent' : 'bg-border')}
                aria-hidden="true"
              />
            )}
            <span
              className={cn(
                'flex size-6 shrink-0 items-center justify-center rounded-full border text-xs font-semibold tabular-nums',
                complete
                  ? 'border-accent bg-accent text-accent-fg'
                  : failed
                    ? 'border-danger-border bg-danger-soft text-danger'
                    : active
                      ? 'border-accent bg-accent-soft text-accent-soft-fg'
                      : 'border-border bg-surface text-text-subtle',
              )}
              aria-hidden="true"
            >
              {complete ? <Check className="size-3.5" strokeWidth={3} /> : index + 1}
            </span>
            <span
              className={cn(
                'text-sm',
                active || complete ? 'font-medium text-text' : 'text-text-subtle',
              )}
              aria-current={active ? 'step' : undefined}
            >
              {step.label}
              {failed && <span className="sr-only"> — problems found</span>}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

/** A numbered wizard card. The number turns into a tick once the step is done. */
export function ImportSection({
  step,
  title,
  description,
  complete,
  footer,
  children,
}: {
  step: number;
  title: ReactNode;
  description?: ReactNode;
  complete?: boolean;
  footer?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card>
      <div className="flex items-start gap-3 border-b border-border px-4 py-3">
        <span
          className={cn(
            'mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold tabular-nums',
            complete ? 'bg-accent text-accent-fg' : 'bg-surface-sunken text-text-muted',
          )}
          aria-hidden="true"
        >
          {complete ? <Check className="size-3.5" strokeWidth={3} /> : step}
        </span>
        <div className="min-w-0">
          <h2 className="text-lg font-semibold tracking-[-0.01em] text-text">
            <span className="sr-only">Step {step}: </span>
            {title}
          </h2>
          {description && <p className="mt-0.5 text-sm text-text-muted">{description}</p>}
        </div>
      </div>
      <CardBody>{children}</CardBody>
      {footer && <CardFooter>{footer}</CardFooter>}
    </Card>
  );
}

/**
 * The format reference, kept open beside the form rather than folded away —
 * a wrong header is the commonest reason an import fails.
 */
export function ImportGuide({ children }: { children: ReactNode }) {
  return (
    <Card className="lg:sticky lg:top-4">
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <BookOpen className="size-4 text-text-subtle" aria-hidden />
            File format
          </span>
        }
      />
      <CardBody className="flex flex-col gap-4 text-sm text-text-muted">{children}</CardBody>
    </Card>
  );
}

/** Column headers as separate chips, so each one can be read and checked. */
export function ImportColumnList({ columns }: { columns: readonly string[] }) {
  return (
    <ul className="flex flex-wrap gap-1">
      {columns.map((column) => (
        <li
          key={column}
          className="rounded-xs border border-border bg-surface-sunken px-1.5 py-0.5 font-mono text-2xs text-text"
        >
          {column}
        </li>
      ))}
    </ul>
  );
}

/**
 * Click to browse or drop a file on it. Only the first file of a drop is used,
 * and anything that is not a CSV is refused here rather than sent to the server.
 */
export function ImportFilePicker({
  file,
  onFileChange,
}: {
  file: File | null;
  onFileChange: (file: File | null) => void;
}) {
  const [dragging, setDragging] = useState(false);
  const [refused, setRefused] = useState<string | null>(null);

  function accept(picked: File | undefined) {
    if (!picked) return;
    if (!/\.csv$/i.test(picked.name) && picked.type !== 'text/csv') {
      setRefused(picked.name);
      return;
    }
    setRefused(null);
    onFileChange(picked);
  }

  function onChange(event: ChangeEvent<HTMLInputElement>) {
    accept(event.target.files?.[0]);
    // Cleared so choosing the same file again, after removing it, still fires.
    event.target.value = '';
  }

  function onDrop(event: DragEvent<HTMLElement>) {
    event.preventDefault();
    setDragging(false);
    accept(event.dataTransfer.files[0]);
  }

  const dropHandlers = {
    onDragOver: (event: DragEvent<HTMLElement>) => {
      event.preventDefault();
      setDragging(true);
    },
    onDragLeave: () => setDragging(false),
    onDrop,
  };

  const input = (
    <input type="file" accept=".csv,text/csv" className="sr-only" onChange={onChange} />
  );

  return (
    <div className="flex flex-col gap-2">
      {file ? (
        <div
          {...dropHandlers}
          className={cn(
            'flex items-center gap-3 rounded-lg border p-3 transition-colors',
            dragging ? 'border-accent bg-accent-soft/40' : 'border-border bg-surface-sunken/50',
          )}
        >
          <span className="flex size-10 shrink-0 items-center justify-center rounded-md bg-success-soft text-success">
            <FileSpreadsheet className="size-5" aria-hidden />
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate font-mono text-sm font-medium text-text">{file.name}</p>
            <p className="text-xs text-text-subtle">{formatFileSize(file.size)}</p>
          </div>
          <label className="inline-flex h-8 cursor-pointer items-center rounded-md border border-border-strong bg-surface px-2.5 text-sm font-medium text-text shadow-sm transition-colors hover:bg-surface-hover focus-within:ring-2 focus-within:ring-accent-ring">
            Replace
            {input}
          </label>
          <IconButton label="Remove file" size="sm" onClick={() => onFileChange(null)}>
            <X />
          </IconButton>
        </div>
      ) : (
        <label
          {...dropHandlers}
          className={cn(
            'flex cursor-pointer flex-col items-center gap-3 rounded-lg border-2 border-dashed px-6 py-8 text-center transition-colors',
            'focus-within:border-accent focus-within:ring-2 focus-within:ring-accent-ring',
            dragging
              ? 'border-accent bg-accent-soft/40'
              : 'border-border-strong hover:border-accent hover:bg-surface-hover',
          )}
        >
          <span className="flex size-11 items-center justify-center rounded-full bg-accent-soft text-accent-soft-fg">
            <CloudUpload className="size-5" aria-hidden />
          </span>
          <span>
            <span className="block text-base font-medium text-text">
              {dragging ? 'Drop to use this file' : 'Drop a CSV here, or click to browse'}
            </span>
            <span className="mt-0.5 block text-xs text-text-subtle">
              Up to {IMPORT_MAX_BYTES / 1024 / 1024} MB and {formatNumber(IMPORT_MAX_ROWS)}{' '}
              rows
            </span>
          </span>
          {input}
        </label>
      )}
      {refused && (
        <p className="text-xs text-danger" role="alert">
          {refused} is not a CSV file. Save it as CSV from your spreadsheet first.
        </p>
      )}
    </div>
  );
}

/**
 * Plain count tile. The numeral stays in the normal text colour — a small
 * coloured label carries the state, so the figure is never harder to read.
 */
export function ImportStat({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone?: 'success' | 'info' | 'danger' | 'warning';
}) {
  return (
    <div className="rounded-md border border-border bg-surface p-3">
      <p
        className={cn(
          'text-xs font-medium',
          tone === 'success'
            ? 'text-success'
            : tone === 'info'
              ? 'text-info'
              : tone === 'danger'
                ? 'text-danger'
                : tone === 'warning'
                  ? 'text-warning'
                  : 'text-text-muted',
        )}
      >
        {label}
      </p>
      <p className="mt-0.5 text-2xl font-semibold tabular-nums text-text">
        {formatNumber(value)}
      </p>
    </div>
  );
}

/** Both job types report errors with exactly these fields. */
export interface ImportErrorRow {
  rowNumber: number;
  column: string;
  code: string;
  message: string;
  value: string | null;
}

export function ImportErrorTable({
  errors,
  truncated,
  onDownload,
}: {
  errors: ImportErrorRow[];
  truncated: boolean;
  onDownload: () => Promise<void>;
}) {
  const [downloading, setDownloading] = useState(false);
  const toast = useToast();

  const columns: Column<ImportErrorRow>[] = [
    {
      id: 'row',
      header: 'Row',
      width: 'w-16',
      align: 'right',
      cell: (error) => (
        <span className="tabular-nums text-text-subtle">{error.rowNumber}</span>
      ),
    },
    {
      id: 'column',
      header: 'Column',
      width: 'w-44',
      cell: (error) => <span className="font-mono text-xs text-text">{error.column}</span>,
    },
    { id: 'message', header: 'Problem', cell: (error) => error.message },
    {
      id: 'value',
      header: 'Value',
      hideBelow: 'lg',
      width: 'w-44',
      cell: (error) =>
        error.value ? (
          <span className="font-mono text-xs text-text-muted">{error.value}</span>
        ) : (
          <span className="text-text-subtle">empty</span>
        ),
    },
  ];

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <AlertTriangle className="size-4 text-danger" aria-hidden />
            Problems
          </span>
        }
        description={
          truncated
            ? 'The first problems are shown. Download the full report for the rest.'
            : undefined
        }
        actions={
          <Button
            size="sm"
            iconLeft={<Download />}
            loading={downloading}
            onClick={() => {
              setDownloading(true);
              onDownload()
                .catch((error: unknown) =>
                  toast.fromError(error, 'Could not download the error report'),
                )
                .finally(() => setDownloading(false));
            }}
          >
            Error report
          </Button>
        }
      />
      <DataTable
        columns={columns}
        rows={errors}
        rowKey={(error) => `${error.rowNumber}-${error.column}-${error.code}`}
      />
    </Card>
  );
}

export function ImportCommittedSummary({
  createdRows,
  updatedRows,
  unchangedRows,
  onStartOver,
  viewLink,
  footnote,
}: {
  createdRows: number;
  updatedRows: number;
  unchangedRows: number;
  onStartOver: () => void;
  viewLink: ReactNode;
  /** Anything else created alongside the rows, e.g. new categories. */
  footnote?: ReactNode;
}) {
  return (
    <Card>
      <CardBody className="flex flex-col items-center gap-4 py-10 text-center">
        <div className="flex size-11 items-center justify-center rounded-full bg-success-soft text-success">
          <CheckCircle2 className="size-5" />
        </div>
        <div>
          <p className="text-xl font-semibold text-text">Import applied</p>
          <p className="mt-1 text-base text-text-muted">Everything has been saved.</p>
        </div>

        <div className="grid w-full max-w-md gap-2 sm:grid-cols-3">
          <ImportStat label="Created" value={createdRows} tone="success" />
          <ImportStat label="Updated" value={updatedRows} tone="info" />
          <ImportStat label="Unchanged" value={unchangedRows} />
        </div>

        {footnote && <div className="w-full max-w-md">{footnote}</div>}

        <div className="flex gap-2">
          <Button onClick={onStartOver}>Import another file</Button>
          {viewLink}
        </div>
      </CardBody>
    </Card>
  );
}

/** Re-exported so wizards do not each import the helper separately. */
export { downloadBlob };
