import { Dialog as RadixDialog } from 'radix-ui';
import { useCallback, useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router';

import { cn } from '@/lib/cn';

import { CommandPalette } from './CommandPalette';
import { Sidebar } from './Sidebar';
import { Topbar } from './Topbar';

const COLLAPSED_KEY = 'inventory.sidebarCollapsed';

export function AppShell() {
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem(COLLAPSED_KEY) === 'true';
    } catch {
      return false;
    }
  });
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const location = useLocation();

  const toggleCollapsed = useCallback(() => {
    setCollapsed((current) => {
      const next = !current;
      try {
        localStorage.setItem(COLLAPSED_KEY, String(next));
      } catch {
        // Storage is optional; the preference just will not persist.
      }
      return next;
    });
  }, []);

  // Ctrl/Cmd-K opens the palette from anywhere. `[` toggles the sidebar, but only
  // outside form fields and open overlays, where it is an ordinary character.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        setPaletteOpen((open) => !open);
        return;
      }
      if (
        event.key === '[' &&
        !event.metaKey &&
        !event.ctrlKey &&
        !event.altKey &&
        !isTypingTarget(event.target) &&
        !document.querySelector('[role="dialog"]')
      ) {
        event.preventDefault();
        toggleCollapsed();
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [toggleCollapsed]);

  // Route changes should always land the user at the top of the new page.
  useEffect(() => {
    document.getElementById('main-content')?.scrollTo({ top: 0 });
  }, [location.pathname]);

  return (
    <div className="flex h-dvh overflow-hidden bg-canvas">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:left-3 focus:top-3 focus:z-100 focus:rounded-md focus:bg-surface-raised focus:px-3 focus:py-2 focus:text-base focus:shadow-lg"
      >
        Skip to content
      </a>

      <aside
        className={cn(
          'hidden shrink-0 transition-[width] duration-200 ease-out lg:block',
          collapsed ? 'w-16' : 'w-60',
        )}
      >
        <Sidebar collapsed={collapsed} />
      </aside>

      <RadixDialog.Root open={mobileNavOpen} onOpenChange={setMobileNavOpen}>
        <RadixDialog.Portal>
          <RadixDialog.Overlay className="fixed inset-0 z-50 bg-[#0d1117]/45 lg:hidden" />
          <RadixDialog.Content className="fixed inset-y-0 left-0 z-50 w-64 lg:hidden">
            <RadixDialog.Title className="sr-only">Navigation</RadixDialog.Title>
            <RadixDialog.Description className="sr-only">
              Move between sections of the inventory.
            </RadixDialog.Description>
            <Sidebar
              collapsed={false}
              onNavigate={() => setMobileNavOpen(false)}
            />
          </RadixDialog.Content>
        </RadixDialog.Portal>
      </RadixDialog.Root>

      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar
          sidebarCollapsed={collapsed}
          onToggleSidebar={toggleCollapsed}
          onOpenNav={() => setMobileNavOpen(true)}
          onOpenCommandPalette={() => setPaletteOpen(true)}
        />
        <main id="main-content" className="min-h-0 flex-1 overflow-y-auto">
          {/* Keyed on the path so React remounts this subtree on navigation and
              the entrance animation replays. Only the path, not the query — a
              filter change must not re-animate the page under the user while
              they are working through results. */}
          <div
            key={location.pathname}
            className="animate-view mx-auto w-full max-w-[100rem] px-4 py-5 sm:px-6"
          >
            <Outlet />
          </div>
        </main>
      </div>

      {paletteOpen && <CommandPalette onOpenChange={setPaletteOpen} />}
    </div>
  );
}

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return (
    target.isContentEditable ||
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement
  );
}
