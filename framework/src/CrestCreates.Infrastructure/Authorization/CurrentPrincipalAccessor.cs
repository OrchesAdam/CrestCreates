using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using CrestCreates.Domain.Shared.Enums;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.Infrastructure.Authorization
{
    public class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AsyncLocal<ClaimsPrincipal> _currentPrincipal = new AsyncLocal<ClaimsPrincipal>();

        public CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public ClaimsPrincipal Principal
        {
            get
            {
                if (_currentPrincipal.Value != null)
                {
                    return _currentPrincipal.Value;
                }

                return _httpContextAccessor?.HttpContext?.User;
            }
        }

        public IDisposable Change(ClaimsPrincipal principal)
        {
            var oldPrincipal = _currentPrincipal.Value;
            _currentPrincipal.Value = principal;

            return new DisposeAction(() =>
            {
                _currentPrincipal.Value = oldPrincipal;
            });
        }

        private class DisposeAction : IDisposable
        {
            private readonly Action _action;

            public DisposeAction(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }

    public interface ICurrentUser
    {
        string Id { get; }
        string UserName { get; }
        bool IsAuthenticated { get; }
        string TenantId { get; }
        string[] Roles { get; }
        Guid? OrganizationId { get; }
        IReadOnlyList<Guid> OrganizationIds { get; }
        DataScope DataScope { get; }
        string FindClaimValue(string claimType);
        string[] FindClaimValues(string claimType);
        bool IsInRole(string roleName);
        bool IsInOrganization(Guid orgId);
    }

    public class CurrentUser : ICurrentUser
    {
        private readonly ICurrentPrincipalAccessor _principalAccessor;
        private IReadOnlyList<Guid>? _organizationIds;

        public CurrentUser(ICurrentPrincipalAccessor principalAccessor)
        {
            _principalAccessor = principalAccessor;
        }

        public string Id => FindClaimValue(ClaimTypes.NameIdentifier)
            ?? FindClaimValue("sub")
            ?? FindClaimValue("uid");

        public string UserName => FindClaimValue(ClaimTypes.Name)
            ?? FindClaimValue("preferred_username")
            ?? FindClaimValue("name");

        public bool IsAuthenticated => _principalAccessor.Principal?.Identity?.IsAuthenticated ?? false;

        public string TenantId => FindClaimValue("tenantid")
            ?? FindClaimValue("tenant_id")
            ?? FindClaimValue("TenantId");

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
                var orgIdStr = FindClaimValue("org_id") ?? FindClaimValue("organizationid");
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

        public DataScope DataScope
        {
            get
            {
                var dataScopeStr = FindClaimValue("data_scope");
                if (int.TryParse(dataScopeStr, out var dataScopeValue) && Enum.IsDefined(typeof(DataScope), dataScopeValue))
                {
                    return (DataScope)dataScopeValue;
                }
                return DataScope.Self;
            }
        }

        public string FindClaimValue(string claimType)
        {
            return _principalAccessor.Principal?.FindFirst(claimType)?.Value;
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
    }
}
