# Adding a permission

1. Add the permission key to `Inventory.Application.Security.Permissions` and its
   `All` collection.
2. Decide which role presets receive it in `RolePresets`; default to no preset.
3. Apply `[Authorize(Policy = Permissions.YourPermission)]` to the endpoint.
4. Add the endpoint and permission to `docs/api/permissions.md`.
5. Add integration tests proving an allowed preset succeeds and at least one
   authenticated but disallowed preset receives `403`.
6. Consider whether the action is high risk and needs a specific audit event, forced
   reauthentication, or security-stamp invalidation.

Policies are registered from the catalogue at startup. Do not authorize by role name
inside controllers and do not treat a hidden UI control as authorization.
