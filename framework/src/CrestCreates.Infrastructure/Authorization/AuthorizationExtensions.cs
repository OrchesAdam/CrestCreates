using System;
using CrestCreates.Authorization.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Infrastructure.Authorization
{
    public class AuthorizationOptions
    {
        public bool EnablePermissionCache { get; set; } = true;
        public int PermissionCacheExpirationMinutes { get; set; } = 20;
        public bool AutoAssignDefaultRoles { get; set; } = true;
    }

    public static class AuthorizationServiceCollectionExtensions
    {
        public static IServiceCollection AddRbacAuthorization(
            this IServiceCollection services,
            Action<AuthorizationOptions> configureOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<AuthorizationOptions>(options => { });
            }

            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddAuthorization();

            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

            return services;
        }

        public static IServiceCollection AddPermissionPolicies(
            this IServiceCollection services,
            Action<Microsoft.AspNetCore.Authorization.AuthorizationOptions> configure = null)
        {
            services.AddAuthorization(options =>
            {
                configure?.Invoke(options);
            });

            return services;
        }
    }

    public static class PermissionDefinitionExtensions
    {
        public static void AddPermissionPolicy(
            this Microsoft.AspNetCore.Authorization.AuthorizationOptions options,
            string policyName,
            params string[] permissions)
        {
            options.AddPolicy(policyName, policy =>
            {
                policy.Requirements.Add(new PermissionAuthorizationRequirement(permissions));
            });
        }

        public static void AddAllPermissionsPolicy(
            this Microsoft.AspNetCore.Authorization.AuthorizationOptions options,
            string policyName,
            params string[] permissions)
        {
            options.AddPolicy(policyName, policy =>
            {
                policy.Requirements.Add(new PermissionAuthorizationRequirement(permissions, requireAll: true));
            });
        }
    }
}
