using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CrestCreates.AspNetCore.Authentication.JwtBearer;

public static class JwtBearerServiceCollectionExtensions
{
    public static IServiceCollection AddJwtBearerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = new CrestCreates.Authorization.Abstractions.JwtOptions();
        configuration.GetSection("Jwt").Bind(jwtOptions);

        services.Configure<CrestCreates.Authorization.Abstractions.JwtOptions>(configuration.GetSection("Jwt"));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddJwtBearerAuthentication(
        this IServiceCollection services,
        Action<CrestCreates.Authorization.Abstractions.JwtOptions> configure)
    {
        var jwtOptions = new CrestCreates.Authorization.Abstractions.JwtOptions();
        configure(jwtOptions);

        services.Configure<CrestCreates.Authorization.Abstractions.JwtOptions>(opt =>
        {
            opt.SecretKey = jwtOptions.SecretKey;
            opt.Issuer = jwtOptions.Issuer;
            opt.Audience = jwtOptions.Audience;
            opt.AccessTokenExpirationMinutes = jwtOptions.AccessTokenExpirationMinutes;
            opt.RefreshTokenExpirationDays = jwtOptions.RefreshTokenExpirationDays;
        });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization();

        return services;
    }
}
