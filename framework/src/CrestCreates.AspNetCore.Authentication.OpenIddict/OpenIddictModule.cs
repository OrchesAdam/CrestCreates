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
                    .UseDbContext<ApplicationDbContext>();
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

    public class ApplicationDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public ApplicationDbContext(Microsoft.EntityFrameworkCore.DbContextOptions options) : base(options)
        {}

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<CrestCreates.Domain.OpenIddict.OpenIddictApplication>();
            builder.Entity<CrestCreates.Domain.OpenIddict.OpenIddictAuthorization>();
            builder.Entity<CrestCreates.Domain.OpenIddict.OpenIddictScope>();
            builder.Entity<CrestCreates.Domain.OpenIddict.OpenIddictToken>();
        }
    }
}
