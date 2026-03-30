using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Infrastructure.Authorization
{
    /// <summary>
    /// 授权配置选项
    /// </summary>
    public class AuthorizationOptions
    {
        /// <summary>
        /// 是否启用权限缓存
        /// </summary>
        public bool EnablePermissionCache { get; set; } = true;

        /// <summary>
        /// 权限缓存过期时间（分钟）
        /// </summary>
        public int PermissionCacheExpirationMinutes { get; set; } = 20;

        /// <summary>
        /// 是否自动添加默认角色给新用户
        /// </summary>
        public bool AutoAssignDefaultRoles { get; set; } = true;
    }

    /// <summary>
    /// 授权服务注册扩展
    /// </summary>
    public static class AuthorizationServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 RBAC 授权系统
        /// </summary>
        public static IServiceCollection AddRbacAuthorization(
            this IServiceCollection services,
            Action<AuthorizationOptions> configureOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // 注册配置
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<AuthorizationOptions>(options => { });
            }

            // 注册 HttpContextAccessor
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // 注册核心服务
            services.TryAddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
            services.TryAddScoped<ICurrentUser, CurrentUser>();
            services.TryAddScoped<IPermissionChecker, PermissionChecker>();

            // 注册权限定义管理器
            services.TryAddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();

            // 注册授权处理器
            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

            return services;
        }

        /// <summary>
        /// 使用内存权限存储
        /// </summary>
        public static IServiceCollection AddInMemoryPermissionStore(
            this IServiceCollection services,
            bool enableCache = true)
        {
            // 注册内存存储
            services.TryAddSingleton<InMemoryPermissionStore>();

            if (enableCache)
            {
                // 使用缓存装饰器
                services.AddMemoryCache();
                services.TryAddScoped<IPermissionStore>(sp =>
                {
                    var innerStore = sp.GetRequiredService<InMemoryPermissionStore>();
                    var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedPermissionStore>>();
                    
                    return new CachedPermissionStore(innerStore, cache, logger);
                });
            }
            else
            {
                services.TryAddScoped<IPermissionStore>(sp => sp.GetRequiredService<InMemoryPermissionStore>());
            }

            // 注册权限授予服务
            services.TryAddScoped<IPermissionGrantService, PermissionGrantService>();

            return services;
        }

        /// <summary>
        /// 添加权限定义提供者
        /// </summary>
        public static IServiceCollection AddPermissionDefinitionProvider<TProvider>(
            this IServiceCollection services)
            where TProvider : class, IPermissionDefinitionProvider
        {
            services.AddTransient<IPermissionDefinitionProvider, TProvider>();
            return services;
        }

        /// <summary>
        /// 添加角色管理（需要提供仓储实现）
        /// </summary>
        public static IServiceCollection AddRoleManagement(
            this IServiceCollection services)
        {
            services.TryAddScoped<IRoleManager, RoleManager>();
            services.TryAddScoped<IUserRoleManager, UserRoleManager>();
            return services;
        }

        /// <summary>
        /// 配置权限策略
        /// </summary>
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

        /// <summary>
        /// 完整配置 RBAC（快速配置）
        /// </summary>
        public static IServiceCollection AddCompleteRbac(
            this IServiceCollection services,
            Action<AuthorizationOptions> configureOptions = null,
            bool enableCache = true)
        {
            services.AddRbacAuthorization(configureOptions);
            services.AddInMemoryPermissionStore(enableCache);
            services.AddRoleManagement();

            return services;
        }
    }

    /// <summary>
    /// 权限定义扩展方法
    /// </summary>
    public static class PermissionDefinitionExtensions
    {
        /// <summary>
        /// 批量添加 CRUD 权限
        /// </summary>
        public static void AddCrudPermissions(
            this PermissionGroupDefinition group,
            string resourceName,
            string displayNamePrefix = null)
        {
            var prefix = displayNamePrefix ?? resourceName;

            var parent = group.AddPermission(
                $"{resourceName}",
                $"{prefix}",
                $"All permissions for {resourceName}");

            parent.AddChild(
                $"{resourceName}.Create",
                $"Create {prefix}",
                $"Create new {resourceName}");

            parent.AddChild(
                $"{resourceName}.Update",
                $"Update {prefix}",
                $"Update existing {resourceName}");

            parent.AddChild(
                $"{resourceName}.Delete",
                $"Delete {prefix}",
                $"Delete {resourceName}");

            parent.AddChild(
                $"{resourceName}.View",
                $"View {prefix}",
                $"View {resourceName}");
        }

        /// <summary>
        /// 从 ASP.NET Core 授权选项获取权限策略
        /// </summary>
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

        /// <summary>
        /// 添加需要所有权限的策略
        /// </summary>
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
