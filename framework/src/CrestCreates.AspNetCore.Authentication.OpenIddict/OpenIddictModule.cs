using CrestCreates.Domain.OpenIddict;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

[CrestModule]
public class OpenIddictModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<OpenIddictDbContext>()
                    .ReplaceDefaultEntities<
                        OpenIddictApplication,
                        OpenIddictAuthorization,
                        OpenIddictScope,
                        OpenIddictToken,
                        long>();
            })
            .AddServer(options =>
            {
                options.AllowAuthorizationCodeFlow()
                       .AllowClientCredentialsFlow()
                       .AllowRefreshTokenFlow();

                options.UseAspNetCore()
                       .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });
    }
}
