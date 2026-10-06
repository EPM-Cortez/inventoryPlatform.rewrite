import {
  KeyRound,
  LogOut,
  Menu as MenuIcon,
  Moon,
  PanelLeftClose,
  PanelLeftOpen,
  Search,
  ShieldCheck,
  Sun,
} from 'lucide-react';
import { useNavigate } from 'react-router';

import { useSession } from '@/auth/session-context';
import { describeRole } from '@/auth/permissions';
import { initials } from '@/lib/format';
import { Button, IconButton } from '@/ui/Button';
import { Menu, MenuContent, MenuItem, MenuSeparator, MenuTrigger } from '@/ui/Menu';
import { Tooltip } from '@/ui/Tooltip';

import { useTheme } from './theme';

export interface TopbarProps {
  sidebarCollapsed: boolean;
  onToggleSidebar: () => void;
  onOpenNav: () => void;
  onOpenCommandPalette: () => void;
}

export function Topbar({
  sidebarCollapsed,
  onToggleSidebar,
  onOpenNav,
  onOpenCommandPalette,
}: TopbarProps) {
  const { user, signOut } = useSession();
  const { theme, toggle } = useTheme();
  const navigate = useNavigate();

  const isMac =
    typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);

  return (
    <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center gap-2 border-b border-border bg-canvas/85 px-3 backdrop-blur-md">
      <IconButton label="Open navigation" onClick={onOpenNav} className="lg:hidden">
        <MenuIcon />
      </IconButton>

      {/* Beside the page it resizes, rather than buried under the nav list. */}
      <Tooltip
        side="bottom"
        content={
          <span className="flex items-center gap-2">
            {sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            <kbd className="rounded-xs border border-border bg-surface-sunken px-1 font-sans text-2xs text-text-subtle">
              [
            </kbd>
          </span>
        }
      >
        <IconButton
          label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          // The tooltip carries the label and the shortcut; a native title would
          // show the same text a second time.
          title={undefined}
          aria-keyshortcuts="["
          aria-expanded={!sidebarCollapsed}
          onClick={onToggleSidebar}
          className="hidden lg:inline-flex"
        >
          {sidebarCollapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
        </IconButton>
      </Tooltip>

      <Button
        onClick={onOpenCommandPalette}
        className="w-full max-w-sm justify-start gap-2 text-text-subtle font-normal"
        iconLeft={<Search />}
      >
        <span className="truncate">Search assets and pages</span>
        <kbd className="ml-auto hidden shrink-0 rounded-xs border border-border bg-surface-sunken px-1.5 py-0.5 font-sans text-2xs text-text-subtle sm:inline-block">
          {isMac ? '⌘' : 'Ctrl'} K
        </kbd>
      </Button>

      <div className="ml-auto flex items-center gap-1">
        <IconButton
          label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
          onClick={toggle}
        >
          {theme === 'dark' ? <Sun /> : <Moon />}
        </IconButton>

        <Menu>
          <MenuTrigger asChild>
            <button
              type="button"
              className="flex items-center gap-2 rounded-md p-1 pr-2 transition-colors hover:bg-surface-hover"
              aria-label="Account menu"
            >
              <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-2xs font-semibold text-accent-soft-fg">
                {initials(user?.displayName ?? '?')}
              </span>
              <span className="hidden max-w-32 truncate text-sm font-medium text-text sm:block">
                {user?.displayName}
              </span>
            </button>
          </MenuTrigger>

          <MenuContent className="min-w-56">
            <div className="px-2 py-1.5">
              <p className="truncate text-base font-medium text-text">
                {user?.displayName}
              </p>
              <p className="mt-0.5 truncate text-xs text-text-subtle">
                {user?.roles.map(describeRole).join(', ') || 'No roles assigned'}
              </p>
            </div>
            <MenuSeparator />
            <MenuItem onSelect={() => void navigate('/security')}>
              <ShieldCheck />
              Security
            </MenuItem>
            <MenuItem onSelect={() => void navigate('/change-password')}>
              <KeyRound />
              Change password
            </MenuItem>
            <MenuSeparator />
            <MenuItem destructive onSelect={() => void signOut()}>
              <LogOut />
              Sign out
            </MenuItem>
          </MenuContent>
        </Menu>
      </div>
    </header>
  );
}
