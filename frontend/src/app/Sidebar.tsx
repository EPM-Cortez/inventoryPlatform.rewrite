import { ScanLine } from 'lucide-react';
import { NavLink, useMatch } from 'react-router';

import { useSession } from '@/auth/session-context';
import { cn } from '@/lib/cn';
import { Tooltip } from '@/ui/Tooltip';

import { NAV_SECTIONS, type NavItem } from './navigation';

const SCHOOL_NAME = import.meta.env.VITE_SCHOOL_NAME || 'Inventory';

export interface SidebarProps {
  /** Toggled from the top bar, where it sits beside the page it affects. */
  collapsed: boolean;
  /** Called after navigating, so the mobile drawer can close itself. */
  onNavigate?: () => void;
}

export function Sidebar({ collapsed, onNavigate }: SidebarProps) {
  const { can } = useSession();

  const sections = NAV_SECTIONS.map((section) => ({
    ...section,
    items: section.items.filter((item) => !item.permission || can(item.permission)),
  })).filter((section) => section.items.length > 0);

  return (
    <div className="flex h-full flex-col gap-1 border-r border-nav-border bg-nav-bg">
      <div
        className={cn(
          'flex h-14 shrink-0 items-center gap-2 px-3',
          collapsed && 'justify-center px-2',
        )}
      >
        <div className="flex size-8 shrink-0 items-center justify-center rounded-md bg-accent text-accent-fg">
          <Boxmark />
        </div>
        {!collapsed && (
          <div className="min-w-0 flex-1">
            <p className="truncate text-base font-semibold tracking-[-0.01em] text-nav-text-active">
              {SCHOOL_NAME}
            </p>
            <p className="truncate text-2xs text-nav-text">Inventory</p>
          </div>
        )}
      </div>

      <nav className="min-h-0 flex-1 overflow-y-auto px-2 pb-2 no-scrollbar">
        {sections.map((section) => (
          <div key={section.id} className="mb-4 last:mb-0">
            {section.label && !collapsed && (
              <p className="px-2 pb-1.5 pt-1 text-2xs font-semibold uppercase tracking-wider text-nav-text/70">
                {section.label}
              </p>
            )}
            {section.label && collapsed && <div className="mx-2 mb-2 h-px bg-nav-border" />}
            <ul className="flex flex-col gap-0.5">
              {section.items.map((item) => (
                <li key={item.to}>
                  <SidebarLink item={item} collapsed={collapsed} onNavigate={onNavigate} />
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="shrink-0 border-t border-nav-border p-2">
        <SidebarLink
          item={{
            label: 'Scan a label',
            to: '/scan',
            icon: ScanLine,
            description: 'Look up an asset from a QR label',
          }}
          collapsed={collapsed}
          onNavigate={onNavigate}
        />
      </div>
    </div>
  );
}

function SidebarLink({
  item,
  collapsed,
  onNavigate,
}: {
  item: NavItem;
  collapsed: boolean;
  onNavigate?: () => void;
}) {
  const Icon = item.icon;

  /*
   * Matched here rather than through `NavLink`'s `isActive` render prop.
   *
   * When the sidebar is collapsed this link is wrapped in a `Tooltip`, whose
   * Radix trigger clones it with `asChild` and merges props by *string*
   * concatenation. A function `className` survives that as `String(fn)`, so
   * every conditional class is silently dropped — which is what left the
   * collapsed icons off-centre and the active item unhighlighted.
   *
   * `NavLink` still owns `aria-current`; this only drives the styling.
   */
  // Only the dashboard needs exact matching; every other section stays lit
  // while the user is on one of its detail pages.
  const isActive = useMatch({ path: item.to, end: !item.matchPrefix }) !== null;

  const link = (
    <NavLink
      to={item.to}
      end={!item.matchPrefix}
      onClick={onNavigate}
      className={cn(
        'group flex items-center gap-2.5 rounded-md py-2 text-base',
        'transition-[color,background-color] duration-(--duration-fast) ease-out',
        collapsed ? 'justify-center px-0' : 'px-2',
        isActive
          ? // Three signals at once, so the state never rests on colour alone:
            // a filled pill, a heavier label, and the icon picking up the accent.
            'bg-nav-active font-semibold text-nav-text-active'
          : 'font-medium text-nav-text hover:bg-nav-hover hover:text-nav-text-active',
      )}
    >
      <Icon
        className={cn(
          'size-4 shrink-0 transition-transform duration-(--duration-fast)',
          isActive ? 'text-accent' : 'group-hover:scale-110',
        )}
      />
      {!collapsed && <span className="truncate">{item.label}</span>}
    </NavLink>
  );

  return collapsed ? (
    <Tooltip content={item.label} side="right">
      {link}
    </Tooltip>
  ) : (
    link
  );
}

function Boxmark() {
  return (
    <svg viewBox="0 0 24 24" className="size-4.5" fill="none" aria-hidden="true">
      <path
        d="M12 3 3.5 7.5v9L12 21l8.5-4.5v-9L12 3Z"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinejoin="round"
      />
      <path
        d="M3.5 7.5 12 12m0 0 8.5-4.5M12 12v9"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinejoin="round"
      />
    </svg>
  );
}
