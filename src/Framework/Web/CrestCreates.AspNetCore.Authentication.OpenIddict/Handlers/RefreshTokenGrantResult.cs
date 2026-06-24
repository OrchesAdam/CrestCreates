using System.Security.Claims;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public sealed class RefreshTokenGrantResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorDescription { get; init; }
    public ClaimsPrincipal? Principal { get; init; }

    public static RefreshTokenGrantResult Fail(string description) =>
        new() { IsSuccess = false, ErrorDescription = description };

    public static RefreshTokenGrantResult Success(ClaimsPrincipal principal) =>
        new() { IsSuccess = true, Principal = principal };
}
