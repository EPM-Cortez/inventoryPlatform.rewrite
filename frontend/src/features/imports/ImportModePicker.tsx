import { FilePlus2, RefreshCcw } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

export interface ImportModePickerProps<TMode extends string> {
  value: TMode;
  onChange: (mode: TMode) => void;
  /** The "create only" member of this resource's mode enum. */
  createOnlyValue: TMode;
  /** `upsertByAssetTag` for assets, `upsertByKey` for reference data. */
  upsertValue: TMode;
  /** What the duplicate is matched on, e.g. "asset tag" or "code". */
  keyNoun: string;
}

/**
 * The single most consequential choice in an import.
 *
 * Getting it wrong is not obviously recoverable — an upsert silently overwrites
 * fields on records that already exist — so both options state what happens to a
 * row whose key is already present, in plain words.
 */
export function ImportModePicker<TMode extends string>({
  value,
  onChange,
  createOnlyValue,
  upsertValue,
  keyNoun,
}: ImportModePickerProps<TMode>) {
  return (
    <div role="radiogroup" aria-label="Import mode" className="grid gap-3 sm:grid-cols-2">
      <ModeOption
        selected={value === createOnlyValue}
        onSelect={() => onChange(createOnlyValue)}
        icon={<FilePlus2 className="size-4" />}
        label="Add new records only"
        detail={`A row whose ${keyNoun} already exists is reported as a problem and the file is not applied. Safest for a first load.`}
      />
      <ModeOption
        selected={value === upsertValue}
        onSelect={() => onChange(upsertValue)}
        icon={<RefreshCcw className="size-4" />}
        label="Add new and update existing"
        detail={`A row whose ${keyNoun} already exists overwrites that record's fields with the values in the file. Use for bulk updates.`}
      />
    </div>
  );
}

function ModeOption({
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
      aria-label={label}
      onClick={onSelect}
      className={cn(
        'flex items-start gap-3 rounded-lg border p-3 text-left transition-colors',
        selected
          ? 'border-accent bg-accent-soft'
          : 'border-border bg-surface hover:border-border-strong hover:bg-surface-hover',
      )}
    >
      <span className={cn('mt-0.5 shrink-0', selected ? 'text-accent' : 'text-text-subtle')}>
        {icon}
      </span>
      <span className="min-w-0">
        <span
          className={cn(
            'block text-base font-medium',
            selected ? 'text-accent-soft-fg' : 'text-text',
          )}
        >
          {label}
        </span>
        <span className="mt-0.5 block text-xs text-text-subtle">{detail}</span>
      </span>
    </button>
  );
}
