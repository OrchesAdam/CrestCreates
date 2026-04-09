using System.Collections.Generic;
using System.Security.Claims;
using CrestCreates.Domain.Permission;

namespace CrestCreates.Infrastructure.Authorization;

public interface IIdentityClaimsBuilder
{
    IReadOnlyList<Claim> Build(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions);
}
