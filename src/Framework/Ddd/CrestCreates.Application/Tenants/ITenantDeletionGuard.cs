using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;

namespace CrestCreates.Application.Tenants;

public interface ITenantDeletionGuard
{
    Task<TenantDeletionGuardResult> CanDeleteAsync(Tenant tenant, CancellationToken cancellationToken = default);
}

public class TenantDeletionGuardResult
{
    public bool CanDelete { get; set; }
    public string? FailureReason { get; set; }
    public string[]? ExistingUsers { get; set; }
    public string[]? ExistingRoles { get; set; }

    public static TenantDeletionGuardResult Success() => new() { CanDelete = true };
    public static TenantDeletionGuardResult Failure(string reason, string[]? users = null, string[]? roles = null) =>
        new() { CanDelete = false, FailureReason = reason, ExistingUsers = users, ExistingRoles = roles };
}
