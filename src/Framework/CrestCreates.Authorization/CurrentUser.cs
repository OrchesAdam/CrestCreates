using System;
using System.Collections.Generic;
using System.Security.Claims;
using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Authorization;

public class CurrentUser : ICurrentUser
{
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private IReadOnlyList<Guid>? _organizationIds;

    public CurrentUser(ICurrentPrincipalAccessor principalAccessor)
    {
        _principalAccessor = principalAccessor;
    }

    public string Id => FirstNonEmptyClaimValue(
        ClaimTypes.NameIdentifier,
        "sub",
        "uid");

    public string UserName => FirstNonEmptyClaimValue(
        ClaimTypes.Name,
        "preferred_username",
        "name");

    public bool IsAuthenticated => _principalAccessor.Principal?.Identity?.IsAuthenticated ?? false;

    public string TenantId => FirstNonEmptyClaimValue(
        "tenantid",
        "tenant_id",
        "TenantId");

    public string[] Roles
    {
        get
        {
            var roles = FindClaimValues(ClaimTypes.Role);
            if (roles.Length > 0)
                return roles;

            return FindClaimValues("role");
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            var orgIdStr = FirstNonEmptyClaimValue("org_id", "organizationid");
            if (Guid.TryParse(orgIdStr, out var orgId))
            {
                return orgId;
            }
            return null;
        }
    }

    public IReadOnlyList<Guid> OrganizationIds
    {
        get
        {
            if (_organizationIds != null)
                return _organizationIds;

            var orgIds = new List<Guid>();

            if (OrganizationId.HasValue)
            {
                orgIds.Add(OrganizationId.Value);
            }

            var orgIdsStr = FindClaimValue("org_ids");
            if (!string.IsNullOrEmpty(orgIdsStr))
            {
                var parts = orgIdsStr.Split(',', ';');
                foreach (var part in parts)
                {
                    if (Guid.TryParse(part.Trim(), out var id) && !orgIds.Contains(id))
                    {
                        orgIds.Add(id);
                    }
                }
            }

            _organizationIds = orgIds.AsReadOnly();
            return _organizationIds;
        }
    }

    public int DataScopeValue
    {
        get
        {
            var dataScopeStr = FirstNonEmptyClaimValue("data_scope");
            if (int.TryParse(dataScopeStr, out var dataScopeValue))
            {
                return dataScopeValue;
            }
            return 0;
        }
    }

    public bool IsSuperAdmin
    {
        get
        {
            var isSuperAdminStr = FindClaimValue("is_super_admin");
            return bool.TryParse(isSuperAdminStr, out var isSuperAdmin) && isSuperAdmin;
        }
    }

    public string FindClaimValue(string claimType)
    {
        return _principalAccessor.Principal?.FindFirst(claimType)?.Value ?? string.Empty;
    }

    public string[] FindClaimValues(string claimType)
    {
        var claims = _principalAccessor.Principal?.FindAll(claimType);
        if (claims == null)
            return Array.Empty<string>();

        var values = new List<string>();
        foreach (var claim in claims)
        {
            if (!string.IsNullOrEmpty(claim.Value))
            {
                values.Add(claim.Value);
            }
        }

        return values.ToArray();
    }

    public bool IsInRole(string roleName)
    {
        return _principalAccessor.Principal?.IsInRole(roleName) ?? false;
    }

    public bool IsInOrganization(Guid orgId)
    {
        return OrganizationIds.Contains(orgId);
    }

    private string FirstNonEmptyClaimValue(params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = FindClaimValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
