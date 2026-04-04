using System;
using System.Security.Claims;

namespace CrestCreates.Authorization.Abstractions;

public interface ICurrentPrincipalAccessor
{
    ClaimsPrincipal Principal { get; }
    IDisposable Change(ClaimsPrincipal principal);
}
