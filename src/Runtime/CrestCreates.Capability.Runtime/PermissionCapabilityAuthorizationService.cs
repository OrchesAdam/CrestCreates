using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class PermissionCapabilityAuthorizationService : ICapabilityAuthorizationService
{
    private readonly IPermissionChecker? _permissionChecker;

    public PermissionCapabilityAuthorizationService(IPermissionChecker? permissionChecker = null)
    {
        _permissionChecker = permissionChecker;
    }

    public async Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct)
    {
        if (requiredPermissions.Count == 0)
            return true;

        if (_permissionChecker == null)
            throw new InvalidOperationException(
                "Capability requires permissions but no IPermissionChecker is registered. " +
                "Register IPermissionChecker via AddCrestAuthorization() to enable capability authorization.");

        var result = await _permissionChecker.IsGrantedAsync(requiredPermissions.ToArray());
        return result.AllGranted;
    }
}
