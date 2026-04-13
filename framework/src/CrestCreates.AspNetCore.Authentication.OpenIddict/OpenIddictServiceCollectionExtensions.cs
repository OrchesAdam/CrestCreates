using System;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Domain.OpenIddict;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                coreOptions.UseEntityFrameworkCore()
                    .UseDbContext<OpenIddictDbContext>()
                    .ReplaceDefaultEntities<
                        OpenIddictApplication,
                        OpenIddictAuthorization,
                        OpenIddictScope,
                        OpenIddictToken,
                        long>();
            })
            .AddServer(serverOptions =>
            {
                serverOptions
                    .SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo");

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

                serverOptions.UseAspNetCore()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(validationOptions =>
            {
                validationOptions.UseAspNetCore();
                validationOptions.UseLocalServer();
            });

        services.AddHttpContextAccessor();
        services.TryAddScoped<IIdentitySecurityLogService, IdentitySecurityLogServiceImpl>();
        services.TryAddScoped<IPasswordGrantHandler, PasswordGrantHandlerImpl>();
        services.TryAddScoped<IRefreshTokenGrantHandler, RefreshTokenGrantHandlerImpl>();

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

    public class ApplicationDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public ApplicationDbContext(DbContextOptions options) : base(options)
        {
        }
    }
}
