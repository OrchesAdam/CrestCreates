using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using System.Security.Claims;

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
        => await AuthorizeAsync(capabilityName, userId, requiredPermissions, ct, principal: null).ConfigureAwait(false);

    public async Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct,
        ClaimsPrincipal? principal)
    {
        if (requiredPermissions.Count == 0)
            return true;

        if (_permissionChecker == null)
            throw new InvalidOperationException(
                "Capability requires permissions but no IPermissionChecker is registered. " +
                "Register IPermissionChecker via AddCrestAuthorization() to enable capability authorization.");

        var result = principal is null
            ? await _permissionChecker.IsGrantedAsync(requiredPermissions.ToArray())
            : await _permissionChecker.IsGrantedAsync(principal, requiredPermissions.ToArray());
        return result.AllGranted;
    }
}
