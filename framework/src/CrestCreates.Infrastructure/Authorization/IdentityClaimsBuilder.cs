using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using CrestCreates.Domain.Permission;

namespace CrestCreates.Infrastructure.Authorization;

public class IdentityClaimsBuilder : IIdentityClaimsBuilder
{
    public IReadOnlyList<Claim> Build(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new("preferred_username", user.UserName),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("tenantid", user.TenantId ?? string.Empty),
            new("is_super_admin", user.IsSuperAdmin.ToString().ToLowerInvariant())
        };

        if (user.OrganizationId.HasValue)
        {
            claims.Add(new Claim("org_id", user.OrganizationId.Value.ToString()));
        }

        foreach (var role in roles
                     .Where(role => !string.IsNullOrWhiteSpace(role))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissions
                     .Where(permission => !string.IsNullOrWhiteSpace(permission))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("permission", permission));
        }

        return claims;
    }
}
