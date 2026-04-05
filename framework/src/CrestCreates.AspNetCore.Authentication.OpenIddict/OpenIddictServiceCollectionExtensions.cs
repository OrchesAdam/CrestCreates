using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

public static class OpenIddictServiceCollectionExtensions
{
    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        Action<OpenIddictServerOptions>? configure = null)
    {
        var options = new OpenIddictServerOptions();
        configure?.Invoke(options);

        services.AddOpenIddict()
            .AddCore(coreOptions =>
            {
                coreOptions.UseQuartz();
            })
            .AddServer(serverOptions =>
            {
                serverOptions
                    .SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo");
                    // .SetLogoutEndpointUris("/connect/logout");

                if (options.EnableAuthorizationCodeFlow)
                {
                    serverOptions.AllowAuthorizationCodeFlow();
                }

                if (options.EnableClientCredentialsFlow)
                {
                    serverOptions.AllowClientCredentialsFlow();
                }

                if (options.EnablePasswordFlow)
                {
                    serverOptions.AllowPasswordFlow();
                }

                if (options.EnableRefreshTokenFlow)
                {
                    serverOptions.AllowRefreshTokenFlow();
                }

                serverOptions
                    .AddEphemeralEncryptionKey()
                    .AddEphemeralSigningKey();

                serverOptions.SetAccessTokenLifetime(TimeSpan.FromMinutes(options.AccessTokenLifetimeMinutes));
                serverOptions.SetRefreshTokenLifetime(TimeSpan.FromDays(options.RefreshTokenLifetimeDays));

                serverOptions.DisableAccessTokenEncryption();
            })
            .AddValidation(validationOptions =>
            {
                validationOptions.UseAspNetCore();
                validationOptions.UseLocalServer();
            });

        return services;
    }

    public static AuthenticationBuilder AddOpenIddictAuthentication(
        this IServiceCollection services)
    {
        return services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });
    }
}
