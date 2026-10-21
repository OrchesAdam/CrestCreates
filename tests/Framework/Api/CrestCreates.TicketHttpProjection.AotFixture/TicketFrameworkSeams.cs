using System.Security.Claims;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.DataFilter;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Permission seam: the native fixture has no authorization store, so the default is grant-all. A
/// scenario can add a permission to <see cref="Denied"/> to prove the endpoint authorization path
/// rejects it before business code.
/// </summary>
public sealed class FixturePermissionChecker : IPermissionChecker
{
    public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

    public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(!Denied.Contains(permissionName));

    public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
        => Task.FromResult(!Denied.Contains(permissionName));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
        => Task.FromResult(Result(permissionNames));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
        => Task.FromResult(Result(permissionNames));

    public Task CheckAsync(string permissionName)
        => Denied.Contains(permissionName)
            ? throw new CrestPermissionException(permissionName)
            : Task.CompletedTask;

    private MultiplePermissionGrantResult Result(string[] permissionNames)
        => new(permissionNames.ToDictionary(name => name, name => !Denied.Contains(name), StringComparer.Ordinal));
}

/// <summary>One stable authenticated identity; Ticket is not tenant-scoped.</summary>
public sealed class FixtureCurrentUser : ICurrentUser
{
    public string Id { get; } = "22222222-2222-2222-2222-222222222222";
    public string UserName { get; } = "ticket-native-fixture";
    public bool IsAuthenticated => true;
    public string TenantId { get; } = string.Empty;
    public string[] Roles { get; } = Array.Empty<string>();
    public Guid? OrganizationId => null;
    public IReadOnlyList<Guid> OrganizationIds { get; } = Array.Empty<Guid>();
    public int DataScopeValue => 0;
    public bool IsSuperAdmin => false;
    public string FindClaimValue(string claimType) => string.Empty;
    public string[] FindClaimValues(string claimType) => Array.Empty<string>();
    public bool IsInRole(string roleName) => false;
    public bool IsInOrganization(Guid orgId) => false;
}

/// <summary>Identity data-permission filter: the fixture has no data-scope store.</summary>
public sealed class IdentityDataPermissionFilter : IDataPermissionFilter
{
    public Task<IQueryable<TEntity>> ApplyFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        => Task.FromResult(query);

    public Task<IQueryable<TEntity>> ApplyTenantFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        => Task.FromResult(query);

    public Task<IQueryable<TEntity>> ApplyOrganizationFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        => Task.FromResult(query);
}
