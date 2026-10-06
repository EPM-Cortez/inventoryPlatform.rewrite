import { ChevronDown, X } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';
import { Combobox, type ComboboxOption } from './Combobox';

export interface FilterButtonProps {
  /** The dimension, e.g. "Status". Shown on the button and in its a11y name. */
  label: string;
  icon?: ReactNode;
  value: string | null;
  onChange: (value: string | null) => void;
  options: ComboboxOption[];
  /** Offer a search box. Worth it once a list runs past a dozen or so. */
  searchable?: boolean;
  loading?: boolean;
  /** The "no filter" choice. Defaults to "Any <label>". */
  emptyLabel?: string;
}

/**
 * One filter in a toolbar row. Unset, it is a quiet dashed outline naming the
 * dimension; set, it fills and shows the chosen value, with its own clear button
 * beside it — so the toolbar itself is the record of what is filtered, and no
 * separate row of "active filter" chips is needed.
 */
export function FilterButton({
  label,
  icon,
  value,
  onChange,
  options,
  searchable = false,
  loading,
  emptyLabel,
}: FilterButtonProps) {
  const active = value !== null;
  const selectedLabel = options.find((option) => option.value === value)?.label;

  return (
    <div
      className={cn(
        'inline-flex h-8 shrink-0 items-stretch overflow-hidden rounded-md border text-sm',
        'transition-colors duration-(--duration-fast) ease-out',
        active
          ? 'border-accent/40 bg-accent-soft text-accent-soft-fg'
          : 'border-dashed border-border-strong bg-surface text-text-muted hover:border-text-subtle hover:text-text',
      )}
    >
      <Combobox
        value={value}
        onChange={onChange}
        options={options}
        searchable={searchable}
        loading={loading}
        emptyOptionLabel={emptyLabel ?? `Any ${label.toLowerCase()}`}
        searchPlaceholder={`Search ${label.toLowerCase()}…`}
        contentClassName="w-auto min-w-56 max-w-80"
        className="flex"
        renderTrigger={({ open }) => (
          <button
            type="button"
            role="combobox"
            aria-haspopup="listbox"
            aria-expanded={open}
            aria-label={
              active ? `${label}: ${selectedLabel ?? 'selected'}. Change filter` : `Filter by ${label.toLowerCase()}`
            }
            className="inline-flex items-center gap-1.5 px-2.5 font-medium outline-none focus-visible:bg-surface-hover [&_svg]:size-3.5 [&_svg]:shrink-0"
          >
            {icon}
            <span>{label}</span>
            {active && (
              <>
                <span className="h-3.5 w-px bg-current opacity-25" aria-hidden="true" />
                <span className="max-w-40 truncate font-semibold">{selectedLabel}</span>
              </>
            )}
            <ChevronDown className="opacity-60" aria-hidden />
          </button>
        )}
      />
      {active && (
        <button
          type="button"
          onClick={() => onChange(null)}
          aria-label={`Clear ${label.toLowerCase()} filter`}
          className="flex items-center border-l border-accent/25 px-1.5 transition-colors hover:bg-accent/10"
        >
          <X className="size-3.5" aria-hidden />
        </button>
      )}
    </div>
  );
}
