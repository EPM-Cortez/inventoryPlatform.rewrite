import { ArrowLeft, CheckCircle2, Upload, Wand2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { referenceImportsApi } from '@/api/endpoints';
import { useCommitReferenceImport, useValidateReferenceImport } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  IMPORT_MAX_BYTES,
  type ReferenceDataImportJob,
  type ReferenceDataImportMode,
  type ReferenceDataImportPreviewRow,
  type ReferenceDataImportResource,
} from '@/api/types';
import { downloadBlob } from '@/lib/download';
import { formatNumber } from '@/lib/format';
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
  ImportColumnList,
  ImportCommittedSummary,
  ImportErrorTable,
  ImportFilePicker,
  ImportGuide,
  ImportSection,
  ImportStat,
  ImportSteps,
} from './ImportShared';
import {
  REFERENCE_RESOURCES,
  type ReferenceResourceConfig,
} from './referenceResources';

interface CommitSummary {
  createdRows: number;
  updatedRows: number;
  unchangedRows: number;
}

/**
 * One wizard for people, departments and locations. The route supplies the
 * resource; everything that differs between them lives in `REFERENCE_RESOURCES`.
 */
export default function ReferenceDataImportPage({
  resource,
}: {
  resource: ReferenceDataImportResource;
}) {
  // Remount on resource change so no state leaks between them.
  return <Wizard key={resource} config={REFERENCE_RESOURCES[resource]} />;
}

function Wizard({ config }: { config: ReferenceResourceConfig }) {
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState<ReferenceDataImportMode>('createOnly');
  const [job, setJob] = useState<ReferenceDataImportJob | null>(null);
  const [committed, setCommitted] = useState<CommitSummary | null>(null);

  const validate = useValidateReferenceImport(config.resource);
  const commit = useCommitReferenceImport();
  const toast = useToast();

  function pickFile(picked: File | null) {
    setFile(picked);
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
        breadcrumbs={[
          { label: config.title, to: config.listPath },
          { label: 'Import' },
        ]}
        title={`Import ${config.plural} from CSV`}
        description="Every import is checked before anything changes. You see exactly what will be created and updated, and nothing is written until you confirm."
        actions={
          <Button asChild>
            <Link to={config.listPath}>
              <ArrowLeft className="mr-1.5 size-4" />
              Back to {config.plural}
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
              <Link to={config.listPath}>View {config.plural}</Link>
            </Button>
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
                title={`How should existing ${config.keyNoun}s be treated?`}
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
                    upsertValue="upsertByKey"
                    keyNoun={config.keyNoun}
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
              <div className="flex flex-col gap-1.5">
                <p className="font-medium text-text">Columns</p>
                <ImportColumnList columns={config.canonicalHeaders} />
                <p className="text-xs">
                  Every row&rsquo;s <code className="font-mono">schemaVersion</code> must
                  be <strong className="text-text">{config.schemaVersion}</strong>. Asset
                  files use a different number, so a copied asset template is rejected.
                </p>
              </div>
              <div className="flex flex-col gap-1.5">
                <p className="font-medium text-text">Short format, also accepted</p>
                <ImportColumnList columns={config.compatibilityHeaders} />
              </div>
              {config.generatedKeyExplanation && (
                <p className="text-xs">{config.generatedKeyExplanation}</p>
              )}
              {config.showParent && (
                <p className="text-xs">
                  <code className="font-mono">parentCode</code> nests a location inside
                  another. A parent can appear anywhere in the file — the whole hierarchy
                  is resolved together.
                </p>
              )}
              <p className="rounded-md bg-surface-sunken px-3 py-2 text-xs">
                <strong className="text-text">Tip:</strong> export first for a template
                that round-trips exactly.
              </p>
            </ImportGuide>
          </div>

          {job && (
            <ReferenceImportReview
              job={job}
              config={config}
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

function ReferenceImportReview({
  job,
  config,
  commit,
  onCommitted,
}: {
  job: ReferenceDataImportJob;
  config: ReferenceResourceConfig;
  commit: ReturnType<typeof useCommitReferenceImport>;
  onCommitted: (summary: CommitSummary) => void;
}) {
  const blocked = job.status === 'validationFailed';

  const previewColumns: Column<ReferenceDataImportPreviewRow>[] = [
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
      id: 'key',
      header: config.keyHeader,
      width: 'w-56',
      cell: (row) => (
        <span className="flex min-w-0 items-center gap-1.5">
          <span className="truncate font-mono text-sm text-text">{row.key}</span>
          {row.keyGenerated && (
            <Tooltip
              content={
                config.generatedKeyExplanation ??
                'This row had no key, so one was generated.'
              }
            >
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
    { id: 'name', header: 'Name', cell: (row) => row.name },
    ...(config.showType
      ? ([
          {
            id: 'type',
            header: 'Type',
            hideBelow: 'lg',
            width: 'w-32',
            cell: (row) => row.type ?? <span className="text-text-subtle">—</span>,
          },
        ] satisfies Column<ReferenceDataImportPreviewRow>[])
      : []),
    ...(config.showParent
      ? ([
          {
            id: 'parent',
            header: 'Inside',
            hideBelow: 'lg',
            width: 'w-40',
            cell: (row) =>
              row.parentKey ? (
                <span className="font-mono text-xs text-text-muted">{row.parentKey}</span>
              ) : (
                <span className="text-text-subtle">top level</span>
              ),
          },
        ] satisfies Column<ReferenceDataImportPreviewRow>[])
      : []),
    {
      id: 'isActive',
      header: 'Active',
      width: 'w-24',
      cell: (row) =>
        row.isActive ? (
          <Badge tone="success">Active</Badge>
        ) : (
          <Badge tone="neutral">Archived</Badge>
        ),
    },
  ];

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

          {job.generatedKeyRows > 0 && (
            <Alert
              tone="warning"
              title={`${formatNumber(job.generatedKeyRows)} ${
                job.generatedKeyRows === 1 ? 'row has' : 'rows have'
              } a generated ${config.keyNoun}`}
            >
              {config.generatedKeyExplanation} Keys you supplied are never replaced, and
              the same file always produces the same keys — so re-importing this file
              with <strong>&ldquo;Add new and update existing&rdquo;</strong> matches
              what this import creates rather than duplicating it.
            </Alert>
          )}

          {blocked ? (
            <Alert tone="danger" title="This file cannot be applied">
              Fix the problems below and check the file again. Nothing has been changed.
            </Alert>
          ) : (
            <Alert tone="info" title="Nothing has been changed yet">
              This is a preview. Applying will create {formatNumber(job.createRows)} and
              update {formatNumber(job.updateRows)}{' '}
              {job.updateRows === 1 ? config.singular : config.plural}.
            </Alert>
          )}
        </div>
      </ImportSection>

      {job.errors.length > 0 && (
        <ImportErrorTable
          errors={job.errors}
          truncated={job.errorsTruncated}
          onDownload={async () => {
            const file = await referenceImportsApi.errorsCsv(job.id);
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
