using System.Collections.Generic;
using System.Security.Claims;

namespace CrestCreates.Infrastructure.Authorization;

public interface IIdentityClaimsBuilder
{
    IReadOnlyList<Claim> Build(IdentityClaimsContext context);
}
