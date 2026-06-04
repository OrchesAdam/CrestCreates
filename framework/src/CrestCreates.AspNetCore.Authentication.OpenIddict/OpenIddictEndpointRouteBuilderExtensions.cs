using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using CrestCreates.Infrastructure.Authorization;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

public static class OpenIddictEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCrestOpenIddictEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/connect")
            .DisableAntiforgery()
            .ExcludeFromDescription();

        group.MapGet("/authorize", (Delegate)AuthorizeAsync)
            .WithName("CrestOpenIddictAuthorizeGet")
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/authorize", (Delegate)AuthorizeAsync)
            .WithName("CrestOpenIddictAuthorizePost")
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status401Unauthorized)
            .DisableAntiforgery();

        group.MapPost("/token", (Delegate)ExchangeAsync)
            .WithName("CrestOpenIddictToken")
            .Accepts<OpenIddictTokenRequest>("application/x-www-form-urlencoded")
            .Produces<OpenIddictTokenResponse>(StatusCodes.Status200OK)
            .Produces<OpenIddictErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OpenIddictErrorResponse>(StatusCodes.Status403Forbidden)
            .DisableAntiforgery();

        group.MapGet("/userinfo", (Delegate)UserinfoAsync)
            .WithName("CrestOpenIddictUserinfoGet")
            .Produces<Dictionary<string, object>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/userinfo", (Delegate)UserinfoAsync)
            .WithName("CrestOpenIddictUserinfoPost")
            .Produces<Dictionary<string, object>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .DisableAntiforgery();

        group.MapGet("/logout", (Delegate)LogoutAsync)
            .WithName("CrestOpenIddictLogoutGet")
            .Produces<OpenIddictLogoutResponse>(StatusCodes.Status200OK);

        group.MapPost("/logout", (Delegate)LogoutAsync)
            .WithName("CrestOpenIddictLogoutPost")
            .Produces<OpenIddictLogoutResponse>(StatusCodes.Status200OK)
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IIdentityClaimsBuilder claimsBuilder)
    {
        var request = httpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var result = await httpContext.AuthenticateAsync();

        if (result?.Succeeded != true)
        {
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = httpContext.Request.Path + httpContext.Request.QueryString
            });
        }

        var authenticatedPrincipal = result.Principal ??
            throw new InvalidOperationException("The authenticated principal cannot be retrieved.");

        var application = await applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The client application cannot be found.");
        var applicationId = await applicationManager.GetIdAsync(application) ??
            throw new InvalidOperationException("The client application identifier cannot be retrieved.");

        var authorizations = await authorizationManager.FindAsync(
            subject: authenticatedPrincipal.GetClaim(ClaimTypes.NameIdentifier)!,
            client: applicationId,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()).ToListAsync();

        if (authorizations.LastOrDefault() == null)
        {
            _ = await authorizationManager.CreateAsync(
                principal: authenticatedPrincipal,
                subject: authenticatedPrincipal.GetClaim(ClaimTypes.NameIdentifier)!,
                client: applicationId,
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes());
        }

        var claimsContext = new IdentityClaimsContext
        {
            UserId = Guid.TryParse(authenticatedPrincipal.GetClaim(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty,
            UserName = authenticatedPrincipal.GetClaim(ClaimTypes.Name) ?? string.Empty,
            Email = authenticatedPrincipal.GetClaim(ClaimTypes.Email),
            TenantId = authenticatedPrincipal.GetClaim("tenantid"),
            OrganizationId = Guid.TryParse(authenticatedPrincipal.GetClaim("org_id"), out var orgId) ? orgId : null,
            IsSuperAdmin = string.Equals(authenticatedPrincipal.GetClaim("is_super_admin"), "true", StringComparison.OrdinalIgnoreCase),
            Roles = authenticatedPrincipal.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList(),
            Permissions = Array.Empty<string>()
        };

        var claims = claimsBuilder.Build(claimsContext);

        var identity = new ClaimsIdentity(claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.SetScopes(request.GetScopes());

        return Results.SignIn(
            principal,
            null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext httpContext,
        IOpenIddictApplicationManager applicationManager,
        IPasswordGrantHandler passwordGrantHandler,
        IRefreshTokenGrantHandler refreshTokenGrantHandler,
        IIdentityClaimsBuilder claimsBuilder)
    {
        var request = httpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordGrantAsync(httpContext, request, passwordGrantHandler, claimsBuilder);
        }

        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsGrantAsync(request, applicationManager);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenGrantAsync(httpContext, refreshTokenGrantHandler);
        }

        if (request.IsAuthorizationCodeGrantType())
        {
            return await HandleAuthorizationCodeGrantAsync(httpContext);
        }

        throw new InvalidOperationException($"The grant type '{request.GrantType}' is not supported.");
    }

    private static async Task<IResult> HandlePasswordGrantAsync(
        HttpContext httpContext,
        OpenIddictRequest request,
        IPasswordGrantHandler passwordGrantHandler,
        IIdentityClaimsBuilder claimsBuilder)
    {
        var result = await passwordGrantHandler.HandleAsync(
            request.Username!,
            request.Password!,
            httpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = result.ErrorDescription
            });

            return Results.Forbid(
                properties,
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claimsContext = new IdentityClaimsContext
        {
            UserId = result.UserId,
            UserName = result.UserName,
            Email = result.Email,
            TenantId = result.TenantId,
            OrganizationId = Guid.TryParse(result.OrganizationId, out var orgId) ? orgId : null,
            IsSuperAdmin = result.IsSuperAdmin,
            Roles = result.Roles,
            Permissions = Array.Empty<string>()
        };

        var claims = claimsBuilder.Build(claimsContext);

        var identity = new ClaimsIdentity(claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.SetScopes(request.GetScopes());
        principal.SetDestinations(_ => [Destinations.AccessToken]);

        return Results.SignIn(
            principal,
            null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleClientCredentialsGrantAsync(
        OpenIddictRequest request,
        IOpenIddictApplicationManager applicationManager)
    {
        var application = await applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The client application cannot be found.");

        var identity = new ClaimsIdentity(
            [
                new Claim(Claims.Subject, await applicationManager.GetClientIdAsync(application) ?? string.Empty),
                new Claim(Claims.Name, await applicationManager.GetDisplayNameAsync(application) ?? string.Empty)
            ],
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());

        return Results.SignIn(
            principal,
            null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleRefreshTokenGrantAsync(
        HttpContext httpContext,
        IRefreshTokenGrantHandler refreshTokenGrantHandler)
    {
        var info = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var result = await refreshTokenGrantHandler.HandleAsync(
            info.Principal!,
            httpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = result.ErrorDescription
            });

            return Results.Forbid(
                properties,
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        return Results.SignIn(
            result.Principal!,
            null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleAuthorizationCodeGrantAsync(HttpContext httpContext)
    {
        var info = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return Results.SignIn(
            info.Principal!,
            null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> UserinfoAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (result?.Principal == null)
        {
            return Results.Challenge(
                new AuthenticationProperties(),
                [OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme]);
        }

        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Challenge(
                new AuthenticationProperties(),
                [OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme]);
        }

        var token = authHeader.Substring("Bearer ".Length);
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);

        try
        {
            var parts = token.Split('.');
            if (parts.Length == 3)
            {
                var payloadBase64 = parts[1];
                var padding = 4 - payloadBase64.Length % 4;
                if (padding < 4)
                {
                    payloadBase64 = payloadBase64.PadRight(payloadBase64.Length + padding, '=');
                }

                var payloadBytes = Convert.FromBase64String(payloadBase64);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);

                using var doc = JsonDocument.Parse(payloadJson);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.Name is "iss" or "aud" or "exp" or "nbf" or "iat" or "jti" or "scope" or "client_id")
                    {
                        continue;
                    }

                    if (property.Name == "sub")
                    {
                        claims[property.Name] = property.Value.GetString() ?? string.Empty;
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        claims[property.Name] = property.Value.GetString()!;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var arr = property.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
                        claims[property.Name] = arr;
                    }
                }
            }
        }
        catch
        {
            foreach (var claim in result.Principal.Claims)
            {
                claims[claim.Type] = claim.Value;
            }
        }

        return Results.Ok(claims);
    }

    private static Task<IResult> LogoutAsync()
    {
        return Task.FromResult<IResult>(Results.Ok(new { message = "已退出登录" }));
    }

    private sealed class OpenIddictTokenRequest
    {
        [JsonPropertyName("grant_type")]
        public string? GrantType { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("redirect_uri")]
        public string? RedirectUri { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class OpenIddictTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class OpenIddictErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private sealed class OpenIddictLogoutResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
