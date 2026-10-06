import { useRolePresets } from '@/api/hooks';
import {
  compareRoles,
  describePermission,
  describeRole,
  describeRolePurpose,
} from '@/auth/permissions';
import { Alert } from '@/ui/Alert';
import { Checkbox } from '@/ui/Checkbox';
import { Skeleton } from '@/ui/Skeleton';

export interface RolePickerProps {
  value: string[];
  onChange: (roles: string[]) => void;
  disabled?: boolean;
}

/**
 * Role checkboxes driven by `GET /users/role-presets`, so a preset added on the
 * server shows up without a frontend release. Each role lists the permissions it
 * actually grants — an administrator choosing between "Technician" and "Inventory
 * manager" should not have to read the backend source to know the difference.
 */
export function RolePicker({ value, onChange, disabled }: RolePickerProps) {
  const presets = useRolePresets();

  if (presets.isLoading) {
    return (
      <div className="grid gap-3 sm:grid-cols-2">
        {Array.from({ length: 4 }, (_, index) => (
          <Skeleton key={index} className="h-12 w-full" />
        ))}
      </div>
    );
  }

  if (presets.isError) {
    return (
      <Alert tone="danger" title="Could not load the role presets">
        Roles cannot be chosen until this loads. Close this form and try again.
      </Alert>
    );
  }

  const sorted = [...(presets.data ?? [])].sort((a, b) => compareRoles(a.name, b.name));

  function toggle(role: string, checked: boolean) {
    onChange(checked ? [...value, role] : value.filter((name) => name !== role));
  }

  return (
    // Two columns so the whole set fits in the dialog without scrolling.
    <div className="grid gap-3 sm:grid-cols-2">
      {sorted.map((preset) => {
        const selected = value.includes(preset.name);
        return (
          <div
            key={preset.name}
            className="rounded-lg border border-border p-3 transition-colors has-[:checked]:border-accent has-[:checked]:bg-accent-soft/40"
          >
            <Checkbox
              checked={selected}
              onCheckedChange={(checked) => toggle(preset.name, checked)}
              disabled={disabled}
              label={describeRole(preset.name)}
              description={describeRolePurpose(preset.name)}
            />
            {selected && (
              <ul className="mt-2.5 flex flex-wrap gap-1 border-t border-border pt-2.5">
                {preset.permissions.map((permission) => (
                  <li
                    key={permission}
                    className="rounded-sm bg-surface-sunken px-1.5 py-0.5 text-2xs text-text-muted"
                    title={permission}
                  >
                    {describePermission(permission)}
                  </li>
                ))}
              </ul>
            )}
          </div>
        );
      })}

      {value.length === 0 && (
        <Alert tone="warning">
          With no role, the account can sign in but will not be able to see or do
          anything.
        </Alert>
      )}
    </div>
  );
}
