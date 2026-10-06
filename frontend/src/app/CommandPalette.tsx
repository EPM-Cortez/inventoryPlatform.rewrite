import { ArrowRight, CornerDownLeft, Search, Tag } from 'lucide-react';
import { Dialog as RadixDialog } from 'radix-ui';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';

import { useAssets } from '@/api/hooks';
import { Permission } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { Spinner } from '@/ui/Spinner';

import { ALL_NAV_ITEMS } from './navigation';

export interface CommandPaletteProps {
  onOpenChange: (open: boolean) => void;
}

/**
 * Ctrl/Cmd-K launcher. Two kinds of result: pages the user can reach, and live
 * asset matches from the register. Asset search only runs once the query is long
 * enough to be meaningful, so opening the palette does not hit the API.
 *
 * Mounted only while open, so every launch starts from an empty box without any
 * reset bookkeeping.
 */
export function CommandPalette({ onOpenChange }: CommandPaletteProps) {
  const [query, setQuery] = useState('');
  const [highlighted, setHighlighted] = useState(0);
  const navigate = useNavigate();
  const { can } = useSession();

  const trimmed = query.trim();
  const canSearchAssets = can(Permission.AssetsRead) && trimmed.length >= 2;

  const { data: assetResults, isFetching } = useAssets(
    { q: trimmed, page: 1, pageSize: 6, sort: 'assetTag' },
    canSearchAssets,
  );

  const pages = useMemo(() => {
    const needle = trimmed.toLowerCase();
    return ALL_NAV_ITEMS.filter((item) => !item.permission || can(item.permission)).filter(
      (item) =>
        !needle ||
        item.label.toLowerCase().includes(needle) ||
        item.description.toLowerCase().includes(needle),
    );
  }, [can, trimmed]);

  const assets = assetResults?.items ?? [];
  const results = useMemo(
    () => [
      ...pages.map((page) => ({ kind: 'page' as const, item: page })),
      ...assets.map((asset) => ({ kind: 'asset' as const, item: asset })),
    ],
    [assets, pages],
  );

  // Derived rather than reset in an effect: the result count changes on every
  // keystroke, and clamping keeps the highlight valid without an extra render.
  const activeIndex = Math.min(highlighted, Math.max(results.length - 1, 0));

  function activate(index: number) {
    const result = results[index];
    if (!result) return;
    onOpenChange(false);
    if (result.kind === 'page') {
      void navigate(result.item.to);
    } else {
      void navigate(`/assets/${result.item.id}`);
    }
  }

  return (
    <RadixDialog.Root open onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-50 bg-[#0d1117]/45 backdrop-blur-[2px] data-[state=open]:animate-[overlay-in_120ms_ease-out]" />
        <RadixDialog.Content
          className="fixed left-1/2 top-[12vh] z-50 w-[calc(100vw-2rem)] max-w-xl -translate-x-1/2 overflow-hidden rounded-xl border border-border bg-surface-raised shadow-overlay data-[state=open]:animate-[dialog-in_140ms_var(--ease-out-quart)]"
          aria-label="Command palette"
        >
          <RadixDialog.Title className="sr-only">Search and navigate</RadixDialog.Title>
          <RadixDialog.Description className="sr-only">
            Jump to a page or find an asset by tag, serial or model.
          </RadixDialog.Description>

          <div className="flex items-center gap-2.5 border-b border-border px-3.5">
            <Search className="size-4 shrink-0 text-text-subtle" />
            <input
              autoFocus
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'ArrowDown') {
                  event.preventDefault();
                  setHighlighted(Math.min(activeIndex + 1, results.length - 1));
                } else if (event.key === 'ArrowUp') {
                  event.preventDefault();
                  setHighlighted(Math.max(activeIndex - 1, 0));
                } else if (event.key === 'Enter') {
                  event.preventDefault();
                  activate(activeIndex);
                }
              }}
              placeholder="Search pages, or find an asset by tag, serial or model…"
              className="h-12 w-full bg-transparent text-lg text-text outline-none placeholder:text-text-subtle"
            />
            {isFetching && <Spinner className="size-4 text-text-subtle" />}
          </div>

          <div className="max-h-[min(24rem,60vh)] overflow-y-auto p-1.5">
            {results.length === 0 && (
              <p className="px-3 py-8 text-center text-sm text-text-subtle">
                {trimmed.length === 1
                  ? 'Keep typing to search assets…'
                  : 'Nothing matched.'}
              </p>
            )}

            {pages.length > 0 && <GroupLabel>Go to</GroupLabel>}
            {pages.map((page, index) => (
              <Row
                key={page.to}
                active={index === activeIndex}
                onSelect={() => activate(index)}
                onHover={() => setHighlighted(index)}
                icon={<page.icon className="size-4" />}
                title={page.label}
                subtitle={page.description}
              />
            ))}

            {assets.length > 0 && <GroupLabel>Assets</GroupLabel>}
            {assets.map((asset, index) => {
              const resultIndex = pages.length + index;
              return (
                <Row
                  key={asset.id}
                  active={resultIndex === activeIndex}
                  onSelect={() => activate(resultIndex)}
                  onHover={() => setHighlighted(resultIndex)}
                  icon={<Tag className="size-4" />}
                  title={asset.assetTag}
                  mono
                  subtitle={
                    [asset.manufacturer, asset.model].filter(Boolean).join(' ') ||
                    asset.category.name
                  }
                />
              );
            })}
          </div>

          <div className="flex items-center gap-4 border-t border-border bg-surface-sunken/60 px-3.5 py-2 text-2xs text-text-subtle">
            <span className="flex items-center gap-1">
              <Kbd>↑</Kbd>
              <Kbd>↓</Kbd> navigate
            </span>
            <span className="flex items-center gap-1">
              <Kbd>
                <CornerDownLeft className="size-2.5" />
              </Kbd>
              open
            </span>
            <span className="flex items-center gap-1">
              <Kbd>Esc</Kbd> close
            </span>
          </div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}

function GroupLabel({ children }: { children: React.ReactNode }) {
  return (
    <p className="px-2.5 pb-1 pt-2 text-2xs font-semibold uppercase tracking-wider text-text-subtle">
      {children}
    </p>
  );
}

function Row({
  active,
  onSelect,
  onHover,
  icon,
  title,
  subtitle,
  mono,
}: {
  active: boolean;
  onSelect: () => void;
  onHover: () => void;
  icon: React.ReactNode;
  title: string;
  subtitle?: string;
  mono?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      onMouseMove={onHover}
      className={cn(
        'flex w-full items-center gap-3 rounded-md px-2.5 py-2 text-left transition-colors',
        active && 'bg-surface-hover',
      )}
    >
      <span className="shrink-0 text-text-subtle">{icon}</span>
      <span className="min-w-0 flex-1">
        <span
          className={cn(
            'block truncate text-base text-text',
            mono && 'font-mono text-sm tracking-tight',
          )}
        >
          {title}
        </span>
        {subtitle && (
          <span className="block truncate text-xs text-text-subtle">{subtitle}</span>
        )}
      </span>
      {active && <ArrowRight className="size-3.5 shrink-0 text-text-subtle" />}
    </button>
  );
}

function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <kbd className="inline-flex h-4 min-w-4 items-center justify-center rounded-xs border border-border bg-surface px-1 font-sans text-2xs text-text-muted">
      {children}
    </kbd>
  );
}
