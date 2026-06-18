using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

/// <summary>
/// Seeds OpenIddict scopes and a default client application at startup.
/// All operations are idempotent — safe to run on every startup.
/// </summary>
public class HostOpenIddictDataSeeder : IDataSeeder
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly ILogger<HostOpenIddictDataSeeder> _logger;

    public HostOpenIddictDataSeeder(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        ILogger<HostOpenIddictDataSeeder> logger)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedScopesAsync(cancellationToken);
        await SeedClientAsync(cancellationToken);
    }

    private async Task SeedScopesAsync(CancellationToken cancellationToken)
    {
        var scopes = new[] { Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess };

        foreach (var scopeName in scopes)
        {
            var existing = await _scopeManager.FindByNameAsync(scopeName, cancellationToken);
            if (existing is null)
            {
                var descriptor = new OpenIddictScopeDescriptor
                {
                    Name = scopeName,
                    DisplayName = scopeName switch
                    {
                        Scopes.OpenId => "OpenID Connect",
                        Scopes.Profile => "User profile",
                        Scopes.Email => "Email address",
                        Scopes.OfflineAccess => "Offline access (refresh token)",
                        _ => scopeName
                    }
                };
                descriptor.Resources.UnionWith(new[]
                {
                    "api", "crestcreates"
                });

                await _scopeManager.CreateAsync(descriptor, cancellationToken);
                _logger.LogInformation("Created OpenIddict scope: {Scope}", scopeName);
            }
        }

        _logger.LogInformation("OpenIddict scope seeding completed.");
    }

    private async Task SeedClientAsync(CancellationToken cancellationToken)
    {
        const string clientId = "crestcreates-client";

        var existing = await _applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("OpenIddict client '{ClientId}' already exists.", clientId);
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "CrestCreates Client"
        };

        descriptor.Permissions.UnionWith(new[]
        {
            Permissions.Endpoints.Token,
            Permissions.Endpoints.Authorization,
            Permissions.GrantTypes.Password,
            Permissions.GrantTypes.RefreshToken,
            Permissions.GrantTypes.ClientCredentials,
            Permissions.Prefixes.Scope + Scopes.OpenId,
            Permissions.Prefixes.Scope + Scopes.Profile,
            Permissions.Prefixes.Scope + Scopes.Email,
            Permissions.Prefixes.Scope + Scopes.OfflineAccess
        });

        await _applicationManager.CreateAsync(descriptor, cancellationToken);
        _logger.LogInformation("Created OpenIddict client: {ClientId}", clientId);
    }
}
