using System;
using System.Collections.Generic;

namespace CrestCreates.Authorization.Abstractions;

public interface ICurrentUser
{
    string Id { get; }
    string UserName { get; }
    bool IsAuthenticated { get; }
    string TenantId { get; }
    string[] Roles { get; }
    Guid? OrganizationId { get; }
    IReadOnlyList<Guid> OrganizationIds { get; }
    int DataScopeValue { get; }
    bool IsSuperAdmin { get; }
    string FindClaimValue(string claimType);
    string[] FindClaimValues(string claimType);
    bool IsInRole(string roleName);
    bool IsInOrganization(Guid orgId);
}
