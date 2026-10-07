import {
  ArrowLeft,
  Building2,
  CheckCircle2,
  FileSpreadsheet,
  Sparkles,
  Upload,
  UserRound,
  UserRoundSearch,
  UserRoundX,
  Wand2,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';

import { importsApi } from '@/api/endpoints';
import { useCommitAssetImport, useValidateAssetImport } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  IMPORT_MAX_BYTES,
  type AssetImportJob,
  type AssetImportMode,
  type AssetImportPreviewRow,
} from '@/api/types';
import { downloadBlob } from '@/lib/download';
import {
  ASSET_CONDITION_LABELS,
  ASSET_STATUS_LABELS,
  formatDate,
  formatNumber,
} from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button } from '@/ui/Button';
import { Card, CardHeader } from '@/ui/Card';
import { DataTable, type Column } from '@/ui/DataTable';
import { PageHeader } from '@/ui/PageHeader';
import { Tooltip } from '@/ui/Tooltip';
import { useToast } from '@/ui/Toast';

import { ImportModePicker } from './ImportModePicker';
import {
  ImportCommittedSummary,
  ImportErrorTable,
  ImportFilePicker,
  ImportGuide,
  ImportSection,
  ImportStat,
  ImportSteps,
} from './ImportShared';

interface CommitSummary {
  createdRows: number;
  updatedRows: number;
  unchangedRows: number;
  createdCategoryCount: number;
  createdLocationCount: number;
}

/**
 * Distinct reference codes the preview shows being created.
 *
 * The preview is capped, so this can be a subset — the authoritative totals are
 * `plannedCategoryCreateCount` and `plannedLocationCreateCount`. Callers must
 * present these as examples when the preview is truncated, never as the full list.
 */
function plannedNewCodes(
  preview: AssetImportPreviewRow[],
): { categories: string[]; locations: string[] } {
  const categories = new Set<string>();
  const locations = new Set<string>();
  for (const row of preview) {
    if (row.categoryWillBeCreated) categories.add(row.resolvedCategoryCode);
    if (row.locationWillBeCreated && row.resolvedLocationCode) {
      locations.add(row.resolvedLocationCode);
    }
  }
  return { categories: [...categories].sort(), locations: [...locations].sort() };
}

export default function AssetImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState<AssetImportMode>('createOnly');
  const [job, setJob] = useState<AssetImportJob | null>(null);
  const [committed, setCommitted] = useState<CommitSummary | null>(null);

  const validate = useValidateAssetImport();
  const commit = useCommitAssetImport();
  const toast = useToast();

  function pickFile(picked: File | null) {
    setFile(picked);
    // A new file invalidates whatever was validated before it.
    setJob(null);
    setCommitted(null);
    validate.reset();
  }

  function startOver() {
    setFile(null);
    setJob(null);
    setCommitted(null);
    validate.reset();
    commit.reset();
  }

  const tooBig = file !== null && file.size > IMPORT_MAX_BYTES;

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-5">
      <PageHeader
        breadcrumbs={[{ label: 'Assets', to: '/assets' }, { label: 'Import' }]}
        title="Import assets from CSV"
        description="Every import is checked before anything changes. You see exactly what will be created and updated, and nothing is written until you confirm."
        actions={
          <Button asChild>
            <Link to="/assets">
              <ArrowLeft className="mr-1.5 size-4" />
              Back to assets
            </Link>
          </Button>
        }
      />

      <ImportSteps
        stage={committed ? 'done' : job ? 'review' : 'setup'}
        blocked={job?.status === 'validationFailed'}
      />

      {committed ? (
        <ImportCommittedSummary
          createdRows={committed.createdRows}
          updatedRows={committed.updatedRows}
          unchangedRows={committed.unchangedRows}
          onStartOver={startOver}
          viewLink={
            <Button variant="primary" asChild>
              <Link to="/assets">View assets</Link>
            </Button>
          }
          footnote={
            committed.createdCategoryCount + committed.createdLocationCount > 0 ? (
              <Alert tone="info" title="Reference data was created too">
                {[
                  committed.createdCategoryCount > 0 &&
                    `${formatNumber(committed.createdCategoryCount)} ${
                      committed.createdCategoryCount === 1 ? 'category' : 'categories'
                    }`,
                  committed.createdLocationCount > 0 &&
                    `${formatNumber(committed.createdLocationCount)} ${
                      committed.createdLocationCount === 1 ? 'location' : 'locations'
                    }`,
                ]
                  .filter(Boolean)
                  .join(' and ')}{' '}
                did not exist and were added. Review them on{' '}
                <Link to="/categories" className="text-accent hover:underline">
                  Categories
                </Link>{' '}
                and{' '}
                <Link to="/locations" className="text-accent hover:underline">
                  Locations
                </Link>
                .
              </Alert>
            ) : undefined
          }
        />
      ) : (
        <>
          <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_20rem]">
            <div className="flex min-w-0 flex-col gap-5">
              <ImportSection step={1} title="Choose a file" complete={file !== null && !tooBig}>
                <div className="flex flex-col gap-3">
                  <ImportFilePicker file={file} onFileChange={pickFile} />
                  {tooBig && (
                    <Alert tone="danger" title="That file is too large">
                      The server accepts up to {IMPORT_MAX_BYTES / 1024 / 1024} MB. Split
                      it into smaller files and import them one at a time.
                    </Alert>
                  )}
                </div>
              </ImportSection>

              <ImportSection
                step={2}
                title="How should existing asset tags be treated?"
                complete={job !== null}
                footer={
                  <>
                    <p className="mr-auto text-xs text-text-subtle">
                      Checking changes nothing — you review before applying.
                    </p>
                    <Button
                      variant="primary"
                      iconLeft={<Upload />}
                      loading={validate.isPending}
                      disabled={!file || tooBig}
                      onClick={() =>
                        file &&
                        validate.mutate(
                          { file, mode },
                          {
                            onSuccess: (result) => {
                              setJob(result);
                              toast.success(
                                'File checked',
                                result.status === 'validationFailed'
                                  ? 'Problems were found. Nothing has been changed.'
                                  : 'Nothing has been changed yet. Review, then apply.',
                              );
                            },
                          },
                        )
                      }
                    >
                      {job ? 'Check again' : 'Check file'}
                    </Button>
                  </>
                }
              >
                <div className="flex flex-col gap-3">
                  <ImportModePicker
                    value={mode}
                    onChange={setMode}
                    createOnlyValue="createOnly"
                    upsertValue="upsertByAssetTag"
                    keyNoun="asset tag"
                  />
                  {validate.error && (
                    <Alert tone="danger" title="The file could not be read" role="alert">
                      {validate.error instanceof ApiError && validate.error.isValidation
                        ? validate.error.allFieldErrors().join(' ')
                        : describeError(validate.error)}
                    </Alert>
                  )}
                </div>
              </ImportSection>
            </div>

            <ImportGuide>
              <p>The header row is detected automatically. Three layouts work:</p>
              <ul className="flex flex-col gap-2.5">
                <GuideLayout title="Current export format">
                  The file from Export CSV on the assets page. Round-trips exactly.
                </GuideLayout>
                <GuideLayout title="Earlier export format">
                  The older column set, without device name, MAC, BIOS GUID or OS fields.
                </GuideLayout>
                <GuideLayout title="Legacy register export">
                  The third-party layout with headings like &ldquo;Device Name&rdquo; and
                  &ldquo;MAC(LAN)&rdquo;.
                </GuideLayout>
              </ul>
              <p className="text-xs">
                Rows with no asset tag are fine — one is generated for you.
              </p>
              <p className="rounded-md bg-surface-sunken px-3 py-2 text-xs">
                <strong className="text-text">Tip:</strong> export a few assets and edit
                that file for a ready-made template.
              </p>
            </ImportGuide>
          </div>

          {job && (
            <AssetImportReview
              job={job}
              commit={commit}
              onCommitted={(result) => {
                setCommitted(result);
                setFile(null);
                setJob(null);
                validate.reset();
              }}
            />
          )}
        </>
      )}
    </div>
  );
}

function GuideLayout({ title, children }: { title: string; children: ReactNode }) {
  return (
    <li className="flex gap-2.5">
      <FileSpreadsheet className="mt-0.5 size-4 shrink-0 text-text-subtle" aria-hidden />
      <div>
        <p className="font-medium text-text">{title}</p>
        <p className="text-xs">{children}</p>
      </div>
    </li>
  );
}

function AssetImportReview({
  job,
  commit,
  onCommitted,
}: {
  job: AssetImportJob;
  commit: ReturnType<typeof useCommitAssetImport>;
  onCommitted: (summary: CommitSummary) => void;
}) {
  const blocked = job.status === 'validationFailed';

  const previewColumns: Column<AssetImportPreviewRow>[] = [
    {
      id: 'row',
      header: 'Row',
      width: 'w-16',
      align: 'right',
      cell: (row) => <span className="tabular-nums text-text-subtle">{row.rowNumber}</span>,
    },
    {
      id: 'action',
      header: 'Action',
      width: 'w-24',
      cell: (row) => (
        <Badge tone={row.action === 'create' ? 'success' : 'info'}>
          {row.action === 'create' ? 'Create' : 'Update'}
        </Badge>
      ),
    },
    {
      id: 'assetTag',
      header: 'Asset tag',
      width: 'w-64',
      cell: (row) => (
        <span className="flex min-w-0 items-center gap-1.5">
          <span className="truncate font-mono text-sm text-text">{row.assetTag}</span>
          {row.assetTagGenerated && (
            <Tooltip content="This row had no asset tag, so one was generated. The same file always produces the same tag.">
              <span className="inline-block shrink-0">
                <Badge tone="warning">
                  <Wand2 className="size-3" />
                  Generated
                </Badge>
              </span>
            </Tooltip>
          )}
        </span>
      ),
    },
    {
      id: 'serial',
      header: 'Serial from file',
      hideBelow: 'lg',
      width: 'w-48',
      cell: (row) => (
        <span className="flex flex-col items-start gap-1">
          <span className="font-mono text-xs text-text">
            {row.sourceSerialNumber ?? '—'}
          </span>
          {row.serialResolution === 'reportedDuplicate' && (
            <Badge tone="warning">Reported, not verified</Badge>
          )}
          {row.serialResolution === 'ignoredPlaceholder' && (
            <Badge tone="info">Placeholder ignored</Badge>
          )}
        </span>
      ),
    },
    {
      id: 'category',
      header: 'Category',
      width: 'w-48',
      cell: (row) => (
        <ResolvedReference
          source={row.category}
          resolvedCode={row.resolvedCategoryCode}
          willBeCreated={row.categoryWillBeCreated}
          kind="category"
        />
      ),
    },
    {
      id: 'status',
      header: 'Status',
      width: 'w-28',
      cell: (row) => ASSET_STATUS_LABELS[row.status] ?? row.status,
    },
    {
      id: 'condition',
      header: 'Condition',
      hideBelow: 'lg',
      width: 'w-28',
      cell: (row) => ASSET_CONDITION_LABELS[row.condition] ?? row.condition,
    },
    {
      id: 'location',
      header: 'Location',
      hideBelow: 'lg',
      width: 'w-44',
      cell: (row) =>
        row.resolvedLocationCode ? (
          <ResolvedReference
            source={row.locationCode ?? row.resolvedLocationCode}
            resolvedCode={row.resolvedLocationCode}
            willBeCreated={row.locationWillBeCreated}
            kind="location"
          />
        ) : (
          <span className="text-text-subtle">—</span>
        ),
    },
    {
      id: 'dates',
      header: 'Dates',
      hideBelow: 'xl',
      width: 'w-52',
      cell: (row) => <ImportDates row={row} />,
    },
    {
      id: 'assignedTo',
      header: 'Assign to',
      width: 'w-52',
      cell: (row) => <AssignmentCell row={row} />,
    },
  ];

  const planned = plannedNewCodes(job.preview);
  const plannedTotal = job.plannedCategoryCreateCount + job.plannedLocationCreateCount;

  return (
    <div className="flex flex-col gap-4">
      <ImportSection
        step={3}
        title="Review and apply"
        description={
          <span className="font-mono text-2xs text-text-subtle">{job.originalFileName}</span>
        }
      >
        <div className="flex flex-col gap-4">
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            <ImportStat label="Rows in file" value={job.totalRows} />
            <ImportStat label="Will create" value={job.createRows} tone="success" />
            <ImportStat label="Will update" value={job.updateRows} tone="info" />
            <ImportStat
              label="With problems"
              value={job.errorRows}
              tone={job.errorRows > 0 ? 'danger' : undefined}
            />
          </div>

          {plannedTotal > 0 && (
            <div className="grid gap-2 sm:grid-cols-2">
              <ImportStat
                label="New categories"
                value={job.plannedCategoryCreateCount}
                tone="warning"
              />
              <ImportStat
                label="New locations"
                value={job.plannedLocationCreateCount}
                tone="warning"
              />
            </div>
          )}

          {plannedTotal > 0 && (
            <Alert
              tone="warning"
              title="This import will also create reference data"
            >
              <p>
                {[
                  job.plannedCategoryCreateCount > 0 &&
                    `${formatNumber(job.plannedCategoryCreateCount)} ${
                      job.plannedCategoryCreateCount === 1 ? 'category' : 'categories'
                    }`,
                  job.plannedLocationCreateCount > 0 &&
                    `${formatNumber(job.plannedLocationCreateCount)} ${
                      job.plannedLocationCreateCount === 1 ? 'location' : 'locations'
                    }`,
                ]
                  .filter(Boolean)
                  .join(' and ')}{' '}
                in this file {plannedTotal === 1 ? 'does' : 'do'} not exist yet.
                Committing creates {plannedTotal === 1 ? 'it' : 'them'} along with the
                assets, in the same transaction.
              </p>
              <p className="mt-1.5">
                Check for typos first — a misspelt category here becomes a real
                category. Cancel and correct the file if any of these look wrong.
              </p>
              {(planned.categories.length > 0 || planned.locations.length > 0) && (
                <div className="mt-2 flex flex-col gap-1.5">
                  {planned.categories.length > 0 && (
                    <CodeList
                      label="New categories"
                      codes={planned.categories}
                      total={job.plannedCategoryCreateCount}
                    />
                  )}
                  {planned.locations.length > 0 && (
                    <CodeList
                      label="New locations"
                      codes={planned.locations}
                      total={job.plannedLocationCreateCount}
                    />
                  )}
                </div>
              )}
            </Alert>
          )}

          {job.reportedDuplicateSerialRows > 0 && (
            <Alert
              tone="warning"
              title={`${formatNumber(job.reportedDuplicateSerialRows)} ${
                job.reportedDuplicateSerialRows === 1 ? 'serial is' : 'serials are'
              } repeated`}
            >
              The original numbers will be kept as reported serials on those assets.
              They will not be used as unique verified identifiers or replaced with
              invented numbers. Check the affected rows before applying.
            </Alert>
          )}

          {job.fuzzyPersonMatchRows > 0 && (
            <Alert
              tone="warning"
              title={`${formatNumber(job.fuzzyPersonMatchRows)} ${
                job.fuzzyPersonMatchRows === 1 ? 'owner was' : 'owners were'
              } matched by approximate spelling`}
            >
              <p>
                The name in the file did not match exactly, so the closest active
                person was used — <code className="font-mono text-2xs">Schappel</code>{' '}
                resolving to <code className="font-mono text-2xs">schappell</code>, for
                instance.
              </p>
              <p className="mt-1.5">
                These are <strong>guesses about who holds a device</strong>. Check every
                one marked <em>Guessed</em> in the table below before applying; a wrong
                match makes the wrong person accountable for the asset.
              </p>
            </Alert>
          )}

          {job.unassignedImportRows > 0 && (
            <Alert
              tone="warning"
              title={`${formatNumber(job.unassignedImportRows)} ${
                job.unassignedImportRows === 1 ? 'row names an owner' : 'rows name owners'
              } who could not be found`}
            >
              <p>
                Those people are missing, archived, or too ambiguous to match safely.
                The {job.unassignedImportRows === 1 ? 'asset' : 'assets'} will still
                import, but as <strong>in stock with nobody assigned</strong> — the
                intended owner is dropped.
              </p>
              <p className="mt-1.5">
                People are never created by an asset import. To keep these assignments,
                cancel, add the missing people first, then import again.
              </p>
            </Alert>
          )}

          {job.inferredDateRows > 0 && (
            <Alert
              tone="info"
              title={`${formatNumber(job.inferredDateRows)} ${
                job.inferredDateRows === 1 ? 'row has' : 'rows have'
              } a date that was filled in`}
            >
              Some dates were not full calendar dates. A year on its own becomes 1
              January for purchase and warranty start dates, and 31 December for warranty
              end dates; <code className="font-mono text-2xs">MM/yyyy</code>,{' '}
              <code className="font-mono text-2xs">MM.yyyy</code> and Excel serial numbers
              are also converted. The Dates column marks every value that was inferred —
              check them if exact dates matter to you.
            </Alert>
          )}

          {job.generatedAssetTagRows > 0 && (
            <Alert
              tone="warning"
              title={`${formatNumber(job.generatedAssetTagRows)} ${
                job.generatedAssetTagRows === 1 ? 'row has' : 'rows have'
              } a generated asset tag`}
            >
              Those rows arrived without a tag, so one was created from the file and the
              row position — something like{' '}
              <code className="font-mono text-2xs">STR-AUTO-D99F9EAF8AF3927E-0002</code>.
              Tags you supplied are never replaced.
              <p className="mt-1.5">
                The same file always produces the same tags, so if you fix other problems
                and re-import this file with{' '}
                <strong>&ldquo;Add new and update existing&rdquo;</strong>, the rows will
                match what this import creates rather than duplicating them.
              </p>
            </Alert>
          )}

          {blocked ? (
            <Alert tone="danger" title="This file cannot be applied">
              Fix the problems below and check the file again. Nothing in the register has
              been changed.
            </Alert>
          ) : (
            <Alert tone="info" title="Nothing has been changed yet">
              This is a preview. Applying will create {formatNumber(job.createRows)} and
              update {formatNumber(job.updateRows)}{' '}
              {job.updateRows === 1 ? 'asset' : 'assets'}.
            </Alert>
          )}
        </div>
      </ImportSection>

      {job.errors.length > 0 && (
        <ImportErrorTable
          errors={job.errors}
          truncated={job.errorsTruncated}
          onDownload={async () => {
            const file = await importsApi.errorsCsv(job.id);
            downloadBlob(file.blob, file.fileName);
          }}
        />
      )}

      {job.preview.length > 0 && (
        <Card className="overflow-hidden">
          <CardHeader
            title="Preview"
            description={
              job.previewTruncated
                ? `The first ${job.preview.length} rows are shown; all ${formatNumber(job.validRows)} valid rows will be applied.`
                : undefined
            }
          />
          <DataTable
            columns={previewColumns}
            rows={job.preview}
            rowKey={(row) => String(row.rowNumber)}
          />
        </Card>
      )}

      {commit.error && (
        <Alert tone="danger" title="Could not apply the import" role="alert">
          {commit.error instanceof ApiError && commit.error.isConcurrencyConflict
            ? 'This import was already applied, or it changed since you checked it. Re-check the file.'
            : describeError(commit.error)}
        </Alert>
      )}

      {!blocked && (
        <div className="flex justify-end">
          <Button
            variant="primary"
            iconLeft={<CheckCircle2 />}
            loading={commit.isPending}
            onClick={() =>
              commit.mutate(
                { importJobId: job.id, version: job.version },
                {
                  onSuccess: (result) =>
                    onCommitted({
                      createdRows: result.createdRows,
                      updatedRows: result.updatedRows,
                      unchangedRows: result.unchangedRows,
                      createdCategoryCount: result.createdCategoryCount,
                      createdLocationCount: result.createdLocationCount,
                    }),
                },
              )
            }
          >
            Apply {formatNumber(job.validRows)} {job.validRows === 1 ? 'row' : 'rows'}
          </Button>
        </div>
      )}
    </div>
  );
}

/**
 * A reference value as it appeared in the file, and what it resolved to.
 *
 * When the two differ, or when the target does not exist yet, showing only one
 * of them hides the thing the user needs to check.
 */
function ResolvedReference({
  source,
  resolvedCode,
  willBeCreated,
  kind,
}: {
  source: string;
  resolvedCode: string;
  willBeCreated: boolean;
  kind: 'category' | 'location';
}) {
  const differs = source.trim().toLowerCase() !== resolvedCode.trim().toLowerCase();

  return (
    <span className="flex min-w-0 flex-col gap-0.5">
      <span className="flex min-w-0 items-center gap-1.5">
        <span className="truncate text-text">{source}</span>
        {willBeCreated && (
          <Tooltip
            content={`No ${kind} matched, so “${resolvedCode}” will be created when you apply this import.`}
          >
            <span className="inline-block shrink-0">
              <Badge tone="warning">
                <Sparkles className="size-3" />
                New
              </Badge>
            </span>
          </Tooltip>
        )}
      </span>
      {differs && (
        <span className="truncate font-mono text-2xs text-text-subtle">
          {resolvedCode}
        </span>
      )}
    </span>
  );
}

/** Normalised dates, marking any the server had to infer. */
function ImportDates({ row }: { row: AssetImportPreviewRow }) {
  const inferred = new Set(row.inferredDateFields.map((field) => field.toLowerCase()));
  const entries: { key: string; label: string; value: string | null }[] = [
    { key: 'purchasedate', label: 'Bought', value: row.purchaseDate },
    { key: 'warrantystartdate', label: 'Cover from', value: row.warrantyStartDate },
    { key: 'warrantyenddate', label: 'Cover to', value: row.warrantyEndDate },
  ];
  const present = entries.filter((entry) => entry.value);

  if (present.length === 0) return <span className="text-text-subtle">—</span>;

  return (
    <span className="flex flex-col gap-0.5">
      {present.map((entry) => {
        const wasInferred = inferred.has(entry.key);
        return (
          <span key={entry.key} className="flex items-center gap-1.5 text-2xs">
            <span className="text-text-subtle">{entry.label}</span>
            <span className={wasInferred ? 'text-warning' : 'text-text-muted'}>
              {formatDate(entry.value)}
            </span>
            {wasInferred && (
              <Tooltip content="The file did not give a full date, so this was filled in.">
                <span className="inline-block">
                  <Badge tone="warning">est.</Badge>
                </span>
              </Tooltip>
            )}
          </span>
        );
      })}
    </span>
  );
}

/**
 * Example codes from the preview.
 *
 * The preview is capped, so when it shows fewer than the true total this must
 * say so rather than reading as a complete list.
 */
function CodeList({
  label,
  codes,
  total,
}: {
  label: string;
  codes: string[];
  total: number;
}) {
  const partial = codes.length < total;

  return (
    <div className="flex flex-wrap items-baseline gap-1.5">
      <span className="text-2xs font-medium uppercase tracking-wide opacity-80">
        {label}
        {partial ? ` (${formatNumber(codes.length)} of ${formatNumber(total)})` : ''}
      </span>
      {codes.map((code) => (
        <code
          key={code}
          className="rounded-xs bg-warning-soft px-1.5 py-0.5 font-mono text-2xs"
        >
          {code}
        </code>
      ))}
      {partial && <span className="text-2xs opacity-80">and more</span>}
    </div>
  );
}

/**
 * Who the row will be assigned to, and how confident that is.
 *
 * Custody is an accountability record, so the three cases must look different:
 * an exact match is unremarkable, a fuzzy match is a guess the user should
 * verify, and an unresolvable name means the intended owner is being dropped.
 */
function AssignmentCell({ row }: { row: AssetImportPreviewRow }) {
  const { assignmentResolution: resolution, assignmentSource, assignedTo } = row;

  if (resolution === 'none') {
    return <span className="text-text-subtle">—</span>;
  }

  if (resolution === 'unassigned') {
    return (
      <Tooltip
        content={`No active person or department matched “${assignmentSource ?? ''}”. The asset imports as in stock with nobody assigned.`}
      >
        <span className="flex min-w-0 flex-col gap-0.5">
          <span className="truncate text-text-subtle line-through">
            {assignmentSource}
          </span>
          <span className="inline-flex">
            <Badge tone="danger">
              <UserRoundX className="size-3" />
              Not matched
            </Badge>
          </span>
        </span>
      </Tooltip>
    );
  }

  if (resolution === 'fuzzyPerson') {
    return (
      <Tooltip
        content={`The file said “${assignmentSource ?? ''}”. The closest active person was “${assignedTo ?? ''}”. Check this is the right person.`}
      >
        <span className="flex min-w-0 flex-col gap-0.5">
          <span className="flex min-w-0 items-center gap-1.5">
            <span className="truncate text-text">{assignedTo}</span>
            <span className="shrink-0">
              <Badge tone="warning">
                <UserRoundSearch className="size-3" />
                Guessed
              </Badge>
            </span>
          </span>
          <span className="truncate font-mono text-2xs text-text-subtle">
            file said {assignmentSource}
          </span>
        </span>
      </Tooltip>
    );
  }

  return (
    <span className="flex min-w-0 items-center gap-1.5">
      {resolution === 'department' ? (
        <Building2 className="size-3.5 shrink-0 text-text-subtle" aria-hidden />
      ) : (
        <UserRound className="size-3.5 shrink-0 text-text-subtle" aria-hidden />
      )}
      <span className="truncate text-text">{assignedTo}</span>
    </span>
  );
}
