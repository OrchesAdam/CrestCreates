using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Controllers;

[ApiController]
[Route("connect")]
public class OpenIddictController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IPasswordGrantHandler _passwordGrantHandler;
    private readonly IRefreshTokenGrantHandler _refreshTokenGrantHandler;

    public OpenIddictController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        IPasswordGrantHandler passwordGrantHandler,
        IRefreshTokenGrantHandler refreshTokenGrantHandler)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _passwordGrantHandler = passwordGrantHandler;
        _refreshTokenGrantHandler = refreshTokenGrantHandler;
    }

    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var result = await HttpContext.AuthenticateAsync();

        if (result?.Succeeded != true)
        {
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.Path + Request.QueryString
            });
        }

        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The client application cannot be found.");

        var authorizations = await _authorizationManager.FindAsync(
            subject: result.Principal.GetClaim(ClaimTypes.NameIdentifier)!,
            client: await _applicationManager.GetIdAsync(application),
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()).ToListAsync();

        var authorization = authorizations.LastOrDefault();
        if (authorization == null)
        {
            authorization = await _authorizationManager.CreateAsync(
                principal: result.Principal,
                subject: result.Principal.GetClaim(ClaimTypes.NameIdentifier)!,
                client: await _applicationManager.GetIdAsync(application),
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes());
        }

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, result.Principal.GetClaim(ClaimTypes.NameIdentifier)!),
            new Claim(Claims.Name, result.Principal.GetClaim(ClaimTypes.Name) ?? string.Empty)
        };

        var tenantId = result.Principal.GetClaim("tenantid");
        if (!string.IsNullOrEmpty(tenantId))
        {
            claims.Add(new Claim("tenantid", tenantId));
        }

        var orgId = result.Principal.GetClaim("org_id");
        if (!string.IsNullOrEmpty(orgId))
        {
            claims.Add(new Claim("org_id", orgId));
        }

        var isSuperAdmin = result.Principal.GetClaim("is_super_admin");
        if (!string.IsNullOrEmpty(isSuperAdmin))
        {
            claims.Add(new Claim("is_super_admin", isSuperAdmin));
        }

        var roles = result.Principal.FindAll(ClaimTypes.Role);
        foreach (var role in roles)
        {
            claims.Add(new Claim(Claims.Role, role.Value));
        }

        var identity = new ClaimsIdentity(claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.SetScopes(request.GetScopes());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordGrantAsync(request);
        }

        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsGrantAsync(request);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenGrantAsync();
        }

        if (request.IsAuthorizationCodeGrantType())
        {
            return await HandleAuthorizationCodeGrantAsync();
        }

        throw new InvalidOperationException($"The grant type '{request.GrantType}' is not supported.");
    }

    private async Task<IActionResult> HandlePasswordGrantAsync(OpenIddictRequest request)
    {
        var result = await _passwordGrantHandler.HandleAsync(
            request.Username!,
            request.Password!,
            HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = result.ErrorDescription
            });
            return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // 构建 Token 内嵌 claims：tenant_id、is_super_admin、roles（不含 permission）
        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, result.UserId.ToString()),
            new Claim(Claims.Name, result.UserName)
        };

        if (!string.IsNullOrEmpty(result.TenantId))
        {
            claims.Add(new Claim("tenant_id", result.TenantId));
        }

        if (!string.IsNullOrEmpty(result.OrganizationId))
        {
            claims.Add(new Claim("org_id", result.OrganizationId));
        }

        claims.Add(new Claim("is_super_admin", result.IsSuperAdmin ? "true" : "false"));

        foreach (var role in result.Roles)
        {
            claims.Add(new Claim(Claims.Role, role));
        }

        var identity = new ClaimsIdentity(claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.SetScopes(request.GetScopes());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleClientCredentialsGrantAsync(OpenIddictRequest request)
    {
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The client application cannot be found.");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(Claims.Subject, await _applicationManager.GetClientIdAsync(application) ?? string.Empty),
                new Claim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application) ?? string.Empty)
            },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        var info = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var result = await _refreshTokenGrantHandler.HandleAsync(
            info.Principal!,
            HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = result.ErrorDescription
            });
            return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return SignIn(result.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleAuthorizationCodeGrantAsync()
    {
        var info = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return SignIn(info.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("userinfo")]
    [HttpPost("userinfo")]
    public async Task<IActionResult> Userinfo()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (result?.Principal == null)
        {
            return Challenge(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = result.Principal.GetClaim(Claims.Subject)!
        };

        var name = result.Principal.GetClaim(Claims.Name);
        if (!string.IsNullOrEmpty(name))
        {
            claims[Claims.Name] = name;
        }

        var email = result.Principal.GetClaim(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(email))
        {
            claims[Claims.Email] = email;
        }

        var tenantId = result.Principal.GetClaim("tenantid");
        if (!string.IsNullOrEmpty(tenantId))
        {
            claims["tenantid"] = tenantId;
        }

        return Ok(claims);
    }

    [HttpGet("logout")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (result?.Principal != null)
        {
            await HttpContext.SignOutAsync();
        }

        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
