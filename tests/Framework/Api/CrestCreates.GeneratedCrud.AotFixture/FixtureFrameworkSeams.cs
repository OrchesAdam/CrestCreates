using System.Security.Claims;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.DataFilter;

namespace CrestCreates.GeneratedCrud.AotFixture;

/// <summary>
/// #128 S4 Step A permission seam: grants every permission. The generated WidgetAppService calls
/// <see cref="IPermissionChecker.CheckAsync"/> on every action and expects it to throw on denial;
/// this fixture intentionally runs without an authorization store, so all grants pass.
/// </summary>
public sealed class FixturePermissionChecker : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);

    public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName) => Task.FromResult(true);

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
        => Task.FromResult(Granted(permissionNames));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
        => Task.FromResult(Granted(permissionNames));

    public Task CheckAsync(string permissionName) => Task.CompletedTask;

    private static MultiplePermissionGrantResult Granted(string[] permissionNames)
        => new(permissionNames.ToDictionary(name => name, _ => true));
}

/// <summary>
/// #128 S4 Step A identity seam: one stable authenticated principal identity. Widget is not
/// tenant-scoped (<c>IMustHaveTenant</c>) and not an audited entity, so the generated audit helpers
/// only read <see cref="Id"/>; the remaining members answer the framework contract honestly.
/// </summary>
public sealed class FixtureCurrentUser : ICurrentUser
{
    public string Id { get; } = "11111111-1111-1111-1111-111111111111";

    public string UserName { get; } = "crud-fixture";

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

/// <summary>
/// #128 S4 Step A data-permission seam: identity filter. The generated CRUD query path always routes
/// through <see cref="IDataPermissionFilter"/>; this fixture has no data-scope store, so the filter
/// applies nothing and the query is returned unchanged.
/// </summary>
public sealed class IdentityDataPermissionFilter : IDataPermissionFilter
{
    public Task<IQueryable<TEntity>> ApplyFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        => Task.FromResult(query);

    public Task<IQueryable<TEntity>> ApplyTenantFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        => Task.FromResult(query);

    public Task<IQueryable<TEntity>> ApplyOrganizationFilterAsync<TEntity>(IQueryable<TEntity> query) where TEntity : class
        => Task.FromResult(query);
}
