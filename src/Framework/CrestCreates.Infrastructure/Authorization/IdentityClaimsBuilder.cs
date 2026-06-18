using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;

namespace CrestCreates.Infrastructure.Authorization;

public class IdentityClaimsBuilder : IIdentityClaimsBuilder
{
    public IReadOnlyList<Claim> Build(IdentityClaimsContext context)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, context.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, context.UserId.ToString()),
            new(ClaimTypes.Name, context.UserName),
            new("preferred_username", context.UserName),
            new(ClaimTypes.Email, context.Email ?? string.Empty),
            new("tenantid", context.TenantId ?? string.Empty),
            new("tenant_id", context.TenantId ?? string.Empty),
            new("is_super_admin", context.IsSuperAdmin.ToString().ToLowerInvariant())
        };

        if (context.OrganizationId.HasValue)
        {
            claims.Add(new Claim("org_id", context.OrganizationId.Value.ToString()));
        }

        foreach (var role in context.Roles
                     .Where(role => !string.IsNullOrWhiteSpace(role))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in context.Permissions
                     .Where(permission => !string.IsNullOrWhiteSpace(permission))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("permission", permission));
        }

        return claims;
    }
}
