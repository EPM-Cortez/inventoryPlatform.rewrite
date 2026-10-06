import { Search, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';

import { cn } from '@/lib/cn';

export interface SearchInputProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  /** Milliseconds to wait after typing stops before calling `onChange`. */
  debounceMs?: number;
  autoFocus?: boolean;
  className?: string;
  'aria-label'?: string;
}

/**
 * Debounced search box. Holds its own draft text so typing stays responsive while
 * the committed value drives the query, and stays in sync when the parent resets
 * filters from elsewhere.
 */
export function SearchInput({
  value,
  onChange,
  placeholder = 'Search…',
  debounceMs = 300,
  autoFocus,
  className,
  'aria-label': ariaLabel,
}: SearchInputProps) {
  const [draft, setDraft] = useState(value);
  const inputRef = useRef<HTMLInputElement>(null);
  const committed = useRef(value);

  // Adopt external resets ("Clear all filters") without clobbering live typing.
  useEffect(() => {
    if (value !== committed.current) {
      committed.current = value;
      setDraft(value);
    }
  }, [value]);

  useEffect(() => {
    if (draft === committed.current) return;
    const timer = window.setTimeout(() => {
      committed.current = draft;
      onChange(draft);
    }, debounceMs);
    return () => window.clearTimeout(timer);
  }, [debounceMs, draft, onChange]);

  function clear() {
    setDraft('');
    committed.current = '';
    onChange('');
    inputRef.current?.focus();
  }

  return (
    <div className={cn('relative', className)}>
      <Search
        className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-text-subtle"
        aria-hidden
      />
      <input
        ref={inputRef}
        type="search"
        value={draft}
        autoFocus={autoFocus}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape' && draft) {
            event.stopPropagation();
            clear();
          }
        }}
        placeholder={placeholder}
        aria-label={ariaLabel ?? placeholder}
        className={cn(
          'h-9 w-full rounded-md border border-border-strong bg-surface pl-8 pr-8 text-base text-text shadow-sm',
          'transition-colors placeholder:text-text-subtle hover:border-text-subtle focus:border-accent',
          // The native clear affordance duplicates ours and is unstyleable.
          '[&::-webkit-search-cancel-button]:appearance-none',
        )}
      />
      {draft && (
        <button
          type="button"
          onClick={clear}
          aria-label="Clear search"
          className="absolute right-2 top-1/2 -translate-y-1/2 rounded-sm p-1 text-text-subtle transition-colors hover:text-text"
        >
          <X className="size-3.5" />
        </button>
      )}
    </div>
  );
}
