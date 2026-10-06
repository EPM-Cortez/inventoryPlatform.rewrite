import {
  Activity,
  Boxes,
  Building2,
  CalendarClock,
  ClipboardList,
  FileCheck2,
  FolderTree,
  KeyRound,
  LayoutDashboard,
  MapPin,
  MonitorSmartphone,
  Package,
  ScrollText,
  Settings,
  ShieldCheck,
  Tags,
  UserCog,
  UserMinus,
  UsersRound,
  type LucideIcon,
} from 'lucide-react';

import { Permission, type PermissionKey } from '@/auth/permissions';

export interface NavItem {
  label: string;
  to: string;
  icon: LucideIcon;
  /** Hidden unless the signed-in user holds this permission. */
  permission?: PermissionKey;
  /** Matches child routes too, e.g. `/assets/:id`. */
  matchPrefix?: boolean;
  description: string;
}

export interface NavSection {
  id: string;
  label: string | null;
  items: NavItem[];
}

/**
 * The whole navigable surface of the app, in one list.
 *
 * Every entry maps to an endpoint the backend actually implements. When a
 * later milestone lands (import/export, audit, settings), add the route here
 * with its permission rather than shipping a disabled placeholder.
 */
export const NAV_SECTIONS: NavSection[] = [
  {
    id: 'overview',
    label: null,
    items: [
      {
        label: 'Dashboard',
        to: '/',
        icon: LayoutDashboard,
        description: 'Register overview and things needing attention',
      },
      {
        label: 'Assets',
        to: '/assets',
        icon: Boxes,
        permission: Permission.AssetsRead,
        matchPrefix: true,
        description: 'Search, filter and maintain the asset register',
      },
      {
        label: 'Devices',
        to: '/devices',
        icon: MonitorSmartphone,
        permission: Permission.AgentsRead,
        matchPrefix: true,
        description: 'Machines reporting in from the Windows agent',
      },
      {
        label: 'Enrollment tokens',
        to: '/enrollment-tokens',
        icon: KeyRound,
        permission: Permission.AgentsManage,
        description: 'Tokens a new machine presents to register itself',
      },
      {
        label: 'Bundles',
        to: '/bundles',
        icon: Package,
        permission: Permission.BundlesRead,
        description: 'Kit that goes out together as one',
      },
      {
        label: 'Print labels',
        to: '/labels',
        icon: Tags,
        permission: Permission.QrPrint,
        description: 'Generate QR label PDFs for a roll or an Avery sheet',
      },
    ],
  },
  {
    id: 'custody',
    label: 'Custody',
    items: [
      {
        label: 'People',
        to: '/people',
        icon: UsersRound,
        permission: Permission.PeopleRead,
        matchPrefix: true,
        description: 'Staff, pupils and contractors who hold assets',
      },
      {
        label: 'Leavers',
        to: '/leavers',
        icon: UserMinus,
        permission: Permission.LeaversRead,
        matchPrefix: true,
        description: 'People going, and the kit still to come back',
      },
      {
        label: 'Departments',
        to: '/departments',
        icon: Building2,
        permission: Permission.PeopleRead,
        matchPrefix: true,
        description: 'Departments that hold assets collectively',
      },
    ],
  },
  {
    id: 'coverage',
    label: 'Coverage',
    items: [
      {
        label: 'Stocktakes',
        to: '/stocktakes',
        icon: ClipboardList,
        permission: Permission.StocktakesRead,
        matchPrefix: true,
        description: 'Check what is really there against what the register says',
      },
      {
        label: 'Overdue loans',
        to: '/loans/overdue',
        icon: CalendarClock,
        permission: Permission.AssignmentsRead,
        description: 'Equipment that was due back and has not come in',
      },
      {
        label: 'Warranties',
        to: '/warranties',
        icon: ShieldCheck,
        permission: Permission.WarrantiesRead,
        description: 'Coverage that has expired or is expiring soon',
      },
    ],
  },
  {
    id: 'configure',
    label: 'Configure',
    items: [
      {
        label: 'Categories',
        to: '/categories',
        icon: FolderTree,
        permission: Permission.AssetsRead,
        description: 'Asset classification',
      },
      {
        label: 'Locations',
        to: '/locations',
        icon: MapPin,
        permission: Permission.AssetsRead,
        matchPrefix: true,
        description: 'Rooms, blocks and stores',
      },
      {
        label: 'Accounts',
        to: '/accounts',
        icon: UserCog,
        permission: Permission.UsersManage,
        description: 'Sign-in accounts for administrators and technicians',
      },
    ],
  },
  {
    id: 'administration',
    label: 'Administration',
    items: [
      {
        label: 'Audit trail',
        to: '/audit',
        icon: ScrollText,
        permission: Permission.AuditRead,
        description: 'Who did what, and whether it worked',
      },
      {
        label: 'School settings',
        to: '/settings',
        icon: Settings,
        permission: Permission.SettingsManage,
        description: 'Identity, time zone and the QR label address',
      },
      {
        label: 'Server status',
        to: '/operations',
        icon: Activity,
        permission: Permission.SettingsManage,
        description: 'Counters, backups and alerts from the running server',
      },
      {
        label: 'Legacy cutover',
        to: '/reconciliation',
        icon: FileCheck2,
        permission: Permission.LegacyReconcile,
        description: 'Compare an old-system export against this register',
      },
    ],
  },
];

/** Flat list, useful for the command palette. */
export const ALL_NAV_ITEMS: NavItem[] = NAV_SECTIONS.flatMap((section) => section.items);
