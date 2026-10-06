import { Check, ChevronsUpDown, Search, X } from 'lucide-react';
import { Popover } from 'radix-ui';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { cn } from '@/lib/cn';
import { Spinner } from './Spinner';

export interface ComboboxOption {
  value: string;
  label: string;
  /** Secondary line, e.g. a code or department. Also searched. */
  description?: string;
}

export interface ComboboxProps {
  value: string | null;
  onChange: (value: string | null) => void;
  options: ComboboxOption[];
  placeholder?: string;
  /** Shown as the first option; picking it clears the value. */
  emptyOptionLabel?: string;
  searchPlaceholder?: string;
  loading?: boolean;
  disabled?: boolean;
  id?: string;
  /** Required when the combobox is not wrapped in a `Field`. */
  'aria-label'?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: true | undefined;
  className?: string;
  /** Called as the user types. Set when the option list is server-filtered. */
  onSearchChange?: (query: string) => void;
  /** Suppresses local filtering — use with `onSearchChange` for server search. */
  serverFiltered?: boolean;
  renderOption?: (option: ComboboxOption) => ReactNode;
  /**
   * Replaces the field-style trigger, e.g. with a toolbar filter button. Must
   * render a single `<button>`; it receives the popover's open/close wiring.
   * The built-in clear button is not drawn — the caller owns clearing.
   */
  renderTrigger?: (state: { selected: ComboboxOption | null; open: boolean }) => ReactNode;
  /** Hides the search box, for a short fixed list. Arrow keys still move. */
  searchable?: boolean;
  /** Overrides the list's width, which otherwise matches the trigger's. */
  contentClassName?: string;
}

/**
 * Searchable single-select. Use for lists that are long or unbounded (locations,
 * people, categories). For a handful of fixed choices a native `<select>` is
 * faster to operate — see `NativeSelect`.
 */
export function Combobox({
  value,
  onChange,
  options,
  placeholder = 'Select…',
  emptyOptionLabel,
  searchPlaceholder = 'Search…',
  loading = false,
  disabled = false,
  id,
  className,
  onSearchChange,
  serverFiltered = false,
  renderOption,
  renderTrigger,
  searchable = true,
  contentClassName,
  ...aria
}: ComboboxProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlighted, setHighlighted] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  const selected = options.find((option) => option.value === value) ?? null;

  const filtered = useMemo(() => {
    if (serverFiltered || !query.trim()) return options;
    const needle = query.trim().toLowerCase();
    return options.filter(
      (option) =>
        option.label.toLowerCase().includes(needle) ||
        option.description?.toLowerCase().includes(needle),
    );
  }, [options, query, serverFiltered]);

  // Clamp rather than reset in an effect: results shrink as the user types, and
  // deriving the highlight keeps it in range without an extra render pass.
  const activeIndex = Math.min(highlighted, Math.max(filtered.length - 1, 0));

  function changeOpen(next: boolean) {
    setOpen(next);
    // Reopening should start from a clean search, not the last one.
    if (!next) {
      setQuery('');
      setHighlighted(0);
    }
  }

  function commit(option: ComboboxOption | null) {
    onChange(option?.value ?? null);
    changeOpen(false);
  }

  function onKeyDown(event: React.KeyboardEvent) {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setHighlighted(Math.min(activeIndex + 1, filtered.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setHighlighted(Math.max(activeIndex - 1, 0));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const option = filtered[activeIndex];
      if (option) commit(option);
    }
  }

  useEffect(() => {
    listRef.current
      ?.querySelector('[data-active="true"]')
      ?.scrollIntoView({ block: 'nearest' });
  }, [activeIndex]);

  return (
    <Popover.Root open={open} onOpenChange={changeOpen}>
      <div className={cn('relative', className)}>
        <Popover.Trigger asChild>
          {renderTrigger ? (
            renderTrigger({ selected, open })
          ) : (
            <button
              type="button"
              id={id}
              disabled={disabled}
              role="combobox"
              aria-expanded={open}
              aria-haspopup="listbox"
              {...aria}
              className={cn(
                'flex h-9 w-full items-center justify-between gap-2 rounded-md border bg-surface',
                'px-2.5 text-left text-base shadow-sm transition-colors',
                'disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-text-subtle',
                aria['aria-invalid']
                  ? 'border-danger-border hover:border-danger'
                  : 'border-border-strong hover:border-text-subtle',
                // Leave room for the clear button so the label never sits under it.
                selected && !disabled && 'pr-14',
              )}
            >
              <span className={cn('truncate', !selected && 'text-text-subtle')}>
                {selected ? selected.label : placeholder}
              </span>
              <ChevronsUpDown className="size-4 shrink-0 text-text-subtle" />
            </button>
          )}
        </Popover.Trigger>

        {selected && !disabled && !renderTrigger && (
          <button
            type="button"
            onClick={() => onChange(null)}
            aria-label="Clear selection"
            className="absolute right-8 top-1/2 -translate-y-1/2 rounded-sm p-1 text-text-subtle transition-colors hover:text-text"
          >
            <X className="size-3.5" />
          </button>
        )}
      </div>

      <Popover.Portal>
        <Popover.Content
          align="start"
          sideOffset={4}
          className={cn(
            'z-50 w-(--radix-popover-trigger-width) overflow-hidden rounded-lg border border-border bg-surface-raised shadow-lg',
            'origin-(--radix-popover-content-transform-origin)',
            'data-[state=open]:animate-[popover-in_var(--duration-fast)_var(--ease-enter)]',
            'data-[state=closed]:animate-[popover-out_var(--duration-instant)_var(--ease-exit)]',
            contentClassName,
          )}
          onOpenAutoFocus={(event) => {
            // Focus the search box, not the first option — or, with no search
            // box, the list itself, so arrow keys work straight away.
            event.preventDefault();
            const content = event.currentTarget as HTMLElement;
            (
              content.querySelector<HTMLInputElement>('input') ??
              content.querySelector<HTMLElement>('[role="listbox"]')
            )?.focus();
          }}
          onKeyDown={onKeyDown}
        >
          {searchable && (
            <div className="flex items-center gap-2 border-b border-border px-2.5">
              <Search className="size-3.5 shrink-0 text-text-subtle" />
              <input
                value={query}
                onChange={(event) => {
                  setQuery(event.target.value);
                  onSearchChange?.(event.target.value);
                }}
                placeholder={searchPlaceholder}
                className="h-9 w-full bg-transparent text-base text-text outline-none placeholder:text-text-subtle"
                aria-label={searchPlaceholder}
              />
              {loading && <Spinner className="size-3.5 text-text-subtle" />}
            </div>
          )}

          <div
            ref={listRef}
            role="listbox"
            tabIndex={-1}
            className="max-h-64 overflow-y-auto p-1 outline-none"
          >
            {emptyOptionLabel && (
              <Option
                active={false}
                selected={value === null}
                onSelect={() => commit(null)}
                label={emptyOptionLabel}
                muted
              />
            )}

            {filtered.length === 0 && !loading && (
              <p className="px-2 py-6 text-center text-sm text-text-subtle">
                {query ? 'No matches' : 'Nothing to choose from'}
              </p>
            )}

            {filtered.map((option, index) => (
              <Option
                key={option.value}
                active={index === activeIndex}
                selected={option.value === value}
                onSelect={() => commit(option)}
                onHover={() => setHighlighted(index)}
                label={option.label}
                description={option.description}
                custom={renderOption?.(option)}
              />
            ))}
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

function Option({
  active,
  selected,
  onSelect,
  onHover,
  label,
  description,
  muted,
  custom,
}: {
  active: boolean;
  selected: boolean;
  onSelect: () => void;
  onHover?: () => void;
  label: string;
  description?: string;
  muted?: boolean;
  custom?: ReactNode;
}) {
  return (
    <button
      type="button"
      role="option"
      aria-selected={selected}
      data-active={active}
      onClick={onSelect}
      onMouseMove={onHover}
      className={cn(
        'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left transition-colors',
        active && 'bg-surface-hover',
        muted && 'text-text-subtle',
      )}
    >
      <span className="min-w-0 flex-1">
        {custom ?? (
          <>
            <span className="block truncate text-base text-current">{label}</span>
            {description && (
              <span className="block truncate text-xs text-text-subtle">{description}</span>
            )}
          </>
        )}
      </span>
      {selected && <Check className="size-4 shrink-0 text-accent" />}
    </button>
  );
}
