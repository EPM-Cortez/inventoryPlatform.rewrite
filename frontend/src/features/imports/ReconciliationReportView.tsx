import { CircleCheck, CircleX, Fingerprint, ShieldCheck, TriangleAlert } from 'lucide-react';
import { useState } from 'react';

import type {
  LegacyReconciliation,
  LegacyReconciliationIssue,
  LegacyReconciliationSample,
} from '@/api/adminTypes';
import type { AssetImportStatus } from '@/api/types';
import { formatDateTime, formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { CopyButton } from '@/ui/CopyButton';
import { DataTable, type Column } from '@/ui/DataTable';

import { ImportStat } from './ImportShared';
import { ReconciliationSignOffForm } from './ReconciliationSignOffForm';

export interface ReconciliationReportViewProps {
  reconciliation: LegacyReconciliation;
  onUpdate: (reconciliation: LegacyReconciliation) => void;
  onRestart: () => void;
}

export function ReconciliationReportView({
  reconciliation,
  onUpdate,
  onRestart,
}: ReconciliationReportViewProps) {
  const { report, signOff, cutoverReady } = reconciliation;
  const blocking = report.issues.filter((issue) => issue.severity === 'blocking');

  return (
    <div className="flex flex-col gap-5">
      <ReadinessBanner
        cutoverReady={cutoverReady}
        signedOff={signOff !== null}
        errorRows={report.counts.errorRows}
        importStatus={report.importStatus}
      />

      <Card>
        <CardHeader
          title="What the file contains"
          description={`${report.sourceFileName} · ${formatDateTime(report.createdAtUtc)}`}
          actions={<Badge tone="neutral">{report.mode}</Badge>}
        />
        <CardBody className="flex flex-col gap-4">
          <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-5">
            <ImportStat label="Rows read" value={report.counts.sourceRows} />
            <ImportStat
              label="Usable"
              value={report.counts.validRows}
              tone={report.counts.validRows > 0 ? 'success' : undefined}
            />
            <ImportStat
              label="With errors"
              value={report.counts.errorRows}
              tone={report.counts.errorRows > 0 ? 'danger' : undefined}
            />
            <ImportStat
              label="Would create"
              value={report.counts.plannedCreates}
              tone="info"
            />
            <ImportStat
              label="Would update"
              value={report.counts.plannedUpdates}
              tone={report.counts.plannedUpdates > 0 ? 'warning' : undefined}
            />
          </div>

          <div className="rounded-md border border-border bg-surface-sunken p-3 text-sm">
            <p className="text-text-muted">
              The register holds{' '}
              <strong className="text-text">
                {formatNumber(report.counts.targetAssetsBefore)}
              </strong>{' '}
              assets now, and would hold{' '}
              <strong className="text-text">
                {formatNumber(report.counts.expectedTargetAssetsAfter)}
              </strong>{' '}
              after this file is committed.
            </p>
          </div>
        </CardBody>
      </Card>

      <MappingsCard reconciliation={reconciliation} />

      {report.issues.length > 0 && (
        <IssuesCard
          issues={report.issues}
          duplicateCount={report.duplicateIssueCount}
          unmappedReferenceCount={report.unmappedReferenceIssueCount}
        />
      )}

      {report.samples.length > 0 && (
        <SamplesCard
          samples={report.samples}
          truncated={report.samplesTruncated}
        />
      )}

      <Card>
        <CardHeader
          title="Report fingerprint"
          description="Sign-off quotes this, so a report that changed after you read it cannot be signed by mistake."
        />
        <CardBody className="flex items-center gap-2">
          <Fingerprint className="size-4 shrink-0 text-text-subtle" aria-hidden />
          <code className="min-w-0 flex-1 truncate font-mono text-xs text-text-muted">
            {report.reportFingerprint}
          </code>
          <CopyButton value={report.reportFingerprint} label="Copy report fingerprint" />
        </CardBody>
      </Card>

      {signOff ? (
        <SignedOffCard reconciliation={reconciliation} onRestart={onRestart} />
      ) : (
        <ReconciliationSignOffForm
          importJobId={report.importJobId}
          reportFingerprint={report.reportFingerprint}
          blockingIssues={blocking}
          errorRows={report.counts.errorRows}
          onSigned={onUpdate}
        />
      )}
    </div>
  );
}

/**
 * The headline answer.
 *
 * Readiness is the server's own three-part test — approved, ready to commit, no
 * error rows — so the banner states which part failed rather than a bare "not
 * ready".
 */
function ReadinessBanner({
  cutoverReady,
  signedOff,
  errorRows,
  importStatus,
}: {
  cutoverReady: boolean;
  signedOff: boolean;
  errorRows: number;
  importStatus: AssetImportStatus;
}) {
  if (cutoverReady) {
    return (
      <Alert tone="success" title="This file is ready to be committed">
        It has been approved, has no error rows, and the import job is in a committable
        state. Commit it from the asset import wizard.
      </Alert>
    );
  }

  const reasons: string[] = [];
  if (errorRows > 0) {
    reasons.push(
      `${formatNumber(errorRows)} ${errorRows === 1 ? 'row has' : 'rows have'} errors`,
    );
  }
  if (importStatus !== 'readyToCommit') {
    reasons.push(
      importStatus === 'validationFailed'
        ? 'the import job failed validation'
        : 'the import job has already been committed',
    );
  }
  if (!signedOff) reasons.push('nobody has signed it off');

  return (
    <Alert tone="warning" title="Not ready to be committed">
      {reasons.length > 0 ? (
        <>
          Because {reasons.join(', and ')}.
          {errorRows > 0 && (
            <p className="mt-1.5">
              Signing off acknowledges the problems for the record. It does{' '}
              <strong>not</strong> make a file with errors committable — fix the source
              rows and run another dry run.
            </p>
          )}
        </>
      ) : (
        'The server has not marked this run as ready.'
      )}
    </Alert>
  );
}

function MappingsCard({ reconciliation }: { reconciliation: LegacyReconciliation }) {
  const { mappings } = reconciliation.report;
  const entries = [
    {
      label: 'Asset tags generated',
      value: mappings.generatedAssetTags,
      hint: 'Rows with no tag of their own would get one made for them.',
    },
    {
      label: 'Categories created',
      value: mappings.plannedCategoryCreates,
      hint: 'Names in the file that do not exist here yet.',
    },
    {
      label: 'Locations created',
      value: mappings.plannedLocationCreates,
      hint: 'Names in the file that do not exist here yet.',
    },
    {
      label: 'Dates inferred',
      value: mappings.inferredDateRows,
      hint: 'Rows where a date was derived rather than read.',
    },
    {
      label: 'People fuzzy matched',
      value: mappings.fuzzyPersonMatches,
      hint: 'Near-miss names matched to a person on the register.',
    },
    {
      label: 'Imported unassigned',
      value: mappings.unassignedImports,
      hint: 'Rows whose holder could not be resolved; they arrive in stock.',
    },
  ].filter((entry) => entry.value > 0);

  if (entries.length === 0) return null;

  return (
    <Card>
      <CardHeader
        title="Decisions the import would make for you"
        description="Everything here is an inference, not a value read straight from the file."
      />
      <CardBody className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {entries.map((entry) => (
          <div
            key={entry.label}
            className="rounded-md border border-border bg-surface-sunken p-3"
          >
            <p className="text-xs font-medium text-warning">{entry.label}</p>
            <p className="mt-0.5 text-2xl font-semibold tabular-nums text-text">
              {formatNumber(entry.value)}
            </p>
            <p className="mt-1 text-xs text-text-subtle">{entry.hint}</p>
          </div>
        ))}
      </CardBody>
    </Card>
  );
}

function IssuesCard({
  issues,
  duplicateCount,
  unmappedReferenceCount,
}: {
  issues: LegacyReconciliationIssue[];
  duplicateCount: number;
  unmappedReferenceCount: number;
}) {
  const [showBlockingOnly, setShowBlockingOnly] = useState(false);
  const blocking = issues.filter((issue) => issue.severity === 'blocking');
  const rows = showBlockingOnly ? blocking : issues;

  const columns: Column<LegacyReconciliationIssue>[] = [
    {
      id: 'row',
      header: 'Row',
      width: 'w-20',
      align: 'right',
      cell: (issue) => (
        <span className="font-mono text-sm text-text-muted">{issue.rowNumber}</span>
      ),
    },
    {
      id: 'severity',
      header: 'Severity',
      width: 'w-28',
      cell: (issue) =>
        issue.severity === 'blocking' ? (
          <Badge tone="danger" dot>
            Blocking
          </Badge>
        ) : (
          <Badge tone="warning" dot>
            {issue.severity}
          </Badge>
        ),
    },
    {
      id: 'column',
      header: 'Column',
      width: 'w-40',
      cell: (issue) => (
        <span className="truncate font-mono text-xs text-text-muted">{issue.column}</span>
      ),
    },
    {
      id: 'message',
      header: 'Problem',
      cell: (issue) => (
        <div className="min-w-0">
          <p className="text-text">{issue.message}</p>
          <p className="truncate font-mono text-2xs text-text-subtle">
            {issue.code}
            {issue.classification && ` · ${issue.classification}`}
            {issue.value !== null && ` · saw "${issue.value}"`}
          </p>
        </div>
      ),
    },
  ];

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={`${formatNumber(issues.length)} ${issues.length === 1 ? 'issue' : 'issues'} found`}
        description={[
          duplicateCount > 0 && `${formatNumber(duplicateCount)} duplicate`,
          unmappedReferenceCount > 0 &&
            `${formatNumber(unmappedReferenceCount)} unmapped reference`,
        ]
          .filter(Boolean)
          .join(' · ')}
        actions={
          blocking.length > 0 && (
            <button
              type="button"
              onClick={() => setShowBlockingOnly((current) => !current)}
              className="rounded-xs text-sm font-medium text-accent hover:underline"
            >
              {showBlockingOnly
                ? `Show all ${formatNumber(issues.length)}`
                : `Show ${formatNumber(blocking.length)} blocking only`}
            </button>
          )
        }
      />
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(issue) => issue.issueKey}
        rowClassName={(issue) =>
          issue.severity === 'blocking' ? 'bg-danger-soft/20' : undefined
        }
      />
    </Card>
  );
}

function SamplesCard({
  samples,
  truncated,
}: {
  samples: LegacyReconciliationSample[];
  truncated: boolean;
}) {
  return (
    <Card>
      <CardHeader
        title="Row-by-row comparison"
        description={
          truncated
            ? 'A sample of rows. Fields that differ are highlighted.'
            : 'Fields that differ from what is on the register are highlighted.'
        }
      />
      <CardBody className="flex flex-col gap-3">
        {samples.map((sample) => (
          <div
            key={`${sample.rowNumber}-${sample.assetTag}`}
            className="rounded-md border border-border"
          >
            <div className="flex flex-wrap items-center gap-2 border-b border-border bg-surface-sunken px-3 py-2">
              <span className="font-mono text-xs text-text-subtle">
                row {sample.rowNumber}
              </span>
              <span className="font-mono text-sm font-medium text-text">
                {sample.assetTag}
              </span>
              <Badge tone={sample.action === 'create' ? 'info' : 'warning'}>
                {sample.action === 'create' ? 'Would create' : 'Would update'}
              </Badge>
              {!sample.targetExists && <Badge tone="neutral">Not on register</Badge>}
              <span className="ml-auto text-xs text-text-subtle">{sample.outcome}</span>
            </div>
            <div className="divide-y divide-border">
              {sample.comparisons.map((comparison) => (
                <div
                  key={comparison.field}
                  className="grid grid-cols-[8rem_1fr_1fr] items-start gap-2 px-3 py-1.5 text-xs"
                >
                  <span className="truncate font-medium text-text-subtle">
                    {comparison.field}
                  </span>
                  <span
                    className={
                      comparison.matches
                        ? 'truncate text-text-muted'
                        : 'truncate font-medium text-text'
                    }
                  >
                    {comparison.source ?? <span className="text-text-subtle">—</span>}
                  </span>
                  <span className="flex min-w-0 items-center gap-1.5">
                    {comparison.matches ? (
                      <CircleCheck className="size-3 shrink-0 text-success" aria-label="matches" />
                    ) : (
                      <CircleX className="size-3 shrink-0 text-warning" aria-label="differs" />
                    )}
                    <span className="truncate text-text-muted">
                      {comparison.target ?? <span className="text-text-subtle">—</span>}
                    </span>
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </CardBody>
    </Card>
  );
}

function SignedOffCard({
  reconciliation,
  onRestart,
}: {
  reconciliation: LegacyReconciliation;
  onRestart: () => void;
}) {
  const signOff = reconciliation.signOff!;
  const approved = signOff.decision === 'approved';

  return (
    <Card
      className={approved ? 'border-success-border' : 'border-danger-border'}
    >
      <CardHeader
        title="Signed off"
        description="This decision is immutable. A new decision needs a new dry run."
        actions={
          <Badge tone={approved ? 'success' : 'danger'} dot>
            {approved ? 'Approved' : 'Rejected'}
          </Badge>
        }
      />
      <CardBody className="flex flex-col gap-3">
        <p className="flex items-center gap-2 text-base text-text">
          {approved ? (
            <ShieldCheck className="size-4 text-success" aria-hidden />
          ) : (
            <TriangleAlert className="size-4 text-danger" aria-hidden />
          )}
          {signOff.signedByDisplayName} · {formatDateTime(signOff.signedAtUtc)}
        </p>

        {signOff.notes && (
          <div>
            <p className="text-xs text-text-subtle">Notes</p>
            <p className="mt-0.5 whitespace-pre-wrap text-sm text-text">
              {signOff.notes}
            </p>
          </div>
        )}

        {signOff.signedExceptions.length > 0 && (
          <div>
            <p className="text-xs text-text-subtle">
              {formatNumber(signOff.signedExceptions.length)} acknowledged{' '}
              {signOff.signedExceptions.length === 1 ? 'exception' : 'exceptions'}
            </p>
            <ul className="mt-1 flex flex-col gap-1.5">
              {signOff.signedExceptions.map((exception) => (
                <li
                  key={exception.issueKey}
                  className="rounded-md border border-border bg-surface-sunken p-2.5 text-sm"
                >
                  <p className="font-mono text-2xs text-text-subtle">
                    row {exception.rowNumber} · {exception.column} · {exception.code}
                  </p>
                  <p className="mt-0.5 text-text">{exception.rationale}</p>
                </li>
              ))}
            </ul>
          </div>
        )}

        <button
          type="button"
          onClick={onRestart}
          className="self-start rounded-xs text-sm font-medium text-accent hover:underline"
        >
          Run another comparison
        </button>
      </CardBody>
    </Card>
  );
}
