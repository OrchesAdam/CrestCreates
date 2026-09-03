using System.Security.Claims;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetExecutionIdentity(IHttpContextAccessor http) : ICurrentUser, ITenantContext, IAgentExecutionContextAccessor
{
    public const string TenantHeader = "X-Asset-Tenant";
    public const string UserHeader = "X-Asset-User";
    public const string RolesHeader = "X-Asset-Roles";
    public const string OrganizationHeader = "X-Asset-Organization";
    public const string OrganizationsHeader = "X-Asset-Organizations";
    public const string DataScopeHeader = "X-Asset-Data-Scope";

    private string? _tenant;
    private string? _user;
    private string[] _roles = [];
    private Guid? _organization;
    private IReadOnlyList<Guid> _organizations = [];
    private DataScope _dataScope = DataScope.Organization;

    public string Id => HeaderOr(UserHeader, _user) ?? string.Empty;
    public string UserName => Id;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(CurrentTenantId);
    public string TenantId => CurrentTenantId ?? string.Empty;
    public string[] Roles => Header(RolesHeader)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? _roles;
    public Guid? OrganizationId => ParseGuid(Header(OrganizationHeader)) ?? _organization;
    public IReadOnlyList<Guid> OrganizationIds
    {
        get
        {
            var ids = ParseGuids(Header(OrganizationsHeader));
            if (ids.Count > 0)
                return ids;
            var organization = ParseGuid(Header(OrganizationHeader));
            return organization is Guid id ? [id] : _organizations;
        }
    }
    public int DataScopeValue => (int)(Enum.TryParse<DataScope>(Header(DataScopeHeader), true, out var scope) ? scope : _dataScope);
    public bool IsSuperAdmin => IsInRole("asset-superadmin");
    public string? CurrentTenantId => HeaderOr(TenantHeader, _tenant);
    public AgentExecutionContext? Current { get; private set; }

    public void Set(string tenant, string user, Guid? organization = null, DataScope dataScope = DataScope.Organization, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        _tenant = tenant;
        _user = user;
        _organization = organization;
        _organizations = organization is Guid id ? [id] : [];
        _dataScope = dataScope;
        _roles = roles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal).ToArray();
    }

    public void SetAgent(string executionId, string invocationId)
        => Current = new AgentExecutionContext
        {
            ExecutionId = executionId,
            InvocationId = invocationId,
            AgentId = "asset-agent",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "asset-agent" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            CausationId = invocationId
        };

    public string FindClaimValue(string claimType) => string.Empty;
    public string[] FindClaimValues(string claimType) => [];
    public bool IsInRole(string roleName) => Roles.Contains(roleName, StringComparer.Ordinal);
    public bool IsInOrganization(Guid orgId) => OrganizationIds.Contains(orgId);

    private string? Header(string name)
    {
        var value = http.HttpContext?.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string? HeaderOr(string header, string? fallback) => Header(header) ?? fallback;
    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
    private static IReadOnlyList<Guid> ParseGuids(string? value) => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseGuid).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray() ?? [];
}

public sealed class AssetAuthenticationHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "asset-sample";
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[AssetExecutionIdentity.UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(user))
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
        var tenant = Request.Headers[AssetExecutionIdentity.TenantHeader].ToString();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user), new(ClaimTypes.Name, user), new("tenantid", tenant) };
        claims.AddRange(Request.Headers[AssetExecutionIdentity.RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, SchemeName)));
    }
}
