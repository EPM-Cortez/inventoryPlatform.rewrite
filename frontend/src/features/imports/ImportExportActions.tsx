import { Download, Upload } from 'lucide-react';
import type { UseMutationResult } from '@tanstack/react-query';
import { Link } from 'react-router';

import type { CsvFile } from '@/api/types';
import { Permission, type PermissionKey } from '@/auth/permissions';
import { useSession } from '@/auth/session-context';
import { downloadBlob } from '@/lib/download';
import { formatNumber } from '@/lib/format';
import { Button } from '@/ui/Button';
import { useToast } from '@/ui/Toast';

export interface ImportExportActionsProps {
  /** Where the wizard for this resource lives, e.g. `/people/import`. */
  importPath: string;
  exportPermission: PermissionKey;
  /** The export mutation for this resource. */
  exportMutation: UseMutationResult<CsvFile, Error, void>;
}

/**
 * The Import and Export pair shown in a list page header.
 *
 * Each export sits behind its own permission — bulk person data is separately
 * grantable from departments and locations — so the buttons appear
 * independently rather than as a single "data" capability.
 */
export function ImportExportActions({
  importPath,
  exportPermission,
  exportMutation,
}: ImportExportActionsProps) {
  const { can } = useSession();
  const toast = useToast();

  return (
    <>
      {can(Permission.ImportsValidate) && (
        <Button asChild>
          <Link to={importPath}>
            <Upload className="mr-1.5 size-4" />
            Import
          </Link>
        </Button>
      )}
      {can(exportPermission) && (
        <Button
          iconLeft={<Download />}
          loading={exportMutation.isPending}
          onClick={() =>
            exportMutation.mutate(undefined, {
              onSuccess: (file) => {
                downloadBlob(file.blob, file.fileName);
                toast.success(
                  'Export ready',
                  `${formatNumber(file.rowCount)} ${file.rowCount === 1 ? 'row' : 'rows'} downloaded.`,
                );
              },
              onError: (error) => toast.fromError(error, 'Could not export'),
            })
          }
        >
          Export CSV
        </Button>
      )}
    </>
  );
}
