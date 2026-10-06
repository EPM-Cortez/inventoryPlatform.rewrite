import { Compass } from 'lucide-react';
import { lazy, Suspense } from 'react';
import { Link, Navigate, Route, Routes } from 'react-router';

import { Permission } from '@/auth/permissions';
import { Button } from '@/ui/Button';
import { EmptyState } from '@/ui/EmptyState';
import { LoadingBlock } from '@/ui/Spinner';

import { AppShell } from './AppShell';
import { RequireAnonymous, RequireAuth, RequirePermission } from './guards';

// Auth screens are needed on the very first paint, so they stay in the main
// bundle. Everything behind the shell is split.
import { ChangePasswordPage } from '@/features/auth/ChangePasswordPage';
import { LoginPage } from '@/features/auth/LoginPage';
import { MfaPage } from '@/features/auth/MfaPage';

const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage'));
const AssetsPage = lazy(() => import('@/features/assets/AssetsPage'));
const AssetDetailPage = lazy(() => import('@/features/assets/AssetDetailPage'));
const AssetEditorPage = lazy(() => import('@/features/assets/AssetEditorPage'));
const LabelsPage = lazy(() => import('@/features/labels/LabelsPage'));
const DevicesPage = lazy(() => import('@/features/agents/DevicesPage'));
const DeviceDetailPage = lazy(() => import('@/features/agents/DeviceDetailPage'));
const EnrollmentProfilesPage = lazy(
  () => import('@/features/agents/EnrollmentProfilesPage'),
);
const AssetImportPage = lazy(() => import('@/features/imports/AssetImportPage'));
const ReferenceDataImportPage = lazy(
  () => import('@/features/imports/ReferenceDataImportPage'),
);
const ReconciliationPage = lazy(() => import('@/features/imports/ReconciliationPage'));
const PeoplePage = lazy(() => import('@/features/people/PeoplePage'));
const PersonDetailPage = lazy(() => import('@/features/people/PersonDetailPage'));
const DepartmentsPage = lazy(() => import('@/features/departments/DepartmentsPage'));
const DepartmentDetailPage = lazy(
  () => import('@/features/departments/DepartmentDetailPage'),
);
const WarrantiesPage = lazy(() => import('@/features/warranties/WarrantiesPage'));
const OverdueLoansPage = lazy(() => import('@/features/assets/OverdueLoansPage'));
const StocktakesPage = lazy(() => import('@/features/stocktakes/StocktakesPage'));
const LeaversPage = lazy(() => import('@/features/leavers/LeaversPage'));
const BundlesPage = lazy(() => import('@/features/bundles/BundlesPage'));
const LeaverCasePage = lazy(() => import('@/features/leavers/LeaverCasePage'));
const StocktakeSessionPage = lazy(
  () => import('@/features/stocktakes/StocktakeSessionPage'),
);
const CategoriesPage = lazy(() => import('@/features/reference/CategoriesPage'));
const LocationsPage = lazy(() => import('@/features/reference/LocationsPage'));
const LocationDetailPage = lazy(() => import('@/features/reference/LocationDetailPage'));
const AccountsPage = lazy(() => import('@/features/users/AccountsPage'));
const SecurityPage = lazy(() => import('@/features/auth/SecurityPage'));
const AuditPage = lazy(() => import('@/features/audit/AuditPage'));
const SettingsPage = lazy(() => import('@/features/settings/SettingsPage'));
const OperationsPage = lazy(() => import('@/features/settings/OperationsPage'));
const ScanPage = lazy(() => import('@/features/scan/ScanPage'));

function Lazy({ children }: { children: React.ReactNode }) {
  return <Suspense fallback={<LoadingBlock />}>{children}</Suspense>;
}

export function AppRoutes() {
  return (
    <Routes>
      <Route
        path="/login"
        element={
          <RequireAnonymous>
            <LoginPage />
          </RequireAnonymous>
        }
      />
      <Route
        path="/login/mfa"
        element={
          <RequireAnonymous>
            <MfaPage />
          </RequireAnonymous>
        }
      />

      <Route
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route
          index
          element={
            <Lazy>
              <DashboardPage />
            </Lazy>
          }
        />

        <Route
          path="assets"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <AssetsPage />
              </Lazy>
            </RequirePermission>
          }
        />
        {/* Declared before `assets/:assetId` so "import" is not read as an id. */}
        <Route
          path="assets/import"
          element={
            <RequirePermission permission={Permission.ImportsValidate}>
              <Lazy>
                <AssetImportPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="assets/new"
          element={
            <RequirePermission permission={Permission.AssetsCreate}>
              <Lazy>
                <AssetEditorPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="assets/:assetId"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <AssetDetailPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="assets/:assetId/edit"
          element={
            <RequirePermission permission={Permission.AssetsUpdate}>
              <Lazy>
                <AssetEditorPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="labels"
          element={
            <RequirePermission permission={Permission.QrPrint}>
              <Lazy>
                <LabelsPage />
              </Lazy>
            </RequirePermission>
          }
        />

        {/* People, departments and locations share one wizard. Listed explicitly
            rather than as `:resource/import` so no other path is captured. */}
        {(['people', 'departments', 'locations'] as const).map((resource) => (
          <Route
            key={resource}
            path={`${resource}/import`}
            element={
              <RequirePermission permission={Permission.ImportsValidate}>
                <Lazy>
                  <ReferenceDataImportPage resource={resource} />
                </Lazy>
              </RequirePermission>
            }
          />
        ))}

        <Route
          path="devices"
          element={
            <RequirePermission permission={Permission.AgentsRead}>
              <Lazy>
                <DevicesPage />
              </Lazy>
            </RequirePermission>
          }
        />
        {/* Top level rather than under `devices` so the sidebar highlights one
            entry — `devices` matches by prefix for `devices/:agentId`. */}
        <Route
          path="enrollment-tokens"
          element={
            <RequirePermission permission={Permission.AgentsManage}>
              <Lazy>
                <EnrollmentProfilesPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="devices/:agentId"
          element={
            <RequirePermission permission={Permission.AgentsRead}>
              <Lazy>
                <DeviceDetailPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="people"
          element={
            <RequirePermission permission={Permission.PeopleRead}>
              <Lazy>
                <PeoplePage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="people/:personId"
          element={
            <RequirePermission permission={Permission.PeopleRead}>
              <Lazy>
                <PersonDetailPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="departments"
          element={
            <RequirePermission permission={Permission.PeopleRead}>
              <Lazy>
                <DepartmentsPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="departments/:departmentId"
          element={
            <RequirePermission permission={Permission.PeopleRead}>
              <Lazy>
                <DepartmentDetailPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="loans/overdue"
          element={
            <RequirePermission permission={Permission.AssignmentsRead}>
              <Lazy>
                <OverdueLoansPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="bundles"
          element={
            <RequirePermission permission={Permission.BundlesRead}>
              <Lazy>
                <BundlesPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="leavers"
          element={
            <RequirePermission permission={Permission.LeaversRead}>
              <Lazy>
                <LeaversPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="leavers/:leaverCaseId"
          element={
            <RequirePermission permission={Permission.LeaversRead}>
              <Lazy>
                <LeaverCasePage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="stocktakes"
          element={
            <RequirePermission permission={Permission.StocktakesRead}>
              <Lazy>
                <StocktakesPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="stocktakes/:sessionId"
          element={
            <RequirePermission permission={Permission.StocktakesRead}>
              <Lazy>
                <StocktakeSessionPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="warranties"
          element={
            <RequirePermission permission={Permission.WarrantiesRead}>
              <Lazy>
                <WarrantiesPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="categories"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <CategoriesPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="locations"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <LocationsPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="locations/:locationId"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <LocationDetailPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="accounts"
          element={
            <RequirePermission permission={Permission.UsersManage}>
              <Lazy>
                <AccountsPage />
              </Lazy>
            </RequirePermission>
          }
        />
        {/* The page was called "Users" before login accounts and custody people
            were separated in the UI. Keep old links working. */}
        <Route path="users" element={<Navigate to="/accounts" replace />} />

        {/* The QR payload URL is `{School:QrBaseUrl}/{token}`, and that base is
            configured to end in `/scan`, so a scanned label lands here. */}
        <Route
          path="scan"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <ScanPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="scan/:token"
          element={
            <RequirePermission permission={Permission.AssetsRead}>
              <Lazy>
                <ScanPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="audit"
          element={
            <RequirePermission permission={Permission.AuditRead}>
              <Lazy>
                <AuditPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="settings"
          element={
            <RequirePermission permission={Permission.SettingsManage}>
              <Lazy>
                <SettingsPage />
              </Lazy>
            </RequirePermission>
          }
        />
        <Route
          path="operations"
          element={
            <RequirePermission permission={Permission.SettingsManage}>
              <Lazy>
                <OperationsPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route
          path="reconciliation"
          element={
            <RequirePermission permission={Permission.LegacyReconcile}>
              <Lazy>
                <ReconciliationPage />
              </Lazy>
            </RequirePermission>
          }
        />

        <Route path="change-password" element={<ChangePasswordPage />} />
        {/* Everyone manages their own second factor — no permission gate, it is
            about the signed-in account rather than anyone else's. */}
        <Route
          path="security"
          element={
            <Lazy>
              <SecurityPage />
            </Lazy>
          }
        />
        <Route path="404" element={<NotFound />} />
        <Route path="*" element={<NotFound />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function NotFound() {
  return (
    <EmptyState
      icon={<Compass />}
      title="Page not found"
      description="That address does not match anything in the inventory."
      action={
        <Button variant="primary" asChild>
          <Link to="/">Back to the dashboard</Link>
        </Button>
      }
    />
  );
}
