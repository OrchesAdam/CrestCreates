using System.Security.Claims;

namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityAuthorizationService
{
    Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct);

    Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct,
        ClaimsPrincipal principal);
}
