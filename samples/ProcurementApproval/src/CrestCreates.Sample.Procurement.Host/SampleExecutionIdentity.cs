using System.Security.Claims;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Sample.Procurement.Host;

public sealed class SampleExecutionIdentity : ICurrentUser, ITenantContext, IAgentExecutionContextAccessor
{
    public const string TenantHeader = "X-Sample-Tenant";
    public const string UserHeader = "X-Sample-User";
    public const string RolesHeader = "X-Sample-Roles";

    private readonly IHttpContextAccessor _http;
    private string? _tenantId;
    private string? _userId;
    private string[] _roles = [];

    public SampleExecutionIdentity(IHttpContextAccessor http) => _http = http;

    public string Id => ResolveUserId() ?? string.Empty;
    public string UserName => Id;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(CurrentTenantId);
    public string TenantId => CurrentTenantId ?? string.Empty;
    public string[] Roles => ResolveRoles();
    public Guid? OrganizationId => null;
    public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
    public int DataScopeValue => 0;
    public bool IsSuperAdmin => false;
    public string? CurrentTenantId => ResolveTenantId();
    public AgentExecutionContext? Current { get; private set; }

    public void Set(string tenantId, string userId, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _tenantId = tenantId;
        _userId = userId;
        _roles = roles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal).ToArray();
    }

    public void SetAgent(
        string executionId,
        string invocationId,
        AgentToolCallOrigin origin = AgentToolCallOrigin.ExplicitRequest)
    {
        Current = new AgentExecutionContext
        {
            ExecutionId = executionId,
            InvocationId = invocationId,
            AgentId = "procurement-agent",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "procurement-agent" },
            CallOrigin = origin,
            CausationId = invocationId
        };
    }

    public string FindClaimValue(string claimType) => string.Empty;
    public string[] FindClaimValues(string claimType) => [];
    public bool IsInRole(string roleName) => Roles.Contains(roleName, StringComparer.Ordinal);
    public bool IsInOrganization(Guid orgId) => false;

    private string? ResolveTenantId()
        => Header(TenantHeader) ?? _tenantId;

    private string? ResolveUserId()
        => Header(UserHeader) ?? _userId;

    private string[] ResolveRoles()
    {
        var header = Header(RolesHeader);
        return header is null
            ? _roles
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private string? Header(string name)
    {
        var value = _http.HttpContext?.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

public sealed class SamplePermissionChecker(ICurrentUser currentUser) : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permissionName)
        => Task.FromResult(IsGranted(permissionName));

    public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
        => Task.FromResult(IsGranted(principal, permissionName));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
        => Task.FromResult(new MultiplePermissionGrantResult(
            permissionNames.ToDictionary(name => name, IsGranted, StringComparer.Ordinal)));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
        => Task.FromResult(new MultiplePermissionGrantResult(
            permissionNames.ToDictionary(name => name, name => IsGranted(principal, name), StringComparer.Ordinal)));

    public async Task CheckAsync(string permissionName)
    {
        if (!await IsGrantedAsync(permissionName).ConfigureAwait(false))
            throw new UnauthorizedAccessException($"Permission '{permissionName}' is required.");
    }

    private bool IsGranted(string permissionName)
        => permissionName switch
        {
            "procurement.approve" => currentUser.IsInRole("procurement-manager"),
            "Procurement.Get" => currentUser.IsAuthenticated,
            "Procurement.Search" => currentUser.IsInRole("procurement-requester")
                || currentUser.IsInRole("procurement-manager"),
            _ => false
        };

    private static bool IsGranted(ClaimsPrincipal principal, string permissionName)
        => permissionName switch
        {
            "procurement.approve" => principal.IsInRole("procurement-manager"),
            "Procurement.Get" => principal.Identity?.IsAuthenticated == true,
            "Procurement.Search" => principal.IsInRole("procurement-requester")
                || principal.IsInRole("procurement-manager"),
            _ => false
        };
}
