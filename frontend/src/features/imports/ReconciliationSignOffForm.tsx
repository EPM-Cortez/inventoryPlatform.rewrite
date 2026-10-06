import { PenLine, ThumbsDown, ThumbsUp } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';

import type {
  LegacyReconciliation,
  LegacyReconciliationDecision,
  LegacyReconciliationIssue,
} from '@/api/adminTypes';
import { useSignOffReconciliation } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import type { Guid } from '@/api/types';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { ConfirmDialog } from '@/ui/ConfirmDialog';
import { Field, Textarea } from '@/ui/Field';

export interface ReconciliationSignOffFormProps {
  importJobId: Guid;
  reportFingerprint: string;
  blockingIssues: LegacyReconciliationIssue[];
  errorRows: number;
  onSigned: (reconciliation: LegacyReconciliation) => void;
}

/**
 * Records a named, immutable decision about a dry run.
 *
 * Two server rules drive the shape of this form: approving requires a written
 * rationale for *every* blocking issue, and a run can only be signed once. Both
 * are enforced here before the request, because discovering either as a 400 or a
 * 409 after typing a long rationale is a bad way to find out.
 */
export function ReconciliationSignOffForm({
  importJobId,
  reportFingerprint,
  blockingIssues,
  errorRows,
  onSigned,
}: ReconciliationSignOffFormProps) {
  const [decision, setDecision] = useState<LegacyReconciliationDecision>('approved');
  const [notes, setNotes] = useState('');
  const [rationales, setRationales] = useState<Record<string, string>>({});
  const [confirming, setConfirming] = useState(false);

  const signOff = useSignOffReconciliation();

  const approving = decision === 'approved';
  const missingRationales = approving
    ? blockingIssues.filter((issue) => !rationales[issue.issueKey]?.trim())
    : [];
  const canSubmit =
    notes.trim().length > 0 && missingRationales.length === 0 && !signOff.isPending;

  function submit() {
    signOff.mutate(
      {
        importJobId,
        body: {
          decision,
          reportFingerprint,
          notes: notes.trim(),
          // Rejection carries no acknowledgements: there is nothing to excuse.
          exceptions: approving
            ? blockingIssues.map((issue) => ({
                issueKey: issue.issueKey,
                rationale: rationales[issue.issueKey]!.trim(),
              }))
            : [],
        },
      },
      {
        onSuccess: (result) => {
          setConfirming(false);
          onSigned(result);
        },
        onError: () => setConfirming(false),
      },
    );
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    setConfirming(true);
  }

  const error = signOff.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Card>
      <CardHeader
        title="Sign off this comparison"
        description="Records who reviewed it and what they decided. The decision cannot be edited afterwards."
      />
      <CardBody>
        <form onSubmit={onSubmit} className="flex flex-col gap-4">
          {apiError?.slug === 'reconciliation-already-signed' ? (
            <Alert tone="danger" title="This run has already been signed" role="alert">
              A sign-off is permanent. Run a new comparison to record a new decision.
            </Alert>
          ) : apiError?.slug === 'reconciliation-report-changed' ? (
            <Alert tone="danger" title="This report is no longer the current one" role="alert">
              The dry run changed after this page loaded. Run the comparison again and
              review the new result before signing.
            </Alert>
          ) : error ? (
            <Alert tone="danger" title="Could not sign off" role="alert">
              {describeError(error)}
            </Alert>
          ) : null}

          <div role="radiogroup" aria-label="Decision" className="grid gap-3 sm:grid-cols-2">
            <DecisionOption
              selected={approving}
              onSelect={() => setDecision('approved')}
              icon={<ThumbsUp className="size-4" />}
              label="Approve"
              detail="The comparison is a fair representation and the plan is acceptable."
            />
            <DecisionOption
              selected={!approving}
              onSelect={() => setDecision('rejected')}
              icon={<ThumbsDown className="size-4" />}
              label="Reject"
              detail="The file is not fit to be used. Record why, and fix it at source."
            />
          </div>

          {approving && errorRows > 0 && (
            <Alert tone="warning" title="Approving does not make this committable">
              {formatNumber(errorRows)} {errorRows === 1 ? 'row has' : 'rows have'}{' '}
              errors. Approval records your judgement of the evidence; the import itself
              will still be refused until those rows are fixed.
            </Alert>
          )}

          {approving && blockingIssues.length > 0 && (
            <div className="flex flex-col gap-3">
              <div>
                <h3 className="text-sm font-medium text-text">
                  Every blocking issue needs a reason
                </h3>
                <p className="mt-0.5 text-sm text-text-muted">
                  {formatNumber(blockingIssues.length)}{' '}
                  {blockingIssues.length === 1 ? 'issue' : 'issues'} to account for. What
                  you write is stored with the sign-off and is not editable later.
                </p>
              </div>

              {blockingIssues.map((issue) => (
                <div
                  key={issue.issueKey}
                  className="rounded-md border border-border bg-surface-sunken p-3"
                >
                  <p className="text-sm text-text">{issue.message}</p>
                  <p className="mt-0.5 font-mono text-2xs text-text-subtle">
                    row {issue.rowNumber} · {issue.column} · {issue.code}
                  </p>
                  <Field
                    label={`Reason for row ${issue.rowNumber}`}
                    hideLabel
                    required
                    className="mt-2"
                  >
                    {(controlProps) => (
                      <Textarea
                        {...controlProps}
                        value={rationales[issue.issueKey] ?? ''}
                        onChange={(event) =>
                          setRationales((current) => ({
                            ...current,
                            [issue.issueKey]: event.target.value,
                          }))
                        }
                        maxLength={1000}
                        rows={2}
                        placeholder="Why this is acceptable to sign off…"
                      />
                    )}
                  </Field>
                </div>
              ))}
            </div>
          )}

          <Field
            label="Notes"
            required
            hint="What was reviewed and why this decision was reached."
          >
            {(controlProps) => (
              <Textarea
                {...controlProps}
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
                maxLength={4000}
                rows={4}
                required
                placeholder="Checked the 12 differing rows against the old system with the office…"
              />
            )}
          </Field>

          <div className="flex items-center justify-between gap-3">
            <p className="text-xs text-text-subtle">
              {missingRationales.length > 0
                ? `${formatNumber(missingRationales.length)} blocking ${missingRationales.length === 1 ? 'issue still needs' : 'issues still need'} a reason.`
                : notes.trim()
                  ? 'Ready to sign.'
                  : 'Notes are required.'}
            </p>
            <Button
              type="submit"
              variant={approving ? 'primary' : 'danger-subtle'}
              iconLeft={<PenLine />}
              disabled={!canSubmit}
            >
              {approving ? 'Approve and sign' : 'Reject and sign'}
            </Button>
          </div>
        </form>
      </CardBody>

      {confirming && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setConfirming(false)}
          title={approving ? 'Sign this off as approved?' : 'Sign this off as rejected?'}
          description={
            <>
              Your name and the time are recorded against this decision, and it{' '}
              <strong>cannot be changed or removed afterwards</strong>. Recording a
              different decision means running a new comparison.
            </>
          }
          confirmLabel={approving ? 'Approve and sign' : 'Reject and sign'}
          destructive={!approving}
          loading={signOff.isPending}
          onConfirm={submit}
        />
      )}
    </Card>
  );
}

function DecisionOption({
  selected,
  onSelect,
  icon,
  label,
  detail,
}: {
  selected: boolean;
  onSelect: () => void;
  icon: ReactNode;
  label: string;
  detail: string;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      onClick={onSelect}
      className={cn(
        'flex flex-col gap-1 rounded-lg border p-3 text-left transition-colors',
        selected
          ? 'border-accent bg-accent-soft/40'
          : 'border-border hover:border-border-strong hover:bg-surface-hover',
      )}
    >
      <span className="flex items-center gap-2 text-base font-medium text-text">
        {icon}
        {label}
      </span>
      <span className="text-sm text-text-muted">{detail}</span>
    </button>
  );
}
