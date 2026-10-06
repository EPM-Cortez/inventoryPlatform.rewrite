import { describe, expect, it } from 'vitest';

import {
  describeAuditAction,
  describeOutcome,
  describeTargetType,
  groupAuditActions,
  isKnownAuditAction,
  outcomeOptions,
  outcomeTone,
  targetTypeOptions,
} from './auditActions';

describe('describeAuditAction', () => {
  it('labels an action it knows', () => {
    expect(describeAuditAction('user.password.reset')).toBe('Password reset by admin');
  });

  it('falls back to the raw value so an entry is never rendered blank', () => {
    expect(describeAuditAction('something.new.the.server.added')).toBe(
      'something.new.the.server.added',
    );
    expect(isKnownAuditAction('something.new.the.server.added')).toBe(false);
  });

  // The API canonicalised these but still filters on the old spellings, so an
  // old bookmarked link must not degrade into raw identifiers.
  it.each([
    ['stocktake.asset-observed', 'stocktake.asset.observed', 'Asset seen'],
    ['saved-search.create', 'saved_search.created', 'Search saved'],
    ['leaver.complete', 'leaver.completed', 'Leaver case completed'],
    ['attachment.upload', 'attachment.uploaded', 'File attached'],
    [
      'assignment.expected-return.update',
      'assignment.expected_return.updated',
      'Return date changed',
    ],
  ])('labels %s and %s the same', (legacy, canonical, label) => {
    expect(describeAuditAction(canonical)).toBe(label);
    expect(describeAuditAction(legacy)).toBe(label);
  });
});

describe('outcomeTone', () => {
  // `succeeded` is canonical now, but rows written before the change still say
  // `success` and are immutable, so both must read as a success forever.
  it.each(['succeeded', 'success'])('treats %s as a success', (outcome) => {
    expect(outcomeTone(outcome)).toBe('success');
    expect(describeOutcome(outcome)).toBe('Succeeded');
  });

  it('separates a hard failure from a rejection', () => {
    expect(outcomeTone('failed')).toBe('danger');
    expect(outcomeTone('rejected')).toBe('warning');
    expect(outcomeTone('validation_failed')).toBe('warning');
  });

  it('does not colour an outcome it has never seen', () => {
    expect(outcomeTone('partially_applied')).toBe('neutral');
    expect(describeOutcome('partially_applied')).toBe('partially_applied');
  });
});

describe('groupAuditActions', () => {
  it('buckets the server list by the longest matching prefix', () => {
    const groups = groupAuditActions([
      'user.created',
      'user.mfa.reset',
      'authentication.login',
    ]);
    const byLabel = Object.fromEntries(
      groups.map((g) => [g.label, g.actions.map((a) => a.value)]),
    );
    // `user.mfa.` must win over `user.`, or two-step events hide among accounts.
    expect(byLabel['Two-step verification']).toEqual(['user.mfa.reset']);
    expect(byLabel['Accounts']).toEqual(['user.created']);
    expect(byLabel['Sign-in']).toEqual(['authentication.login']);
  });

  it('keeps an unrecognised action selectable rather than dropping it', () => {
    const groups = groupAuditActions(['purchase_order.raised']);
    expect(groups).toHaveLength(1);
    expect(groups[0]?.label).toBe('Other');
    // Unlabelled, so it shows as the raw value — but it is still filterable.
    expect(groups[0]?.actions[0]).toEqual({
      value: 'purchase_order.raised',
      label: 'purchase_order.raised',
    });
  });

  it('puts Other last however the server ordered its list', () => {
    const groups = groupAuditActions(['zz.unknown', 'authentication.login']);
    expect(groups.at(-1)?.label).toBe('Other');
  });

  it('returns nothing for an empty vocabulary rather than inventing options', () => {
    expect(groupAuditActions([])).toEqual([]);
  });

  it('lists no action twice', () => {
    const values = groupAuditActions([
      'user.created',
      'user.mfa.reset',
      'agent.enrolled',
    ]).flatMap((g) => g.actions.map((a) => a.value));
    expect(new Set(values).size).toBe(values.length);
  });
});

describe('describeTargetType', () => {
  it('reads a stored type in the words the UI uses elsewhere', () => {
    expect(describeTargetType('user')).toBe('Account');
    expect(describeTargetType('managed_agent')).toBe('Device agent');
  });

  it('labels the canonical and the old spelling the same', () => {
    expect(describeTargetType('asset_import_job')).toBe('Asset import');
    expect(describeTargetType('assetImportJob')).toBe('Asset import');
    expect(describeTargetType('asset_label_batch')).toBe('Label batch');
    expect(describeTargetType('asset-label-batch')).toBe('Label batch');
  });

  it('passes an unknown type through', () => {
    expect(describeTargetType('purchase_order')).toBe('purchase_order');
  });
});

describe('option builders', () => {
  it('sorts target types by their label, not their stored value', () => {
    const options = targetTypeOptions(['user', 'asset', 'stocktake_session']);
    expect(options.map((o) => o.label)).toEqual(['Account', 'Asset', 'Stocktake']);
  });

  it('offers only the outcomes the server reports', () => {
    const options = outcomeOptions(['succeeded', 'failed']);
    expect(options).toEqual([
      { value: 'succeeded', label: 'Succeeded' },
      { value: 'failed', label: 'Failed' },
    ]);
  });
});
