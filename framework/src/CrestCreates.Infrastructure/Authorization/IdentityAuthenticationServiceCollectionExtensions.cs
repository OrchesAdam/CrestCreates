using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Infrastructure.Authorization;

public static class IdentityAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddCrestIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<IdentityAuthenticationOptions>(
            configuration.GetSection(IdentityAuthenticationOptions.SectionName));

        services.TryAddScoped<IPasswordHasher, PasswordHasher>();
        services.TryAddScoped<IPasswordPolicyValidator, PasswordPolicyValidator>();
        services.TryAddScoped<IIdentityClaimsBuilder, IdentityClaimsBuilder>();
        
        return services;
    }
}
