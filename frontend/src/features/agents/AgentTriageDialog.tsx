import { useState, type FormEvent } from 'react';

import { useDismissAgent, useRestoreAgentReview, useUnlinkAgent } from '@/api/hooks';
import type { ManagedAgentListItem } from '@/api/agentTypes';
import { ApiError, describeError } from '@/api/problem';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Textarea } from '@/ui/Field';
import { useToast } from '@/ui/Toast';

export type AgentTriageAction = 'unlink' | 'dismiss' | 'restore';

const COPY: Record<
  AgentTriageAction,
  {
    title: string;
    confirm: string;
    /** What actually happens, in the terms the person deciding cares about. */
    explanation: string;
    placeholder: string;
    success: string;
    failure: string;
    variant: 'primary' | 'danger';
  }
> = {
  unlink: {
    title: 'Unlink from asset',
    confirm: 'Unlink',
    explanation:
      'The asset keeps its record and its history; it simply stops being the thing this machine reports as. The machine stays enrolled, keeps checking in, and goes back into the review queue.',
    placeholder: 'Linked to the wrong asset — this is the spare, not IT-0421',
    success: 'Unlinked',
    failure: 'Could not unlink',
    variant: 'danger',
  },
  dismiss: {
    title: 'Dismiss from review',
    confirm: 'Dismiss',
    explanation:
      'Takes this machine out of the review queue without linking it to anything. It stays enrolled and keeps reporting — this only records that someone looked and decided it does not need an asset.',
    placeholder: 'Contractor laptop — not school equipment',
    success: 'Dismissed from review',
    failure: 'Could not dismiss',
    variant: 'primary',
  },
  restore: {
    title: 'Put back in review',
    confirm: 'Restore to review',
    explanation:
      'Puts this machine back in the review queue so it shows up again as needing an asset. Nothing else about it changes.',
    placeholder: 'Dismissed by mistake — this one is ours',
    success: 'Back in the review queue',
    failure: 'Could not restore it to review',
    variant: 'primary',
  },
};

export interface AgentTriageDialogProps {
  action: AgentTriageAction;
  agent: ManagedAgentListItem;
  onClose: () => void;
}

/**
 * Unlink, dismiss and restore-to-review.
 *
 * All three are the same shape — a reason and the agent's version — and all
 * three are reversible, which is what separates them from revoking. The reason
 * is required by the server and is the only record of *why* a machine is in the
 * state someone finds it in months later, so it is asked for properly rather
 * than defaulted.
 */
export function AgentTriageDialog({ action, agent, onClose }: AgentTriageDialogProps) {
  const [reason, setReason] = useState('');
  const toast = useToast();
  const copy = COPY[action];

  const unlink = useUnlinkAgent();
  const dismiss = useDismissAgent();
  const restore = useRestoreAgentReview();
  const mutation = action === 'unlink' ? unlink : action === 'dismiss' ? dismiss : restore;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = reason.trim();
    if (!trimmed) return;

    mutation.mutate(
      { agentId: agent.id, body: { reason: trimmed, version: agent.version } },
      {
        onSuccess: () => {
          toast.success(copy.success, agent.hostname);
          onClose();
        },
      },
    );
  }

  const error = mutation.error;
  const apiError = error instanceof ApiError ? error : null;
  const formId = 'agent-triage-form';

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title={copy.title}
      description={agent.hostname}
      size="sm"
      dismissible={!mutation.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button
            type="submit"
            form={formId}
            variant={copy.variant}
            loading={mutation.isPending}
            disabled={!reason.trim()}
          >
            {copy.confirm}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={onSubmit} className="flex flex-col gap-4">
        {apiError?.isConcurrencyConflict ? (
          <Alert tone="warning" title="This device just changed" role="alert">
            Nothing was changed. Close and reopen this dialog to work from the current
            state.
          </Alert>
        ) : error ? (
          <Alert tone="danger" title={copy.failure} role="alert">
            {describeError(error)}
          </Alert>
        ) : null}

        <p className="text-sm text-text-muted">{copy.explanation}</p>

        {action === 'unlink' && agent.linkedAssetTag && (
          <Alert tone="info" title={`Currently linked to ${agent.linkedAssetTag}`}>
            The asset&rsquo;s own details are untouched — only the device tab stops
            showing what this machine reports.
          </Alert>
        )}

        <Field label="Reason" required hint="Recorded in the audit trail.">
          {(controlProps) => (
            <Textarea
              {...controlProps}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              rows={3}
              required
              autoFocus
              placeholder={copy.placeholder}
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}
